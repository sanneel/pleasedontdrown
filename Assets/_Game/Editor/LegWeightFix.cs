using System;
using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Points of a body skinned to both legs at once: a strip of shoe stretched from one foot to the other, a flap of
    /// inner thigh hanging between the legs (the hotel attendants, seen once the motion clips took real strides). Each
    /// such point keeps only the leg it mostly belongs to.
    ///
    /// <b>ReportBatch</b> counts them per body, by height (shins and feet, thighs, crotch). The crotch is blended on purpose
    /// on every body (shorts and skirts don't tear there), and so are skirts, shorts and coats hanging between the thighs,
    /// so <b>FixBatch</b> only fixes the bodies named with <c>-bodies a,b</c>, below the crotch, in place.
    /// </summary>
    public static class LegWeightFix
    {
        private static readonly int[] LeftLeg = { (int)Bone.ThighL, (int)Bone.ShinL, (int)Bone.FootL };
        private static readonly int[] RightLeg = { (int)Bone.ThighR, (int)Bone.ShinR, (int)Bone.FootR };

        public static void ReportBatch() => Run(false);

        public static void FixBatch() => Run(true);

        private static void Run(bool fix)
        {
            try
            {
                int fixedBodies = 0;
                string[] args = Environment.GetCommandLineArgs();
                int bi = Array.IndexOf(args, "-bodies");
                var named = new HashSet<string>(bi >= 0 && bi + 1 < args.Length ? args[bi + 1].Split(',') : Array.Empty<string>());
                if (fix && named.Count == 0) throw new ArgumentException("-bodies <name,name> (fix only bodies seen to need it)");
                foreach (string guid in AssetDatabase.FindAssets("t:AvatarBody"))
                {
                    var body = AssetDatabase.LoadAssetAtPath<AvatarBody>(AssetDatabase.GUIDToAssetPath(guid));
                    if (body == null || body.Mesh == null) continue;
                    bool fixThis = fix && named.Contains(body.name);
                    Mesh mesh = body.Mesh;
                    BoneWeight[] weights = mesh.boneWeights;
                    Vector3[] points = mesh.vertices; // (root space: the feet on y = 0)
                    float hip = Mathf.Max(0.3f, body.HipHeight);
                    int crossing = 0, low = 0, thigh = 0, crotch = 0;
                    for (int v = 0; v < weights.Length; v++)
                    {
                        float left = Share(weights[v], LeftLeg), right = Share(weights[v], RightLeg);
                        if (left <= 0.02f || right <= 0.02f) continue;
                        crossing++;
                        float h = points[v].y / hip;
                        if (h < 0.5f) low++;            // knees and below: a shoe or a shin on both legs is always wrong
                        else if (h < 0.8f) thigh++;     // the thighs: wrong unless it's a skirt hanging between them
                        else crotch++;                  // the crotch: blended on purpose (shorts and skirts don't tear)
                        if (fixThis && h < 0.8f) weights[v] = Keep(weights[v], left >= right ? RightLeg : LeftLeg);
                    }
                    Debug.Log($"[LegWeights] {body.name}: {crossing} of {weights.Length} points on both legs: {low} at the shins and feet, {thigh} on the thighs, {crotch} at the crotch");
                    if (fixThis && low + thigh > 0)
                    {
                        mesh.boneWeights = weights;
                        EditorUtility.SetDirty(mesh);
                        fixedBodies++;
                    }
                }
                if (fix)
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[LegWeights] fixed {fixedBodies} bodies");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LegWeights] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// WebBatch: triangles joining the two legs below the crotch (one corner on the left leg, another on the right):
        /// a sheet between the shoes, a web between the thighs. Counts them per body, by height.
        /// </summary>
        public static void WebBatch()
        {
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:AvatarBody"))
                {
                    var body = AssetDatabase.LoadAssetAtPath<AvatarBody>(AssetDatabase.GUIDToAssetPath(guid));
                    if (body == null || body.Mesh == null || body.name.Contains("_v")) continue;
                    Mesh mesh = body.Mesh;
                    BoneWeight[] weights = mesh.boneWeights;
                    Vector3[] points = mesh.vertices;
                    float hip = Mathf.Max(0.3f, body.HipHeight);
                    int[] side = new int[weights.Length]; // -1 left leg, +1 right leg, 0 neither
                    for (int v = 0; v < weights.Length; v++)
                    {
                        float l = Share(weights[v], LeftLeg), r = Share(weights[v], RightLeg);
                        side[v] = l > 0.5f ? -1 : r > 0.5f ? 1 : 0;
                    }
                    int low = 0, thigh = 0, crotch = 0;
                    float longest = 0f;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        int[] tris = mesh.GetTriangles(sub);
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                            bool hasL = side[a] < 0 || side[b] < 0 || side[c] < 0, hasR = side[a] > 0 || side[b] > 0 || side[c] > 0;
                            if (!hasL || !hasR) continue;
                            float h = Mathf.Min(points[a].y, Mathf.Min(points[b].y, points[c].y)) / hip;
                            if (h < 0.5f) low++;
                            else if (h < 0.8f) thigh++;
                            else crotch++;
                            longest = Mathf.Max(longest, Mathf.Max((points[a] - points[b]).magnitude, Mathf.Max((points[b] - points[c]).magnitude, (points[c] - points[a]).magnitude)));
                        }
                    }
                    Debug.Log($"[LegWeights] web {body.name}: triangles joining the legs: {low} at the shins and feet, {thigh} on the thighs, {crotch} at the crotch (longest edge {longest:0.00} m)");
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LegWeights] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// FixWebBatch <c>-bodies a,b</c>: removes those bodies' triangles joining the legs below the crotch (made where
        /// the model stood with heels and thighs touching; they stretched into a strip between the shoes and a flap between
        /// the thighs as soon as the legs parted). The meshes are fixed in place.
        /// </summary>
        public static void FixWebBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int bi = Array.IndexOf(args, "-bodies");
                var named = new HashSet<string>(bi >= 0 && bi + 1 < args.Length ? args[bi + 1].Split(',') : Array.Empty<string>());
                if (named.Count == 0) throw new ArgumentException("-bodies <name,name>");
                foreach (string guid in AssetDatabase.FindAssets("t:AvatarBody"))
                {
                    var body = AssetDatabase.LoadAssetAtPath<AvatarBody>(AssetDatabase.GUIDToAssetPath(guid));
                    if (body == null || body.Mesh == null || !named.Contains(body.name)) continue;
                    Mesh mesh = body.Mesh;
                    BoneWeight[] weights = mesh.boneWeights;
                    Vector3[] points = mesh.vertices;
                    float hip = Mathf.Max(0.3f, body.HipHeight);
                    int removed = 0;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        int[] tris = mesh.GetTriangles(sub);
                        var kept = new List<int>(tris.Length);
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                            int sa = Side(weights[a]), sb = Side(weights[b]), sc = Side(weights[c]);
                            bool joins = (sa < 0 || sb < 0 || sc < 0) && (sa > 0 || sb > 0 || sc > 0);
                            float h = Mathf.Min(points[a].y, Mathf.Min(points[b].y, points[c].y)) / hip;
                            if (joins && h < 0.8f) { removed++; continue; }
                            kept.Add(a); kept.Add(b); kept.Add(c);
                        }
                        mesh.SetTriangles(kept, sub);
                    }
                    EditorUtility.SetDirty(mesh);
                    Debug.Log($"[LegWeights] {body.name}: removed {removed} triangles joining the legs");
                }
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LegWeights] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// FixStretchBatch <c>-bodies a,b</c>: the body put in wide strides (each leg forward, then apart to the sides);
        /// a triangle below the crotch that stretches to more than three times its length in any of them is a web between
        /// the legs (the rest of the attendants' slivers, their corners shared by both legs) and is removed.
        /// </summary>
        public static void FixStretchBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int bi = Array.IndexOf(args, "-bodies");
                var named = new HashSet<string>(bi >= 0 && bi + 1 < args.Length ? args[bi + 1].Split(',') : Array.Empty<string>());
                if (named.Count == 0) throw new ArgumentException("-bodies <name,name>");
                float Arg(string name, float fallback)
                {
                    int i = Array.IndexOf(args, name);
                    return i >= 0 && i + 1 < args.Length ? float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : fallback;
                }
                float ratio = Arg("-ratio", 3f), shortest = Arg("-min", 0.05f);
                AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                foreach (string guid in AssetDatabase.FindAssets("t:AvatarBody"))
                {
                    var body = AssetDatabase.LoadAssetAtPath<AvatarBody>(AssetDatabase.GUIDToAssetPath(guid));
                    if (body == null || body.Mesh == null || !named.Contains(body.name)) continue;
                    var go = new GameObject("Stretch " + body.name);
                    var rig = go.AddComponent<AvatarRig>();
                    rig.Build(new AvatarLook { Body = (byte)body.Id, Figure = 1 });
                    UnityEngine.Avatar avatar = AvatarClips.BuildAvatar(rig);
                    var handler = new HumanPoseHandler(avatar, rig.transform);
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);
                    float[] rest = (float[])pose.muscles.Clone();
                    int Muscle(string name) => Array.IndexOf(HumanTrait.MuscleName, name);
                    int lfb = Muscle("Left Upper Leg Front-Back"), rfb = Muscle("Right Upper Leg Front-Back");
                    int lio = Muscle("Left Upper Leg In-Out"), rio = Muscle("Right Upper Leg In-Out");
                    int lst = Muscle("Left Lower Leg Stretch"), rst = Muscle("Right Lower Leg Stretch");

                    Mesh mesh = body.Mesh;
                    Vector3[] bind = mesh.vertices;
                    float hip = Mathf.Max(0.3f, body.HipHeight);
                    var stretched = new HashSet<long>();
                    var baked = new Mesh();
                    foreach (var (lf, rf, li, ri) in new[] { (1f, -1f, 0f, 0f), (-1f, 1f, 0f, 0f), (0.3f, 0.3f, 1f, 1f), (0.8f, -0.6f, 0.5f, 0.5f) })
                    {
                        Array.Copy(rest, pose.muscles, rest.Length);
                        pose.muscles[lfb] = Mathf.Lerp(rest[lfb], lf > 0 ? -1f : 1f, Mathf.Abs(lf));
                        pose.muscles[rfb] = Mathf.Lerp(rest[rfb], rf > 0 ? -1f : 1f, Mathf.Abs(rf));
                        pose.muscles[lio] = Mathf.Lerp(rest[lio], 1f, li);
                        pose.muscles[rio] = Mathf.Lerp(rest[rio], 1f, ri);
                        pose.muscles[lst] = rest[lst];
                        pose.muscles[rst] = rest[rst];
                        handler.SetHumanPose(ref pose);
                        rig.Renderer.BakeMesh(baked, true);
                        Vector3[] posed = baked.vertices;
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            int[] tris = mesh.GetTriangles(sub);
                            for (int t = 0; t < tris.Length; t += 3)
                            {
                                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                                if (Mathf.Min(bind[a].y, Mathf.Min(bind[b].y, bind[c].y)) / hip >= 0.8f) continue;
                                float before = Mathf.Max((bind[a] - bind[b]).magnitude, Mathf.Max((bind[b] - bind[c]).magnitude, (bind[c] - bind[a]).magnitude));
                                float after = Mathf.Max((posed[a] - posed[b]).magnitude, Mathf.Max((posed[b] - posed[c]).magnitude, (posed[c] - posed[a]).magnitude));
                                if (after > shortest && after > ratio * Mathf.Max(before, 0.002f)) stretched.Add(((long)sub << 32) | (uint)t);
                            }
                        }
                    }
                    int removed = 0;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        int[] tris = mesh.GetTriangles(sub);
                        var kept = new List<int>(tris.Length);
                        for (int t = 0; t < tris.Length; t += 3)
                        {
                            if (stretched.Contains(((long)sub << 32) | (uint)t)) { removed++; continue; }
                            kept.Add(tris[t]); kept.Add(tris[t + 1]); kept.Add(tris[t + 2]);
                        }
                        mesh.SetTriangles(kept, sub);
                    }
                    EditorUtility.SetDirty(mesh);
                    Debug.Log($"[LegWeights] {body.name}: removed {removed} triangles that stretch between the legs");
                    handler.Dispose();
                    UnityEngine.Object.DestroyImmediate(avatar);
                    UnityEngine.Object.DestroyImmediate(baked);
                    UnityEngine.Object.DestroyImmediate(go);
                }
                AssetDatabase.SaveAssets();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[LegWeights] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static int Side(BoneWeight w)
        {
            float l = Share(w, LeftLeg), r = Share(w, RightLeg);
            return l > 0.5f ? -1 : r > 0.5f ? 1 : 0;
        }

        private static float Share(BoneWeight w, int[] bones)
        {
            float s = 0f;
            if (Array.IndexOf(bones, w.boneIndex0) >= 0) s += w.weight0;
            if (Array.IndexOf(bones, w.boneIndex1) >= 0) s += w.weight1;
            if (Array.IndexOf(bones, w.boneIndex2) >= 0) s += w.weight2;
            if (Array.IndexOf(bones, w.boneIndex3) >= 0) s += w.weight3;
            return s;
        }

        /// <summary>The weights without the other leg's bones, the rest scaled back up to a whole.</summary>
        private static BoneWeight Keep(BoneWeight w, int[] drop)
        {
            var list = new List<(int bone, float weight)>(4);
            void Add(int bone, float weight) { if (weight > 0f && Array.IndexOf(drop, bone) < 0) list.Add((bone, weight)); }
            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            float total = 0f;
            foreach (var (_, weight) in list) total += weight;
            list.Sort((a, b) => b.weight.CompareTo(a.weight));
            var result = new BoneWeight();
            for (int i = 0; i < list.Count; i++)
            {
                float weight = total > 0f ? list[i].weight / total : 0f;
                switch (i)
                {
                    case 0: result.boneIndex0 = list[i].bone; result.weight0 = weight; break;
                    case 1: result.boneIndex1 = list[i].bone; result.weight1 = weight; break;
                    case 2: result.boneIndex2 = list[i].bone; result.weight2 = weight; break;
                    case 3: result.boneIndex3 = list[i].bone; result.weight3 = weight; break;
                }
            }
            return result;
        }
    }
}
