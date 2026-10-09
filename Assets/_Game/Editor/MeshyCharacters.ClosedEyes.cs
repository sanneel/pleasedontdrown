using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Closed eyes for the painted (Meshy) faces, done in the texture itself: a copy of the body's texture with each
    /// painted eye filled with a continuous skin-colour field sampled around it, and a clean
    /// closed-eye line drawn across it. The game swaps to that texture while the eyes are shut (a blink, asleep,
    /// knocked out): no patches stuck on the face, which always read as plasters.
    /// </summary>
    public static partial class MeshyCharacters
    {
        /// <summary>Set by BuildFace for the body being baked: the closed-eyes texture (null: none).</summary>
        private static Color32[] _closedEyes;
        private static int _closedEyesSize;

        /// <summary>
        /// Paint the closed eyes into a full-size copy of the texture. <paramref name="eyes"/> are the painted eyes on
        /// the face (x, y in face space); every face triangle near them is rasterised in texture space, so each texel
        /// knows where on the face it is.
        /// </summary>
        private static bool PaintClosedEyes(Texture texture, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] triangles, float floor,
            IReadOnlyList<Rect> eyes, Color skin, Color lash, string file)
        {
            _closedEyes = null;
            int size = Mathf.Clamp(texture.width, 512, 4096);
            Color32[] source = ReadPixels(texture, size);
            var color = source.Select(c => (Color)c).ToArray();
            var occupied = new bool[source.Length];
            var covered = new bool[source.Length];
            var frontFacing = new bool[source.Length];
            var positions = new Vector3[source.Length];
            var ovals = eyes.Select(e => new Vector4(e.center.x, e.center.y,
                e.width * 0.66f + 0.003f, e.height * 0.66f + 0.003f)).ToArray();
            float reach = ovals.Max(o => Mathf.Max(o.z, o.w)) * 1.8f;

            // Record all islands, not just the face: gutter padding must never overwrite another body part.
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 mid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                bool front = (normals[a] + normals[b] + normals[c]).normalized.z > 0.25f;
                bool face = vertices[a].y >= floor && vertices[b].y >= floor && vertices[c].y >= floor &&
                    ovals.Any(o => Mathf.Abs(mid.x - o.x) < reach && Mathf.Abs(mid.y - o.y) < reach);
                Vector2 ua = uvs[a] * size, ub = uvs[b] * size, uc = uvs[c] * size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x))));
                int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y))));
                int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y))));
                float area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                if (Mathf.Abs(area) < 1e-6f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f - ua.x, py = y + 0.5f - ua.y;
                        float wb = (px * (uc.y - ua.y) - (uc.x - ua.x) * py) / area;
                        float wc = ((ub.x - ua.x) * py - px * (ub.y - ua.y)) / area;
                        float wa = 1f - wb - wc;
                        if (wa < -0.00001f || wb < -0.00001f || wc < -0.00001f) continue;
                        int k = y * size + x;
                        occupied[k] = true;
                        if (!face) continue;
                        covered[k] = true;
                        frontFacing[k] = front;
                        positions[k] = vertices[a] * wa + vertices[b] * wb + vertices[c] * wc;
                    }
            }
            var samples = Enumerable.Range(0, source.Length).Where(k => covered[k]).ToArray();
            var painted = new bool[source.Length];
            foreach (Vector4 o in ovals)
            {
                float Radius(int k)
                {
                    Vector3 p = positions[k];
                    float x = (p.x - o.x) / o.z, y = (p.y - o.y) / o.w;
                    return x * x + y * y;
                }
                // The sclera lies on the face. Hair and ears can project into the same oval but lie at another depth.
                var whites = samples.Where(k => frontFacing[k] && Radius(k) < 0.65f && White(source[k])).ToArray();
                var central = samples.Where(k => frontFacing[k] && Radius(k) < 0.65f && Distance(source[k], skin) < 0.24f).ToArray();
                var depths = whites.Length >= 8 ? whites : central;
                if (depths.Length < 8)
                    depths = samples.Where(k => frontFacing[k] && Radius(k) < 0.35f).OrderBy(k => Distance(source[k], skin)).Take(128).ToArray();
                if (depths.Length < 8) { Debug.LogWarning($"[FaceRepair] {file}: insufficient eye surface samples."); return false; }
                float depth = Median(depths.Select(k => positions[k].z));
                bool OnFace(int k) => positions[k].z > depth - 0.018f && positions[k].z < depth + 0.012f;
                var ring = samples.Where(k => frontFacing[k] && Radius(k) > 0.85f && Radius(k) < 2.3f && OnFace(k) &&
                    Distance(source[k], skin) < 0.22f).ToArray();
                // Some recoloured variants have a different eyelid tone than their hands. Fall back to the
                // geometric skin ring, not a floating lid patch, when the colour-distance gate is too strict.
                if (ring.Length < 20)
                    ring = samples.Where(k => frontFacing[k] && Radius(k) > 0.85f && Radius(k) < 2.3f && OnFace(k)).ToArray();
                if (ring.Length < 8) { Debug.LogWarning($"[FaceRepair] {file}: insufficient surrounding skin samples."); return false; }

                // Fit a continuous skin field in face space, shared by every UV island. Robust colour rejection
                // keeps brows, stray lashes and hair out of this field; gradients preserve the local complexion.
                Color median = new Color(Median(ring.Select(k => color[k].r)), Median(ring.Select(k => color[k].g)), Median(ring.Select(k => color[k].b)));
                ring = ring.Where(k => Distance(color[k], median) < 0.12f).ToArray();
                Color mean = Color.black, slopeX = Color.black, slopeY = Color.black;
                float xx = 0, yy = 0, xy = 0, mx = 0, my = 0;
                foreach (int k in ring)
                {
                    mx += (positions[k].x - o.x) / o.z; my += (positions[k].y - o.y) / o.w; mean += color[k];
                }
                if (ring.Length == 0) return false;
                mx /= ring.Length; my /= ring.Length; mean /= ring.Length;
                Color xc = Color.black, yc = Color.black;
                foreach (int k in ring)
                {
                    float x = (positions[k].x - o.x) / o.z - mx, y = (positions[k].y - o.y) / o.w - my;
                    xx += x*x; yy += y*y; xy += x*y;
                    xc += (color[k] - mean)*x; yc += (color[k] - mean)*y;
                }
                float det = xx*yy-xy*xy;
                if (det > 1e-6f) { slopeX = (xc*yy-yc*xy)/det; slopeY = (yc*xx-xc*xy)/det; }
                for (int channel = 0; channel < 3; channel++)
                {
                    slopeX[channel] = Mathf.Clamp(slopeX[channel], -0.08f, 0.08f);
                    slopeY[channel] = Mathf.Clamp(slopeY[channel], -0.08f, 0.08f);
                }
                // Hair that reaches into the oval (the temples, a fringe): its colour, from the ring's non-skin texels.
                // The lid is never painted over it (skin cut wedges into the hair at the temples).
                var hairRing = samples.Where(k => frontFacing[k] && Radius(k) > 0.85f && Radius(k) < 2.3f && OnFace(k) &&
                    Distance(source[k], skin) > 0.25f && !White(source[k]) && ((Color)source[k]).grayscale > 0.1f).ToArray();
                bool hasHair = hairRing.Length >= 30;
                Color hair = hasHair
                    ? new Color(Median(hairRing.Select(k => color[k].r)), Median(hairRing.Select(k => color[k].g)), Median(hairRing.Select(k => color[k].b)))
                    : Color.clear;

                // The closed lid's line: as long as the painted eye itself (its whites), not the whole box round the
                // lashes (a 5 cm cut across the face), a little below its middle and sagging like a relaxed lid.
                float wx0 = -0.55f, wx1 = 0.55f, wyMid = 0f, wyTop = 0.35f;
                if (whites.Length >= 8)
                {
                    var wx = whites.Select(k => (positions[k].x - o.x) / o.z).OrderBy(v => v).ToList();
                    var wy = whites.Select(k => (positions[k].y - o.y) / o.w).OrderBy(v => v).ToList();
                    wx0 = wx[Mathf.FloorToInt(0.03f * (wx.Count - 1))];
                    wx1 = wx[Mathf.CeilToInt(0.97f * (wx.Count - 1))];
                    wyMid = wy[wy.Count / 2];
                    wyTop = wy[Mathf.CeilToInt(0.97f * (wy.Count - 1))];
                }
                float lineMid = 0.5f * (wx0 + wx1);
                float lineHalf = Mathf.Clamp(0.5f * (wx1 - wx0) * 1.08f, 0.3f, 0.75f);
                // Where the lashes end above this eye: over the eye the painting comes in layers, the lashes and
                // eyeliner low, then bare skin, then the brow (and hair). Everything above the start of that bare
                // band is kept (a fixed height cut short brows that start low and kept lashes that reach high).
                var paint = new int[30]; // millimetres over the whites
                var all = new int[30];
                foreach (int k in samples)
                {
                    if (!OnFace(k)) continue;
                    float bx = (positions[k].x - o.x) / o.z, by = (positions[k].y - o.y) / o.w;
                    float h = (by - wyTop) * o.w;
                    if (Mathf.Abs(bx - lineMid) >= lineHalf || h <= 0f || h >= 0.03f) continue;
                    int bin = (int)(h / 0.001f);
                    all[bin]++;
                    if (Distance(source[k], median) > 0.15f) paint[bin]++;
                }
                bool Bare(int bin) => all[bin] > 0 && paint[bin] <= 0.2f * all[bin];
                // (The widest bare band: a thin one can lie between the whites and the lash line too.)
                float browCut = 0.012f;
                int bestRun = 1;
                for (int bin = 2, run = 0; bin < 30; bin++)
                {
                    run = Bare(bin) ? run + 1 : 0;
                    if (run > bestRun) { bestRun = run; browCut = 0.001f * (bin - run + 2); }
                }
                Debug.Log($"[FaceRepair] {file}: eye at x {o.x:F3}: lashes end {browCut * 1000f:F1} mm over the whites");
                float outer = Mathf.Sign(o.x); // the outer corner is away from the nose
                foreach (int k in samples)
                {
                    float q = Radius(k);
                    if (!OnFace(k)) continue;
                    float x = (positions[k].x - o.x) / o.z, y = (positions[k].y - o.y) / o.w;
                    // Long upper lashes reach out of the oval and were left as broken dark strokes over the closed
                    // lid: what isn't skin hugging the eye (along it, above its middle, under the brow) goes too.
                    bool strayLash = q >= 0.92f && q < 2.4f && Mathf.Abs(x - lineMid) < 1.25f * lineHalf && y > wyMid &&
                        (y - wyTop) * o.w < browCut && Distance(color[k], median) > 0.15f;
                    if (q >= 1f && !strayLash) continue;
                    // Outside the eye itself, leave non-skin features (hair, brows) intact.
                    if (!strayLash && q > 0.95f && Distance(color[k], median) > 0.25f) continue;
                    if (!strayLash && hasHair && q > 0.45f && !White(source[k]) && Distance(color[k], hair) < 0.14f && Distance(color[k], median) > 0.18f) continue;
                    // Up in the brow only skin is painted: the oval reaches into the brows, and painting their lower
                    // half over left square blocks of brow.
                    if ((y - wyTop) * o.w > browCut && Distance(color[k], median) > 0.15f) continue;
                    Color lid = mean + slopeX*(x-mx) + slopeY*(y-my);
                    float u = (x - lineMid) / lineHalf;
                    float au = Mathf.Abs(u);
                    // In metres, then into the oval's units: up to 1.8 mm thick in the middle, tapering to a point.
                    float curve = wyMid - 0.12f - 0.2f * Mathf.Max(0f, 1f - u * u);
                    float d = (y - curve) * o.w;
                    float thick = au < 1f ? 0.0009f * Mathf.Sqrt(1f - u * u) + 0.00025f : 0f;
                    // The outer end flicks up a touch into a short lash, like a sleeping cartoon eye.
                    if (u * outer > 0.55f && au < 1.12f) d -= (au - 0.55f) * (au - 0.55f) * 0.012f;
                    float along = Mathf.SmoothStep(0f, 1f, (1.12f - au) / 0.18f);
                    float stroke = !strayLash && (thick > 0f || au < 1.12f)
                        ? (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Mathf.Max(thick, 0.00025f) * 0.45f, Mathf.Max(thick, 0.00025f) * 1.25f, Mathf.Abs(d)))) * along
                        : 0f;
                    // A soft crease of the lid above the line, and the lid itself a shade darker than the cheek.
                    float above = Mathf.Max(0f, d);
                    float shade = au < 1.1f ? Mathf.Exp(-above * above / (0.004f * 0.004f)) * (1f - Mathf.Min(1f, au) * 0.5f) * 0.07f : 0f;
                    lid *= 1f - shade;
                    lid = Color.Lerp(lid, Color.Lerp(lash, mean, 0.12f), stroke);
                    lid.a = 1f;
                    float blend = strayLash ? 1f : 1f-Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1f, q));
                    color[k] = Color.Lerp(color[k], lid, blend);
                    painted[k] = true;
                }
            }
            if (painted.Count(v => v) < 40) return false;
            // Four texels of dilation into empty atlas gutters prevent bilinear/mipmap sampling of the old eye.
            var frontier = Enumerable.Range(0, painted.Length).Where(k => painted[k]).ToList();
            for (int pass = 0; pass < 4; pass++)
            {
                var next = new List<int>();
                foreach (int k in frontier)
                {
                    int x = k % size, y = k / size;
                    void Pad(int nx, int ny)
                    {
                        if (nx < 0 || nx >= size || ny < 0 || ny >= size) return;
                        int n = ny*size+nx;
                        if (occupied[n] || painted[n]) return;
                        color[n] = color[k]; painted[n] = true; next.Add(n);
                    }
                    Pad(x-1,y); Pad(x+1,y); Pad(x,y-1); Pad(x,y+1);
                }
                frontier = next;
            }
            _closedEyes = color.Select(c => (Color32)c).ToArray();
            _closedEyesSize = size;
            Debug.Log($"[FaceRepair] {file}: painted {painted.Count(v => v)} texels with face-space skin and protected atlas gutters.");
            return true;
        }
        /// <summary>Save the closed-eyes texture painted for this body and a material with it (null: none painted).</summary>
        private static Material SaveClosedEyes(string file, Material open)
        {
            if (_closedEyes == null || open == null) return null;
            // (4096 ones as JPEG: as PNG each would add some 12 MB to the repository.)
            bool big = _closedEyesSize > 2048;
            string texturePath = $"{OutputDir}/{file}_eyes_closed.{(big ? "jpg" : "png")}";
            string other = $"{OutputDir}/{file}_eyes_closed.{(big ? "png" : "jpg")}";
            if (File.Exists(other)) AssetDatabase.DeleteAsset(other);
            var texture = new Texture2D(_closedEyesSize, _closedEyesSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(_closedEyes);
            texture.Apply();
            File.WriteAllBytes(texturePath, big ? texture.EncodeToJPG(95) : texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            ImportCompressed(texturePath, _closedEyesSize);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            string materialPath = $"{OutputDir}/{file}_eyes_closed.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(open);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else material.CopyPropertiesFromMaterial(open);
            material.SetTexture("_BaseMap", baseMap);
            material.mainTexture = baseMap;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            _closedEyes = null;
            return material;
        }
    }
}
