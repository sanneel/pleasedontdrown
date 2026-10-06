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
    /// Bakes generated characters into <see cref="AvatarBody"/> assets. Input: a GLB from
    /// ArtSource/Tools/prepare_character.py (one figure, A-pose, skinned to bones named like AvatarRig.Bone).
    /// The mesh is re-skinned to the game's own skeleton: same bones, placed at this character's joints, and bound so
    /// that the rig's rest pose (arms and legs straight down) turns the A-pose arms down. Every procedural pose and
    /// gesture then works on it unchanged.
    /// </summary>
    public static partial class MeshyCharacters
    {
        private const string SourceDir = "Assets/_Game/Art/Characters";
        private const string OutputDir = "Assets/_Game/Avatar/Bodies";
        private const string LibraryPath = "Assets/_Game/Resources/" + AvatarBodyLibrary.ResourcePath + ".asset";

        /// <summary>id, display name, GLB file (in Art/Characters).</summary>
        private static readonly (byte id, string name, string file)[] Bodies =
        {
            (AvatarLook.Bodies.Sandy, "Sandy", "sandy"),
            (AvatarLook.Bodies.SandyBoss, "Sandy (boss)", "sandy_boss"),
            (AvatarLook.Bodies.TouristRed, "Tourist (red bikini)", "tourist_bikini_red"),
            (AvatarLook.Bodies.TouristSporty, "Tourist (sporty)", "tourist_bikini_sporty"),
            (AvatarLook.Bodies.TouristPurple, "Tourist (purple bikini)", "tourist_bikini_purple"),
            (AvatarLook.Bodies.TouristBuddy, "Tourist (sunburnt dad)", "tourist_buddy"),
            (AvatarLook.Bodies.Robber, "Robber", "robber"),
            (AvatarLook.Bodies.Goofy, "Goofy lifeguard", "goofy"),
        };

        /// <summary>Bodies that keep their painted face as it is: the robber's eyes are behind sunglasses (lids would blink on the lenses).</summary>
        private static readonly HashSet<string> PaintedFaceOnly = new() { "robber" };

        private static readonly int BoneTotal = (int)Bone.Count + 2 * HandBones.BoneCount;

        /// <summary>-executeMethod PleaseDontDrown.Editor.MeshyCharacters.BakeBatch: only the bodies, no scene build.</summary>
        public static void BakeBatch()
        {
            try
            {
                BakeAll();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build] body bake FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Refresh only tourist assets after a Blender art pass; keep the library and other bodies intact.</summary>
        public static void BakeTourists()
        {
            foreach (var (id, name, file) in Bodies.Where(b => b.file.StartsWith("tourist_")))
            {
                Bake($"{SourceDir}/{file}.glb", id, name, file);
                for (int n = 1; n <= 16; n++)
                {
                    string variant = $"{file}_v{n:00}";
                    string glb = $"{SourceDir}/{variant}.glb";
                    if (File.Exists(glb)) Bake(glb, AvatarLook.Bodies.Variant(id, n), $"{name} #{n}", variant);
                }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Re-bake only the players' goofy lifeguard (its hands, say), keeping every other body as it is.</summary>
        public static void BakeGoofy()
        {
            try
            {
                foreach (var (id, name, file) in Bodies.Where(b => b.file == "goofy"))
                    Bake($"{SourceDir}/{file}.glb", id, name, file);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build] goofy bake FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Bakes every body whose GLB is present and writes the library the game loads.</summary>
        public static void BakeAll()
        {
            Directory.CreateDirectory(OutputDir);
            var baked = new List<AvatarBody>();
            var all = new List<(byte id, string name, string file)>(Bodies);
            foreach (byte baseId in AvatarLook.Bodies.VariantBases)
            {
                var (_, baseName, baseFile) = Bodies.First(b => b.id == baseId);
                for (int n = 1; n <= 16; n++)
                    all.Add((AvatarLook.Bodies.Variant(baseId, n), $"{baseName} #{n}", $"{baseFile}_v{n:00}"));
            }
            foreach (var (id, name, file) in all)
            {
                string glb = $"{SourceDir}/{file}.glb";
                if (!File.Exists(glb)) continue;
                baked.Add(Bake(glb, id, name, file));
            }
            var library = AssetDatabase.LoadAssetAtPath<AvatarBodyLibrary>(LibraryPath);
            if (library == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                library = ScriptableObject.CreateInstance<AvatarBodyLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Bodies = baked.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Build] generated bodies: {string.Join(", ", baked.Select(b => b.DisplayName))}");
        }

        /// <summary>
        /// A plain URP Lit material with only Meshy's colour texture: its normal and metal/roughness maps were baked for
        /// the full-detail model (seam streaks on the reduced one) and "metal" patches render as dark specks on skin.
        /// </summary>
        private static Material BodyMaterial(Material imported, string file)
        {
            Texture baseColor = FindBaseColor(imported);
            string path = $"{OutputDir}/{file}_material.mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(lit);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = lit;
            mat.SetTexture("_BaseMap", baseColor);
            mat.mainTexture = baseColor;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.28f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            if (baseColor == null) Debug.LogWarning($"[Build] body {file}: no colour texture found on {imported.name}");
            return mat;
        }

        // ------------------------------------------------------------------ hands

        /// <summary>
        /// A model's hand, measured: the frame the game's hands use (fingers down -Y, thumb toward +Z, palm on -X for a
        /// right hand and +X for a left one) laid over however the model holds it, and where the block of its four
        /// fingers lies in that frame.
        /// </summary>
        private sealed class HandFit
        {
            public Quaternion Rotation;
            public bool Fingers;
            public readonly Vector3[] Bases = new Vector3[HandBones.Fingers];   // hand space
            public readonly float[] Lengths = new float[HandBones.BoneCount];
            public float Knuckles;      // from the wrist down the hand to the knuckle line
            public float Reach;         // ... to the fingertips
            public float ThumbEdge;     // z of the index finger's outer edge (the thumb lies beyond it)
            public float Width;         // across the four fingers

            /// <summary>As AvatarBody keeps them: laid out as a right hand (HandBones mirrors the left one back).</summary>
            public Vector3[] StoredBases(float side) => Bases.Select(b => new Vector3(b.x * side, b.y, b.z)).ToArray();
        }

        private static int Dominant(BoneWeight w)
        {
            int bone = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { bone = w.boneIndex1; best = w.weight1; }
            if (w.weight2 > best) { bone = w.boneIndex2; best = w.weight2; }
            if (w.weight3 > best) bone = w.boneIndex3;
            return bone;
        }

        private static float Percentile(List<float> sorted, float p) =>
            sorted[Mathf.Clamp(Mathf.RoundToInt(p * (sorted.Count - 1)), 0, sorted.Count - 1)];

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Finds how a model holds its hand. Fingers: from the wrist to the far end of the hand. Across the fingers
        /// the hand is wide and through the palm it is thin, which gives the palm's plane; the thumb sticks out of
        /// one edge, which tells the two edges apart; and a thumb on that edge of a right (or left) hand leaves only
        /// one side the palm can be on.
        /// </summary>
        private static HandFit FitHand(Vector3[] vertices, BoneWeight[] weights, int handBone, Vector3 wrist, Vector3 arm, float side, string file)
        {
            var fit = new HandFit { Rotation = Quaternion.FromToRotation(Vector3.down, arm) };
            var hand = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
                if (Dominant(weights[i]) == handBone) hand.Add(vertices[i] - wrist);
            string which = side < 0f ? "left" : "right";
            if (hand.Count < 40)
            {
                Debug.LogWarning($"[Build] body {file}: {which} hand has only {hand.Count} vertices: fingers stay stiff");
                return fit;
            }
            List<float> along = hand.Select(v => Vector3.Dot(v, arm)).OrderBy(a => a).ToList();
            float reach = Percentile(along, 0.97f);
            Vector3 far = Vector3.zero;
            int farCount = 0;
            foreach (Vector3 v in hand)
                if (Vector3.Dot(v, arm) > 0.6f * reach) { far += v; farCount++; }
            Vector3 fingers = farCount > 0 ? (far / farCount).normalized : arm;
            reach = Percentile(hand.Select(v => Vector3.Dot(v, fingers)).OrderBy(a => a).ToList(), 0.97f);

            Vector3 e1 = Vector3.Cross(fingers, Vector3.forward);
            if (e1.sqrMagnitude < 0.01f) e1 = Vector3.Cross(fingers, Vector3.right);
            e1.Normalize();
            Vector3 e2 = Vector3.Cross(fingers, e1);
            List<Vector3> block = hand.Where(v => Vector3.Dot(v, fingers) > 0.55f * reach && Vector3.Dot(v, fingers) < 0.95f * reach).ToList();
            if (reach < 0.05f || block.Count < 12)
            {
                Debug.LogWarning($"[Build] body {file}: {which} hand too small to measure ({reach:0.000} m): fingers stay stiff");
                return fit;
            }
            float mx = block.Average(v => Vector3.Dot(v, e1)), my = block.Average(v => Vector3.Dot(v, e2));
            float sxx = 0f, syy = 0f, sxy = 0f;
            foreach (Vector3 v in block)
            {
                float x = Vector3.Dot(v, e1) - mx, y = Vector3.Dot(v, e2) - my;
                sxx += x * x;
                syy += y * y;
                sxy += x * y;
            }
            float angle = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
            Vector3 wide = (e1 * Mathf.Cos(angle) + e2 * Mathf.Sin(angle)).normalized;

            List<float> across = block.Select(v => Vector3.Dot(v, wide)).OrderBy(a => a).ToList();
            float lo = Percentile(across, 0.03f), hi = Percentile(across, 0.97f);
            List<float> middle = hand.Where(v => Vector3.Dot(v, fingers) > 0.15f * reach && Vector3.Dot(v, fingers) < 0.6f * reach)
                .Select(v => Vector3.Dot(v, wide)).OrderBy(a => a).ToList();
            float over = middle.Count > 0 ? Percentile(middle, 0.98f) - hi : 0f;
            float under = middle.Count > 0 ? lo - Percentile(middle, 0.02f) : 0f;
            float sign;
            if (Mathf.Max(over, under) > 0.015f) sign = over >= under ? 1f : -1f;
            else if (Mathf.Max(over, under) > 0.006f)
            {
                // A thumb that barely shows (the goofy lifeguard's cartoon hand: 8 mm) can't be told from a lumpy
                // finger: read that way, it put the palm on the back of the hand and every grip bent the fingers
                // backwards into a splayed starfish. These models face +z, so the thumb points forward.
                sign = Vector3.Dot(wide, Vector3.forward) >= 0f ? 1f : -1f;
                Debug.Log($"[Build] body {file}: {which} thumb only {Mathf.Max(over, under):0.000} m out, taking it to point forward");
            }
            else
            {
                // No thumb to be seen: take it to point away from the body (palms forward), as these models stand.
                sign = Vector3.Dot(wide, new Vector3(side, 0f, 0f)) >= 0f ? 1f : -1f;
                Debug.LogWarning($"[Build] body {file}: {which} thumb not found, assuming it points outward");
            }
            Vector3 thumb = wide * sign;
            fit.Rotation = Quaternion.LookRotation(thumb, -fingers);

            // The four fingers, in the hand's own frame.
            Quaternion toHand = Quaternion.Inverse(fit.Rotation);
            List<Vector3> local = hand.Select(v => toHand * v).ToList();
            List<float> z = local.Where(q => -q.y > 0.7f * reach && -q.y < 0.95f * reach).Select(q => q.z).OrderBy(a => a).ToList();
            if (z.Count < 8) return fit;
            float zLo = Percentile(z, 0.03f), zHi = Percentile(z, 0.97f), width = zHi - zLo;
            List<float> xs = local.Where(q => -q.y > 0.5f * reach && q.z >= zLo && q.z <= zHi).Select(q => q.x).OrderBy(a => a).ToList();
            if (width < 0.02f || xs.Count < 8)
            {
                Debug.LogWarning($"[Build] body {file}: {which} fingers too narrow to skin ({width:0.000} m)");
                return fit;
            }
            float xMid = Percentile(xs, 0.5f);
            fit.Reach = reach;
            fit.Knuckles = 0.5f * reach;
            fit.ThumbEdge = zHi;
            fit.Width = width;
            float fingerLength = reach - fit.Knuckles;
            float[] share = { 0.42f, 0.32f, 0.26f };
            for (int f = 0; f < HandBones.Fingers; f++)
            {
                // (The thumb's bones carry no skin: the model's thumb stays on the hand.)
                fit.Bases[f] = f == 0 ? new Vector3(xMid, -0.25f * reach, zHi) : new Vector3(xMid, -fit.Knuckles, zHi - (f - 0.5f) * width / 4f);
                for (int seg = 0; seg < HandBones.Segments; seg++)
                    fit.Lengths[HandBones.BoneIndex(f, seg)] = fingerLength * share[seg];
            }
            fit.Fingers = true;
            Debug.Log($"[Build] body {file}: {which} hand {reach:0.000} m long, fingers {width:0.000} m across, " +
                      $"palm faces {fit.Rotation * new Vector3(-side, 0f, 0f):0.0}, thumb {thumb:0.0} (stuck out {Mathf.Max(over, under):0.000} m)");
            return fit;
        }

        /// <summary>
        /// Binds the finger bones where the game builds them and moves the finger block's skin onto them: along each
        /// finger from bone to bone, and across from finger to finger, with soft joins (the fingers are one mitten).
        /// </summary>
        private static void SkinFingers(HandFit fit, Vector3[] vertices, BoneWeight[] weights, Matrix4x4[] bind, int handBone, int firstFinger, float side)
        {
            Matrix4x4 handBind = bind[handBone];
            for (int f = 0; f < HandBones.Fingers; f++)
            {
                Vector3 at = fit.Bases[f];
                for (int seg = 0; seg < HandBones.Segments; seg++)
                {
                    int i = HandBones.BoneIndex(f, seg);
                    bind[firstFinger + i] = handBind * Matrix4x4.Translate(at);
                    at += new Vector3(0f, -fit.Lengths[i], 0f);
                }
            }

            Matrix4x4 toHand = handBind.inverse;
            float fingerLength = fit.Reach - fit.Knuckles, blend = 0.18f * fingerLength;
            float len0 = fit.Lengths[HandBones.BoneIndex(1, 0)], len1 = fit.Lengths[HandBones.BoneIndex(1, 1)];
            var parts = new List<(int bone, float weight)>(8);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (Dominant(weights[i]) != handBone) continue;
                Vector3 q = toHand.MultiplyPoint3x4(vertices[i]);
                float t = -q.y - fit.Knuckles;
                if (t < -blend) continue;
                if (q.z > fit.ThumbEdge + 0.12f * fit.Width && -q.y < 0.8f * fit.Reach) continue; // the thumb
                float a0 = Smooth((t + blend) / (2f * blend));
                float a1 = Smooth((t - len0 + blend) / (2f * blend));
                float a2 = Smooth((t - len0 - len1 + blend) / (2f * blend));
                float u = Mathf.Clamp((fit.ThumbEdge - q.z) / fit.Width * 4f - 0.5f, 0f, 3f);
                int near = Mathf.Min(2, Mathf.FloorToInt(u));
                float next = Smooth(u - near);
                parts.Clear();
                parts.Add((handBone, 1f - a0));
                for (int k = 0; k < 2; k++)
                {
                    int finger = near + 1 + k;
                    float fw = k == 0 ? 1f - next : next;
                    parts.Add((firstFinger + HandBones.BoneIndex(finger, 0), a0 * (1f - a1) * fw));
                    parts.Add((firstFinger + HandBones.BoneIndex(finger, 1), a0 * a1 * (1f - a2) * fw));
                    parts.Add((firstFinger + HandBones.BoneIndex(finger, 2), a0 * a1 * a2 * fw));
                }
                parts.Sort((x, y) => y.weight.CompareTo(x.weight));
                float total = parts[0].weight + parts[1].weight + parts[2].weight + parts[3].weight;
                weights[i] = new BoneWeight
                {
                    boneIndex0 = parts[0].bone, weight0 = parts[0].weight / total,
                    boneIndex1 = parts[1].bone, weight1 = parts[1].weight / total,
                    boneIndex2 = parts[2].bone, weight2 = parts[2].weight / total,
                    boneIndex3 = parts[3].bone, weight3 = parts[3].weight / total
                };
            }
        }

        // ------------------------------------------------------------------ face

        /// <summary>Geometry added to a model: more vertices for its mesh, each on one bone.</summary>
        private sealed class ExtraGeometry
        {
            public readonly List<Vector3> Vertices = new(), Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<BoneWeight> Weights = new();
            public readonly List<int> Triangles = new();
        }

        /// <summary>
        /// Where a base model's painted eyes and mouth are, tied to the vertices round them. Its look-alikes are the
        /// same mesh reshaped and repainted (pale skin and white hair can pass for the whites of eyes, so looking for
        /// those again is unreliable): on a look-alike the eyes are wherever those same vertices have gone.
        /// </summary>
        private sealed class FaceFit
        {
            public int VertexCount;
            public Spot EyeL, EyeR, Mouth;
            public bool HasMouth;
            public float MouthWidth;
        }

        /// <summary>A box on the face and the vertices near it (their middle and spread, to carry it to a reshaped face).</summary>
        private sealed class Spot
        {
            public Rect Box;
            public int[] Anchors;
            public Vector2 Middle, Spread;

            public static Spot At(Rect box, Vector3[] vertices, Vector3[] normals, float floor)
            {
                float margin = Mathf.Max(box.width, box.height) * 0.6f + 0.01f;
                Rect wide = Rect.MinMaxRect(box.xMin - margin, box.yMin - margin, box.xMax + margin, box.yMax + margin);
                var spot = new Spot { Box = box };
                spot.Anchors = Enumerable.Range(0, vertices.Length)
                    .Where(i => vertices[i].y > floor && normals[i].z > 0.25f && wide.Contains(new Vector2(vertices[i].x, vertices[i].y))).ToArray();
                Measure(spot.Anchors, vertices, out spot.Middle, out spot.Spread);
                return spot;
            }

            private static void Measure(int[] anchors, Vector3[] vertices, out Vector2 middle, out Vector2 spread)
            {
                middle = spread = Vector2.zero;
                if (anchors.Length == 0) return;
                foreach (int i in anchors) middle += new Vector2(vertices[i].x, vertices[i].y);
                middle /= anchors.Length;
                foreach (int i in anchors)
                    spread += new Vector2((vertices[i].x - middle.x) * (vertices[i].x - middle.x), (vertices[i].y - middle.y) * (vertices[i].y - middle.y));
                spread = new Vector2(Mathf.Sqrt(spread.x / anchors.Length), Mathf.Sqrt(spread.y / anchors.Length));
            }

            /// <summary>The same box on a reshaped copy of the mesh.</summary>
            public Rect On(Vector3[] vertices)
            {
                Measure(Anchors, vertices, out Vector2 middle, out Vector2 spread);
                var scale = new Vector2(Spread.x > 1e-4f ? spread.x / Spread.x : 1f, Spread.y > 1e-4f ? spread.y / Spread.y : 1f);
                Vector2 size = Vector2.Scale(Box.size, scale);
                return new Rect(middle + Vector2.Scale(Box.center - Middle, scale) - size * 0.5f, size);
            }
        }

        private static readonly Dictionary<string, FaceFit> BaseFaces = new();

        private static string BaseOf(string file)
        {
            int at = file.LastIndexOf("_v", StringComparison.Ordinal);
            return at > 0 && at == file.Length - 4 && char.IsDigit(file[at + 2]) && char.IsDigit(file[at + 3]) ? file.Substring(0, at) : file;
        }

        private struct Sample
        {
            public Vector3 P, N;
            public Vector2 Uv;
            public Color C;
        }

        private static Texture FindBaseColor(Material imported)
        {
            foreach (string property in imported.GetTexturePropertyNames())
                if (property.IndexOf("baseColor", StringComparison.OrdinalIgnoreCase) >= 0 || property == "_BaseMap" || property == "_MainTex")
                {
                    Texture found = imported.GetTexture(property);
                    if (found != null) return found;
                }
            return null;
        }

        private static Color32[] ReadPixels(Texture source, int size)
        {
            RenderTexture target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, target);
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply();
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        private static float Distance(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));
        private static bool White(Color c) => Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 0.62f && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)) < 0.2f;
        private static float Redness(Color c) => (c.r - (c.g + c.b) * 0.5f) / Mathf.Max(c.r, 0.05f);
        private static float Blueness(Color c) => (c.b - c.g) / Mathf.Max(c.r, 0.05f);
        private static float Median(IEnumerable<float> values) => Percentile(values.OrderBy(v => v).ToList(), 0.5f);

        /// <summary>
        /// Least squares: uv = (a x + b y + c) for each of u and v, over the samples' face positions. The residual is
        /// the typical miss in UV units.
        /// </summary>
        private static bool FitAffine(List<Sample> samples, out Vector3 u, out Vector3 v, out float residual)
        {
            u = v = Vector3.zero;
            residual = float.MaxValue;
            double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = samples.Count;
            double sxu = 0, syu = 0, su = 0, sxv = 0, syv = 0, sv = 0;
            foreach (Sample s in samples)
            {
                double x = s.P.x, y = s.P.y;
                sxx += x * x; sxy += x * y; sx += x; syy += y * y; sy += y;
                sxu += x * s.Uv.x; syu += y * s.Uv.x; su += s.Uv.x;
                sxv += x * s.Uv.y; syv += y * s.Uv.y; sv += s.Uv.y;
            }
            // Solve [sxx sxy sx; sxy syy sy; sx sy n] * (a b c) = rhs by Cramer's rule.
            double Det(double a1, double a2, double a3, double b1, double b2, double b3, double c1, double c2, double c3) =>
                a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1);
            double det = Det(sxx, sxy, sx, sxy, syy, sy, sx, sy, n);
            if (Math.Abs(det) < 1e-18) return false;
            Vector3 Solve(double r1, double r2, double r3) => new(
                (float)(Det(r1, sxy, sx, r2, syy, sy, r3, sy, n) / det),
                (float)(Det(sxx, r1, sx, sxy, r2, sy, sx, r3, n) / det),
                (float)(Det(sxx, sxy, r1, sxy, syy, r2, sx, sy, r3) / det));
            u = Solve(sxu, syu, su);
            v = Solve(sxv, syv, sv);
            double err = 0;
            foreach (Sample s in samples)
            {
                float du = u.x * s.P.x + u.y * s.P.y + u.z - s.Uv.x, dv = v.x * s.P.x + v.y * s.P.y + v.z - s.Uv.y;
                err += du * du + dv * dv;
            }
            residual = (float)Math.Sqrt(err / n);
            return true;
        }

        /// <summary>
        /// These models' faces are painted on. The painted eyes and mouth are found in the texture (the whites of the
        /// eyes; lips and teeth), and three small pieces are added in front of them: a lid of the face's own skin
        /// colour over each eye, hinged along its top edge on the eye bone, with a dark lash line along its lower
        /// edge; and a dark oval on the mouth bone. Scaled flat (bone scale y = 0) they are not there at all; the game
        /// scales the lids down over the eyes to blink and the oval up to open the mouth.
        /// </summary>
        private static bool BuildFace(string file, Mesh source, Vector3[] vertices, Vector3[] normals, BoneWeight[] weights, Material imported,
            Vector3 headJoint, ExtraGeometry extra, out Vector3 lidL, out Vector3 lidR, out Vector3 mouthAt, out float eyeLevel)
        {
            lidL = lidR = mouthAt = Vector3.zero;
            eyeLevel = 0f;
            Texture texture = FindBaseColor(imported);
            if (texture == null) return false;
            const int size = 1024;
            Color32[] pixels = ReadPixels(texture, size);
            Vector2[] uvs = source.uv;
            Color At(Vector2 uv)
            {
                int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * size), 0, size - 1);
                int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * size), 0, size - 1);
                return pixels[y * size + x];
            }

            // Skin: what the hands are painted with.
            var handColors = new List<Color>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = Dominant(weights[i]);
                if (bone == (int)Bone.HandL || bone == (int)Bone.HandR) handColors.Add(At(uvs[i]));
            }
            if (handColors.Count < 20) return false;
            var skin = new Color(Median(handColors.Select(c => c.r)), Median(handColors.Select(c => c.g)), Median(handColors.Select(c => c.b)));

            // The front of the head, as points with their colour.
            var samples = new List<Sample>();
            int[] triangles = source.triangles;
            const int steps = 5;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float floor = headJoint.y - 0.03f;
                if (vertices[a].y < floor || vertices[b].y < floor || vertices[c].y < floor) continue;
                Vector3 n = (normals[a] + normals[b] + normals[c]).normalized;
                if (n.z < 0.25f) continue;
                for (int i = 0; i <= steps; i++)
                    for (int j = 0; j <= steps - i; j++)
                    {
                        float wa = i / (float)steps, wb = j / (float)steps, wc = 1f - wa - wb;
                        Vector2 uv = uvs[a] * wa + uvs[b] * wb + uvs[c] * wc;
                        samples.Add(new Sample { P = vertices[a] * wa + vertices[b] * wb + vertices[c] * wc, N = n, Uv = uv, C = At(uv) });
                    }
            }
            List<Sample> faceSkin = samples.Where(s => s.N.z > 0.5f && Distance(s.C, skin) < 0.2f).ToList();
            if (faceSkin.Count < 200)
            {
                Debug.LogWarning($"[Build] body {file}: no face skin found (skin {skin}): the face stays still");
                return false;
            }
            List<float> faceX = faceSkin.Select(s => Mathf.Abs(s.P.x)).OrderBy(v => v).ToList(), faceY = faceSkin.Select(s => s.P.y).OrderBy(v => v).ToList();
            float half = Percentile(faceX, 0.98f), bottom = Percentile(faceY, 0.02f), top = Percentile(faceY, 0.98f), height = top - bottom;

            // Eyes: the whites, one patch each side of the nose, in the upper part of the face.
            bool Eye(float side, out Rect box)
            {
                box = default;
                List<Sample> white = samples.Where(s => White(s.C) && Distance(s.C, skin) > 0.12f && s.P.x * side > 0.008f && Mathf.Abs(s.P.x) < half &&
                                                        s.P.y > bottom + 0.3f * height && s.P.y < top).ToList();
                if (white.Count < 20) return false;
                var middle = new Vector2(Median(white.Select(s => s.P.x)), Median(white.Select(s => s.P.y)));
                white = white.Where(s => Vector2.Distance(new Vector2(s.P.x, s.P.y), middle) < 0.035f).ToList();
                if (white.Count < 15) return false;
                box = Rect.MinMaxRect(white.Min(s => s.P.x), white.Min(s => s.P.y), white.Max(s => s.P.x), white.Max(s => s.P.y));
                if (box.width < 0.006f || box.height < 0.004f) return false;
                // The whites are only part of an eye: take in what else is painted right there (a big iris, the lashes),
                // but not the brow above.
                Rect reach = Rect.MinMaxRect(box.xMin - box.width * 0.3f, box.yMin - box.height * 0.3f, box.xMax + box.width * 0.3f, box.yMax + box.height * 0.3f);
                List<Sample> painted = samples.Where(s => reach.Contains(new Vector2(s.P.x, s.P.y)) && s.P.x * side > 0.004f && Distance(s.C, skin) > 0.3f).ToList();
                if (painted.Count > 0)
                    box = Rect.MinMaxRect(Mathf.Min(box.xMin, painted.Min(s => s.P.x)), Mathf.Min(box.yMin, painted.Min(s => s.P.y)),
                        Mathf.Max(box.xMax, painted.Max(s => s.P.x)), Mathf.Max(box.yMax, painted.Max(s => s.P.y)));
                return true;
            }
            bool found = Eye(-1f, out Rect eyeL) & Eye(1f, out Rect eyeR);
            // A pair of eyes is the same size: where one was only half found (little white showing), the other tells.
            var eyeSize = new Vector2(Mathf.Min(0.065f, Mathf.Max(eyeL.width, eyeR.width)), Mathf.Min(0.05f, Mathf.Max(eyeL.height, eyeR.height)));
            eyeL = new Rect(eyeL.center - eyeSize * 0.5f, eyeSize);
            eyeR = new Rect(eyeR.center - eyeSize * 0.5f, eyeSize);

            float faceFloor = headJoint.y - 0.03f;
            string baseFile = BaseOf(file);
            FaceFit basis = null;
            string eyeNote = "";
            if (baseFile != file && BaseFaces.TryGetValue(baseFile, out basis) && basis.VertexCount == vertices.Length &&
                basis.EyeL.Anchors.Length >= 6 && basis.EyeR.Anchors.Length >= 6)
            {
                eyeL = basis.EyeL.On(vertices);
                eyeR = basis.EyeR.On(vertices);
                found = true;
                eyeNote = " (where the base model's are)";
            }
            else basis = null;
            if (!found)
            {
                Debug.LogWarning($"[Build] body {file}: eyes not found in the texture: the face stays still");
                return false;
            }
            eyeLevel = (eyeL.center.y + eyeR.center.y) * 0.5f;

            // Colours to paint with: the skin round the eyes (for the lids) and the darkest spot in them (lashes, mouth).
            float level = eyeLevel;
            Vector2 SkinRound(Rect eye)
            {
                // The usual skin colour in a band round the eye: the lid must not stand out from what is next to it.
                Rect outer = Rect.MinMaxRect(eye.xMin - 0.015f, eye.yMin - 0.015f, eye.xMax + 0.015f, eye.yMax + 0.015f);
                // A closed lid is the skin above the eye come down: that strip first (it is often a shade darker than the cheek).
                Rect above = Rect.MinMaxRect(eye.xMin, eye.yMax + 0.001f, eye.xMax, eye.yMax + 0.012f);
                List<Sample> around = faceSkin.Where(s => above.Contains(new Vector2(s.P.x, s.P.y))).ToList();
                if (around.Count < 20) around = faceSkin.Where(s => outer.Contains(new Vector2(s.P.x, s.P.y)) && !eye.Contains(new Vector2(s.P.x, s.P.y))).ToList();
                if (around.Count < 20) around = faceSkin.Where(s => Mathf.Abs(s.P.y - level) < 0.05f).ToList();
                if (around.Count < 20) around = faceSkin;
                var usual = new Color(Median(around.Select(s => s.C.r)), Median(around.Select(s => s.C.g)), Median(around.Select(s => s.C.b)));
                // A shade under the usual skin: a lid over an eyeball sits in the eye's shadow.
                var lid = usual * 0.93f;
                return around.OrderBy(s => Distance(s.C, lid)).First().Uv;
            }
            Color SkinRoundColor(Rect eye) => At(SkinRound(eye));
            Rect both = Rect.MinMaxRect(eyeL.xMin, Mathf.Min(eyeL.yMin, eyeR.yMin), eyeR.xMax, Mathf.Max(eyeL.yMax, eyeR.yMax));
            Vector2 darkUv = samples.Where(s => both.Contains(new Vector2(s.P.x, s.P.y)) && !(s.P.x > eyeL.xMax && s.P.x < eyeR.xMin))
                .OrderBy(s => s.C.r + s.C.g + s.C.b).First().Uv;

            // A rounded patch lying on the face, between two heights of its oval (v: -1 bottom .. 1 top).
            void Patch(Vector2 center, float rx, float ry, float vFrom, float vTo, float lift, Vector2 uv, Bone bone, Func<float, float, float, Vector2> uvAt = null)
            {
                List<Sample> near = samples.Where(s => Mathf.Abs(s.P.x - center.x) < rx + 0.012f && Mathf.Abs(s.P.y - center.y) < ry + 0.012f).ToList();
                // Only the face itself: a lock of hair hanging in front of the eye (25 cm out) pulled lid points onto
                // it, and the stretched lid broke up into pieces.
                if (near.Count > 0)
                {
                    // (How far out the face is, from its skin only: the hair in front can outnumber it.)
                    List<Sample> skinNear = near.Where(s => Distance(s.C, skin) < 0.2f).ToList();
                    float faceZ = Median((skinNear.Count >= 20 ? skinNear : near).Select(s => s.P.z));
                    near = near.Where(s => Mathf.Abs(s.P.z - faceZ) < 0.025f).ToList(); // (hair behind the face too: the inside of a lock)
                }
                const int nx = 10, ny = 8;
                int first = extra.Vertices.Count;
                // One direction for the whole patch (lit evenly, like the skin round it), not each bump's own.
                Vector3 facing = Vector3.zero;
                foreach (Sample s in near) facing += s.N;
                facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
                for (int j = 0; j <= ny; j++)
                    for (int i = 0; i <= nx; i++)
                    {
                        float u = -1f + 2f * i / nx, v = Mathf.Lerp(vFrom, vTo, j / (float)ny);
                        float x = center.x + rx * u * Mathf.Sqrt(1f - 0.5f * v * v), y = center.y + ry * v * Mathf.Sqrt(1f - 0.5f * u * u);
                        Sample on = default;
                        float best = float.MaxValue, front = float.MinValue;
                        Vector3 round = Vector3.zero;
                        foreach (Sample s in near)
                        {
                            float d = (s.P.x - x) * (s.P.x - x) + (s.P.y - y) * (s.P.y - y);
                            if (d < best) { best = d; on = s; }
                            if (d < 0.014f * 0.014f) round += s.N;
                            // Modelled lashes and eyeballs stand out from the face: the patch goes over whatever is in front.
                            // (Over a wide circle: a bulging painted eye has few vertices, and a lid that only looked
                            // 8 mm around itself went behind the bulge in patches.)
                            if (d < 0.016f * 0.016f) front = Mathf.Max(front, s.P.z);
                        }
                        // In the middle it clears whatever stands out (the eyeball, modelled lashes); toward its rim it
                        // comes down onto the face itself, so it reads as skin and not as a plate stuck on.
                        float rim = Mathf.Pow(Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)), 3f);
                        float z = Mathf.Lerp(Mathf.Max(on.P.z, front), on.P.z, rim * 0.85f) + lift * Mathf.Lerp(1f, 0.25f, rim);
                        extra.Vertices.Add(new Vector3(x, y, z));
                        // Lit like the face under it: mostly the way the face curves there, evened out so the bumps of
                        // a modelled eye don't show as blotches, and rounded a little like a lid over an eyeball.
                        Vector3 lit = facing * 0.45f + (round.sqrMagnitude > 1e-6f ? round.normalized : facing) * 0.55f;
                        Vector3 lidNormal = (lit.normalized + new Vector3(u * 0.3f, v * 0.22f, 0f)).normalized;
                        // Toward its rim it takes the face's own shading, so it doesn't stand out as a lighter disc.
                        float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, Mathf.Max(Mathf.Abs(u), Mathf.Abs(v))));
                        extra.Normals.Add(Vector3.Slerp(lidNormal, on.N.sqrMagnitude > 1e-6f ? on.N.normalized : lidNormal, edge));
                        extra.Uvs.Add(uvAt != null ? uvAt(x, y, v) : uv);
                        extra.Weights.Add(new BoneWeight { boneIndex0 = (int)bone, weight0 = 1f });
                    }
                // Even the patch out (it only ever moves forward: nothing behind may poke through).
                for (int pass = 0; pass < 2; pass++)
                {
                    var depth = new float[(nx + 1) * (ny + 1)];
                    for (int j = 0; j <= ny; j++)
                        for (int i = 0; i <= nx; i++)
                        {
                            float sum = 0f;
                            int count = 0;
                            for (int dj = -1; dj <= 1; dj++)
                                for (int di = -1; di <= 1; di++)
                                {
                                    int jj = j + dj, ii = i + di;
                                    if (jj < 0 || jj > ny || ii < 0 || ii > nx) continue;
                                    sum += extra.Vertices[first + jj * (nx + 1) + ii].z;
                                    count++;
                                }
                            depth[j * (nx + 1) + i] = sum / count;
                        }
                    for (int k = 0; k < depth.Length; k++)
                    {
                        int i = k % (nx + 1), j = k / (nx + 1);
                        if (i == 0 || i == nx || j == 0 || j == ny) continue; // the rim stays down on the face
                        Vector3 at = extra.Vertices[first + k];
                        extra.Vertices[first + k] = new Vector3(at.x, at.y, Mathf.Max(at.z, depth[k]));
                    }
                }
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        int a = first + j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                        extra.Triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
            }
            float SurfaceZ(Vector2 at) => samples.OrderBy(s => (s.P.x - at.x) * (s.P.x - at.x) + (s.P.y - at.y) * (s.P.y - at.y)).First().P.z;

            Vector3 Lid(Rect eye, Rect other, Bone bone)
            {
                // Faces are symmetric: the lid covers this eye as found and the other eye mirrored across the
                // middle, so a badly found eye (small flat painted eyes) still ends up all under its lid.
                Rect mirror = Rect.MinMaxRect(-other.xMax, other.yMin, -other.xMin, other.yMax);
                eye = Rect.MinMaxRect(Mathf.Min(eye.xMin, mirror.xMin), Mathf.Min(eye.yMin, mirror.yMin),
                    Mathf.Max(eye.xMax, mirror.xMax), Mathf.Max(eye.yMax, mirror.yMax));
                // A little bigger than the painted eye (an oval inside a box misses its corners).
                float rx = eye.width * 0.5f * 1.28f + 0.002f, ry = eye.height * 0.5f * 1.3f + 0.002f; // all of the painted eye, its corners too
                // The lid is the skin just above the eye come down over it: textured with that strip of the face
                // itself (its own tone and shading detail), not one flat colour, which read as a smudge.
                Vector2 flat = SkinRound(eye);
                List<Sample> strip = faceSkin.Where(s => s.P.y > eye.yMax && s.P.y < eye.yMax + 0.016f && Distance(s.C, skin) < 0.11f
                                                         && Mathf.Abs(s.P.x - eye.center.x) < rx + 0.004f).ToList(); // (pale hair passed for skin)
                // One piece of the texture only: a strip crossing a UV seam would mix two far-apart places into
                // the lid (bits of eye and hair showed on it).
                if (strip.Count > 0)
                {
                    var middleUv = new Vector2(Median(strip.Select(s => s.Uv.x)), Median(strip.Select(s => s.Uv.y)));
                    strip = strip.Where(s => (s.Uv - middleUv).sqrMagnitude < 0.03f * 0.03f).ToList();
                }
                // Face position -> texture: one smooth (affine) map fitted to that strip, so the lid's texture is one
                // continuous piece of skin (mapping each lid point to its own nearest sample let the in-between
                // texture wander across the painted eye). If it doesn't fit cleanly (a seam, a mirror), one flat colour.
                Func<float, float, float, Vector2> skinAbove = null;
                if (strip.Count >= 30 && FitAffine(strip, out Vector3 fu, out Vector3 fv, out float residual) && residual < 0.004f)
                    skinAbove = (x, y, v) =>
                    {
                        float want = eye.yMax + 0.002f + (v + 1f) * 0.5f * 0.011f; // lid bottom = right above the eye, top = higher
                        return new Vector2(fu.x * x + fu.y * want + fu.z, fv.x * x + fv.y * want + fv.z);
                    };
                // Check it: every bit of the lid must come out skin-coloured (no eye, lash or hair on it). Else flat.
                if (skinAbove != null)
                    for (int j = 0; j <= 8 && skinAbove != null; j++)
                        for (int i = 0; i <= 10; i++)
                        {
                            float u = -1f + 2f * i / 10f, v = -1f + 2f * j / 8f;
                            float x = eye.center.x + rx * u * Mathf.Sqrt(1f - 0.5f * v * v), y = eye.center.y + ry * v * Mathf.Sqrt(1f - 0.5f * u * u);
                            if (Distance(At(skinAbove(x, y, v)), skin) > 0.16f) { skinAbove = null; break; }
                        }
                Patch(eye.center, rx, ry, -1f, 1f, 0.004f, flat, bone, skinAbove);
                Patch(eye.center, rx, ry, -1f, -0.84f, 0.006f, darkUv, bone); // the lashes: a thin dark line along the lid's edge
                var hinge = new Vector2(eye.center.x, eye.center.y + ry);
                return new Vector3(hinge.x, hinge.y, SurfaceZ(hinge));
            }
            // Closed eyes painted into a copy of the texture (MeshyCharacters.ClosedEyes.cs); the old lid patches only
            // where that can't be done. Each eye as found, together with the other one mirrored (a badly found eye).
            Rect Both(Rect eye, Rect other) => Rect.MinMaxRect(Mathf.Min(eye.xMin, -other.xMax), Mathf.Min(eye.yMin, other.yMin),
                Mathf.Max(eye.xMax, -other.xMin), Mathf.Max(eye.yMax, other.yMax));
            Color lashColor = Color.Lerp(At(darkUv), Color.black, 0.35f);
            if (PaintClosedEyes(texture, vertices, normals, uvs, triangles, headJoint.y - 0.03f, new[] { Both(eyeL, eyeR), Both(eyeR, eyeL) },
                    SkinRoundColor(eyeL), lashColor, file))
            {
                lidL = new Vector3(eyeL.center.x, eyeL.yMax, SurfaceZ(new Vector2(eyeL.center.x, eyeL.yMax)));
                lidR = new Vector3(eyeR.center.x, eyeR.yMax, SurfaceZ(new Vector2(eyeR.center.x, eyeR.yMax)));
            }
            else
            {
                lidL = Lid(eyeL, eyeR, Bone.EyeL);
                lidR = Lid(eyeR, eyeL, Bone.EyeR);
            }

            // Mouth: lips (redder and bluer than the skin) and teeth, under the eyes, in the middle.
            float skinRed = Redness(skin), skinBlue = Blueness(skin), between = (eyeR.center.x - eyeL.center.x) * 0.5f;
            List<Sample> lips = samples.Where(s => Mathf.Abs(s.P.x) < between * 1.1f && s.P.y < level - 0.03f && s.P.y > bottom + 0.04f * height && s.N.z > 0.5f &&
                                                   (White(s.C) || (Redness(s.C) > skinRed + 0.06f && Blueness(s.C) > skinBlue + 0.03f))).ToList();
            string mouthNote = "no mouth found";
            mouthAt = new Vector3(0f, level - 0.06f, SurfaceZ(new Vector2(0f, level - 0.06f)));
            if (lips.Count >= 15)
            {
                var middle = new Vector2(Median(lips.Select(s => s.P.x)), Median(lips.Select(s => s.P.y)));
                lips = lips.Where(s => Vector2.Distance(new Vector2(s.P.x, s.P.y), middle) < 0.04f).ToList();
            }
            var fit = new FaceFit { VertexCount = vertices.Length, EyeL = Spot.At(eyeL, vertices, normals, faceFloor), EyeR = Spot.At(eyeR, vertices, normals, faceFloor) };
            if (basis != null)
            {
                // A look-alike's mouth is where its base's has gone.
                lips.Clear();
                if (basis.HasMouth && basis.Mouth.Anchors.Length >= 6)
                {
                    Rect box = basis.Mouth.On(vertices);
                    float rx = Mathf.Clamp(box.width * 0.3f, 0.009f, 0.019f);
                    // (No open-mouth patch: a dark disc on painted lips only ever looked like a hole.)
                    mouthAt = new Vector3(box.center.x, box.center.y, SurfaceZ(box.center));
                    mouthNote = "mouth where the base model's is";
                }
            }
            if (lips.Count >= 15)
            {
                float width = lips.Max(s => s.P.x) - lips.Min(s => s.P.x);
                // Where the lips meet: the darkest fifth of them (the line of the mouth, or its inside when it is open).
                List<Sample> dark = lips.Where(s => !White(s.C)).OrderBy(s => s.C.r + s.C.g + s.C.b).ToList();
                if (dark.Count < 5) dark = lips;
                List<Sample> line = dark.Take(Mathf.Max(5, dark.Count / 5)).ToList();
                var center = new Vector2(Mathf.Clamp(lips.Average(s => s.P.x), -0.012f, 0.012f), Median(line.Select(s => s.P.y)));
                // An open mouth as wide as a third of the lips (it was a 1-2 cm dot), the dark red of the line
                // between the lips, not the black of the lashes.
                float rx = Mathf.Clamp(width * 0.3f, 0.009f, 0.019f);
                Vector2 mouthUv = line.Count > 0 ? line[0].Uv : darkUv;
                // (No open-mouth patch: a dark disc on painted lips only ever looked like a hole.)
                mouthAt = new Vector3(center.x, center.y, SurfaceZ(center));
                mouthNote = $"mouth {width * 100f:0.0} cm wide at y {center.y:0.000}";
                fit.HasMouth = true;
                fit.Mouth = Spot.At(new Rect(center.x - width * 0.5f, center.y - 0.01f, width, 0.02f), vertices, normals, faceFloor);
            }
            if (baseFile == file) BaseFaces[file] = fit;
            Debug.Log($"[Build] body {file}: face {half * 200f:0.0} cm wide, eyes at y {eyeLevel:0.000} " +
                      $"({eyeL.width * 100f:0.0} x {eyeL.height * 100f:0.0} cm){eyeNote}, {mouthNote}");
            return true;
        }

        private static AvatarBody Bake(string glbPath, byte id, string displayName, string file)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (prefab == null) throw new InvalidOperationException($"GLB import failed: {glbPath}");
            GameObject model = Object.Instantiate(prefab);
            try
            {
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null) throw new InvalidOperationException($"{glbPath}: no skinned mesh (run prepare_character.py)");

                // Joint positions, from the GLB's bones.
                var joint = new Dictionary<Bone, Vector3>();
                var sourceToRig = new int[skin.bones.Length];
                for (int i = 0; i < skin.bones.Length; i++)
                {
                    sourceToRig[i] = -1;
                    if (skin.bones[i] != null && Enum.TryParse(skin.bones[i].name, out Bone bone) && bone < Bone.Count)
                    {
                        joint[bone] = skin.bones[i].position;
                        sourceToRig[i] = (int)bone;
                    }
                }
                foreach (Bone needed in new[] { Bone.Hips, Bone.Spine, Bone.Chest, Bone.Neck, Bone.Head, Bone.UpperArmL, Bone.ForearmL,
                             Bone.HandL, Bone.UpperArmR, Bone.ForearmR, Bone.HandR, Bone.ThighL, Bone.ShinL, Bone.FootL,
                             Bone.ThighR, Bone.ShinR, Bone.FootR })
                    if (!joint.ContainsKey(needed)) throw new InvalidOperationException($"{glbPath}: bone {needed} missing");

                // The mesh as it stands (A-pose), in model space.
                var posed = new Mesh();
                skin.BakeMesh(posed, true);
                Matrix4x4 toModel = skin.transform.localToWorldMatrix;
                Vector3[] vertices = posed.vertices.Select(v => toModel.MultiplyPoint3x4(v)).ToArray();
                Vector3[] normals = posed.normals.Select(n => toModel.MultiplyVector(n).normalized).ToArray();
                Vector4[] tangents = posed.tangents.Select(t =>
                {
                    Vector3 d = toModel.MultiplyVector(t).normalized;
                    return new Vector4(d.x, d.y, d.z, t.w);
                }).ToArray();

                // Skin weights: same weights, re-pointed at the rig's bone order.
                Mesh source = skin.sharedMesh;
                BoneWeight[] weights = source.boneWeights;
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    int Map(int index) => index >= 0 && index < sourceToRig.Length && sourceToRig[index] >= 0 ? sourceToRig[index] : (int)Bone.Hips;
                    w.boneIndex0 = Map(w.boneIndex0);
                    w.boneIndex1 = Map(w.boneIndex1);
                    w.boneIndex2 = Map(w.boneIndex2);
                    w.boneIndex3 = Map(w.boneIndex3);
                    weights[i] = w;
                }

                // prepare_character.py always exports facing glTF +Z, which is the rig's +Z here: no turning needed
                // (guessing from the feet fails on flip-flops, whose soles stick out behind as far as in front).
                if (joint[Bone.UpperArmL].x > 0f)
                    throw new InvalidOperationException($"{glbPath}: left arm on the right (not exported by prepare_character.py?)");

                var body = ScriptableObject.CreateInstance<AvatarBody>();
                body.Id = id;
                body.DisplayName = displayName;
                var rest = new Vector3[(int)Bone.Count];
                var bind = new Matrix4x4[BoneTotal];
                Vector3 P(Bone b) => joint[b];
                float Dist(Bone a, Bone b) => Vector3.Distance(P(a), P(b));
                Quaternion Along(Bone from, Bone to) => Quaternion.FromToRotation(Vector3.down, P(to) - P(from));
                void Set(Bone b, Vector3 local, Vector3 world, Quaternion bindRotation)
                {
                    rest[(int)b] = local;
                    bind[(int)b] = Matrix4x4.TRS(world, bindRotation, Vector3.one);
                }

                HandFit handFitL = null, handFitR = null;
                Set(Bone.Hips, P(Bone.Hips), P(Bone.Hips), Quaternion.identity);
                Set(Bone.Spine, P(Bone.Spine) - P(Bone.Hips), P(Bone.Spine), Quaternion.identity);
                Set(Bone.Chest, P(Bone.Chest) - P(Bone.Spine), P(Bone.Chest), Quaternion.identity);
                Set(Bone.Neck, P(Bone.Neck) - P(Bone.Chest), P(Bone.Neck), Quaternion.identity);
                Set(Bone.Head, P(Bone.Head) - P(Bone.Neck), P(Bone.Head), Quaternion.identity);
                foreach (bool left in new[] { true, false })
                {
                    Bone upper = left ? Bone.UpperArmL : Bone.UpperArmR, fore = left ? Bone.ForearmL : Bone.ForearmR;
                    Bone hand = left ? Bone.HandL : Bone.HandR, thigh = left ? Bone.ThighL : Bone.ThighR;
                    Bone shin = left ? Bone.ShinL : Bone.ShinR, foot = left ? Bone.FootL : Bone.FootR;
                    // Limbs: rest straight down their -Y; bound turned along the model's A-pose limb.
                    Set(upper, P(upper) - P(Bone.Chest), P(upper), Along(upper, fore));
                    Set(fore, new Vector3(0f, -Dist(upper, fore), 0f), P(fore), Along(fore, hand));
                    // The hand is bound in the game's own hand frame (see HandFit), and rests turned the way the model
                    // holds it; its fingers get bones of their own.
                    HandFit fit = FitHand(vertices, weights, (int)hand, P(hand), (P(hand) - P(fore)).normalized, left ? -1f : 1f, file);
                    Set(hand, new Vector3(0f, -Dist(fore, hand), 0f), P(hand), fit.Rotation);
                    Quaternion handRest = Quaternion.Inverse(Along(fore, hand)) * fit.Rotation;
                    if (left) { handFitL = fit; body.HandRestL = handRest; }
                    else { handFitR = fit; body.HandRestR = handRest; }
                    Set(thigh, P(thigh) - P(Bone.Hips), P(thigh), Along(thigh, shin));
                    Set(shin, new Vector3(0f, -Dist(thigh, shin), 0f), P(shin), Along(shin, foot));
                    Set(foot, new Vector3(0f, -Dist(shin, foot), 0f), P(foot), Quaternion.identity); // soles stay flat
                    // Shoulder helper (prepare_character.py): on the shoulder joint, bound unturned; AvatarRig turns it
                    // half as far as the upper arm has turned from its bind. Older GLBs have no skin on it.
                    Set(left ? Bone.ShoulderL : Bone.ShoulderR, P(upper) - P(Bone.Chest), P(upper), Quaternion.identity);
                }

                // Face and bust bones carry no mesh here, but gaze, expressions and jiggle still read them.
                float top = vertices.Max(v => v.y);
                float headSize = top - P(Bone.Head).y;
                float face = vertices.Where(v => v.y > P(Bone.Head).y && Mathf.Abs(v.x) < 0.06f).Select(v => v.z).DefaultIfEmpty(0.1f).Max() - P(Bone.Head).z;
                float eyeY = headSize * 0.42f;
                foreach (float side in new[] { -1f, 1f })
                {
                    bool left = side < 0f;
                    Vector3 eye = new Vector3(0.12f * headSize * side, eyeY, face * 0.9f);
                    Set(left ? Bone.EyeL : Bone.EyeR, eye, P(Bone.Head) + eye, Quaternion.identity);
                    Vector3 brow = eye + new Vector3(0f, 0.12f * headSize, 0f);
                    Set(left ? Bone.BrowL : Bone.BrowR, brow, P(Bone.Head) + brow, Quaternion.identity);
                    // Women come with bust bones (prepare_character.py --bust 1) weighted to the chest; else a stand-in spot.
                    Bone bustBone = left ? Bone.BustL : Bone.BustR;
                    Vector3 bust = joint.TryGetValue(bustBone, out Vector3 bustAt)
                        ? bustAt - P(Bone.Chest)
                        : new Vector3(0.08f * side, 0.02f, 0.09f) * (P(Bone.Hips).y / 0.92f);
                    Set(bustBone, bust, P(Bone.Chest) + bust, Quaternion.identity);
                }
                Vector3 mouth = new Vector3(0f, headSize * 0.2f, face * 0.9f);
                Set(Bone.Mouth, mouth, P(Bone.Head) + mouth, Quaternion.identity);
                for (int i = (int)Bone.Count; i < BoneTotal; i++) bind[i] = Matrix4x4.identity;
                // Fingers: the four fingers curl as one mitten on the finger bones (both hands or neither).
                body.HasFingers = handFitL.Fingers && handFitR.Fingers;
                if (body.HasFingers)
                {
                    SkinFingers(handFitL, vertices, weights, bind, (int)Bone.HandL, (int)Bone.Count, -1f);
                    SkinFingers(handFitR, vertices, weights, bind, (int)Bone.HandR, (int)Bone.Count + HandBones.BoneCount, 1f);
                    body.FingerBasesL = handFitL.StoredBases(-1f);
                    body.FingerBasesR = handFitR.StoredBases(1f);
                    body.FingerLengthsL = handFitL.Lengths;
                    body.FingerLengthsR = handFitR.Lengths;
                }

                body.RestPositions = rest;
                body.Scale = P(Bone.Hips).y / 0.92f;
                body.UpperArmLength = Dist(Bone.UpperArmL, Bone.ForearmL);
                body.ForearmLength = Dist(Bone.ForearmL, Bone.HandL);
                body.HandLength = body.ForearmLength * 0.385f; // the code-built hand/forearm ratio
                body.ThighLength = Dist(Bone.ThighL, Bone.ShinL);
                body.ShinLength = Dist(Bone.ShinL, Bone.FootL);
                body.AnkleHeight = P(Bone.FootL).y;
                body.HipHeight = P(Bone.Hips).y;
                body.EyeHeight = P(Bone.Head).y + eyeY;
                // The head's box (for things worn on it: the thief's beanie and bandana).
                Vector3 headAt = P(Bone.Head);
                body.HeadTop = headSize;
                body.HeadHalfWidth = body.HeadFront = body.HeadBack = 0.01f;
                foreach (Vector3 v in vertices)
                {
                    if (v.y < headAt.y + 0.03f) continue;
                    body.HeadHalfWidth = Mathf.Max(body.HeadHalfWidth, Mathf.Abs(v.x - headAt.x));
                    body.HeadFront = Mathf.Max(body.HeadFront, v.z - headAt.z);
                    body.HeadBack = Mathf.Max(body.HeadBack, headAt.z - v.z);
                }

                // The face: eyelids and an open mouth over the painted ones (found in the texture).
                var extra = new ExtraGeometry();
                Vector3 lidL = default, lidR = default, mouthAt = default;
                float eyeLevel = 0f;
                body.HasFace = !PaintedFaceOnly.Contains(file) && BuildFace(file, source, vertices, normals, weights, skin.sharedMaterial, P(Bone.Head), extra,
                    out lidL, out lidR, out mouthAt, out eyeLevel);
                if (body.HasFace)
                {
                    Set(Bone.EyeL, lidL - P(Bone.Head), lidL, Quaternion.identity);
                    Set(Bone.EyeR, lidR - P(Bone.Head), lidR, Quaternion.identity);
                    Set(Bone.Mouth, mouthAt - P(Bone.Head), mouthAt, Quaternion.identity);
                    body.EyeHeight = eyeLevel;
                    body.RestPositions = rest;
                }

                // The players' funny body: part ids for recolouring, the googly eyes, nose and teeth (MeshyCharacters.Funny.cs).
                FunnyFit funny = FunnyBodies.Contains(file)
                    ? FitFunny(file, source, vertices, normals, weights, skin.sharedMaterial, P(Bone.Head), P(Bone.Neck), P(Bone.Hips), P(Bone.Chest), P(Bone.Spine))
                    : null;

                string meshPath = $"{OutputDir}/{file}_mesh.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                bool isNew = mesh == null;
                if (isNew) mesh = new Mesh();
                mesh.Clear();
                mesh.name = file;
                mesh.indexFormat = source.indexFormat;
                bool hasTangents = tangents.Length == vertices.Length;
                int baseCount = vertices.Length;
                mesh.vertices = vertices.Concat(extra.Vertices).ToArray();
                mesh.normals = normals.Concat(extra.Normals).ToArray();
                if (hasTangents) mesh.tangents = tangents.Concat(extra.Vertices.Select(_ => new Vector4(1f, 0f, 0f, 1f))).ToArray();
                mesh.uv = source.uv.Concat(extra.Uvs).ToArray();
                mesh.subMeshCount = source.subMeshCount;
                for (int sm = 0; sm < source.subMeshCount; sm++)
                {
                    int[] triangles = source.GetTriangles(sm);
                    if (sm == 0) triangles = triangles.Concat(extra.Triangles.Select(t => t + baseCount)).ToArray();
                    mesh.SetTriangles(triangles, sm);
                }
                mesh.boneWeights = weights.Concat(extra.Weights).ToArray();
                mesh.bindposes = bind.Select(m => m.inverse).ToArray();
                if (funny != null) AddFunnyShapes(mesh, funny, vertices, weights, mesh.vertexCount);
                mesh.RecalculateBounds();
                if (isNew) AssetDatabase.CreateAsset(mesh, meshPath);
                else EditorUtility.SetDirty(mesh);
                body.Mesh = mesh;
                body.Material = BodyMaterial(skin.sharedMaterial, file);
                body.ClosedEyesMaterial = body.HasFace ? SaveClosedEyes(file, body.Material) : null; // (MeshyCharacters.ClosedEyes.cs)
                if (funny != null) ApplyFunny(body, funny, file, P(Bone.Head));

                string bodyPath = $"{OutputDir}/{file}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<AvatarBody>(bodyPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(body, existing);
                    existing.name = file;
                    Object.DestroyImmediate(body);
                    body = existing;
                    EditorUtility.SetDirty(body);
                }
                else
                {
                    AssetDatabase.CreateAsset(body, bodyPath);
                }
                Object.DestroyImmediate(posed);
                Debug.Log($"[Build] body {displayName}: {vertices.Length} vertices, hips {body.HipHeight:0.00} m, " +
                          $"shoulders {P(Bone.UpperArmL).y:0.00} m, fingers {(body.HasFingers ? "yes" : "NO")}, face {(body.HasFace ? "yes" : "NO")}");
                return body;
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }
    }
}
