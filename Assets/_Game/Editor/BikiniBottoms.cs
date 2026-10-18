using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The Tripo women came wearing thong bikini bottoms; Shako wants ordinary full-back ones (2026-10-18). This paints
    /// a normal bikini bottom onto each woman's texture, in the colour of her own bikini bottom: the back from just
    /// under the hip bones to the fold under the cheeks, round the sides of the hips, and between the legs. The texture
    /// keeps its shading (the new cloth is the skin's light and shadow in the bikini's colour). The area is found in
    /// 3D from the skeleton (where the hip joint is, which bone moves each triangle), then its triangles are filled on
    /// the texture through their UVs. Run again after MeshyCharacters rebuilds the bodies (that brings the thongs back).
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.BikiniBottoms.Run</c> (writes the colour JPGs in Avatar/Bodies;
    /// pictures of each painted area to Screenshots/Review/Bikini).
    /// </summary>
    public static class BikiniBottoms
    {
        private static readonly string[] Women =
        {
            "tourist_lola", "tourist_girl_red", "tourist_girl_blonde", "tourist_girl_redhead", "tourist_girl_black",
            "tourist_girl_pink", "tourist_girl_lavender", "tourist_girl_barista", "tourist_girl_bartender"
        };

        // Heights over the hip joint, as a share of the hip joint's height over the floor (so any body size works):
        // the waistband at the back and the fold under the cheeks; the leg opening rises toward the side of the hip.
        private const float Waistband = 0.115f, Fold = -0.105f, SideRise = 0.13f;
        // Coverage reaches this far in front of the hip joint (the sides of the hips; the front is already a bikini).
        private const float FrontReach = 0.03f;

        [MenuItem("PLEASE DON'T DROWN/Characters/Paint full bikini bottoms (no thongs)")]
        public static void Run()
        {
            string review = Path.GetFullPath("Screenshots/Review/Bikini");
            Directory.CreateDirectory(review);
            foreach (string file in Women)
            {
                var body = AssetDatabase.LoadAssetAtPath<AvatarBody>($"Assets/_Game/Avatar/Bodies/{file}.asset");
                if (body == null || body.Mesh == null || body.Material == null) { Debug.LogWarning($"[Bikini] {file}: no body"); continue; }
                Texture tex = body.Material.GetTexture("_BaseMap") ?? body.Material.mainTexture;
                string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                if (string.IsNullOrEmpty(texPath)) { Debug.LogWarning($"[Bikini] {file}: no texture"); continue; }
                Paint(file, body.Mesh, texPath, review);
            }
            AssetDatabase.Refresh();
            Debug.Log("[Bikini] done");
        }

        private static void Paint(string file, Mesh mesh, string texPath, string review)
        {
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int[] tri = mesh.triangles;
            BoneWeight[] w = mesh.boneWeights;
            Matrix4x4[] bind = mesh.bindposes;
            Vector3 BonePos(Bone b) => bind[(int)b].inverse.MultiplyPoint3x4(Vector3.zero);
            Vector3 hips = BonePos(Bone.Hips), thighL = BonePos(Bone.ThighL), thighR = BonePos(Bone.ThighR);
            float floor = v.Min(p => p.y);
            float scale = hips.y - floor; // the hip joint's height over the floor
            float halfWidth = Mathf.Max(0.05f, Mathf.Abs(thighL.x - thighR.x)) * 1.05f; // hip joint to hip joint, a little wider

            // Always from the original (kept beside the review pictures the first time), so a re-run never paints twice.
            string original = Path.Combine(review, "original", Path.GetFileName(texPath));
            Directory.CreateDirectory(Path.GetDirectoryName(original));
            if (!File.Exists(original)) File.Copy(texPath, original);
            var bytes = File.ReadAllBytes(original);
            var img = new Texture2D(2, 2, TextureFormat.RGB24, false);
            img.LoadImage(bytes);
            int W = img.width, H = img.height;
            Color[] px = img.GetPixels();

            bool Lower(int vi)
            {
                // Moved by the pelvis or the thighs (never an arm or a hand hanging at hip height).
                BoneWeight b = w[vi];
                int top = b.weight0 >= b.weight1 ? b.boneIndex0 : b.boneIndex1;
                return top is (int)Bone.Hips or (int)Bone.Spine or (int)Bone.ThighL or (int)Bone.ThighR;
            }

            bool InBottom(Vector3 c)
            {
                float y = (c.y - hips.y) / scale;
                float side = Mathf.Abs(c.x - hips.x) / halfWidth; // 0 middle, 1 the hip joints
                float low = Fold + SideRise * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, side));
                if (y > Waistband || y < low) return false;
                // Round the side of the hip toward the front piece: the leg line climbs as it comes forward (a diagonal
                // cut, as a bikini wraps the hip); the middle of the front stays as modelled.
                float forward = Mathf.InverseLerp(hips.z - FrontReach * scale, hips.z + 0.09f * scale, c.z);
                if (forward >= 1f || (forward > 0f && side < 0.7f)) return false;
                low += 0.1f * Mathf.SmoothStep(0f, 1f, forward);
                if (y < low) return false;
                return side < 1.9f;
            }

            // Her bikini bottom's colour: the front of it, low and in the middle. Her skin: the small of the back.
            var cloth = new List<Color>();
            var skin = new List<Color>();
            for (int t = 0; t < tri.Length; t += 3)
            {
                Vector3 c = (v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3f;
                Vector2 cu = (uv[tri[t]] + uv[tri[t + 1]] + uv[tri[t + 2]]) / 3f;
                Color col = px[Mathf.Clamp((int)(cu.y * H), 0, H - 1) * W + Mathf.Clamp((int)(cu.x * W), 0, W - 1)];
                float y = (c.y - hips.y) / scale;
                if (Mathf.Abs(c.x - hips.x) < 0.3f * halfWidth && c.z > hips.z && y < 0.0f && y > -0.16f) cloth.Add(col);
                if (Lower(tri[t]) && Mathf.Abs(c.x - hips.x) > 0.4f * halfWidth && c.z < hips.z && y < -0.32f && y > -0.45f) skin.Add(col); // backs of the thighs (hair hangs over the back)
            }
            if (cloth.Count == 0 || skin.Count == 0) { Debug.LogWarning($"[Bikini] {file}: no samples (cloth {cloth.Count}, skin {skin.Count})"); return; }
            Color skinTone = Median(skin);
            // The front bottom's texels that clearly aren't skin are the bikini.
            var notSkin = cloth.Where(c => ColorDistance(c, skinTone) > 0.3f).ToList();
            if (notSkin.Count < 3) { Debug.LogWarning($"[Bikini] {file}: no bikini colour found at the front"); return; }
            Color bikini = Median(notSkin);
            float skinLuma = Luma(skinTone);

            // Fill the chosen triangles on the texture (and a pixel round each, so no skin shows along UV seams).
            var mask = new bool[W * H];
            int chosen = 0;
            for (int t = 0; t < tri.Length; t += 3)
            {
                int a = tri[t], b = tri[t + 1], d = tri[t + 2];
                if (!Lower(a) || !Lower(b) || !Lower(d)) continue;
                // Texel by texel, from the exact point on the body (whole triangles left a jagged edge).
                if (Raster(uv[a], uv[b], uv[d], v[a], v[b], v[d], W, H, mask, InBottom)) chosen++;
            }
            Grow(mask, W, H, 1);
            // The cloth: one even colour with the body's soft light and shadow (the light is blurred, so the old thong's
            // edges, straps and logo don't show through).
            var light = new float[px.Length];
            for (int i = 0; i < px.Length; i++) light[i] = mask[i] ? Luma(px[i]) : -1f;
            light = BlurMasked(light, W, H, 9);
            int painted = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (!mask[i]) continue;
                float shade = Mathf.Clamp(light[i] / Mathf.Max(0.05f, skinLuma), 0.75f, 1.1f);
                Color n = bikini * shade;
                n.a = 1f;
                px[i] = n;
                painted++;
            }
            // The empty gaps between the texture's pieces next to the new cloth take its colour too (the mipmaps blend
            // those in: they showed as thin light lines along the seams).
            var used = new bool[W * H];
            for (int t = 0; t < tri.Length; t += 3)
                Raster(uv[tri[t]], uv[tri[t + 1]], uv[tri[t + 2]], v[tri[t]], v[tri[t + 1]], v[tri[t + 2]], W, H, used, _ => true);
            var done = (bool[])mask.Clone();
            for (int step = 0; step < 12; step++)
            {
                var next = (bool[])done.Clone();
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                    {
                        int i = y * W + x;
                        if (done[i] || used[i]) continue;
                        int from = done[i - 1] ? i - 1 : done[i + 1] ? i + 1 : done[i - W] ? i - W : done[i + W] ? i + W : -1;
                        if (from < 0) continue;
                        px[i] = px[from];
                        next[i] = true;
                    }
                done = next;
            }
            img.SetPixels(px);
            img.Apply();
            File.WriteAllBytes(texPath, texPath.EndsWith(".png") ? img.EncodeToPNG() : img.EncodeToJPG(95));
            // A small picture of what was painted (white) on the texture, to look at.
            var preview = new Texture2D(W, H, TextureFormat.RGB24, false);
            var pv = new Color[W * H];
            for (int i = 0; i < pv.Length; i++) pv[i] = mask[i] ? Color.white : px[i] * 0.35f;
            preview.SetPixels(pv);
            preview.Apply();
            TextureScale(preview, 512);
            File.WriteAllBytes(Path.Combine(review, file + ".png"), preview.EncodeToPNG());
            Debug.Log($"[Bikini] {file}: bikini colour #{ColorUtility.ToHtmlStringRGB(bikini)} (from {cloth.Count} triangles), skin #{ColorUtility.ToHtmlStringRGB(skinTone)}, " +
                      $"{chosen} triangles, {painted} texels painted, hip height {scale:F2}, hip half width {halfWidth:F3}");
        }

        /// <summary>Box blur of the values >= 0 (the others are left out of every average), radius r, rows then columns.</summary>
        private static float[] BlurMasked(float[] src, int W, int H, int r)
        {
            float[] Pass(float[] a, bool rows)
            {
                var o = new float[a.Length];
                int n1 = rows ? H : W, n2 = rows ? W : H;
                for (int j = 0; j < n1; j++)
                {
                    float sum = 0f; int count = 0;
                    int Idx(int k) => rows ? j * W + k : k * W + j;
                    for (int k = 0; k < Mathf.Min(r, n2); k++) if (a[Idx(k)] >= 0f) { sum += a[Idx(k)]; count++; }
                    for (int k = 0; k < n2; k++)
                    {
                        int add = k + r, rem = k - r - 1;
                        if (add < n2 && a[Idx(add)] >= 0f) { sum += a[Idx(add)]; count++; }
                        if (rem >= 0 && a[Idx(rem)] >= 0f) { sum -= a[Idx(rem)]; count--; }
                        o[Idx(k)] = a[Idx(k)] < 0f ? -1f : count > 0 ? sum / count : a[Idx(k)];
                    }
                }
                return o;
            }
            return Pass(Pass(src, true), false);
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private static float ColorDistance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        private static Color Median(List<Color> list)
        {
            float M(System.Func<Color, float> f) { var s = list.Select(f).OrderBy(x => x).ToList(); return s[s.Count / 2]; }
            return new Color(M(c => c.r), M(c => c.g), M(c => c.b), 1f);
        }

        private static bool Raster(Vector2 a, Vector2 b, Vector2 c, Vector3 pa, Vector3 pb, Vector3 pc, int W, int H, bool[] mask, System.Func<Vector3, bool> inside)
        {
            Vector2 A = new(a.x * W, a.y * H), B = new(b.x * W, b.y * H), C = new(c.x * W, c.y * H);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-6f) return false;
            bool any = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((B.x - p.x) * (C.y - p.y) - (C.x - p.x) * (B.y - p.y)) / area;
                    float w1 = ((C.x - p.x) * (A.y - p.y) - (A.x - p.x) * (C.y - p.y)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                    if (!inside(pa * w0 + pb * w1 + pc * w2)) continue;
                    mask[y * W + x] = true;
                    any = true;
                }
            return any;
        }

        private static void Grow(bool[] mask, int W, int H, int steps)
        {
            for (int s = 0; s < steps; s++)
            {
                var next = (bool[])mask.Clone();
                for (int y = 1; y < H - 1; y++)
                    for (int x = 1; x < W - 1; x++)
                        if (!mask[y * W + x] && (mask[y * W + x - 1] || mask[y * W + x + 1] || mask[(y - 1) * W + x] || mask[(y + 1) * W + x]))
                            next[y * W + x] = true;
                System.Array.Copy(next, mask, mask.Length);
            }
        }

        private static void TextureScale(Texture2D tex, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            tex.Reinitialize(size, size);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
