using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;
using Region = PleaseDontDrown.Avatars.AvatarFunny.Region;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The players' funny body (AvatarLook.Bodies.Goofy): besides the usual bake it gets what <see cref="AvatarFunny"/>
    /// needs to make every player's lifeguard their own. A copy of the texture whose alpha says which part each texel
    /// is (skin, hair, top, shorts, teeth, nose) so the colours can be swapped; where the painted googly eyes, the
    /// nose and the teeth are; and shape keys for a rounder or flatter belly and a bigger or smaller nose.
    /// </summary>
    public static partial class MeshyCharacters
    {
        private static readonly HashSet<string> FunnyBodies = new() { "goofy" };

        private const int RecolorSize = 1024;

        private sealed class FunnyFit
        {
            public Color32[] Pixels;
            public byte[] Regions;
            public Vector3 EyeL, EyeR, NoseTip, TeethAt; // model space
            public float EyeRadius, NoseRadius;
            public Bounds Teeth;
            public float HeadY, NeckY, HipsY, ChestY, SpineZ, Height;
        }

        private static bool SkinLike(Color c) =>
            c.r > 0.35f && c.r >= c.g && c.g >= c.b * 0.92f && c.r - c.b > 0.08f && Redness(c) < 0.45f;

        private static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        /// <summary>Finds the googly eyes, nose and teeth and works out which part of the body every texel paints.</summary>
        private static FunnyFit FitFunny(string file, Mesh source, Vector3[] vertices, Vector3[] normals, BoneWeight[] weights, Material imported,
            Vector3 head, Vector3 neck, Vector3 hips, Vector3 chest, Vector3 spine)
        {
            Texture texture = FindBaseColor(imported);
            if (texture == null) throw new System.InvalidOperationException($"{file}: no colour texture for the funny bake");
            const int size = RecolorSize;
            var fit = new FunnyFit
            {
                Pixels = ReadPixels(texture, size), Regions = new byte[size * size],
                HeadY = head.y, NeckY = neck.y, HipsY = hips.y, ChestY = chest.y, SpineZ = spine.z, Height = vertices.Max(v => v.y)
            };
            Vector2[] uvs = source.uv;
            Color At(Vector2 uv)
            {
                int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * size), 0, size - 1);
                int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * size), 0, size - 1);
                return fit.Pixels[y * size + x];
            }

            // The whites on the front of the head: the eyes up top, the teeth below (split by height).
            var whites = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
                if (vertices[i].y > neck.y && vertices[i].z > head.z && normals[i].z > 0.15f && White(At(uvs[i]))) whites.Add(vertices[i]);
            if (whites.Count < 40) throw new System.InvalidOperationException($"{file}: no googly eyes found ({whites.Count} white vertices on the face)");
            float hi = whites.Max(v => v.y), lo = whites.Min(v => v.y);
            for (int pass = 0; pass < 12; pass++)
            {
                float split = (hi + lo) * 0.5f;
                hi = whites.Where(v => v.y >= split).Average(v => v.y);
                lo = whites.Where(v => v.y < split).DefaultIfEmpty(new Vector3(0f, lo, 0f)).Average(v => v.y);
            }
            float cut = (hi + lo) * 0.5f;
            List<Vector3> eyes = whites.Where(v => v.y >= cut).ToList();
            List<Vector3> teeth = whites.Where(v => v.y < cut && Mathf.Abs(v.x) < 0.12f).ToList();
            Vector3 EyeOf(IEnumerable<Vector3> side, out float radius)
            {
                List<Vector3> pts = side.ToList();
                var c = new Vector2(pts.Average(v => v.x), pts.Average(v => v.y));
                radius = Percentile(pts.Select(v => Vector2.Distance(new Vector2(v.x, v.y), c)).OrderBy(d => d).ToList(), 0.9f);
                float front = Percentile(pts.Select(v => v.z).OrderBy(z => z).ToList(), 0.95f);
                return new Vector3(c.x, c.y, front);
            }
            fit.EyeL = EyeOf(eyes.Where(v => v.x < 0f), out float rl); // the rig's left is -X
            fit.EyeR = EyeOf(eyes.Where(v => v.x >= 0f), out float rr);
            fit.EyeRadius = (rl + rr) * 0.5f;
            if (teeth.Count > 6)
            {
                fit.Teeth = new Bounds(teeth[0], Vector3.zero);
                foreach (Vector3 t in teeth) fit.Teeth.Encapsulate(t);
            }
            else fit.Teeth = new Bounds(new Vector3(0f, (fit.EyeL.y + head.y) * 0.5f, fit.EyeL.z), new Vector3(0.06f, 0.03f, 0.02f));
            fit.TeethAt = new Vector3(fit.Teeth.center.x, fit.Teeth.max.y, fit.Teeth.max.z);

            // The nose: the furthest-forward point between the eyes and the teeth, near the middle.
            float eyeY = (fit.EyeL.y + fit.EyeR.y) * 0.5f, spacing = Mathf.Abs(fit.EyeR.x - fit.EyeL.x);
            fit.NoseTip = fit.TeethAt;
            float bestZ = float.MinValue;
            foreach (Vector3 v in vertices)
                if (Mathf.Abs(v.x) < spacing * 0.3f && v.y > fit.Teeth.max.y && v.y < eyeY && v.z > bestZ)
                {
                    bestZ = v.z;
                    fit.NoseTip = v;
                }
            fit.NoseRadius = Mathf.Max(0.02f, fit.EyeRadius * 0.8f);

            // Paint by numbers: rasterise every triangle into the texture, each texel told by its colour and where it is.
            var covered = new bool[size * size];
            Region Classify(Color c, Vector3 p)
            {
                if (p.y > neck.y)
                {
                    bool inEye = Vector2.Distance(new Vector2(p.x, p.y), new Vector2(fit.EyeL.x, fit.EyeL.y)) < fit.EyeRadius * 1.15f ||
                                 Vector2.Distance(new Vector2(p.x, p.y), new Vector2(fit.EyeR.x, fit.EyeR.y)) < fit.EyeRadius * 1.15f;
                    if (!inEye && SkinLike(c) && Vector3.Distance(p, fit.NoseTip) < fit.NoseRadius) return Region.Nose;
                    if (White(c) || c.r > 0.8f && c.g > 0.72f && c.b > 0.5f && !SkinLike(c))
                    {
                        Bounds t = fit.Teeth;
                        t.Expand(new Vector3(0.02f, 0.02f, 0.06f));
                        if (!inEye && t.Contains(p)) return Region.Teeth;
                        return Region.Keep;
                    }
                    // Hair: dark brown above the eyes (and the brows), or anywhere on the back of the head.
                    if (!inEye && (p.y > eyeY - fit.EyeRadius * 0.3f || p.z < head.z - 0.02f) && Luma(c) < 0.4f && c.r >= c.b) return Region.Hair;
                    return SkinLike(c) ? Region.Skin : Region.Keep;
                }
                if (Redness(c) > 0.45f && c.r > 0.3f) return Region.Shorts;
                if (White(c) && p.y > hips.y - 0.06f) return Region.Top;
                return SkinLike(c) ? Region.Skin : Region.Keep;
            }
            for (int sm = 0; sm < source.subMeshCount; sm++)
            {
                int[] tris = source.GetTriangles(sm);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    Vector2 ua = uvs[a] * size, ub = uvs[b] * size, uc = uvs[c] * size;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, Mathf.Min(ub.y, uc.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(ua.y, Mathf.Max(ub.y, uc.y))));
                    float area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                    if (Mathf.Abs(area) < 1e-6f) continue;
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var q = new Vector2(x + 0.5f, y + 0.5f);
                        float wb = ((q.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (q.y - ua.y)) / area;
                        float wc = ((ub.x - ua.x) * (q.y - ua.y) - (q.x - ua.x) * (ub.y - ua.y)) / area;
                        float wa = 1f - wb - wc;
                        const float e = -0.02f;
                        if (wa < e || wb < e || wc < e) continue;
                        Vector3 p = vertices[a] * wa + vertices[b] * wb + vertices[c] * wc;
                        int index = y * size + x;
                        fit.Regions[index] = (byte)Classify(fit.Pixels[index], p);
                        covered[index] = true;
                    }
                }
            }
            // Spread the parts a few texels past the triangles' edges (texture filtering reads there too).
            for (int pass = 0; pass < 4; pass++)
            {
                var grown = (bool[])covered.Clone();
                for (int y = 1; y < size - 1; y++)
                for (int x = 1; x < size - 1; x++)
                {
                    int i = y * size + x;
                    if (covered[i]) continue;
                    foreach (int n in new[] { i - 1, i + 1, i - size, i + size })
                        if (covered[n])
                        {
                            fit.Regions[i] = fit.Regions[n];
                            grown[i] = true;
                            break;
                        }
                }
                covered = grown;
            }
            int[] counts = new int[(int)Region.Count];
            foreach (byte r in fit.Regions) counts[r]++;
            Debug.Log($"[Build] funny {file}: eyes at {fit.EyeL:F3} / {fit.EyeR:F3} r {fit.EyeRadius:F3}, nose {fit.NoseTip:F3}, teeth {fit.Teeth.center:F3} " +
                      $"size {fit.Teeth.size:F3}; texels " + string.Join(", ", Enumerable.Range(0, counts.Length).Select(r => $"{(Region)r} {counts[r]}")));
            return fit;
        }

        /// <summary>Belly and nose shape keys (AvatarFunny sets their weights from the look).</summary>
        private static void AddFunnyShapes(Mesh mesh, FunnyFit fit, Vector3[] vertices, BoneWeight[] weights, int total)
        {
            mesh.ClearBlendShapes(); // a re-bake reuses the mesh asset
            float k = fit.Height / 1.6f; // sizes below are for a 1.6 m lifeguard
            // Belly: out (or in) round the front of the tummy, most at its roundest point.
            float bellyY = (fit.HipsY + fit.ChestY) * 0.5f, reach = (fit.ChestY - fit.HipsY) * 0.5f + 0.1f * k;
            float best = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = Dominant(weights[i]);
                if (bone is (int)Bone.Hips or (int)Bone.Spine or (int)Bone.Chest && vertices[i].y > fit.HipsY - 0.05f && vertices[i].y < fit.ChestY && vertices[i].z > best)
                {
                    best = vertices[i].z;
                    bellyY = vertices[i].y;
                }
            }
            Vector3[] Belly(float amount)
            {
                var d = new Vector3[total];
                for (int i = 0; i < vertices.Length; i++)
                {
                    int bone = Dominant(weights[i]);
                    if (bone is not ((int)Bone.Hips or (int)Bone.Spine or (int)Bone.Chest)) continue;
                    Vector3 v = vertices[i];
                    var out_ = new Vector3(v.x, 0f, v.z - fit.SpineZ);
                    if (out_.sqrMagnitude < 1e-6f) continue;
                    out_.Normalize();
                    float front = Mathf.Clamp01(0.25f + 0.75f * out_.z);
                    float t = (v.y - bellyY) / reach;
                    d[i] = out_ * (amount * k * Mathf.Exp(-t * t * 2f) * front);
                }
                return d;
            }
            mesh.AddBlendShapeFrame(AvatarFunny.BellyRound, 100f, Belly(0.06f), null, null);
            mesh.AddBlendShapeFrame(AvatarFunny.BellyHuge, 100f, Belly(0.15f), null, null);
            mesh.AddBlendShapeFrame(AvatarFunny.BellyFlat, 100f, Belly(-0.04f), null, null);

            // Nose: grown or shrunk about its root, fading out round it.
            Vector3 root = fit.NoseTip - Vector3.forward * fit.NoseRadius * 0.8f;
            Vector3[] Nose(float factor)
            {
                var d = new Vector3[total];
                float r = fit.NoseRadius * 1.35f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (vertices[i].y < fit.NeckY) continue;
                    float dist = Vector3.Distance(vertices[i], fit.NoseTip);
                    if (dist > r) continue;
                    float w = 1f - dist / r;
                    w = w * w * (3f - 2f * w);
                    d[i] = (vertices[i] - root) * ((factor - 1f) * w);
                }
                return d;
            }
            mesh.AddBlendShapeFrame(AvatarFunny.NoseBig, 100f, Nose(1.7f), null, null);
            mesh.AddBlendShapeFrame(AvatarFunny.NoseSmall, 100f, Nose(0.55f), null, null);
        }

        /// <summary>The recolour texture (colours + part ids in alpha) and the head-space spots, onto the body asset.</summary>
        private static void ApplyFunny(AvatarBody body, FunnyFit fit, string file, Vector3 head)
        {
            const int size = RecolorSize;
            var lumas = new List<float>[(int)Region.Count];
            for (int r = 0; r < lumas.Length; r++) lumas[r] = new List<float>();
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = fit.Pixels[i];
                byte region = fit.Regions[i];
                pixels[i] = new Color32(c.r, c.g, c.b, (byte)(region * AvatarFunny.RegionStep));
                if ((i & 3) == 0) lumas[region].Add(Luma(c));
            }
            body.RegionLuma = lumas.Select(l => l.Count > 0 ? Median(l) : 0.5f).ToArray();

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = $"{OutputDir}/{file}_recolor.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.maxTextureSize = size;
            importer.SaveAndReimport();
            body.Recolor = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            body.Material.SetTexture("_BaseMap", body.Recolor);
            body.Material.mainTexture = body.Recolor;
            EditorUtility.SetDirty(body.Material);

            body.GooglyL = fit.EyeL - head;
            body.GooglyR = fit.EyeR - head;
            body.GooglyRadius = fit.EyeRadius;
            body.NoseTip = fit.NoseTip - head;
            body.TeethAt = fit.TeethAt - head;
        }
    }
}
