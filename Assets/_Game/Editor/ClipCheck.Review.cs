using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Rescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Reviewing clips frame by frame.
    ///
    /// <b>SheetBatch</b>: every clip in Resources/Mixamo (or <c>-clips "A|B"</c>) played alone on a few bodies
    /// (<c>-bodies 16,8</c>), eight moments from start to end, standing on a floor: pictures to
    /// Screenshots/ClipSheets/&lt;clip&gt;/&lt;body&gt;_&lt;n&gt;.jpg (ArtSource/Tools/clip_sheets.py lays them out), and
    /// a log line per clip and body: its length, whether it loops, where the hips start and end, how low the feet go.
    ///
    /// <b>StatesBatch</b>: the animator itself (clips on) through what the game does: talking (calm and angry), walking,
    /// knocked down and getting up, terrified, treading water, CPR on a real tourist with compressions. Ten pictures a
    /// second to Screenshots/ClipStates/&lt;state&gt;_&lt;body&gt;/; the clip action in the log.
    /// </summary>
    public static partial class ClipCheck
    {
        /// <summary>
        /// Outside play mode bodies never run OnEnable, so BodySpace (hands, knees and trunks kept out of other people)
        /// doesn't know them: register them as the game does, or the checks see nobody to keep out of.
        /// </summary>
        private static void InBodySpace(AvatarRig rig, bool on)
        {
            if (rig == null) return;
            System.Reflection.MethodInfo method = typeof(BodySpace).GetMethod(on ? "Add" : "Remove",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            method?.Invoke(null, new object[] { rig });
        }

        private static readonly AvatarRig.Bone[] AllRigBones = (AvatarRig.Bone[])Enum.GetValues(typeof(AvatarRig.Bone));

        private static string Arg(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        private static byte[] BodiesArg(string fallback) =>
            Arg("-bodies", fallback).Split(',').Select(b => byte.Parse(b.Trim(), CultureInfo.InvariantCulture)).ToArray();

        private static Camera ReviewCamera()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.farClipPlane = 2000f;
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 34f;
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            return camera;
        }

        /// <summary>A grey floor under a spot high over the sea (to see feet on the ground, knees on it, a body lying).</summary>
        private static GameObject Floor(Vector3 at)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ReviewFloor";
            floor.transform.position = at - Vector3.up * 0.05f;
            floor.transform.localScale = new Vector3(8f, 0.1f, 8f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.62f, 0.6f, 0.55f) };
            floor.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            return floor;
        }

        public static void SheetBatch()
        {
            try
            {
                Camera camera = ReviewCamera();
                var at = new Vector3(400f, 300f, 0f);
                GameObject floor = Floor(at);
                string clipsArg = Arg("-clips", "");
                IEnumerable<string> files = clipsArg.Length > 0
                    ? clipsArg.Split('|')
                    : AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Resources/Mixamo" })
                        .Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))).OrderBy(f => f);
                byte[] bodies = BodiesArg("16,8");
                const string root = "Screenshots/ClipSheets";
                Directory.CreateDirectory(root);
                foreach (string file in files)
                {
                    AnimationClip clip = AvatarClips.Get(file);
                    if (clip == null) { Debug.LogWarning($"[ClipReview] no clip {file}"); continue; }
                    string folder = $"{root}/{file}";
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    Directory.CreateDirectory(folder);
                    foreach (byte body in bodies)
                    {
                        var go = new GameObject("Sheet " + body);
                        go.transform.position = at;
                        AvatarRig rig = Spawn(go, body);
                        var clips = new AvatarClips(rig);
                        if (!clips.Prepare()) throw new InvalidOperationException("clips didn't prepare");
                        float lowestFoot = float.MaxValue;
                        Vector3 first = Vector3.zero, last = Vector3.zero;
                        for (int n = 0; n < 8; n++)
                        {
                            float t = clip.length * n / 7f;
                            rig.ResetPose();
                            clips.Action(file, t, 1f);
                            clips.Evaluate(0f, 0f);
                            Vector3 hips = go.transform.InverseTransformPoint(rig[AvatarRig.Bone.Hips].position);
                            if (n == 0) first = hips;
                            last = hips;
                            lowestFoot = Mathf.Min(lowestFoot, rig[AvatarRig.Bone.FootL].position.y - at.y, rig[AvatarRig.Bone.FootR].position.y - at.y);
                            // Framed on the body wherever the clip has taken it (a fall goes a metre back).
                            Vector3 mid = new Vector3(rig[AvatarRig.Bone.Hips].position.x, at.y + 0.75f, rig[AvatarRig.Bone.Hips].position.z);
                            camera.transform.position = mid + new Vector3(-3.6f, 0.9f, 2.4f);
                            camera.transform.LookAt(mid - Vector3.up * 0.15f);
                            Render(camera, $"{folder}/{body:00}_{n}.jpg", 360, 420);
                        }
                        Debug.Log($"[ClipReview] {file} body {body}: {clip.length:0.00} s, loops {clip.isLooping}, hips {first:F2} -> {last:F2}, feet down to {lowestFoot:F2} m");
                        clips.Release();
                        Object.DestroyImmediate(go);
                    }
                }
                Object.DestroyImmediate(floor);
                Debug.Log("[ClipReview] sheets done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipReview] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>CurveBatch: for <c>-clips "A|B"</c> on Lola, the hips' height, place and facing every 0.1 s (finding
        /// where in a clip the fall or the getting up really happens).</summary>
        public static void CurveBatch()
        {
            try
            {
                AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                foreach (string file in Arg("-clips", "").Split('|'))
                {
                    AnimationClip clip = AvatarClips.Get(file);
                    if (clip == null) continue;
                    var go = new GameObject("Curve");
                    AvatarRig rig = Spawn(go, AvatarLook.Bodies.Lola);
                    var clips = new AvatarClips(rig);
                    clips.Prepare();
                    for (float t = 0f; t <= clip.length + 0.001f; t += 0.1f)
                    {
                        rig.ResetPose();
                        clips.Action(file, t, 1f);
                        clips.Evaluate(0f, 0f);
                        Transform hips = rig[AvatarRig.Bone.Hips];
                        Debug.Log($"[ClipCurve] {file} {t:0.0}: hips {hips.position:F2} facing {AvatarClips.FacingYaw(hips.rotation):0}");
                    }
                    clips.Release();
                    Object.DestroyImmediate(go);
                }
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipReview] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// CprBatch: CPR exactly as the game stages it, on every body there is. Each one (<c>-bodies</c>, default all
        /// but the lifeguard) flat on its back on a floor as the real tourist prefab lies; the lifeguard (each of his
        /// four bellies) where PlayerAvatar.KneelBeside puts him, pressing every 0.6 s. At the bottom of the presses it
        /// measures where the hands are on that body: the lower palm's height over its skin right under it (on it, or
        /// floating, or sunk), how far the stacked hands are off the body's middle and up or down it from the press
        /// point, how deep any of his hand or the rest of him goes into it (his baked skin), and how bent the elbows
        /// are. Close-ups of the hands from across and above to Screenshots/CprCheck/. Ends "[CprCheck] ALL PASS" or
        /// lists what failed. <c>-clips</c>: with the clips on.
        /// </summary>
        public static void CprBatch()
        {
            try
            {
                Camera camera = ReviewCamera();
                bool clips = Environment.GetCommandLineArgs().Contains("-clips");
                const string root = "Screenshots/CprCheck";
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(root);
                var victimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab");
                byte[] bodies = BodiesArg(string.Join(",", AllBodies.Where(b => b != AvatarLook.Bodies.Goofy)));
                int bellies = int.Parse(Arg("-bellies", "4"), CultureInfo.InvariantCulture);
                VictimBody.PressTOverride = float.Parse(Arg("-press-t", "-1"), CultureInfo.InvariantCulture);
                int failures = 0, k = 0;
                var failed = new List<string>();
                foreach (byte body in bodies)
                    for (byte belly = 0; belly < bellies; belly++)
                    {
                        var at = new Vector3(400f + 12f * (k % 12), 300f, 12f * (k / 12));
                        k++;
                        GameObject floor = Floor(at);
                        GameObject victimObject = Object.Instantiate(victimPrefab);
                        foreach (Rigidbody rb in victimObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                        var victim = victimObject.GetComponent<VictimBody>();
                        victimObject.transform.SetPositionAndRotation(at + Vector3.up * 0.14f, Quaternion.LookRotation(Vector3.up, Vector3.right));
                        victim.ApplyLooks(new AvatarLook { Body = body, Figure = (byte)(AvatarLook.Bodies.IsFeminine(body) ? 1 : 0) }, body);
                        AvatarRig victimRig = victimObject.GetComponentInChildren<AvatarRig>();
                        if (victimRig != null && victimRig.Renderer != null) victimRig.Renderer.forceMatrixRecalculationPerRender = true;
                        Vector3 press = victim.PressPoint, head = victim.HeadPosition;
                        // Where that is on the body: 0 at the shoulders, 1 at the hip joints (the lower breastbone is about a third).
                        float torsoAt = 0f;
                        if (victimRig != null)
                        {
                            Vector3 sh = (victimRig[AvatarRig.Bone.UpperArmL].position + victimRig[AvatarRig.Bone.UpperArmR].position) * 0.5f;
                            Vector3 hp = (victimRig[AvatarRig.Bone.ThighL].position + victimRig[AvatarRig.Bone.ThighR].position) * 0.5f;
                            torsoAt = Vector3.Dot(press - sh, (hp - sh).normalized) / Mathf.Max(0.01f, (hp - sh).magnitude);
                        }
                        // As PlayerAvatar.OnPump / KneelBeside.
                        Vector3 down = press - head;
                        down.y = 0f;
                        float reach = victim.KneelReach + 0.07f;
                        Vector3 along = -down.normalized, across = Vector3.Cross(Vector3.up, along);
                        Vector3 kneel = VictimBody.KneelSpot(press, head);
                        Vector3 spot = new Vector3(kneel.x, at.y, kneel.z) + across * reach;
                        Quaternion facing = Quaternion.LookRotation(-across, Vector3.up);

                        var go = new GameObject("Rescuer");
                        go.transform.SetPositionAndRotation(spot, facing);
                        AvatarLook rescuerLook = AvatarLook.Lifeguard;
                        rescuerLook.Belly = belly;
                        var rig = go.AddComponent<AvatarRig>();
                        rig.Build(rescuerLook);
                        if (rig.Renderer != null) rig.Renderer.forceMatrixRecalculationPerRender = true;
                        var animator = go.AddComponent<AvatarAnimator>();
                        animator.Rig = rig;
                        animator.UseClips = clips;
                        animator.RootOverride = facing;
                        InBodySpace(victimRig, true);
                        InBodySpace(rig, true);

                        const float dt = 1f / 30f;
                        var baked = new Mesh();
                        // Her real skin, to measure the hands against: her mesh as it lies, baked into a collider.
                        var skinMesh = new Mesh();
                        var skinObject = new GameObject("PatientSkin");
                        MeshCollider skinCollider = skinObject.AddComponent<MeshCollider>();
                        if (victimRig != null && victimRig.Renderer is SkinnedMeshRenderer victimSkin)
                        {
                            victimSkin.BakeMesh(skinMesh, true);
                            skinObject.transform.SetPositionAndRotation(victimSkin.transform.position, victimSkin.transform.rotation);
                            skinCollider.sharedMesh = skinMesh;
                        }
                        Physics.SyncTransforms();
                        // Straight down from over a point to her skin (her height there), or NaN when she isn't under it.
                        float SkinUnder(Vector3 p) =>
                            skinCollider.Raycast(new Ray(new Vector3(p.x, at.y + 1.5f, p.z), Vector3.down), out RaycastHit hit, 2f) ? hit.point.y : float.NaN;
                        string SkinOwner(Vector3 p)
                        {
                            if (!skinCollider.Raycast(new Ray(new Vector3(p.x, at.y + 1.5f, p.z), Vector3.down), out RaycastHit hit, 2f)) return "-";
                            var vs = victimRig.Renderer as SkinnedMeshRenderer;
                            int v = skinMesh.triangles[hit.triangleIndex * 3];
                            BoneWeight bw = vs.sharedMesh.boneWeights[v];
                            Vector3 o = hit.point - press;
                            return $"{vs.bones[bw.boneIndex0].name}, {Vector3.Dot(o, along) * 100f:0} cm toward her head, {Vector3.Dot(o, across) * 100f:0} toward him, {o.y * 100f:0} up from the press point";
                        }
                        string deepestFinger = "";
                        var handBones = new HashSet<Transform>();
                        foreach (bool right in new[] { false, true })
                        {
                            handBones.Add(rig[right ? AvatarRig.Bone.HandR : AvatarRig.Bone.HandL]);
                            HandBones hb = rig.Hand(right);
                            if (hb != null) foreach (Transform t in hb.Bones) handBones.Add(t);
                        }
                        // How deep his hands and the rest of him go into the patient (his baked skin, by what it's weighted to).
                        string restParts = "", deepestAt = "";
                        (float hands, float rest) Inside()
                        {
                            var smr = rig.Renderer as SkinnedMeshRenderer;
                            if (smr == null || victimRig == null) return (0f, 0f);
                            smr.BakeMesh(baked, true);
                            Vector3[] verts = baked.vertices;
                            BoneWeight[] weights = smr.sharedMesh.boneWeights;
                            Transform[] bones = smr.bones;
                            float hands = 0f, rest = 0f;
                            var parts = new Dictionary<string, float>();
                            for (int i = 0; i < verts.Length; i += 2)
                            {
                                float d = BodySpace.DepthIn(victimRig, smr.transform.TransformPoint(verts[i]), 0f);
                                if (d <= 0f) continue;
                                Transform owner = i < weights.Length && weights[i].boneIndex0 < bones.Length ? bones[weights[i].boneIndex0] : null;
                                if (owner != null && handBones.Contains(owner)) { hands = Mathf.Max(hands, d); continue; }
                                if (d > rest)
                                {
                                    Vector3 w = smr.transform.TransformPoint(verts[i]);
                                    string near = victimRig == null ? "" : AllRigBones.OrderBy(b => victimRig[b] == null ? 99f : (victimRig[b].position - w).sqrMagnitude).First().ToString();
                                    deepestAt = $"at {go.transform.InverseTransformPoint(w):F2} (his space), near her {near}";
                                }
                                rest = Mathf.Max(rest, d);
                                if (d > 0.01f && owner != null) parts[owner.name] = Mathf.Max(parts.TryGetValue(owner.name, out float was) ? was : 0f, d);
                            }
                            if (parts.Count > 0) restParts = string.Join(" ", parts.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value * 100f:0.0}"));
                            return (hands, rest);
                        }
                        // His hand's own skin over hers: the lowest gap of the palm (vertices on the hand bone) and of the
                        // fingers (on the finger bones), each against her skin straight below it. Negative: in her.
                        var handVerts = new Mesh();
                        (float palm, float fingers) HandGaps(bool right)
                        {
                            var smr = rig.Renderer as SkinnedMeshRenderer;
                            if (smr == null) return (1f, 1f);
                            smr.BakeMesh(handVerts, true);
                            Vector3[] verts = handVerts.vertices;
                            BoneWeight[] weights = smr.sharedMesh.boneWeights;
                            Transform[] bones = smr.bones;
                            Transform handBone = rig[right ? AvatarRig.Bone.HandR : AvatarRig.Bone.HandL];
                            HandBones hb = rig.Hand(right);
                            var fingerSet = hb != null ? new HashSet<Transform>(hb.Bones) : new HashSet<Transform>();
                            float palm = 1f, fingers = 1f;
                            for (int i = 0; i < verts.Length && i < weights.Length; i++)
                            {
                                int b = weights[i].boneIndex0;
                                if (b >= bones.Length) continue;
                                bool onPalm = bones[b] == handBone, onFinger = fingerSet.Contains(bones[b]);
                                if (!onPalm && !onFinger) continue;
                                Vector3 w = smr.transform.TransformPoint(verts[i]);
                                float skinY = SkinUnder(w);
                                if (float.IsNaN(skinY)) continue;
                                float gap = w.y - skinY;
                                if (onPalm) palm = Mathf.Min(palm, gap);
                                else
                                {
                                    if (gap < fingers && gap < -0.02f) deepestFinger = $"{bones[b].name} {gap * 100f:0.0} cm into {SkinOwner(w)}";
                                    fingers = Mathf.Min(fingers, gap);
                                }
                            }
                            return (palm, fingers);
                        }
                        Vector3 Palm(bool right)
                        {
                            Transform hand = rig[right ? AvatarRig.Bone.HandR : AvatarRig.Bone.HandL];
                            return hand.position - hand.up * (rig.HandLength * 0.5f);
                        }
                        float Elbow(bool right)
                        {
                            Transform u = rig[right ? AvatarRig.Bone.UpperArmR : AvatarRig.Bone.UpperArmL], f = rig[right ? AvatarRig.Bone.ForearmR : AvatarRig.Bone.ForearmL],
                                h = rig[right ? AvatarRig.Bone.HandR : AvatarRig.Bone.HandL];
                            return 180f - Vector3.Angle(u.position - f.position, h.position - f.position);
                        }

                        float worstGap = 0f, worstSide = 0f, worstHands = 0f, worstRest = 0f, alongAt = 0f, elbow = 0f, worstFingers = 1f;
                        for (int frame = 0; frame < 150; frame++)
                        {
                            float t = frame * dt;
                            AvatarAnimator.TimeOverride = 400f + t;
                            if (frame >= 30 && (frame - 30) % 18 == 0) animator.Play(AvatarGesture.Pump, press);
                            animator.Motion = new AvatarMotion { FacingYaw = facing.eulerAngles.y, Grounded = true, Cpr = true, CprPoint = press, CprClear = victim.SideClearToward(spot) };
                            animator.Tick(dt);
                            // At the bottom of a press (two frames after it) once kneeling: where the hands are on this body.
                            if (frame < 66 || (frame - 30) % 18 != 2) continue;
                            Vector3 lower = Palm(true), upper = Palm(false), mid = (lower + upper) * 0.5f;
                            // The lower palm's skin on hers (its lowest point over her skin), and how far any finger of
                            // either hand goes into her.
                            (float palmGap, float lowerFingers) = HandGaps(true);
                            (_, float upperFingers) = HandGaps(false);
                            // (Hands resting on their fingertips on something standing up from the chest, a stiff coat's
                            // lapels, rest on it: up to 4.5 cm over the skin under them.)
                            float gap = palmGap > 0.03f && palmGap < 0.045f && Mathf.Min(lowerFingers, upperFingers) < 0.01f ? Mathf.Min(lowerFingers, upperFingers) : palmGap;
                            if (deepestFinger != "") Debug.Log($"[CprCheck]     body {body} frame {frame}: finger {deepestFinger} (press at {victimRig.transform.InverseTransformPoint(press)})");
                            deepestFinger = "";
                            worstFingers = Mathf.Min(worstFingers, lowerFingers, upperFingers);
                            Vector3 off = mid - press;
                            float side = Vector3.Dot(off, across);
                            (float hands, float rest) = Inside();
                            if (Mathf.Abs(gap) > Mathf.Abs(worstGap)) worstGap = gap;
                            if (Mathf.Abs(side) > Mathf.Abs(worstSide)) worstSide = side;
                            worstHands = Mathf.Max(worstHands, hands);
                            worstRest = Mathf.Max(worstRest, rest);
                            alongAt = Vector3.Dot(off, along);
                            elbow = Mathf.Max(Elbow(true), Elbow(false));
                        }
                        // Pictures (one lifeguard per body: normal belly and the beach ball).
                        if (belly == 0 || belly == 2)
                        {
                            Vector3 hands = (Palm(true) + Palm(false)) * 0.5f;
                            foreach ((string view, Vector3 offset, float fov) in new[] { ("across", -across * 1.1f + Vector3.up * 0.45f + along * 0.1f, 40f),
                                         ("top", Vector3.up * 1.3f - across * 0.25f + along * 0.15f, 40f), ("feet", -along * 1.3f + Vector3.up * 0.3f, 40f),
                                         ("wide", -across * 2.2f + Vector3.up * 1.0f - along * 0.6f, 40f),
                                         ("head", along * 0.75f + Vector3.up * 0.75f - across * 0.15f, 45f) })
                            {
                                camera.fieldOfView = fov;
                                camera.transform.position = hands + offset;
                                camera.transform.LookAt(view == "wide" ? Vector3.Lerp(hands, rig[AvatarRig.Bone.Chest].position, 0.4f) : hands);
                                Render(camera, $"{root}/b{body:00}_belly{belly}_{view}.jpg", 520, 420);
                            }
                        }
                        AvatarAnimator.TimeOverride = null;
                        var why = new List<string>();
                        if (Mathf.Abs(worstGap) > 0.03f) why.Add(worstGap > 0f ? $"palm {worstGap * 100f:0} cm over the skin" : $"palm {-worstGap * 100f:0} cm into the skin");
                        if (Mathf.Abs(worstSide) > 0.04f) why.Add($"hands {Mathf.Abs(worstSide) * 100f:0} cm off the middle");
                        // (At the bottom of a press, against her skin as it lies at rest: the press itself goes a centimetre or two in.)
                        if (worstFingers < -0.02f) why.Add($"fingers {-worstFingers * 100f:0} cm into her skin");
                        if (worstRest > 0.01f) why.Add($"his body {worstRest * 100f:0} cm in, {deepestAt}");
                        bool ok = why.Count == 0;
                        if (!ok) { failures++; failed.Add($"{body}/{belly}"); }
                        Debug.Log($"[CprCheck] {(ok ? "PASS" : "FAIL")} body {body} belly {belly}: press at {torsoAt:0.00} of the torso, palm {worstGap * 100f:0.0} cm over the skin, " +
                                  $"{worstSide * 100f:0.0} cm aside, {alongAt * 100f:0.0} cm up the body from the press point, fingers {-worstFingers * 100f:0.0} cm into her skin, " +
                                  $"body {worstRest * 100f:0.0} cm in {restParts}, elbows bent {elbow:0} deg{(ok ? "" : ": " + string.Join(", ", why))}");
                        animator.SendMessage("OnDestroy", SendMessageOptions.DontRequireReceiver);
                        InBodySpace(victimRig, false);
                        InBodySpace(rig, false);
                        Object.DestroyImmediate(go);
                        Object.DestroyImmediate(victimObject);
                        Object.DestroyImmediate(skinObject);
                        Object.DestroyImmediate(floor);
                    }
                Debug.Log(failures == 0 ? "[CprCheck] ALL PASS" : $"[CprCheck] {failures} FAILED: {string.Join(" ", failed)}");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CprCheck] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// FeetBatch: every body on the clips standing (each lively idle), talking and walking, set up as the game sets
        /// them (the body a child of its character, at a height of its own in it) on floors at sea level, up on a porch
        /// and down in a dip: the lower sole must stay on the floor. (Tested only at the origin, the clips' bodies
        /// walked over the sea and knee-deep in the sand in the game, 2026-10-20.)
        /// </summary>
        public static void FeetBatch()
        {
            try
            {
                ReviewCamera();
                byte[] bodies = BodiesArg(string.Join(",", AllBodies));
                int failures = 0, k = 0;
                foreach (byte body in bodies)
                    foreach (float floorY in new[] { 0f, 1.8f, -0.6f })
                        foreach (float inside in new[] { 0f, 0.15f, -0.2f })
                            foreach (string state in new[] { "idle", "talk", "walk" })
                            {
                                var at = new Vector3(600f + 12f * (k % 20), floorY, 12f * (k / 20));
                                k++;
                                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                                floor.transform.position = at - Vector3.up * 0.5f;
                                floor.transform.localScale = new Vector3(10f, 1f, 10f);
                                Physics.SyncTransforms();
                                var character = new GameObject("Character " + body);
                                character.transform.position = at;
                                var go = new GameObject("Avatar");
                                go.transform.SetParent(character.transform, false);
                                go.transform.localPosition = Vector3.up * inside;
                                AvatarRig rig = Spawn(go, body);
                                var animator = go.AddComponent<AvatarAnimator>();
                                animator.Rig = rig;
                                animator.UseClips = true;
                                animator.Lively = true;
                                animator.Seed = 3 + k;
                                const float dt = 1f / 30f;
                                float worstOver = 0f, worstUnder = 0f;
                                for (float t = 0f; t < 3f; t += dt)
                                {
                                    AvatarAnimator.TimeOverride = 300f + t;
                                    var m = new AvatarMotion { FacingYaw = 0f, Grounded = true, Talking = state == "talk" };
                                    if (state == "walk") m.Velocity = Vector3.forward * 1.3f;
                                    character.transform.position += m.Velocity * dt;
                                    // (The game keeps the character on the floor: here the floor is flat at floorY.)
                                    animator.Motion = m;
                                    animator.Tick(dt);
                                    Physics.SyncTransforms();
                                    if (t < 1f) continue;
                                    float sole = Mathf.Min(rig[AvatarRig.Bone.FootL].position.y, rig[AvatarRig.Bone.FootR].position.y) - rig.AnkleHeight;
                                    float gap = sole - floorY;
                                    worstOver = Mathf.Max(worstOver, gap);
                                    worstUnder = Mathf.Min(worstUnder, gap);
                                }
                                AvatarAnimator.TimeOverride = null;
                                bool clips = animator.ClipDriven;
                                bool ok = clips && worstOver < 0.03f && worstUnder > -0.03f;
                                if (!ok) failures++;
                                if (!ok || body == bodies[0])
                                    Debug.Log($"[Feet] {(ok ? "PASS" : "FAIL")} body {body} floor {floorY:0.0} inside {inside:0.0} {state}: lower sole {worstUnder * 100f:0.0} to {worstOver * 100f:0.0} cm off the floor{(clips ? "" : " (not on the clips!)")}");
                                Object.DestroyImmediate(character);
                                Object.DestroyImmediate(floor);
                            }
                Debug.Log(failures == 0 ? $"[Feet] ALL PASS ({k} cases)" : $"[Feet] {failures} FAILED of {k}");
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Feet] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        public static void StatesBatch()
        {
            try
            {
                Camera camera = ReviewCamera();
                byte[] bodies = BodiesArg("8,16,6");
                string[] states = Arg("-states", "talk,walk,down,scared,tread,cpr").Split(',');
                const string root = "Screenshots/ClipStates";
                Directory.CreateDirectory(root);
                var victimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab");
                int k = 0;
                foreach (string state in states)
                    foreach (byte body in bodies)
                    {
                        var at = new Vector3(400f + 30f * k++, 300f, 0f);
                        GameObject floor = Floor(at);
                        string folder = $"{root}/{state}_{body:00}";
                        if (Directory.Exists(folder)) Directory.Delete(folder, true);
                        Directory.CreateDirectory(folder);

                        var go = new GameObject("State " + body);
                        go.transform.position = at;
                        AvatarRig rig = Spawn(go, body);
                        var animator = go.AddComponent<AvatarAnimator>();
                        animator.Rig = rig;
                        animator.UseClips = true;
                        animator.Lively = true;
                        animator.Seed = 7 + body;

                        // CPR: a real tourist lying on the floor, the rescuer kneeling at her chest as the game puts him.
                        GameObject victimObject = null;
                        Vector3 chest = Vector3.zero;
                        if (state == "cpr")
                        {
                            victimObject = Object.Instantiate(victimPrefab);
                            foreach (Rigidbody rb in victimObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                            var victim = victimObject.GetComponent<VictimBody>();
                            Vector3 spot = at + new Vector3(0f, 0f, 0.0f);
                            victimObject.transform.SetPositionAndRotation(spot + Vector3.up * 0.14f, Quaternion.LookRotation(Vector3.up, Vector3.right));
                            victim.ApplyLooks(AvatarLook.RandomTourist(1), 1);
                            chest = victim.ChestPoint;
                            go.transform.position = new Vector3(chest.x, at.y, chest.z - victim.KneelReach);
                        }

                        const float dt = 1f / 30f;
                        float length = state switch { "down" => 4.6f, "talk" => 8f, "cpr" => 4f, _ => 4f };
                        int frame = 0;
                        for (float t = 0f; t < length; t += dt, frame++)
                        {
                            AvatarAnimator.TimeOverride = 200f + t;
                            var m = new AvatarMotion { FacingYaw = 0f, Grounded = true };
                            switch (state)
                            {
                                case "talk": m.Talking = true; m.Mood = t < 4f ? AvatarMood.Neutral : AvatarMood.Angry; break;
                                case "walk": m.Velocity = Vector3.forward * (t < 0.5f ? 0f : t < 3f ? 1.3f : 3.5f); break;
                                case "down": m.Pose = t < 2.4f ? AvatarPose.Down : AvatarPose.Normal; m.Mood = AvatarMood.Hurt; break;
                                case "scared": m.Pose = t < 3f ? AvatarPose.Scared : AvatarPose.Normal; m.Mood = AvatarMood.Scared; break;
                                case "tread": m.Swimming = true; break;
                                case "cpr":
                                    m.Cpr = true;
                                    m.CprPoint = chest;
                                    if (frame % 18 == 0) animator.Play(AvatarGesture.Pump, chest);
                                    break;
                            }
                            if (state == "walk") go.transform.position += m.Velocity * dt;
                            animator.Motion = m;
                            animator.Tick(dt);
                            if (frame % 3 != 0) continue;
                            if (frame % 15 == 0) Debug.Log($"[ClipReview] {state} body {body} t {t:0.0}: clips {animator.ClipDriven} action {animator.ClipAction ?? "-"}");
                            Vector3 mid = state == "cpr" ? chest + Vector3.up * 0.25f : go.transform.position + Vector3.up * (state == "down" ? 0.5f : 0.85f);
                            if (state == "down") mid += Vector3.back * 0.5f;
                            // (CPR: from across the tourist, facing the rescuer.)
                            camera.transform.position = mid + (state == "cpr" ? new Vector3(0.9f, 0.85f, 2.0f) : new Vector3(-3.2f, 0.6f, 2.2f));
                            camera.transform.LookAt(mid);
                            Render(camera, $"{folder}/f{frame / 3:000}.jpg", 480, 480);
                        }
                        AvatarAnimator.TimeOverride = null;
                        animator.SendMessage("OnDestroy", SendMessageOptions.DontRequireReceiver);
                        Object.DestroyImmediate(go);
                        if (victimObject != null) Object.DestroyImmediate(victimObject);
                        Object.DestroyImmediate(floor);
                    }
                Debug.Log("[ClipReview] states done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipReview] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }
    }
}
