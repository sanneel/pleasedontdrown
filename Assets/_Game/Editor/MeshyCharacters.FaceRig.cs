using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The living face (2026-10-19), rigged at bake time over the face BuildFace found:
    ///  * eyes: the front of the face is drawn straight on into a small picture (the face image), the painted eyes are
    ///    cut out of it exactly (every texel that isn't skin, joined to the eye), and two textures are saved: the eyes
    ///    as they are, and the same with the eyes filled in with the skin round them (a lid). The face shader
    ///    (Avatar/AvatarFace.shader) draws the lid down over the eye to blink, with a lash line along its edge, and
    ///    moves the iris inside the eye to look about. It works on the face as drawn from the front, not on the
    ///    texture: no UV seams, gutters or modelled eyeballs to get in the way;
    ///  * mouth: the face is cut along the line where the lips meet, a dark mouth is put behind it, and a jaw shape
    ///    key turns the chin down about the jaw's hinge, so the lips part;
    ///  * shape keys for a smile, a frown and the brows (up, angry, sad), on the brows found above the eyes.
    /// The mesh keeps every vertex the model had (the cut only adds), and each vertex carries where it is on the face at
    /// rest (UV3), so the face shader finds the eyes however the head moves.
    /// </summary>
    public static partial class MeshyCharacters
    {
        /// <summary>What BuildFace found on a painted face, in face space (the model's own x and y, metres).</summary>
        private sealed class FoundFace
        {
            public Rect EyeL, EyeR;
            public bool SeeL, SeeR;     // a painted eye there to shut and move (not an eye patch or sunglasses)
            public float EyeLevel;
            public Color Skin;
            public bool Guessed;        // the eyes weren't found in the texture: where eyes usually are
            public bool HasMouth, MouthHinted;
            public Vector2 Mouth;       // where the lips meet, in the middle
            public float MouthWidth;
        }

        /// <summary>The mesh being put together: the model's vertices, then those the mouth cut and the mouth add.</summary>
        private sealed class MeshParts
        {
            public List<Vector3> V, N;
            public List<Vector4> T;
            public List<Vector2> Uv;
            public List<BoneWeight> W;
            public List<int>[] Tris;
            public int Original;
            /// <summary>Vertices made by the mouth cut (index Original + k): on the edge a-b, t of the way from a.</summary>
            public readonly List<(int a, int b, float t)> Parents = new();
            /// <summary>Which side of the lips each vertex is on, where the cut decided it (1 under, 0 over).</summary>
            public readonly Dictionary<int, bool> Lower = new();
            public int MouthFirst = -1;  // the mouth inside: its first vertex (to the end)
        }

        /// <summary>The front of the head drawn straight on (looking down -z), one texel every Px metres.</summary>
        private sealed class FaceImage
        {
            public float X0, Y0, Px;
            public int W, H;
            public Color[] C;
            public float[] Z;
            public bool Has(int k) => Z[k] > float.MinValue;
            public int K(int i, int j) => j * W + i;
            public bool In(int i, int j) => i >= 0 && j >= 0 && i < W && j < H;
            public float X(int i) => X0 + (i + 0.5f) * Px;
            public float Y(int j) => Y0 + (j + 0.5f) * Px;
            public int I(float x) => Mathf.FloorToInt((x - X0) / Px);
            public int J(float y) => Mathf.FloorToInt((y - Y0) / Px);

            /// <summary>The front surface's z at (x, y) (NaN: no face there).</summary>
            public float FrontZ(float x, float y)
            {
                int i = I(x), j = J(y);
                if (!In(i, j)) return float.NaN;
                int k = K(i, j);
                return Has(k) ? Z[k] : float.NaN;
            }
        }

        /// <summary>A painted eye, cut out of the face image: its upper and lower edge column by column.</summary>
        private sealed class EyeFit
        {
            public int I0, I1;
            public float[] Top, Bottom;
            public Color Sclera, Lash, LocalSkin;
            public float Threshold;
            public float Cx, Cy, Hx, Hy;
            public bool Fallback;
            // The shut lid's shape: the face's depth over the eye, smoothed over it from the skin round it (a window of the face image).
            public float[] ShutZ, ShutWeight;
            public int ZI0, ZJ0, ZW, ZH;
            public float TopAt(int i) => Top[Mathf.Clamp(i - I0, 0, Top.Length - 1)];
            public float BottomAt(int i) => Bottom[Mathf.Clamp(i - I0, 0, Bottom.Length - 1)];
            public float MaxTop => Top.Max();
        }

        private sealed class FaceRig
        {
            public string File;
            public FaceImage Image;
            public EyeFit L, R;
            public float MidX;
            public Texture2D Open, Lid;
            public Vector4 Rect;            // the eye textures on the face: x0, y0, 1 / width, 1 / height
            // The mouth: the line where the lips meet (y every Px from LineX0), cut CutHalf either side of the middle.
            public bool Mouth, MouthMoved; // (moved: not where BuildFace put the mouth bone)
            public float Cx, CutHalf, MouthZ, ChinDepth, LineX0;
            public float[] Line;
            // The brows: middle x, height, half width (x 0: none).
            public Vector3 BrowL, BrowR;
            public float EyeTopL, EyeTopR;

            public float LineAt(float x)
            {
                float f = (x - LineX0) / Image.Px;
                int a = Mathf.Clamp(Mathf.FloorToInt(f), 0, Line.Length - 1), b = Mathf.Min(a + 1, Line.Length - 1);
                return Mathf.Lerp(Line[a], Line[b], Mathf.Clamp01(f - a));
            }

            /// <summary>1 on the front of the face (within 7 mm of what is seen from the front there), 0 behind it.</summary>
            public float Front(Vector3 p, float near = 0.007f, float far = 0.02f)
            {
                float z = Image.FrontZ(p.x, p.y);
                return float.IsNaN(z) ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(z - far, z - near, p.z));
            }
        }

        // ------------------------------------------------------------------ fitting

        private const float FaceTexel = 0.00015f; // 0.15 mm: finer than the models' own textures on their faces

        private static FaceRig FitFaceRig(string file, FoundFace found, Mesh source, Vector3[] vertices, Vector3[] normals, Material imported)
        {
            Texture texture = FindBaseColor(imported);
            if (texture == null) return null;
            int size = Mathf.Clamp(texture.width, 512, 4096);
            Color32[] pixels = ReadPixels(texture, size);
            // The whole front of the face (the models stand centred on x = 0), from under the mouth to over the brows.
            float low = Mathf.Min(found.HasMouth ? found.Mouth.y : found.EyeLevel - 0.07f, found.EyeLevel - 0.06f) - 0.06f;
            float high = Mathf.Max(Mathf.Max(found.EyeL.yMax, found.EyeR.yMax), found.EyeLevel + 0.03f) + 0.055f;
            float faceX = Mathf.Clamp((found.EyeL.center.x + found.EyeR.center.x) * 0.5f, -0.05f, 0.05f);
            var region = Rect.MinMaxRect(faceX - 0.13f, low, faceX + 0.13f, high);
            var rig = new FaceRig { File = file, MidX = (found.EyeL.center.x + found.EyeR.center.x) * 0.5f };
            rig.Image = RasterFace(vertices, normals, source.uv, source.triangles, pixels, size, region, FaceTexel);
            // The face's own skin (the hands BuildFace took it from can be gloved): the commonest skin-like colour
            // down the middle of the face, from the eyes down.
            {
                FaceImage img = rig.Image;
                var bins = new Dictionary<int, (Vector3 sum, int count)>();
                for (int j = Mathf.Max(0, img.J(found.EyeLevel - 0.09f)); j <= Mathf.Min(img.H - 1, img.J(found.EyeLevel + 0.01f)); j += 2) // (eyes to chin: not the hair)
                    for (int i = Mathf.Max(0, img.I(faceX - 0.03f)); i <= Mathf.Min(img.W - 1, img.I(faceX + 0.03f)); i += 2)
                    {
                        int k = img.K(i, j);
                        if (!img.Has(k)) continue;
                        Color c = img.C[k];
                        float l = Luma(c);
                        if (c.r < c.g || c.g < c.b * 0.9f || c.r - c.b < 0.08f || l < 0.2f || l > 0.95f) continue;
                        int bin = (int)(c.r * 15.99f) * 256 + (int)(c.g * 15.99f) * 16 + (int)(c.b * 15.99f);
                        bins.TryGetValue(bin, out var b);
                        bins[bin] = (b.sum + new Vector3(c.r, c.g, c.b), b.count + 1);
                    }
                if (bins.Count > 0)
                {
                    var commonest = bins.Values.OrderByDescending(b => b.count).First();
                    if (commonest.count > 100) { Vector3 m = commonest.sum / commonest.count; found.Skin = new Color(m.x, m.y, m.z); }
                }
            }

            // The eyes, found again on the face as seen from the front: the whites, in a pair. A look-alike (the same mesh
            // repainted) has them where its base model's are (pale hair and pale whites fool a search of its own).
            bool seeL = found.SeeL, seeR = found.SeeR;
            Rect whitesL = default, whitesR = default;
            string baseFile = FaceBaseOf(file);
            float floorY = found.EyeLevel - 0.15f;
            if (baseFile != file && BaseFaces.TryGetValue(baseFile, out FaceFit basis) && basis.WhitesL != null && basis.VertexCount == vertices.Length)
            {
                whitesL = basis.WhitesL.On(vertices);
                whitesR = basis.WhitesR.On(vertices);
                rig.MidX = (whitesL.center.x + whitesR.center.x) * 0.5f;
                Debug.Log($"[Build] face {file}: eyes where {baseFile}'s are");
            }
            else if ((seeL || seeR) && FindEyeWhites(rig.Image, found, out whitesL, out whitesR, out bool oneL, out bool oneR, out string note))
            {
                seeL = oneL;
                seeR = oneR;
                rig.MidX = (whitesL.center.x + whitesR.center.x) * 0.5f;
                Debug.Log($"[Build] face {file}: eyes {note}");
            }
            else if (found.Guessed)
            {
                seeL = seeR = false;
                Debug.LogWarning($"[Build] face {file}: no eyes found: they stay as painted");
            }
            else if (seeL || seeR)
            {
                // What BuildFace found, the middle of it taken for the whites.
                Rect Core(Rect r) => new(r.center - r.size * 0.25f, r.size * 0.5f);
                whitesL = Core(found.EyeL);
                whitesR = Core(found.EyeR);
                Debug.LogWarning($"[Build] face {file}: no pair of eye whites on the face: using the eyes found in the texture");
            }
            if (baseFile == file && whitesL.width > 0f && BaseFaces.TryGetValue(file, out FaceFit own))
            {
                own.WhitesL = Spot.At(whitesL, vertices, normals, floorY);
                own.WhitesR = Spot.At(whitesR, vertices, normals, floorY);
            }
            BaseEyes.TryGetValue(baseFile, out var baseEyes);
            bool alike = SameMeshAs.ContainsKey(file) && baseEyes.l != null && baseEyes.r != null; // (the same mesh repainted: reshaped look-alikes cut their own)
            rig.L = seeL ? FitEye(rig.Image, whitesL, found.Skin, $"{file} left", alike ? baseEyes.l : null) : null;
            rig.R = seeR ? FitEye(rig.Image, whitesR, found.Skin, $"{file} right", alike ? baseEyes.r : null) : null;
            // One eye cut out, the other only an oval: the good one mirrored onto the other's whites (a pair matches).
            if (rig.L != null && rig.R != null && rig.L.Fallback != rig.R.Fallback)
            {
                EyeFit good = rig.L.Fallback ? rig.R : rig.L, bad = rig.L.Fallback ? rig.L : rig.R;
                Rect goodWhites = rig.L.Fallback ? whitesR : whitesL, badWhites = rig.L.Fallback ? whitesL : whitesR;
                var shape = new EyeShape { Img = rig.Image, Fit = MirroredEye(rig.Image, good), Whites = Rect.MinMaxRect(-goodWhites.xMax, goodWhites.yMin, -goodWhites.xMin, goodWhites.yMax) };
                // (mirrored about x = 0: the shape's whites are mirrored the same way, so ShapeLike moves it onto the bad eye's)
                EyeFit fixedEye = FitEye(rig.Image, badWhites, found.Skin, $"{file} {(rig.L.Fallback ? "left" : "right")} (mirrored)", shape);
                if (rig.L.Fallback) rig.L = fixedEye; else rig.R = fixedEye;
                Debug.Log($"[Build] face {file}: the {(bad == rig.L ? "left" : "right")} eye takes the other's outline, mirrored");
            }
            if (baseFile == file && rig.L != null && rig.R != null)
                BaseEyes[file] = (new EyeShape { Img = rig.Image, Fit = rig.L, Whites = whitesL }, new EyeShape { Img = rig.Image, Fit = rig.R, Whites = whitesR });
            if (rig.L != null || rig.R != null) MakeEyeTextures(rig);
            if (rig.L != null) FitShutShape(rig.Image, rig.L);
            if (rig.R != null) FitShutShape(rig.Image, rig.R);
            // The mouth where a face's mouth is, below the eyes (the texture search sometimes takes a shirt's painted
            // seam or the chin for it; a closed mouth painted as just a line isn't found at all): else the darkest
            // line across the face there.
            Rect refL = whitesL.width > 0f ? whitesL : found.EyeL, refR = whitesR.width > 0f ? whitesR : found.EyeR;
            Vector2 eyesAt = (refL.center + refR.center) * 0.5f;
            float spacing = Mathf.Max(0.03f, Mathf.Abs(refR.center.x - refL.center.x));
            bool plausible = found.HasMouth && (baseFile != file || (found.Mouth.y < eyesAt.y - 0.45f * spacing && found.Mouth.y > eyesAt.y - 1.8f * spacing &&
                             Mathf.Abs(found.Mouth.x - eyesAt.x) < 0.35f * spacing));
            if (!plausible && !found.MouthHinted && FindMouthLine(rig.Image, eyesAt, spacing, out Vector2 line, out float lineWidth))
            {
                Debug.Log($"[Build] face {file}: mouth {(found.HasMouth ? $"found at y {found.Mouth.y:0.000} is no mouth" : "not found in the texture")}: " +
                          $"the line at ({line.x:0.000}, {line.y:0.000}), {lineWidth * 100f:0.0} cm");
                found.HasMouth = true;
                found.Mouth = line;
                found.MouthWidth = lineWidth;
                rig.MouthMoved = true;
            }
            else if (!plausible && !found.MouthHinted)
            {
                // Nothing like a mouth there: where a mouth usually is (the jaw can still drop to talk).
                found.HasMouth = true;
                found.Mouth = new Vector2(eyesAt.x, eyesAt.y - 0.95f * spacing);
                found.MouthWidth = 0.4f * spacing;
                found.MouthHinted = true; // (a straight line: no lips to follow)
                rig.MouthMoved = true;
                Debug.LogWarning($"[Build] face {file}: no mouth on the face: guessed at y {found.Mouth.y:0.000}");
            }
            if (found.HasMouth) FitMouth(rig, found);
            FitBrows(rig, found);
            SaveFitPicture(rig);
            return rig;
        }

        /// <summary>
        /// The whites of the eyes on the face image: pale, grey-ish texels that aren't skin, grouped into patches (an iris
        /// splits a white in two: those join up), kept where there is skin round them (not pale hair or a white collar)
        /// and something dark right by them (an iris, the lashes); then the best mirrored pair about the middle of the
        /// face. One alone (an eye patch): its pair is it mirrored (and not seen).
        /// </summary>
        private static bool FindEyeWhites(FaceImage img, FoundFace found, out Rect left, out Rect right, out bool seeL, out bool seeR, out string note)
        {
            left = right = default;
            seeL = seeR = false;
            note = "";
            float px = img.Px;
            Color skin = found.Skin;
            bool Pale(int k)
            {
                if (!img.Has(k)) return false;
                Color c = img.C[k];
                float hi = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), lo = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                return hi - lo < 0.24f && Luma(c) > 0.62f && Distance(c, skin) > 0.16f; // (bright: a grey beard or grey hair is darker)
            }
            // Patches of texels passing a test (8-connected), skipping specks.
            List<Rect> Patches(Func<int, bool> test)
            {
                var seen = new bool[img.W * img.H];
                var result = new List<Rect>();
                var stack = new Stack<int>();
                for (int start = 0; start < seen.Length; start++)
                {
                    if (seen[start] || !test(start)) continue;
                    seen[start] = true;
                    stack.Push(start);
                    int count = 0, i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
                    while (stack.Count > 0)
                    {
                        int k = stack.Pop();
                        int i = k % img.W, j = k / img.W;
                        count++;
                        i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i); j0 = Mathf.Min(j0, j); j1 = Mathf.Max(j1, j);
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int ni = i + di, nj = j + dj;
                                if (!img.In(ni, nj)) continue;
                                int nk = img.K(ni, nj);
                                if (seen[nk] || !test(nk)) continue;
                                seen[nk] = true;
                                stack.Push(nk);
                            }
                    }
                    if (count < 12) continue;
                    result.Add(Rect.MinMaxRect(img.X(i0) - px * 0.5f, img.Y(j0) - px * 0.5f, img.X(i1) + px * 0.5f, img.Y(j1) + px * 0.5f));
                }
                return result;
            }
            List<Rect> patches = Patches(Pale);
            // Patches close together (either side of an iris) are one eye.
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int a = 0; a < patches.Count && !merged; a++)
                    for (int b = a + 1; b < patches.Count && !merged; b++)
                    {
                        Rect A = patches[a], B = patches[b];
                        float gapX = Mathf.Max(0f, Mathf.Max(A.xMin, B.xMin) - Mathf.Min(A.xMax, B.xMax));
                        float gapY = Mathf.Max(0f, Mathf.Max(A.yMin, B.yMin) - Mathf.Min(A.yMax, B.yMax));
                        if (gapX > 0.016f || gapY > 0.003f) continue;
                        Rect union = Rect.MinMaxRect(Mathf.Min(A.xMin, B.xMin), Mathf.Min(A.yMin, B.yMin), Mathf.Max(A.xMax, B.xMax), Mathf.Max(A.yMax, B.yMax));
                        if (union.width > 0.08f || union.height > 0.05f) continue;
                        patches[a] = union;
                        patches.RemoveAt(b);
                        merged = true;
                    }
            }
            // Eye-like: the size of an eye, skin round it, something dark by it.
            float Share(Rect box, Func<Color, bool> test, Rect hole = default)
            {
                int all = 0, hit = 0;
                for (int j = Mathf.Max(0, img.J(box.yMin)); j <= Mathf.Min(img.H - 1, img.J(box.yMax)); j += 2)
                    for (int i = Mathf.Max(0, img.I(box.xMin)); i <= Mathf.Min(img.W - 1, img.I(box.xMax)); i += 2)
                    {
                        if (hole.width > 0f && hole.Contains(new Vector2(img.X(i), img.Y(j)))) continue;
                        int k = img.K(i, j);
                        if (!img.Has(k)) continue;
                        all++;
                        if (test(img.C[k])) hit++;
                    }
                return all > 0 ? hit / (float)all : 0f;
            }
            Rect Grow(Rect r, float fx, float fy, float add) =>
                Rect.MinMaxRect(r.xMin - r.width * fx - add, r.yMin - r.height * fy - add, r.xMax + r.width * fx + add, r.yMax + r.height * fy + add);
            var eyes = new List<(Rect box, float score)>();
            foreach (Rect p in patches)
            {
                if (p.width < 0.004f || p.width > 0.08f || p.height < 0.002f || p.height > 0.05f) continue;
                float dark = Share(Grow(p, 0.3f, 0.3f, 0.001f), c => Luma(c) < 0.3f);
                float skinRound = Share(Grow(p, 0.5f, 0.5f, 0.004f), c => Distance(c, skin) < 0.22f, Grow(p, 0.2f, 0.2f, 0.001f));
                if (dark < 0.04f || skinRound < 0.25f) continue;
                eyes.Add((p, skinRound + Mathf.Min(dark * 3f, 1f)));
            }
            // Small eyes with little white showing: the dark of the iris, with skin all round it (scored under any whites).
            foreach (Rect p in Patches(k => img.Has(k) && Luma(img.C[k]) < 0.28f))
            {
                if (p.width < 0.004f || p.width > 0.035f || p.height < 0.003f || p.height > 0.025f || p.width > p.height * 2.5f) continue;
                float skinRound = Share(Grow(p, 0.6f, 0.6f, 0.003f), c => Distance(c, skin) < 0.22f, Grow(p, 0.25f, 0.25f, 0.001f));
                if (skinRound < 0.4f) continue;
                eyes.Add((Grow(p, 0.35f, 0.08f, 0.001f), skinRound * 0.8f));
            }
            // The best pair: level, either side of the middle, alike in size.
            float best = float.MinValue;
            foreach ((Rect a, float sa) in eyes)
                foreach ((Rect b, float sb) in eyes)
                {
                    if (a.center.x >= b.center.x) continue;
                    float apart = b.center.x - a.center.x, offCentre = Mathf.Abs(a.center.x + b.center.x) * 0.5f;
                    float level = Mathf.Abs(a.center.y - b.center.y), ratio = Mathf.Max(a.width, b.width) / Mathf.Max(1e-4f, Mathf.Min(a.width, b.width));
                    if (apart < 0.035f || apart > 0.2f || offCentre > 0.06f || level > 0.015f || ratio > 2.6f) continue;
                    float score = sa + sb - offCentre * 15f - level * 60f - (ratio - 1f) * 0.5f;
                    if (score <= best) continue;
                    best = score;
                    left = a;
                    right = b;
                }
            if (best > float.MinValue)
            {
                seeL = found.SeeL;
                seeR = found.SeeR;
                note = $"{left.width * 100f:0.0} x {left.height * 100f:0.0} cm whites at ({left.center.x:0.000}, {left.center.y:0.000}) / ({right.center.x:0.000}, {right.center.y:0.000})";
                return true;
            }
            // One eye (the other under a patch, as BuildFace found): the best one off the middle, its pair mirrored.
            if (found.SeeL == found.SeeR) return false;
            (Rect box, float score) single = eyes.Where(e => Mathf.Abs(e.box.center.x) > 0.015f && Mathf.Abs(e.box.center.x) < 0.08f &&
                                                             Mathf.Abs(e.box.center.y - found.EyeLevel) < 0.03f).OrderByDescending(e => e.score).FirstOrDefault();
            if (single.box.width <= 0f) return false;
            Rect mirrored = Rect.MinMaxRect(-single.box.xMax, single.box.yMin, -single.box.xMin, single.box.yMax);
            bool isLeft = single.box.center.x < 0f;
            left = isLeft ? single.box : mirrored;
            right = isLeft ? mirrored : single.box;
            seeL = isLeft;
            seeR = !isLeft;
            note = $"one eye ({(isLeft ? "left" : "right")}, {single.box.width * 100f:0.0} x {single.box.height * 100f:0.0} cm whites), the other hidden";
            return true;
        }

        private static FaceImage RasterFace(Vector3[] v, Vector3[] n, Vector2[] uv, int[] tris, Color32[] tex, int size, Rect region, float px)
        {
            var img = new FaceImage { X0 = region.xMin, Y0 = region.yMin, Px = px, W = Mathf.CeilToInt(region.width / px), H = Mathf.CeilToInt(region.height / px) };
            img.C = new Color[img.W * img.H];
            img.Z = new float[img.W * img.H];
            for (int k = 0; k < img.Z.Length; k++) img.Z[k] = float.MinValue;
            Color Texel(int x, int y) => tex[Mathf.Clamp(y, 0, size - 1) * size + Mathf.Clamp(x, 0, size - 1)];
            Color Sample(Vector2 at)
            {
                float x = Mathf.Repeat(at.x, 1f) * size - 0.5f, y = Mathf.Repeat(at.y, 1f) * size - 0.5f;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                return Color.Lerp(Color.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), fx), Color.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), fx), fy);
            }
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Vector3 A = v[a], B = v[b], C = v[c];
                if (A.y < region.yMin && B.y < region.yMin && C.y < region.yMin) continue;
                if (A.y > region.yMax && B.y > region.yMax && C.y > region.yMax) continue;
                if ((n[a] + n[b] + n[c]).z <= 0.05f) continue; // the back of the head, the insides
                int i0 = Mathf.Max(0, img.I(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), i1 = Mathf.Min(img.W - 1, img.I(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
                int j0 = Mathf.Max(0, img.J(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), j1 = Mathf.Min(img.H - 1, img.J(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                if (i0 > i1 || j0 > j1) continue;
                float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                if (Mathf.Abs(area) < 1e-12f) continue;
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        float px_ = img.X(i) - A.x, py = img.Y(j) - A.y;
                        float wb = (px_ * (C.y - A.y) - (C.x - A.x) * py) / area;
                        float wc = ((B.x - A.x) * py - px_ * (B.y - A.y)) / area;
                        float wa = 1f - wb - wc;
                        if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                        float z = A.z * wa + B.z * wb + C.z * wc;
                        int k = img.K(i, j);
                        if (z <= img.Z[k]) continue;
                        img.Z[k] = z;
                        img.C[k] = Sample(uv[a] * wa + uv[b] * wb + uv[c] * wc);
                    }
            }
            return img;
        }

        private static Color MedianColor(List<Color> colors) =>
            new(Median(colors.Select(c => c.r)), Median(colors.Select(c => c.g)), Median(colors.Select(c => c.b)), 1f);


        /// <summary>
        /// The painted eye round its whites: every texel that isn't the skin round it (whites, iris, lashes, liner),
        /// joined to the whites, at the face's depth there (not hair beside it), inside a window a little bigger than the
        /// whites (more above, for the lashes; so a brow is never taken in). Its upper and lower edge per column, smoothed
        /// and grown a touch. An oval if that fails.
        /// </summary>
        private static EyeFit FitEye(FaceImage img, Rect eye, Color skin, string name, EyeShape shape = null)
        {
            float px = img.Px;
            Rect win = Rect.MinMaxRect(eye.xMin - eye.width * 0.2f - 0.0015f, eye.yMin - eye.height * 0.45f - 0.0015f,
                eye.xMax + eye.width * 0.2f + 0.0015f, eye.yMax + eye.height * 0.45f + 0.0015f);
            int wi0 = Mathf.Max(0, img.I(win.xMin)), wi1 = Mathf.Min(img.W - 1, img.I(win.xMax));
            int wj0 = Mathf.Max(0, img.J(win.yMin)), wj1 = Mathf.Min(img.H - 1, img.J(win.yMax));
            var fit = new EyeFit();
            float depth = img.FrontZ(eye.center.x, eye.center.y);
            if (float.IsNaN(depth)) depth = img.FrontZ(eye.xMin + eye.width * 0.2f, eye.center.y);

            // The skin round it: a band just outside the window, as close to the face's skin as can be.
            int band = Mathf.RoundToInt(0.004f / px);
            var ring = new List<Color>();
            for (int j = wj0 - band; j <= wj1 + band; j += 2)
                for (int i = wi0 - band; i <= wi1 + band; i += 2)
                {
                    if (!img.In(i, j) || (i >= wi0 && i <= wi1 && j >= wj0 && j <= wj1)) continue;
                    int k = img.K(i, j);
                    if (img.Has(k) && Distance(img.C[k], skin) < 0.25f) ring.Add(img.C[k]);
                }
            fit.LocalSkin = ring.Count >= 30 ? MedianColor(ring) : skin;
            fit.Threshold = 0.12f;
            if (ring.Count >= 30)
            {
                List<float> spread = ring.Select(c => Distance(c, fit.LocalSkin)).OrderBy(d => d).ToList();
                fit.Threshold = Mathf.Clamp(Percentile(spread, 0.9f) * 1.5f + 0.05f, 0.09f, 0.22f);
            }
            bool NotSkin(int i, int j)
            {
                int k = img.K(i, j);
                return img.Has(k) && Distance(img.C[k], fit.LocalSkin) > fit.Threshold && (float.IsNaN(depth) || Mathf.Abs(img.Z[k] - depth) < 0.012f);
            }

            if (shape != null)
            {
                // A look-alike: its base model's eye, carried over to where its whites are (its own paint can have
                // eyeshadow round the eye that would join on).
                ShapeLike(img, eye, shape, fit);
                return Finish();
            }

            // Flood out from the whites over everything that isn't skin.
            int w = wi1 - wi0 + 1, h = wj1 - wj0 + 1;
            var mark = new bool[w * h];
            var stack = new Stack<(int i, int j)>();
            for (int j = Mathf.Max(wj0, img.J(eye.yMin)); j <= Mathf.Min(wj1, img.J(eye.yMax)); j++)
                for (int i = Mathf.Max(wi0, img.I(eye.xMin)); i <= Mathf.Min(wi1, img.I(eye.xMax)); i++)
                    if (NotSkin(i, j)) { mark[(j - wj0) * w + (i - wi0)] = true; stack.Push((i, j)); }
            int marked = stack.Count;
            while (stack.Count > 0)
            {
                (int i, int j) = stack.Pop();
                foreach ((int di, int dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int ni = i + di, nj = j + dj;
                    if (ni < wi0 || ni > wi1 || nj < wj0 || nj > wj1) continue;
                    int m = (nj - wj0) * w + (ni - wi0);
                    if (mark[m] || !NotSkin(ni, nj)) continue;
                    mark[m] = true;
                    marked++;
                    stack.Push((ni, nj));
                }
            }

            // Its edges, column by column.
            var top = new float[w];
            var bottom = new float[w];
            int first = -1, last = -1;
            for (int c = 0; c < w; c++)
            {
                top[c] = bottom[c] = float.NaN;
                for (int r = 0; r < h; r++)
                {
                    if (!mark[r * w + c]) continue;
                    float y = img.Y(wj0 + r);
                    if (float.IsNaN(bottom[c])) bottom[c] = y;
                    top[c] = y;
                }
                if (!float.IsNaN(top[c])) { if (first < 0) first = c; last = c; }
            }
            bool ok = marked >= 30 && first >= 0;
            if (ok)
            {
                float width = (last - first + 1) * px, height = top.Where(v => !float.IsNaN(v)).Max() - bottom.Where(v => !float.IsNaN(v)).Min();
                ok = width > eye.width * 0.8f && height > eye.height * 0.8f;
            }
            if (ok)
            {
                // Gaps filled in, then smoothed (a median against odd texels, then a little blur).
                int n = last - first + 1;
                float[] t = new float[n], b = new float[n];
                for (int c = 0; c < n; c++) { t[c] = top[first + c]; b[c] = bottom[first + c]; }
                FillGaps(t);
                FillGaps(b);
                // No spikes: a shade over the lid or under a hat brim joined on is cut back to the eye's usual height.
                List<float> middleTops = t.Skip(n / 5).Take(Mathf.Max(1, n * 3 / 5)).OrderBy(v => v).ToList(), middleBottoms = b.Skip(n / 5).Take(Mathf.Max(1, n * 3 / 5)).OrderBy(v => v).ToList();
                float midTop = Percentile(middleTops, 0.35f), midBottom = Percentile(middleBottoms, 0.65f);
                float spread = Mathf.Max(midTop - midBottom, 0.002f) * 0.3f;
                float roof = eye.yMax + eye.height * 0.18f + 0.001f, floor = eye.yMin - eye.height * 0.18f - 0.001f; // (lashes, not a brow or a brim)
                for (int c = 0; c < n; c++) { t[c] = Mathf.Min(t[c], Mathf.Min(midTop + spread, roof)); b[c] = Mathf.Max(b[c], Mathf.Max(midBottom - spread, floor)); }
                t = Smooth(Median(t, 5), 7);
                b = Smooth(Median(b, 5), 7);
                // An eye's lower edge bows down: no bumps up into it (the iris dipping to it cut the corners' whites off).
                int dip = Mathf.Max(2, n / 6);
                var lowest = new float[n];
                for (int c = 0; c < n; c++) { lowest[c] = b[c]; for (int k = Mathf.Max(0, c - dip); k <= Mathf.Min(n - 1, c + dip); k++) lowest[c] = Mathf.Min(lowest[c], b[k] + Mathf.Abs(k - c) * px * 0.35f); }
                b = Smooth(lowest, 4);
                // Grown a little all round (the soft edge of the paint), the ends coming to a point.
                const int grow = 4; // (0.6 mm: the soft rim of the paint)
                fit.I0 = wi0 + first - grow;
                fit.I1 = wi0 + last + grow;
                fit.Top = new float[n + 2 * grow];
                fit.Bottom = new float[n + 2 * grow];
                for (int c = 0; c < fit.Top.Length; c++)
                {
                    int s = Mathf.Clamp(c - grow, 0, n - 1);
                    bool end = c < grow || c >= n + grow;
                    float middle = (t[s] + b[s]) * 0.5f;
                    fit.Top[c] = (end ? middle : t[s]) + grow * px;
                    fit.Bottom[c] = (end ? middle : b[s]) - grow * px;
                }
                fit.Top = Smooth(fit.Top, 1);
                fit.Bottom = Smooth(fit.Bottom, 1);
            }
            else
            {
                // An oval round the eye found.
                fit.Fallback = true;
                float hx = eye.width * 0.62f + 0.001f, hy = eye.height * 0.75f + 0.0015f; // (the lashes round the whites)
                fit.I0 = img.I(eye.center.x - hx);
                fit.I1 = img.I(eye.center.x + hx);
                fit.Top = new float[fit.I1 - fit.I0 + 1];
                fit.Bottom = new float[fit.Top.Length];
                for (int c = 0; c < fit.Top.Length; c++)
                {
                    float u = Mathf.Clamp((img.X(fit.I0 + c) - eye.center.x) / hx, -1f, 1f);
                    float half = hy * Mathf.Sqrt(1f - u * u);
                    fit.Top[c] = eye.center.y + half;
                    fit.Bottom[c] = eye.center.y - half;
                }
                Debug.LogWarning($"[Build] face {name}: the painted eye couldn't be cut out ({marked} texels): an oval instead");
            }

            return Finish();

            // Its colours (the whites, the lashes) and its bounds.
            EyeFit Finish()
            {
                var inside = new List<Color>();
                for (int i = fit.I0; i <= fit.I1; i++)
                    for (int j = img.J(fit.BottomAt(i)); j <= img.J(fit.TopAt(i)); j++)
                        if (img.In(i, j) && img.Has(img.K(i, j))) inside.Add(img.C[img.K(i, j)]);
                List<Color> whites = inside.Where(White).ToList();
                fit.Sclera = whites.Count >= 10 ? MedianColor(whites) : new Color(0.93f, 0.93f, 0.92f);
                List<Color> darkest = inside.OrderBy(Luma).Take(Mathf.Max(1, inside.Count / 12)).ToList();
                fit.Lash = darkest.Count > 0 ? MedianColor(darkest) * 0.85f : new Color(0.08f, 0.06f, 0.06f);
                fit.Lash.a = 1f;
                fit.Cx = (img.X(fit.I0) + img.X(fit.I1)) * 0.5f;
                fit.Hx = (fit.I1 - fit.I0 + 1) * px * 0.5f;
                float hi = fit.Top.Max(), lo = fit.Bottom.Min();
                fit.Cy = (hi + lo) * 0.5f;
                fit.Hy = (hi - lo) * 0.5f;
                return fit;
            }
        }

        /// <summary>A base model's cut-out eye, and its whites (for its look-alikes: MeshyCharacters.FaceRig.cs).</summary>
        private sealed class EyeShape
        {
            public FaceImage Img;
            public EyeFit Fit;
            public Rect Whites;
        }

        private static readonly Dictionary<string, (EyeShape l, EyeShape r)> BaseEyes = new();


        /// <summary>An eye's outline mirrored about x = 0 (same face image).</summary>
        private static EyeFit MirroredEye(FaceImage img, EyeFit e)
        {
            var m = new EyeFit { I0 = img.I(-img.X(e.I1)), I1 = img.I(-img.X(e.I0)) };
            m.Top = new float[m.I1 - m.I0 + 1];
            m.Bottom = new float[m.Top.Length];
            for (int c = 0; c < m.Top.Length; c++)
            {
                int at = img.I(-img.X(m.I0 + c));
                m.Top[c] = e.TopAt(at);
                m.Bottom[c] = e.BottomAt(at);
            }
            return m;
        }
        /// <summary>The base model's eye outline, moved and scaled from its whites onto these whites.</summary>
        private static void ShapeLike(FaceImage img, Rect eye, EyeShape shape, EyeFit fit)
        {
            float sx = eye.width / Mathf.Max(shape.Whites.width, 1e-4f), sy = eye.height / Mathf.Max(shape.Whites.height, 1e-4f);
            float X(float baseX) => eye.center.x + (baseX - shape.Whites.center.x) * sx;
            float BaseX(float x) => shape.Whites.center.x + (x - eye.center.x) / sx;
            float Y(float baseY) => eye.center.y + (baseY - shape.Whites.center.y) * sy;
            fit.I0 = img.I(X(shape.Img.X(shape.Fit.I0)));
            fit.I1 = Mathf.Max(fit.I0 + 2, img.I(X(shape.Img.X(shape.Fit.I1))));
            fit.Top = new float[fit.I1 - fit.I0 + 1];
            fit.Bottom = new float[fit.Top.Length];
            for (int c = 0; c < fit.Top.Length; c++)
            {
                int at = shape.Img.I(BaseX(img.X(fit.I0 + c)));
                fit.Top[c] = Y(shape.Fit.TopAt(at));
                fit.Bottom[c] = Y(shape.Fit.BottomAt(at));
            }
        }

        /// <summary>
        /// The shape of a shut lid: over the eye (and 2 mm round it) the face's depth is filled in smoothly from the
        /// skin all round it, rounded out a little over the eyeball. A modelled eye (bulging, sunk in a socket, with
        /// lashes standing out) is pressed into that shape as the lid shuts (a shape key), so the shut lid is skin
        /// lying on the face, not paint on a hollow.
        /// </summary>
        private static void FitShutShape(FaceImage img, EyeFit e)
        {
            float px = img.Px;
            int grow = Mathf.RoundToInt(0.002f / px);
            int i0 = Mathf.Max(0, e.I0 - grow - 3), i1 = Mathf.Min(img.W - 1, e.I1 + grow + 3);
            int j0 = Mathf.Max(0, img.J(e.Bottom.Min()) - grow - 3), j1 = Mathf.Min(img.H - 1, img.J(e.MaxTop) + grow + 3);
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            var z = new float[w * h];
            var weight = new float[w * h];
            var unknown = new bool[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int ii = i0 + i, k = j * w + i;
                    float y = img.Y(j0 + j), top = e.TopAt(ii), bottom = e.BottomAt(ii);
                    float dy = y > top ? (y - top) / px : y < bottom ? (bottom - y) / px : 0f;
                    float dx = ii < e.I0 ? e.I0 - ii : ii > e.I1 ? ii - e.I1 : 0f;
                    float outside = Mathf.Max(dx, dy);
                    weight[k] = Mathf.Clamp01(1f - outside / grow);
                    unknown[k] = outside < grow;
                    int ik = img.K(ii, j0 + j);
                    z[k] = img.Has(ik) ? img.Z[ik] : float.NaN;
                }
            // Holes in what is known (no face there): their neighbours' depth.
            for (int k = 0; k < z.Length; k++)
                if (!unknown[k] && float.IsNaN(z[k])) unknown[k] = true;
            // Filled in smoothly from the skin round it (Laplace's equation).
            FillSmooth(new[] { z }, unknown, w, h);
            // Rounded over the eyeball.
            float bulge = Mathf.Clamp(e.Hy * 0.2f, 0.0008f, 0.003f);
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float u = (img.X(i0 + i) - e.Cx) / Mathf.Max(e.Hx, 1e-4f), v = (img.Y(j0 + j) - e.Cy) / Mathf.Max(e.Hy, 1e-4f);
                    z[j * w + i] += bulge * Mathf.Max(0f, 1f - u * u - v * v);
                }
            e.ShutZ = z;
            e.ShutWeight = weight;
            e.ZI0 = i0;
            e.ZJ0 = j0;
            e.ZW = w;
            e.ZH = h;
        }

        /// <summary>The shut lid's depth, slope and weight at (x, y) (weight 0: not on this eye).</summary>
        private static float ShutAt(FaceImage img, EyeFit e, float x, float y, out float z, out Vector2 slope)
        {
            z = 0f;
            slope = Vector2.zero;
            if (e?.ShutZ == null) return 0f;
            float fx = (x - img.X(e.ZI0)) / img.Px, fy = (y - img.Y(e.ZJ0)) / img.Px;
            if (fx < 0f || fy < 0f || fx > e.ZW - 1.001f || fy > e.ZH - 1.001f) return 0f;
            int i = Mathf.Min((int)fx, e.ZW - 2), j = Mathf.Min((int)fy, e.ZH - 2);
            float tx = fx - i, ty = fy - j;
            float Bilinear(float[] a) => Mathf.Lerp(Mathf.Lerp(a[j * e.ZW + i], a[j * e.ZW + i + 1], tx), Mathf.Lerp(a[(j + 1) * e.ZW + i], a[(j + 1) * e.ZW + i + 1], tx), ty);
            z = Bilinear(e.ShutZ);
            slope = new Vector2(
                (Mathf.Lerp(e.ShutZ[j * e.ZW + i + 1], e.ShutZ[(j + 1) * e.ZW + i + 1], ty) - Mathf.Lerp(e.ShutZ[j * e.ZW + i], e.ShutZ[(j + 1) * e.ZW + i], ty)) / img.Px,
                (Mathf.Lerp(e.ShutZ[(j + 1) * e.ZW + i], e.ShutZ[(j + 1) * e.ZW + i + 1], tx) - Mathf.Lerp(e.ShutZ[j * e.ZW + i], e.ShutZ[j * e.ZW + i + 1], tx)) / img.Px);
            return Bilinear(e.ShutWeight);
        }

        /// <summary>
        /// Fills the unknown cells of a grid smoothly from the known ones round them (Laplace's equation, relaxed): on
        /// grids halved again and again first (a hole a few hundred cells across never settles on the full grid), each
        /// finer one starting from the coarser one's answer.
        /// </summary>
        private static void FillSmooth(float[][] channels, bool[] unknown, int w, int h)
        {
            if (w * h > 1024 && w > 8 && h > 8)
            {
                int cw = (w + 1) / 2, ch = (h + 1) / 2;
                var coarseUnknown = new bool[cw * ch];
                float[][] coarse = channels.Select(_ => new float[cw * ch]).ToArray();
                for (int j = 0; j < ch; j++)
                    for (int i = 0; i < cw; i++)
                    {
                        int count = 0;
                        for (int dj = 0; dj < 2; dj++)
                            for (int di = 0; di < 2; di++)
                            {
                                int fi = 2 * i + di, fj = 2 * j + dj;
                                if (fi >= w || fj >= h || unknown[fj * w + fi]) continue;
                                for (int c = 0; c < channels.Length; c++) coarse[c][j * cw + i] += channels[c][fj * w + fi];
                                count++;
                            }
                        coarseUnknown[j * cw + i] = count == 0;
                        if (count > 0) for (int c = 0; c < channels.Length; c++) coarse[c][j * cw + i] /= count;
                    }
                FillSmooth(coarse, coarseUnknown, cw, ch);
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                        if (unknown[j * w + i])
                            for (int c = 0; c < channels.Length; c++) channels[c][j * w + i] = coarse[c][(j / 2) * cw + i / 2];
            }
            else
            {
                for (int c = 0; c < channels.Length; c++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int k = 0; k < unknown.Length; k++) if (!unknown[k]) { sum += channels[c][k]; count++; }
                    float mean = count > 0 ? sum / count : 0f;
                    for (int k = 0; k < unknown.Length; k++) if (unknown[k]) channels[c][k] = mean;
                }
            }
            for (int iteration = 0; iteration < 60; iteration++)
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        int k = j * w + i;
                        if (!unknown[k]) continue;
                        int n = (i > 0 ? 1 : 0) + (i < w - 1 ? 1 : 0) + (j > 0 ? 1 : 0) + (j < h - 1 ? 1 : 0);
                        foreach (float[] v in channels)
                        {
                            float sum = (i > 0 ? v[k - 1] : 0f) + (i < w - 1 ? v[k + 1] : 0f) + (j > 0 ? v[k - w] : 0f) + (j < h - 1 ? v[k + w] : 0f);
                            v[k] += (sum / n - v[k]) * 1.6f;
                        }
                    }
        }

        private static void FillGaps(float[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!float.IsNaN(values[i])) continue;
                int a = i - 1, b = i + 1;
                while (b < values.Length && float.IsNaN(values[b])) b++;
                float va = a >= 0 ? values[a] : values[b], vb = b < values.Length ? values[b] : va;
                for (int k = i; k < b && k < values.Length; k++) values[k] = a >= 0 && b < values.Length ? Mathf.Lerp(va, vb, (k - a) / (float)(b - a)) : (a >= 0 ? va : vb);
                i = b - 1;
            }
        }

        private static float[] Median(float[] values, int radius)
        {
            var result = new float[values.Length];
            var window = new List<float>();
            for (int i = 0; i < values.Length; i++)
            {
                window.Clear();
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++) window.Add(values[k]);
                window.Sort();
                result[i] = window[window.Count / 2];
            }
            return result;
        }

        private static float[] Smooth(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++) { sum += values[k]; count++; }
                result[i] = sum / count;
            }
            return result;
        }

        /// <summary>
        /// The two eye textures, over both eyes and 3 mm round them: the eyes as painted (alpha: the eye's mask, soft at
        /// its edge) and with lids (each eye filled in smoothly from the skin all round it; alpha: how high up the eye
        /// each texel is, 0 its lower edge, 1 its upper, stored as (t + 0.5) / 2).
        /// </summary>
        private static void MakeEyeTextures(FaceRig rig)
        {
            FaceImage img = rig.Image;
            float px = img.Px;
            int margin = Mathf.RoundToInt(0.003f / px);
            EyeFit[] eyes = new[] { rig.L, rig.R }.Where(e => e != null).ToArray();
            int i0 = Mathf.Max(0, eyes.Min(e => e.I0) - margin), i1 = Mathf.Min(img.W - 1, eyes.Max(e => e.I1) + margin);
            int j0 = Mathf.Max(0, img.J(eyes.Min(e => e.Bottom.Min())) - margin), j1 = Mathf.Min(img.H - 1, img.J(eyes.Max(e => e.MaxTop)) + margin);
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            var color = new Color[w * h];
            var known = new bool[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = img.K(i0 + i, j0 + j);
                    color[j * w + i] = img.C[k];
                    known[j * w + i] = img.Has(k);
                }
            // Texels with no face behind them (rare: between the lashes and the lid) take their neighbours' colour.
            for (int pass = 0; pass < 8 && known.Contains(false); pass++)
            {
                var next = (bool[])known.Clone();
                for (int j = 0; j < h; j++)
                    for (int i = 0; i < w; i++)
                    {
                        if (known[j * w + i]) continue;
                        Color sum = Color.clear;
                        int count = 0;
                        for (int dj = -1; dj <= 1; dj++)
                            for (int di = -1; di <= 1; di++)
                            {
                                int ni = i + di, nj = j + dj;
                                if (ni < 0 || nj < 0 || ni >= w || nj >= h || !known[nj * w + ni]) continue;
                                sum += color[nj * w + ni];
                                count++;
                            }
                        if (count == 0) continue;
                        color[j * w + i] = sum / count;
                        next[j * w + i] = true;
                    }
                known = next;
            }

            var mask = new float[w * h];
            var height = new float[w * h];
            var inside = new bool[w * h];
            var fill = new bool[w * h]; // filled in for the lids: the eye, and anything not skin just round it
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int ii = i0 + i;
                    float x = img.X(ii), y = img.Y(j0 + j);
                    EyeFit e = x < rig.MidX ? rig.L : rig.R;
                    int k = j * w + i;
                    if (e == null) { height[k] = -0.5f; continue; }
                    float top = e.TopAt(ii), bottom = e.BottomAt(ii);
                    height[k] = (y - bottom) / Mathf.Max(top - bottom, px);
                    float dy = y > top ? (y - top) / px : y < bottom ? (bottom - y) / px : 0f;
                    float dx = ii < e.I0 ? e.I0 - ii : ii > e.I1 ? ii - e.I1 : 0f;
                    inside[k] = dx <= 0f && dy <= 0f;
                    // Filled from what is round it, all but paler than the skin (pale halos round painted lashes and pale bags
                    // made grey lids) or near black (a lash wing): eyeshadow is kept, the lid carries it down.
                    float l = Luma(color[k]), skinLuma = Luma(e.LocalSkin);
                    fill[k] = inside[k] || (l > skinLuma + 0.05f && Distance(color[k], e.LocalSkin) > 0.08f) || l < 0.15f;
                    mask[k] = Mathf.Clamp01(1f - Mathf.Max(dx, dy) / 2.5f);
                }

            // The lids: each eye filled in smoothly from the skin all round it (a membrane: Laplace's equation).
            var channels = new[] { color.Select(c => c.r).ToArray(), color.Select(c => c.g).ToArray(), color.Select(c => c.b).ToArray() };
            FillSmooth(channels, fill, w, h);
            var lid = new Color[w * h];
            for (int k = 0; k < lid.Length; k++) lid[k] = new Color(channels[0][k], channels[1][k], channels[2][k], 1f);

            var open = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var shut = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var openPixels = new Color32[w * h];
            var shutPixels = new Color32[w * h];
            for (int k = 0; k < w * h; k++)
            {
                Color o = color[k], s = lid[k];
                o.a = mask[k];
                s.a = Mathf.Clamp01((height[k] + 0.5f) * 0.5f);
                openPixels[k] = o;
                shutPixels[k] = s;
            }
            open.SetPixels32(openPixels);
            shut.SetPixels32(shutPixels);
            rig.Open = SaveFaceTexture(open, $"{OutputDir}/{rig.File}_face_open.png");
            rig.Lid = SaveFaceTexture(shut, $"{OutputDir}/{rig.File}_face_lid.png");
            Object.DestroyImmediate(open);
            Object.DestroyImmediate(shut);
            float x0 = img.X(i0) - px * 0.5f, y0 = img.Y(j0) - px * 0.5f;
            rig.Rect = new Vector4(x0, y0, 1f / (w * px), 1f / (h * px));
        }

        private static Texture2D SaveFaceTexture(Texture2D texture, string path)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The darkest line across the face below the eyes (a mouth painted as a line, or the shade between the lips):
        /// the row most darker than the skin 2 mm above and below it, near where a mouth sits (about as far under the
        /// eyes as they are apart); then how far along it the dark goes.
        /// </summary>
        private static bool FindMouthLine(FaceImage img, Vector2 eyes, float spacing, out Vector2 at, out float width)
        {
            at = default;
            width = 0f;
            float px = img.Px;
            int i0 = Mathf.Max(0, img.I(eyes.x - 0.3f * spacing)), i1 = Mathf.Min(img.W - 1, img.I(eyes.x + 0.3f * spacing));
            int j0 = Mathf.Max(0, img.J(eyes.y - 1.45f * spacing)), j1 = Mathf.Min(img.H - 1, img.J(eyes.y - 0.55f * spacing));
            if (i1 <= i0 || j1 <= j0) return false;
            float LumaAt(int i, int j)
            {
                if (!img.In(i, j)) return float.NaN;
                int k = img.K(i, j);
                return img.Has(k) ? Luma(img.C[k]) : float.NaN;
            }
            var row = new float[j1 - j0 + 1];
            for (int j = j0; j <= j1; j++)
            {
                float sum = 0f;
                int count = 0;
                for (int i = i0; i <= i1; i++)
                {
                    float l = LumaAt(i, j);
                    if (float.IsNaN(l)) continue;
                    sum += l;
                    count++;
                }
                row[j - j0] = count > 0 ? sum / count : float.NaN;
            }
            int band = Mathf.Max(2, Mathf.RoundToInt(0.002f / px));
            float best = 0f;
            int bestJ = -1;
            for (int r = band; r < row.Length - band; r++)
            {
                if (float.IsNaN(row[r]) || float.IsNaN(row[r - band]) || float.IsNaN(row[r + band])) continue;
                float contrast = Mathf.Min(row[r - band], row[r + band]) - row[r]; // (a line: lighter on both sides, not the edge of a moustache)
                float down = (eyes.y - img.Y(j0 + r)) / spacing;
                float score = contrast * Mathf.Max(0.2f, 1f - 0.8f * Mathf.Abs(down - 1f));
                if (score > best) { best = score; bestJ = j0 + r; }
            }
            if (bestJ < 0 || best < 0.01f) return false;
            // Along it, out from the middle while it stays dark (gaps under 1.5 mm let through).
            float Darkness(int i)
            {
                float line = float.MaxValue;
                for (int dj = -2; dj <= 2; dj++) { float l = LumaAt(i, bestJ + dj); if (!float.IsNaN(l)) line = Mathf.Min(line, l); }
                float above = LumaAt(i, bestJ + band * 2), below = LumaAt(i, bestJ - band * 2);
                if (line == float.MaxValue || float.IsNaN(above) || float.IsNaN(below)) return 0f;
                return (above + below) * 0.5f - line;
            }
            int middle = img.I(eyes.x), gap = Mathf.RoundToInt(0.0015f / px), reach = Mathf.RoundToInt(0.5f * spacing / px);
            int left = middle, right = middle;
            for (int i = middle, miss = 0; i > middle - reach && miss <= gap; i--) { if (Darkness(i) > best * 0.4f) { left = i; miss = 0; } else miss++; }
            for (int i = middle, miss = 0; i < middle + reach && miss <= gap; i++) { if (Darkness(i) > best * 0.4f) { right = i; miss = 0; } else miss++; }
            width = (right - left) * px;
            if (width < 0.012f) { width = 0.4f * spacing; left = middle - Mathf.RoundToInt(width * 0.5f / px); right = left + Mathf.RoundToInt(width / px); }
            width = Mathf.Min(width, 0.45f * spacing); // (not the smile creases running on into the cheeks)
            at = new Vector2((img.X(left) + img.X(right)) * 0.5f, img.Y(bestJ));
            return true;
        }

        /// <summary>
        /// The painted lips on the face image (redder and bluer than the skin, or the teeth), round where BuildFace found
        /// the mouth: the patch there and any others close by. Their box, and their upper and lower edge per column.
        /// </summary>
        private static bool FindLips(FaceImage img, FoundFace found, out Rect lips, out int c0, out float[] top, out float[] bottom)
        {
            lips = default;
            c0 = 0;
            top = bottom = null;
            float skinRed = Redness(found.Skin), skinBlue = Blueness(found.Skin);
            Rect search = Rect.MinMaxRect(found.Mouth.x - 0.045f, found.Mouth.y - 0.02f, found.Mouth.x + 0.045f, found.Mouth.y + 0.018f);
            int i0 = Mathf.Max(0, img.I(search.xMin)), i1 = Mathf.Min(img.W - 1, img.I(search.xMax));
            int j0 = Mathf.Max(0, img.J(search.yMin)), j1 = Mathf.Min(img.H - 1, img.J(search.yMax));
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            if (w <= 0 || h <= 0) return false;
            var lip = new bool[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int k = img.K(i0 + i, j0 + j);
                    if (!img.Has(k)) continue;
                    Color c = img.C[k];
                    lip[j * w + i] = (Redness(c) > skinRed + 0.05f && Blueness(c) > skinBlue + 0.02f) || (White(c) && Distance(c, found.Skin) > 0.15f);
                }
            // Patches; the one nearest the mouth found, and those within 6 mm of it.
            var label = new int[w * h];
            var boxes = new List<RectInt>();
            var stack = new Stack<int>();
            for (int s = 0; s < lip.Length; s++)
            {
                if (!lip[s] || label[s] != 0) continue;
                int id = boxes.Count + 1, x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue, count = 0;
                label[s] = id;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int k = stack.Pop(), i = k % w, j = k / w;
                    count++;
                    x0 = Mathf.Min(x0, i); x1 = Mathf.Max(x1, i); y0 = Mathf.Min(y0, j); y1 = Mathf.Max(y1, j);
                    for (int dj = -1; dj <= 1; dj++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = i + di, nj = j + dj;
                            if (ni < 0 || nj < 0 || ni >= w || nj >= h) continue;
                            int nk = nj * w + ni;
                            if (!lip[nk] || label[nk] != 0) continue;
                            label[nk] = id;
                            stack.Push(nk);
                        }
                }
                boxes.Add(count >= 40 ? new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1) : new RectInt(0, 0, 0, 0));
            }
            int near = -1;
            float nearest = float.MaxValue;
            var want = new Vector2(img.I(found.Mouth.x) - i0, img.J(found.Mouth.y) - j0);
            for (int b = 0; b < boxes.Count; b++)
            {
                RectInt r = boxes[b];
                if (r.width == 0 || r.width * img.Px < 0.012f) continue;
                float d = Vector2.Distance(r.center, want) - r.width * 0.25f;
                if (d < nearest) { nearest = d; near = b; }
            }
            if (near < 0) return false;
            int gap = Mathf.RoundToInt(0.006f / img.Px);
            RectInt box = boxes[near];
            var keep = new HashSet<int> { near + 1 };
            for (int b = 0; b < boxes.Count; b++)
            {
                RectInt r = boxes[b];
                if (r.width == 0 || b == near) continue;
                if (r.xMin > box.xMax + gap || r.xMax < box.xMin - gap || r.yMin > box.yMax + gap || r.yMax < box.yMin - gap) continue;
                keep.Add(b + 1);
            }
            int lx0 = int.MaxValue, lx1 = int.MinValue, ly0 = int.MaxValue, ly1 = int.MinValue;
            for (int k = 0; k < label.Length; k++)
            {
                if (!keep.Contains(label[k])) continue;
                int i = k % w, j = k / w;
                lx0 = Mathf.Min(lx0, i); lx1 = Mathf.Max(lx1, i); ly0 = Mathf.Min(ly0, j); ly1 = Mathf.Max(ly1, j);
            }
            float widthM = (lx1 - lx0 + 1) * img.Px;
            if (widthM < 0.015f || widthM > 0.09f) return false;
            c0 = i0 + lx0;
            top = new float[lx1 - lx0 + 1];
            bottom = new float[top.Length];
            for (int i = lx0; i <= lx1; i++)
            {
                top[i - lx0] = bottom[i - lx0] = float.NaN;
                for (int j = ly0; j <= ly1; j++)
                {
                    if (!keep.Contains(label[j * w + i])) continue;
                    float y = img.Y(j0 + j);
                    if (float.IsNaN(bottom[i - lx0])) bottom[i - lx0] = y;
                    top[i - lx0] = y;
                }
            }
            FillGaps(top);
            FillGaps(bottom);
            lips = Rect.MinMaxRect(img.X(i0 + lx0), img.Y(j0 + ly0), img.X(i0 + lx1), img.Y(j0 + ly1));
            return true;
        }

        /// <summary>
        /// Where the lips meet, column by column across the mouth: the darkest path (the line between the lips, or
        /// under the painted teeth) through the lips found, kept near their middle and smooth from one column to the
        /// next. A measured mouth is a straight line where it was measured.
        /// </summary>
        private static void FitMouth(FaceRig rig, FoundFace found)
        {
            FaceImage img = rig.Image;
            float halfWidth = Mathf.Clamp(found.MouthWidth * 0.5f, 0.012f, 0.04f);
            float middleY = found.Mouth.y;
            rig.Cx = found.Mouth.x;
            int lipsC0 = 0;
            float[] lipTop = null, lipBottom = null;
            if (!found.MouthHinted && FindLips(img, found, out Rect lips, out lipsC0, out lipTop, out lipBottom))
            {
                rig.Cx = lips.center.x;
                halfWidth = Mathf.Clamp(lips.width * 0.5f, 0.01f, 0.04f);
                middleY = lips.center.y;
                Debug.Log($"[Build] face {rig.File}: lips {lips.width * 100f:0.0} x {lips.height * 100f:0.0} cm at ({lips.center.x:0.000}, {lips.center.y:0.000})");
            }
            rig.CutHalf = halfWidth * 0.8f;
            if (lipTop != null)
            {
                // Only where the lips are thick enough to part: their thin corners (and smile creases run on from them) stay joined.
                float thickest = lipTop.Zip(lipBottom, (a, b) => a - b).Max();
                int middle = Mathf.Clamp(img.I(rig.Cx) - lipsC0, 0, lipTop.Length - 1), lo = middle, hi = middle;
                while (lo > 0 && lipTop[lo - 1] - lipBottom[lo - 1] > thickest * 0.35f) lo--;
                while (hi < lipTop.Length - 1 && lipTop[hi + 1] - lipBottom[hi + 1] > thickest * 0.35f) hi++;
                float reach = Mathf.Min(rig.Cx - img.X(lipsC0 + lo), img.X(lipsC0 + hi) - rig.Cx);
                rig.CutHalf = Mathf.Clamp(reach * 0.95f, 0.008f, rig.CutHalf);
            }
            int c0 = img.I(rig.Cx - halfWidth - 0.004f), c1 = img.I(rig.Cx + halfWidth + 0.004f);
            int n = c1 - c0 + 1;
            rig.LineX0 = img.X(c0);
            rig.Line = new float[n];
            if (found.MouthHinted)
            {
                for (int c = 0; c < n; c++) rig.Line[c] = found.Mouth.y;
            }
            else
            {
                float MidLip(int i, out float half)
                {
                    half = 0.004f;
                    if (lipTop == null) return middleY;
                    int at = Mathf.Clamp(i - lipsC0, 0, lipTop.Length - 1);
                    half = Mathf.Max(0.001f, (lipTop[at] - lipBottom[at]) * 0.5f);
                    return (lipTop[at] + lipBottom[at]) * 0.5f;
                }
                float span = lipTop != null ? Mathf.Max(0.004f, lipTop.Zip(lipBottom, (a, b) => a - b).Max() * 0.6f) : 0.0035f;
                int r0 = img.J(middleY - span), r1 = img.J(middleY + span), rows = r1 - r0 + 1;
                var cost = new float[n * rows];
                var from = new int[n * rows];
                for (int c = 0; c < n; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        int i = c0 + c, j = r0 + r;
                        float here = img.In(i, j) && img.Has(img.K(i, j)) ? Luma(img.C[img.K(i, j)]) : 1f;
                        float lipMiddle = MidLip(i, out float lipHalf);
                        here += 0.25f * Mathf.Abs(img.Y(j) - lipMiddle) / lipHalf; // (closed painted lips with no dark line: through their middle)
                        float best = 0f;
                        int bestFrom = r;
                        if (c > 0)
                        {
                            best = float.MaxValue;
                            for (int d = -1; d <= 1; d++)
                            {
                                int p = r + d;
                                if (p < 0 || p >= rows) continue;
                                float v = cost[(c - 1) * rows + p] + 0.08f * Mathf.Abs(d);
                                if (v < best) { best = v; bestFrom = p; }
                            }
                        }
                        cost[c * rows + r] = best + here;
                        from[c * rows + r] = bestFrom;
                    }
                int end = 0;
                for (int r = 1; r < rows; r++) if (cost[(n - 1) * rows + r] < cost[(n - 1) * rows + end]) end = r;
                for (int c = n - 1; c >= 0; c--)
                {
                    rig.Line[c] = img.Y(r0 + end);
                    end = from[c * rows + end];
                }
                rig.Line = Smooth(rig.Line, Mathf.RoundToInt(0.0012f / img.Px));
            }
            float lineY = rig.LineAt(rig.Cx);
            float z = img.FrontZ(rig.Cx, lineY);
            if (float.IsNaN(z))
            {
                Debug.LogWarning($"[Build] face {rig.File}: no face at the mouth: it stays shut");
                return;
            }
            rig.Mouth = true;
            rig.MouthZ = z;
            rig.ChinDepth = Mathf.Clamp((found.EyeLevel - lineY) * 0.8f, 0.025f, 0.06f);
        }

        /// <summary>The brows: the first band above each eye that isn't skin (a fringe higher up doesn't count).</summary>
        private static void FitBrows(FaceRig rig, FoundFace found)
        {
            FaceImage img = rig.Image;
            Vector3 Brow(EyeFit e, Rect rect, out float eyeTop)
            {
                eyeTop = e != null ? e.MaxTop : rect.yMax;
                if (e == null) return new Vector3(rect.center.x, rect.yMax + 0.011f, rect.width * 0.6f);
                int m = Mathf.RoundToInt(0.003f / img.Px);
                int r0 = img.J(eyeTop + 0.0025f), r1 = Mathf.Min(img.H - 1, img.J(eyeTop + 0.028f));
                int b0 = -1, b1 = -1;
                for (int j = r0; j <= r1; j++)
                {
                    int all = 0, dark = 0;
                    for (int i = Mathf.Max(0, e.I0 - m); i <= Mathf.Min(img.W - 1, e.I1 + m); i++)
                    {
                        int k = img.K(i, j);
                        if (!img.Has(k)) continue;
                        all++;
                        if (Distance(img.C[k], e.LocalSkin) > e.Threshold) dark++;
                    }
                    float share = all > 0 ? dark / (float)all : 0f;
                    if (b0 < 0) { if (share > 0.3f) b0 = j; }
                    else if (share < 0.15f) { b1 = j; break; }
                }
                float y = b0 >= 0 ? (img.Y(b0) + img.Y(b1 >= 0 ? b1 : Mathf.Min(r1, b0 + 20))) * 0.5f : eyeTop + 0.012f;
                return new Vector3(e.Cx, y, e.Hx * 1.1f);
            }
            rig.BrowL = Brow(rig.L, found.EyeL, out rig.EyeTopL);
            rig.BrowR = Brow(rig.R, found.EyeR, out rig.EyeTopR);
            // One eye under a patch: its brow mirrors the other.
            if (rig.L == null && rig.R != null) { rig.BrowL = new Vector3(2f * rig.MidX - rig.BrowR.x, rig.BrowR.y, rig.BrowR.z); rig.EyeTopL = rig.EyeTopR; }
            if (rig.R == null && rig.L != null) { rig.BrowR = new Vector3(2f * rig.MidX - rig.BrowL.x, rig.BrowL.y, rig.BrowL.z); rig.EyeTopR = rig.EyeTopL; }
        }

        /// <summary>For checking a fit by eye: the face image with the eyes' edges, the shut line, the lips' line and the brows drawn on.</summary>
        private static void SaveFitPicture(FaceRig rig)
        {
            FaceImage img = rig.Image;
            var picture = new Texture2D(img.W, img.H, TextureFormat.RGB24, false);
            var pixels = new Color32[img.W * img.H];
            for (int k = 0; k < pixels.Length; k++) pixels[k] = img.Has(k) ? (Color32)img.C[k] : new Color32(30, 30, 40, 255);
            void Dot(float x, float y, Color32 c)
            {
                int i = img.I(x), j = img.J(y);
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                        if (img.In(i + di, j + dj)) pixels[img.K(i + di, j + dj)] = c;
            }
            foreach (EyeFit e in new[] { rig.L, rig.R })
            {
                if (e == null) continue;
                for (int i = e.I0; i <= e.I1; i++)
                {
                    float x = img.X(i), top = e.TopAt(i), bottom = e.BottomAt(i);
                    Dot(x, top, new Color32(60, 255, 60, 255));
                    Dot(x, bottom, new Color32(60, 255, 60, 255));
                    Dot(x, Mathf.Lerp(bottom, top, AvatarFace.ShutAt), new Color32(255, 230, 40, 255));
                }
            }
            if (rig.Mouth)
                for (float x = rig.Cx - rig.CutHalf; x <= rig.Cx + rig.CutHalf; x += img.Px)
                    Dot(x, rig.LineAt(x), new Color32(40, 230, 255, 255));
            foreach (Vector3 b in new[] { rig.BrowL, rig.BrowR })
                for (float x = b.x - b.z; x <= b.x + b.z; x += img.Px * 2f)
                    Dot(x, b.y, new Color32(255, 50, 50, 255));
            picture.SetPixels32(pixels);
            picture.Apply();
            Directory.CreateDirectory("Screenshots/Faces/Fit");
            File.WriteAllBytes($"Screenshots/Faces/Fit/{rig.File}.png", picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
        }

        // ------------------------------------------------------------------ the mesh

        /// <summary>
        /// Finer triangles round the lips (each in four, up to three times, until under 2.5 mm), the ones next to them
        /// split to match (no cracks): on a coarse face the cut and the jaw would otherwise move whole centimetre-wide
        /// triangles, and the open mouth came out as notches and folds.
        /// </summary>
        private static void RefineMouth(FaceRig rig, MeshParts g)
        {
            bool Around(Vector3 p) => Mathf.Abs(p.x - rig.Cx) < rig.CutHalf + 0.012f && Mathf.Abs(p.y - rig.LineAt(p.x)) < 0.014f && rig.Front(p, 0.02f, 0.03f) > 0.5f; // (a modelled lip groove too)
            int made = 0;
            for (int level = 0; level < 3; level++)
            {
                var mids = new Dictionary<(int, int), int>();
                int Mid(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (mids.TryGetValue(key, out int m)) return m;
                    m = g.V.Count;
                    g.V.Add((g.V[a] + g.V[b]) * 0.5f);
                    g.N.Add((g.N[a] + g.N[b]).normalized);
                    if (g.T != null)
                    {
                        Vector3 dir = ((Vector3)g.T[a] + (Vector3)g.T[b]).normalized;
                        g.T.Add(new Vector4(dir.x, dir.y, dir.z, g.T[a].w));
                    }
                    g.Uv.Add((g.Uv[a] + g.Uv[b]) * 0.5f);
                    g.W.Add(g.W[a]);
                    g.Parents.Add((a, b, 0.5f));
                    mids[key] = m;
                    made++;
                    return m;
                }
                foreach (List<int> tris in g.Tris)
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        Vector3 A = g.V[a], B = g.V[b], C = g.V[c];
                        if (!Around((A + B + C) / 3f)) continue;
                        float longest = Mathf.Max(Vector3.Distance(A, B), Mathf.Max(Vector3.Distance(B, C), Vector3.Distance(C, A)));
                        if (longest < 0.0025f) continue;
                        Mid(a, b); Mid(b, c); Mid(c, a);
                    }
                if (mids.Count == 0) break;
                // Every triangle with a split edge, split to match (all three: in four).
                for (int s = 0; s < g.Tris.Length; s++)
                {
                    List<int> tris = g.Tris[s];
                    var next = new List<int>(tris.Count + mids.Count * 6);
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        bool hasAb = mids.TryGetValue(a < b ? (a, b) : (b, a), out int ab);
                        bool hasBc = mids.TryGetValue(b < c ? (b, c) : (c, b), out int bc);
                        bool hasCa = mids.TryGetValue(c < a ? (c, a) : (a, c), out int ca);
                        if (hasAb && hasBc && hasCa) next.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                        else if (hasAb && hasBc) next.AddRange(new[] { ab, b, bc, a, ab, bc, a, bc, c });
                        else if (hasBc && hasCa) next.AddRange(new[] { ca, bc, c, a, b, bc, a, bc, ca });
                        else if (hasCa && hasAb) next.AddRange(new[] { a, ab, ca, ab, b, c, ab, c, ca });
                        else if (hasAb) next.AddRange(new[] { a, ab, c, ab, b, c });
                        else if (hasBc) next.AddRange(new[] { a, b, bc, a, bc, c });
                        else if (hasCa) next.AddRange(new[] { a, b, ca, ca, b, c });
                        else next.AddRange(new[] { a, b, c });
                    }
                    g.Tris[s] = next;
                }
            }
            if (made > 0) Debug.Log($"[Build] face {rig.File}: {made} vertices added round the lips");
        }

        /// <summary>
        /// Cuts the face along the lips' line (between CutHalf either side of the middle; the corners stay joined),
        /// so the lower lip can drop away from the upper. Each cut triangle becomes three, on new vertices along the
        /// line: one copy for each side.
        /// </summary>
        private static void CutMouth(FaceRig rig, MeshParts g)
        {
            float Below(int v) => rig.LineAt(g.V[v].x) - g.V[v].y; // > 0 under the line
            bool Near(int v)
            {
                Vector3 p = g.V[v];
                return Mathf.Abs(p.x - rig.Cx) < rig.CutHalf + 0.012f && Mathf.Abs(p.y - rig.LineAt(p.x)) < 0.015f && rig.Front(p, 0.02f, 0.03f) > 0.5f; // (the walls of a modelled lip groove too: left joined they stretched into strings)
            }
            var made = new Dictionary<(int, int, bool), int>();
            int Split(int a, int b, bool lower)
            {
                if (a > b) (a, b) = (b, a);
                if (made.TryGetValue((a, b, lower), out int existing)) return existing;
                float da = Below(a), db = Below(b);
                float t = Mathf.Clamp01(da / (da - db));
                int index = g.V.Count;
                g.V.Add(Vector3.Lerp(g.V[a], g.V[b], t));
                g.N.Add(Vector3.Lerp(g.N[a], g.N[b], t).normalized);
                if (g.T != null)
                {
                    Vector4 ta = g.T[a], tb = g.T[b];
                    Vector3 dir = Vector3.Lerp(ta, tb, t).normalized;
                    g.T.Add(new Vector4(dir.x, dir.y, dir.z, ta.w));
                }
                g.Uv.Add(Vector2.Lerp(g.Uv[a], g.Uv[b], t));
                g.W.Add(t < 0.5f ? g.W[a] : g.W[b]);
                g.Parents.Add((a, b, t));
                g.Lower[index] = lower;
                made[(a, b, lower)] = index;
                return index;
            }
            int cuts = 0;
            foreach (List<int> tris in g.Tris)
            {
                int count = tris.Count;
                for (int t = 0; t < count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (!Near(a) || !Near(b) || !Near(c)) continue;
                    bool la = Below(a) > 0f, lb = Below(b) > 0f, lc = Below(c) > 0f;
                    if (la == lb && lb == lc) continue;
                    // The one on its own side, then the other two in the triangle's own order.
                    (int lone, int m1, int m2) = la != lb && la != lc ? (a, b, c) : lb != la && lb != lc ? (b, c, a) : (c, a, b);
                    float dl = Below(lone), d1 = Below(m1), d2 = Below(m2);
                    Vector3 p1 = Vector3.Lerp(g.V[lone], g.V[m1], dl / (dl - d1)), p2 = Vector3.Lerp(g.V[lone], g.V[m2], dl / (dl - d2));
                    bool in1 = Mathf.Abs(p1.x - rig.Cx) <= rig.CutHalf, in2 = Mathf.Abs(p2.x - rig.Cx) <= rig.CutHalf;
                    if (!in1 && !in2) continue;
                    bool low = dl > 0f;
                    if (in1 != in2)
                    {
                        // The slit ends inside this triangle: cut from the edge it comes in by to its end on the line
                        // (one vertex there, shared by both lips), the rest of the triangle whole round that end.
                        int mIn = in1 ? m1 : m2, mOut = in1 ? m2 : m1;
                        float tIn = Below(lone) / (Below(lone) - Below(mIn)), tOut = Below(lone) / (Below(lone) - Below(mOut));
                        Vector3 pIn = in1 ? p1 : p2, pOut = in1 ? p2 : p1;
                        float endX = rig.Cx + Mathf.Sign(pOut.x - rig.Cx) * rig.CutHalf;
                        float s = Mathf.Clamp01((endX - pIn.x) / (pOut.x - pIn.x));
                        int q = g.V.Count;
                        T Mix<T>(List<T> list, Func<T, T, float, T> lerp) => lerp(lerp(list[lone], list[mIn], tIn), lerp(list[lone], list[mOut], tOut), s);
                        g.V.Add(Mix(g.V, Vector3.Lerp));
                        g.N.Add(Mix(g.N, Vector3.Lerp).normalized);
                        if (g.T != null) g.T.Add(Mix(g.T, Vector4.Lerp));
                        g.Uv.Add(Mix(g.Uv, Vector2.Lerp));
                        g.W.Add(g.W[lone]);
                        g.Parents.Add((lone, mIn, tIn));
                        g.Lower[q] = false;
                        int inLone = Split(lone, mIn, low), inRest = Split(lone, mIn, !low);
                        // A fan round the end, in the triangle's own turning order; the edge the slit comes in by in two.
                        int[] fan = mIn == m1
                            ? new[] { lone, inLone, q, inRest, m1, q, m1, m2, q, m2, lone, q }
                            : new[] { lone, m1, q, m1, m2, q, m2, inRest, q, inLone, lone, q };
                        tris[t] = fan[0]; tris[t + 1] = fan[1]; tris[t + 2] = fan[2];
                        for (int k = 3; k < fan.Length; k++) tris.Add(fan[k]);
                        cuts++;
                        continue;
                    }
                    int p1Lone = Split(lone, m1, low), p2Lone = Split(lone, m2, low), p1Rest = Split(lone, m1, !low), p2Rest = Split(lone, m2, !low);
                    tris[t] = lone; tris[t + 1] = p1Lone; tris[t + 2] = p2Lone;
                    tris.AddRange(new[] { p1Rest, m1, m2, p1Rest, m2, p2Rest });
                    cuts++;
                }
            }
            Debug.Log($"[Build] face {rig.File}: mouth cut through {cuts} triangles, {rig.CutHalf * 200f:0.0} cm long");
            if (cuts < 4) rig.Mouth = false;
        }

        /// <summary>
        /// The inside of the mouth: a dark pouch behind the lips' line (an oval shell seen from inside, so from outside it
        /// is never drawn), its lower half dropping with the jaw. The face shader colours it (UV3.w = 1).
        /// </summary>
        private static void AddMouthInside(FaceRig rig, MeshParts g)
        {
            float lineY = rig.LineAt(rig.Cx);
            // Wider than the slit and deep; then pressed in wherever it would come out of the face (a slanting look
            // in through the open lips always ends on its far wall, never on the inside of the head).
            float rx = rig.CutHalf + 0.01f, ry = 0.016f, rz = 0.022f;
            var center = new Vector3(rig.Cx, lineY - 0.002f, rig.MouthZ - 0.006f);
            const int rings = 12, segments = 24;
            g.MouthFirst = g.V.Count;
            var head = new BoneWeight { boneIndex0 = (int)Bone.Head, weight0 = 1f };
            for (int r = 0; r <= rings; r++)
            {
                float theta = Mathf.PI * r / rings; // 0 top .. pi bottom
                for (int s = 0; s <= segments; s++)
                {
                    float phi = 2f * Mathf.PI * s / segments;
                    var unit = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                    Vector3 at = center + Vector3.Scale(unit, new Vector3(rx, ry, rz));
                    float surface = rig.Image.FrontZ(at.x, at.y);
                    if (!float.IsNaN(surface)) at.z = Mathf.Min(at.z, surface - 0.0015f);
                    g.V.Add(at);
                    g.N.Add(-new Vector3(unit.x / rx, unit.y / ry, unit.z / rz).normalized); // facing in
                    g.T?.Add(new Vector4(1f, 0f, 0f, 1f));
                    g.Uv.Add(Vector2.zero);
                    g.W.Add(head);
                }
            }
            // A closed shell wound facing out, in a submesh of its own drawn with front faces culled (the body's mouth
            // material): what shows is its inside, while to anything that measures the body (which side of the skin a
            // point is on) it is an ordinary closed surface inside the head.
            var tris = new List<int>();
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = g.MouthFirst + r * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            g.Tris = g.Tris.Append(tris).ToArray();
        }

        /// <summary>Where each vertex is on the face at rest (UV3): x, y, 1 on the front of the face; the mouth inside: z how far down, w 1 + how far in.</summary>
        private static List<Vector4> FaceData(FaceRig rig, MeshParts g)
        {
            var data = new List<Vector4>(g.V.Count);
            float lineY = rig != null && rig.Mouth ? rig.LineAt(rig.Cx) : 0f;
            for (int i = 0; i < g.V.Count; i++)
            {
                Vector3 p = g.V[i];
                if (rig == null) { data.Add(Vector4.zero); continue; }
                if (g.MouthFirst >= 0 && i >= g.MouthFirst)
                {
                    // (z: how far down the inside: the tongue down there)
                    data.Add(new Vector4(p.x, p.y, Mathf.Clamp01((lineY - p.y) / 0.012f), 1f + Mathf.Clamp01((rig.MouthZ - p.z) / 0.03f)));
                    continue;
                }
                bool onEye = false;
                foreach (EyeFit e in new[] { rig.L, rig.R })
                    if (ShutAt(rig.Image, e, p.x, p.y, out float lidZ, out _) > 0f && Mathf.Abs(p.z - lidZ) < 0.03f) onEye = true; // (all of a modelled eye, behind its lashes too)
                data.Add(new Vector4(p.x, p.y, onEye || rig.Front(p, 0.012f, 0.016f) > 0.5f ? 1f : 0f, 0f));
            }
            return data;
        }

        /// <summary>
        /// The face's shape keys (AvatarFace sets them): the jaw, a smile, a frown, the brows up, angry and sad. Shape
        /// keys already on the mesh (the funny body's belly and nose) get the cut's new vertices filled in between
        /// their parents.
        /// </summary>
        private static void AddFaceShapes(FaceRig rig, MeshParts g, Mesh mesh)
        {
            int n = g.V.Count;
            // Shape keys already there: the new vertices move as the two they were cut between.
            var kept = new List<(string name, float weight, Vector3[] d)>();
            for (int s = 0; s < mesh.blendShapeCount; s++)
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    var d = new Vector3[n];
                    mesh.GetBlendShapeFrameVertices(s, f, d, null, null);
                    for (int k = 0; k < g.Parents.Count; k++)
                    {
                        (int a, int b, float t) = g.Parents[k];
                        d[g.Original + k] = Vector3.Lerp(d[a], d[b], t);
                    }
                    kept.Add((mesh.GetBlendShapeName(s), mesh.GetBlendShapeFrameWeight(s, f), d));
                }
            mesh.ClearBlendShapes();
            foreach ((string name, float weight, Vector3[] d) in kept) mesh.AddBlendShapeFrame(name, weight, d, null, null);

            var front = new float[n];
            for (int i = 0; i < n; i++) front[i] = g.MouthFirst >= 0 && i >= g.MouthFirst ? 1f : rig.Front(g.V[i]);

            if (rig.Mouth)
            {
                // The jaw: everything under the lips' line (the chin, the lower lip, the bottom of the mouth inside) turned
                // down about a hinge in front of the ears, fading out past the mouth's corners, below the chin and at the back.
                float lineY = rig.LineAt(rig.Cx);
                var hinge = new Vector3(rig.Cx, lineY + 0.012f, rig.MouthZ - 0.085f);
                Quaternion open = Quaternion.AngleAxis(AvatarFace.JawDegrees, Vector3.right);
                var d = new Vector3[n];
                var dn = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = g.V[i];
                    float below = rig.LineAt(p.x) - p.y;
                    bool lower = g.Lower.TryGetValue(i, out bool side) ? side : below > 0f;
                    if (!lower) continue;
                    below = Mathf.Max(0f, below);
                    float across = Mathf.Abs(p.x - rig.Cx);
                    float wx = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rig.CutHalf * 0.55f + below * 0.9f, rig.CutHalf + below * 1.6f, across));
                    float wy = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rig.ChinDepth, rig.ChinDepth + 0.03f, below));
                    float wz = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rig.MouthZ - 0.09f, rig.MouthZ - 0.045f, p.z));
                    float w = wx * wy * wz;
                    if (w <= 0f) continue;
                    d[i] = (open * (p - hinge) + hinge - p) * w;
                    dn[i] = (open * g.N[i] - g.N[i]) * w;
                }
                mesh.AddBlendShapeFrame(AvatarFace.JawShape, 100f, d, dn, null);

                // Smile and frown: the corners of the mouth up and out (the cheeks with them), or down.
                float cornerOffset = rig.CutHalf * 1.12f;
                Vector3[] Corners(Vector3 corner, Vector3 cheek)
                {
                    var shape = new Vector3[n];
                    for (int i = 0; i < n; i++)
                    {
                        if (front[i] <= 0f) continue;
                        Vector3 p = g.V[i];
                        foreach (float sideSign in new[] { -1f, 1f })
                        {
                            float cx = rig.Cx + sideSign * cornerOffset, cy = rig.LineAt(cx);
                            float r2 = (p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy);
                            float w = Mathf.Exp(-r2 / (2f * 0.006f * 0.006f));
                            float ux = p.x - (cx + sideSign * 0.004f), uy = p.y - (cy + 0.014f);
                            float wc = Mathf.Exp(-(ux * ux + uy * uy) / (2f * 0.009f * 0.009f));
                            shape[i] += (new Vector3(corner.x * sideSign, corner.y, corner.z) * w + cheek * wc) * front[i];
                        }
                    }
                    return shape;
                }
                mesh.AddBlendShapeFrame(AvatarFace.SmileShape, 100f, Corners(new Vector3(0.0009f, 0.0026f, 0.0006f), new Vector3(0f, 0.0012f, 0.0012f)), null, null);
                mesh.AddBlendShapeFrame(AvatarFace.FrownShape, 100f, Corners(new Vector3(0.0004f, -0.0024f, 0f), Vector3.zero), null, null);
            }

            // The shut lids' shape: each eye pressed into the smooth skin over it.
            foreach ((EyeFit e, string name) in new[] { (rig.L, AvatarFace.ShutLShape), (rig.R, AvatarFace.ShutRShape) })
            {
                if (e?.ShutZ == null) continue;
                var d = new Vector3[n];
                var dn = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    if (g.MouthFirst >= 0 && i >= g.MouthFirst) continue;
                    Vector3 p = g.V[i];
                    float w = ShutAt(rig.Image, e, p.x, p.y, out float z, out Vector2 slope);
                    if (w <= 0f || p.z > z + 0.025f || p.z < z - 0.03f) continue; // (hair in front, the back of the head)
                    d[i] = new Vector3(0f, 0f, (z - p.z) * w);
                    dn[i] = (new Vector3(-slope.x, -slope.y, 1f).normalized - g.N[i]) * w;
                }
                mesh.AddBlendShapeFrame(name, 100f, d, dn, null);
            }

            // The brows: up, angry (in and down at the middle), sad (up at the middle).
            Vector3[] Brows(Func<float, float, Vector3> move)
            {
                var shape = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    if (front[i] <= 0f || (g.MouthFirst >= 0 && i >= g.MouthFirst)) continue;
                    Vector3 p = g.V[i];
                    bool left = p.x < rig.MidX;
                    Vector3 b = left ? rig.BrowL : rig.BrowR;
                    float eyeTop = left ? rig.EyeTopL : rig.EyeTopR;
                    if (b.z <= 0f) continue;
                    float wx = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b.z * 0.8f, b.z * 1.6f, Mathf.Abs(p.x - b.x)));
                    float wy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(eyeTop + 0.001f, b.y, p.y)) *
                               (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(b.y + 0.005f, b.y + 0.035f, p.y)));
                    if (wx * wy <= 0f) continue;
                    float toward = Mathf.Sign(rig.MidX - b.x); // toward the nose
                    float inner = Mathf.Clamp01(0.5f + (p.x - b.x) * toward / (2f * b.z));
                    shape[i] = move(toward, inner) * (wx * wy * front[i]);
                }
                return shape;
            }
            mesh.AddBlendShapeFrame(AvatarFace.BrowUpShape, 100f, Brows((_, _) => new Vector3(0f, 0.0042f, 0.0004f)), null, null);
            mesh.AddBlendShapeFrame(AvatarFace.BrowAngryShape, 100f, Brows((toward, inner) => new Vector3(toward * 0.0012f, -0.0034f, 0.0008f) * (0.35f + 0.65f * inner)), null, null);
            mesh.AddBlendShapeFrame(AvatarFace.BrowSadShape, 100f, Brows((_, inner) => new Vector3(0f, 0.0038f * inner * inner - 0.001f * (1f - inner), 0f)), null, null);
        }

        /// <summary>The face shader and what it needs, on the body's material; the mouth's inside gets its own (front faces culled).</summary>
        private static void DressFace(FaceRig rig, AvatarBody body, Material material, string file)
        {
            body.FaceEyes = rig != null && rig.Open != null;
            body.FaceMouth = rig != null && rig.Mouth;
            body.MouthMaterial = null;
            if (rig == null) return;
            Shader shader = Shader.Find(AvatarFace.ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Build] face shader {AvatarFace.ShaderName} missing: {rig.File} keeps a still face");
                body.FaceEyes = body.FaceMouth = false;
                return;
            }
            material.shader = shader;
            material.SetFloat("_FaceOn", body.FaceEyes ? 1f : 0f);
            if (body.FaceEyes)
            {
                material.SetTexture("_FaceOpen", rig.Open);
                material.SetTexture("_FaceLid", rig.Lid);
                material.SetVector("_FaceRect", rig.Rect);
                material.SetFloat("_FaceMidX", rig.MidX);
                Vector4 Eye(EyeFit e) => e == null ? Vector4.zero : new Vector4(e.Cx, e.Cy, e.Hx, e.Hy);
                material.SetVector("_FaceEyeL", Eye(rig.L));
                material.SetVector("_FaceEyeR", Eye(rig.R));
                EyeFit any = rig.L ?? rig.R;
                material.SetColor("_FaceLash", any.Lash);
                material.SetColor("_FaceSclera", any.Sclera);
                material.SetFloat("_FaceShutAt", AvatarFace.ShutAt);
            }
            EditorUtility.SetDirty(material);
            if (!body.FaceMouth) return;
            string path = $"{OutputDir}/{file}_mouth.mat";
            var mouth = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mouth == null)
            {
                mouth = new Material(material);
                AssetDatabase.CreateAsset(mouth, path);
            }
            else mouth.CopyPropertiesFromMaterial(material);
            mouth.shader = shader;
            mouth.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            EditorUtility.SetDirty(mouth);
            body.MouthMaterial = mouth;
        }
    }
}
