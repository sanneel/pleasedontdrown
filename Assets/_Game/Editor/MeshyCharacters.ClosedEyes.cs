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
            int size = Mathf.Clamp(texture.width, 512, 2048);
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
                foreach (int k in samples)
                {
                    float q = Radius(k);
                    if (q >= 1f || !OnFace(k)) continue;
                    float x = (positions[k].x - o.x) / o.z, y = (positions[k].y - o.y) / o.w;
                    // Outside the eye itself, leave non-skin features (hair, brows) intact.
                    if (q > 0.95f && Distance(color[k], median) > 0.25f) continue;
                    Color lid = mean + slopeX*(x-mx) + slopeY*(y-my);
                    float u = x / 0.77f;
                    float curve = -(0.08f + 0.25f*(1f-u*u));
                    float d = y-curve;
                    float thick = 0.065f*Mathf.Sqrt(Mathf.Max(0f, 1f-u*u)) + 0.008f;
                    float stroke = Mathf.Abs(u) < 1f ? (1f-Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thick*0.35f, thick, Mathf.Abs(d)))) *
                        Mathf.SmoothStep(0f, 1f, (1f-Mathf.Abs(u))/0.10f) : 0f;
                    float shadow = d > 0f ? Mathf.Exp(-d*d/0.13f)*Mathf.Max(0,1f-u*u)*0.035f : 0f;
                    lid *= 1f-shadow;
                    lid = Color.Lerp(lid, lash, stroke);
                    lid.a = 1f;
                    float blend = 1f-Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1f, q));
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
            string texturePath = $"{OutputDir}/{file}_eyes_closed.png";
            var texture = new Texture2D(_closedEyesSize, _closedEyesSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(_closedEyes);
            texture.Apply();
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
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
