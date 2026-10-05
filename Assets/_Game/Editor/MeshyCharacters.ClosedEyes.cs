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
    /// painted eye filled in with the skin round it (smoothly blended in from its edges, texel by texel) and a clean
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
            var color = new Color[source.Length];
            for (int i = 0; i < source.Length; i++) color[i] = source[i];
            var covered = new bool[source.Length];   // a face texel near the eyes (rasterised)
            var fill = new bool[source.Length];      // inside an eye: to be skin
            var line = new float[source.Length];     // closed-eye line coverage 0..1
            var shade = new float[source.Length];    // the lid's soft shadow just above the line

            // Ovals a little bigger than the painted eyes (lashes, liner and shadow included).
            var ovals = eyes.Select(e => new Vector4(e.center.x, e.center.y, e.width * 0.5f * 1.32f + 0.003f, e.height * 0.5f * 1.32f + 0.003f)).ToList();
            float reach = ovals.Max(o => Mathf.Max(o.z, o.w)) * 1.8f;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (vertices[a].y < floor || vertices[b].y < floor || vertices[c].y < floor) continue;
                if ((normals[a] + normals[b] + normals[c]).normalized.z < 0.25f) continue; // not the front of the face
                Vector3 mid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                if (!ovals.Any(o => Mathf.Abs(mid.x - o.x) < reach && Mathf.Abs(mid.y - o.y) < reach)) continue;
                // Rasterise the triangle in texture space.
                Vector2 ua = uvs[a] * size, ub = uvs[b] * size, uc = uvs[c] * size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y))));
                float area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                if (Mathf.Abs(area) < 1e-6f || (x1 - x0) * (y1 - y0) > 40000) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float wb = ((p.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (p.y - ua.y)) / area;
                        float wc = ((ub.x - ua.x) * (p.y - ua.y) - (p.x - ua.x) * (ub.y - ua.y)) / area;
                        float wa = 1f - wb - wc;
                        const float e = -0.02f; // a hair outside the edges too, so neighbouring triangles leave no gaps
                        if (wa < e || wb < e || wc < e) continue;
                        Vector3 at = vertices[a] * wa + vertices[b] * wb + vertices[c] * wc;
                        int k = y * size + x;
                        covered[k] = true;
                        foreach (Vector4 o in ovals)
                        {
                            float dx = (at.x - o.x) / o.z, dy = (at.y - o.y) / o.w;
                            if (dx * dx + dy * dy >= 1f) continue;
                            fill[k] = true;
                            // The closed eye: lashes along a gentle downward curve a little below the middle, thick in
                            // the middle and thin at the corners; the lid just above it a touch darker (it's round).
                            if (Mathf.Abs(dx) < 0.95f)
                            {
                                float curve = o.y - o.w * (0.08f + 0.32f * (1f - dx * dx));
                                float thick = o.w * 0.11f * (1f - 0.7f * dx * dx) + 0.0006f;
                                float d = at.y - curve;
                                line[k] = Mathf.Max(line[k], 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thick * 0.55f, thick, Mathf.Abs(d))));
                                if (d > 0f) shade[k] = Mathf.Max(shade[k], Mathf.Exp(-(d / (o.w * 0.45f)) * (d / (o.w * 0.45f))) * (1f - dx * dx));
                            }
                        }
                    }
            }
            int filled = fill.Count(f => f);
            if (filled < 40)
            {
                Debug.LogWarning($"[Build] body {file}: closed eyes not painted (the eyes cover {filled} texels)");
                return false;
            }

            // Skin in: start from the usual skin colour, then let the skin round each eye flow in (each texel the
            // average of its face neighbours, many times over) for a seamless, shaded fill.
            for (int k = 0; k < color.Length; k++) if (fill[k]) color[k] = skin;
            var order = Enumerable.Range(0, color.Length).Where(k => fill[k]).ToArray();
            for (int pass = 0; pass < 600; pass++)
                foreach (int k in order)
                {
                    int x = k % size, y = k / size;
                    Color sum = default;
                    int count = 0;
                    void Add(int xx, int yy)
                    {
                        if (xx < 0 || yy < 0 || xx >= size || yy >= size) return;
                        int kk = yy * size + xx;
                        if (!covered[kk]) return; // off the face (texture background, another part): ignored
                        sum += color[kk];
                        count++;
                    }
                    Add(x - 1, y); Add(x + 1, y); Add(x, y - 1); Add(x, y + 1);
                    if (count > 0) color[k] = sum / count;
                }
            // The lid's shading and the lash line on top.
            foreach (int k in order)
            {
                Color c = color[k] * (1f - 0.07f * shade[k]);
                c.a = 1f;
                color[k] = Color.Lerp(c, lash, line[k]);
            }
            _closedEyes = new Color32[color.Length];
            for (int i = 0; i < color.Length; i++) _closedEyes[i] = color[i];
            _closedEyesSize = size;
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
