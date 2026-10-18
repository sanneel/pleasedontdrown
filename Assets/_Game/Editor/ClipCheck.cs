using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Checks for motion-captured clips on our bodies (Avatars.AvatarClips).
    ///
    /// <b>RunBatch</b>: every character body is made into a Unity humanoid; each must come out valid, read back the
    /// pose it stands in, and raise its arms when the humanoid's arm muscles say so (the mapping is right). Logs
    /// "[ClipCheck] ALL PASS". Needs no clips.
    ///
    /// <b>CompareBatch</b> (once the clips are in Resources/Mixamo): the same body twice, side by side, walking the same
    /// route (stand, walk, run, walk, stand): on the left the hand-made animation, on the right the clips. Frames go to
    /// Screenshots/ClipCompare/ (make a video with ffmpeg). <c>-body &lt;id&gt;</c> picks the body (default 16, Lola).
    /// </summary>
    public static class ClipCheck
    {
        // Every body in the game (AvatarLook.Bodies), women first.
        private static readonly byte[] AllBodies = { 1, 2, 16, 17, 18, 19, 20, 21, 22, 23, 24, 11, 27, 28, 6, 26, 7, 25, 8, 9, 10, 12, 13 };

        public static void RunBatch()
        {
            int failed = 0;
            try
            {
                AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                foreach (byte body in AllBodies)
                {
                    var go = new GameObject("ClipCheck " + body);
                    try
                    {
                        AvatarRig rig = Spawn(go, body);
                        failed += Check(rig, body) ? 0 : 1;
                    }
                    finally { Object.DestroyImmediate(go); }
                }
                Debug.Log(failed == 0 ? "[ClipCheck] ALL PASS" : $"[ClipCheck] {failed} FAILED");
                EditorApplication.Exit(failed == 0 ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipCheck] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static AvatarRig Spawn(GameObject go, byte body)
        {
            var rig = go.AddComponent<AvatarRig>();
            AvatarLook look = body == AvatarLook.Bodies.Goofy ? AvatarLook.Lifeguard
                : new AvatarLook { Body = body, Figure = (byte)(AvatarLook.Bodies.IsFeminine(body) ? 1 : 0) };
            rig.Build(look);
            // (Outside play mode a body with an Animator is only re-skinned by the game loop, which batch runs don't
            // have: make every render take the bones as they are.)
            if (rig.Renderer != null) rig.Renderer.forceMatrixRecalculationPerRender = true;
            return rig;
        }

        private static bool Check(AvatarRig rig, byte body)
        {
            UnityEngine.Avatar avatar = AvatarClips.BuildAvatar(rig);
            bool ok = avatar != null && avatar.isValid && avatar.isHuman;
            string why = ok ? "" : "not a valid humanoid";
            if (ok)
            {
                // Read the pose it stands in and put it straight back: nothing may move.
                var handler = new HumanPoseHandler(avatar, rig.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                Vector3 handL = rig[AvatarRig.Bone.HandL].position, handR = rig[AvatarRig.Bone.HandR].position;
                handler.SetHumanPose(ref pose);
                float drift = Mathf.Max((rig[AvatarRig.Bone.HandL].position - handL).magnitude, (rig[AvatarRig.Bone.HandR].position - handR).magnitude);
                if (drift > 0.03f) { ok = false; why = $"the hands moved {drift * 100f:0} cm reading the pose back"; }

                // The arms' muscles move the arms the right way: all the way up puts the hands over the shoulders, all
                // the way down puts them below.
                int armL = Array.IndexOf(HumanTrait.MuscleName, "Left Arm Down-Up"), armR = Array.IndexOf(HumanTrait.MuscleName, "Right Arm Down-Up");
                float Lift(float muscle, AvatarRig.Bone hand, AvatarRig.Bone shoulder)
                {
                    pose.muscles[armL] = muscle;
                    pose.muscles[armR] = muscle;
                    handler.SetHumanPose(ref pose);
                    return rig[hand].position.y - rig[shoulder].position.y;
                }
                float upL = Lift(1f, AvatarRig.Bone.HandL, AvatarRig.Bone.UpperArmL), upR = Lift(1f, AvatarRig.Bone.HandR, AvatarRig.Bone.UpperArmR);
                float downL = Lift(-1f, AvatarRig.Bone.HandL, AvatarRig.Bone.UpperArmL), downR = Lift(-1f, AvatarRig.Bone.HandR, AvatarRig.Bone.UpperArmR);
                if (ok && !(upL > 0.15f && upR > 0.15f && downL < -0.15f && downR < -0.15f))
                {
                    ok = false;
                    why = $"arm muscles: up L {upL:0.00} R {upR:0.00}, down L {downL:0.00} R {downR:0.00} m from the shoulders";
                }
                handler.Dispose();
            }
            Debug.Log($"[ClipCheck] {(ok ? "PASS" : "FAIL")}: body {body} ({rig.GeneratedBody?.name ?? "code-built"}) {why}");
            if (avatar != null) Object.DestroyImmediate(avatar);
            rig.ResetPose();
            return ok;
        }

        /// <summary>InfoBatch: what the imported Mixamo files are (their humanoid, their clips).</summary>
        public static void InfoBatch()
        {
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Resources/Mixamo" }))
            {
                string file = AssetDatabase.GUIDToAssetPath(path);
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(file))
                {
                    if (o is UnityEngine.Avatar a) Debug.Log($"[ClipCheck] {file}: avatar valid {a.isValid} human {a.isHuman}");
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                        Debug.Log($"[ClipCheck] {file}: clip '{c.name}' {c.length:0.00} s, human motion {c.isHumanMotion}, looping {c.isLooping}, average speed {c.averageSpeed}");
                }
                var importer = (ModelImporter)AssetImporter.GetAtPath(file);
                Debug.Log($"[ClipCheck] {file}: {importer.humanDescription.human.Length} human bones mapped, e.g. " +
                          string.Join(", ", Array.ConvertAll(importer.humanDescription.human, h => h.humanName + "=" + h.boneName)).Substring(0, 200));
            }
            EditorApplication.Exit(0);
        }

        /// <summary>ElbowBatch: the idle and walk at a few moments, the elbow bend on Mixamo's own skeleton and on ours.</summary>
        public static void ElbowBatch()
        {
            try
            {
                AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                var go = new GameObject("ElbowCheck");
                AvatarRig rig = Spawn(go, AvatarLook.Bodies.Lola);
                foreach (string file in new[] { "Breathing Idle", "Walking" })
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Resources/Mixamo/{file}.fbx");
                    GameObject source = Object.Instantiate(model);
                    AnimationClip clip = null;
                    foreach (Object o in AssetDatabase.LoadAllAssetsAtPath($"Assets/_Game/Resources/Mixamo/{file}.fbx"))
                        if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clip = c;
                    Transform Find(string bone) => source.GetComponentsInChildren<Transform>().First(t => t.name == "mixamorig:" + bone);
                    foreach (float at in new[] { 0f, 0.25f, 0.5f })
                    {
                        float time = at * clip.length;
                        clip.SampleAnimation(source, time);
                        float mixL = Elbow(Find("LeftArm"), Find("LeftForeArm"), Find("LeftHand"));
                        float mixR = Elbow(Find("RightArm"), Find("RightForeArm"), Find("RightHand"));
                        // Ours, posed by the same clip at the same moment.
                        rig.ResetPose();
                        UnityEngine.Avatar avatar = AvatarClips.BuildAvatar(rig);
                        if (!go.TryGetComponent(out Animator animator)) animator = go.AddComponent<Animator>();
                        animator.avatar = avatar;
                        animator.Rebind();
                        var graph = UnityEngine.Playables.PlayableGraph.Create("elbow");
                        var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "o", animator);
                        var play = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                        output.SetSourcePlayable(play);
                        play.SetTime(time);
                        graph.Evaluate(0f);
                        float ourL = Elbow(rig[AvatarRig.Bone.UpperArmL], rig[AvatarRig.Bone.ForearmL], rig[AvatarRig.Bone.HandL]);
                        float ourR = Elbow(rig[AvatarRig.Bone.UpperArmR], rig[AvatarRig.Bone.ForearmR], rig[AvatarRig.Bone.HandR]);
                        Vector3 mixForeL = (Find("LeftHand").position - Find("LeftForeArm").position).normalized;
                        Vector3 ourForeL = (rig[AvatarRig.Bone.HandL].position - rig[AvatarRig.Bone.ForearmL].position).normalized;
                        Debug.Log($"[ClipCheck] {file} at {at:0.00}: elbow bend Mixamo L {mixL:0} R {mixR:0}, ours L {ourL:0} R {ourR:0}; left forearm points Mixamo {mixForeL} ours {ourForeL}");
                        // The hand: toward the middle finger, and the thumb's side (the palm's turn).
                        Vector3 mixFinger = (Find("LeftHandMiddle1").position - Find("LeftHand").position).normalized;
                        Vector3 mixThumb = (Find("LeftHandIndex1").position - Find("LeftHandPinky1").position).normalized;
                        Transform middle = rig.transform.GetComponentsInChildren<Transform>().First(t => t.name == "Middle1L");
                        Transform thumb = rig.transform.GetComponentsInChildren<Transform>().First(t => t.name == "Index1L");
                        Transform pinky = rig.transform.GetComponentsInChildren<Transform>().First(t => t.name == "Pinky1L");
                        Vector3 ourFinger = (middle.position - rig[AvatarRig.Bone.HandL].position).normalized;
                        Vector3 ourThumb = (thumb.position - pinky.position).normalized;
                        Debug.Log($"[ClipCheck]   left hand: fingers point Mixamo {mixFinger} ours {ourFinger} ({Vector3.Angle(mixFinger, ourFinger):0} deg apart); across the knuckles Mixamo {mixThumb} ours {ourThumb} ({Vector3.Angle(mixThumb, ourThumb):0} deg apart)");
                        graph.Destroy();
                        Object.DestroyImmediate(avatar);
                    }
                    Object.DestroyImmediate(source);
                }
                Object.DestroyImmediate(go);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipCheck] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>How far the elbow is bent (0: straight), degrees.</summary>
        private static float Elbow(Transform upper, Transform fore, Transform hand) =>
            Vector3.Angle(fore.position - upper.position, hand.position - fore.position);

        // ------------------------------------------------------------------ single poses (finding a retarget problem)

        /// <summary>
        /// PoseBatch: one body in Unity's neutral humanoid pose (every muscle 0) and in the clips at a few moments,
        /// front and side, to Screenshots/Review/clippose_*.jpg.
        /// </summary>
        public static void PoseBatch()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
                var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
                camera.farClipPlane = 2000f;
                camera.nearClipPlane = 0.05f;
                camera.fieldOfView = 30f;
                AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                Directory.CreateDirectory("Screenshots/Review");
                var go = new GameObject("PoseCheck");
                go.transform.position = new Vector3(400f, 300f, 0f);
                AvatarRig rig = Spawn(go, AvatarLook.Bodies.Lola);
                void Shoot(string name)
                {
                    Vector3 mid = go.transform.position + Vector3.up * 0.9f;
                    camera.transform.position = mid + new Vector3(0f, 0.2f, 5.2f);
                    camera.transform.LookAt(mid);
                    RenderTo(camera, $"Screenshots/Review/clippose_{name}_front.jpg");
                    camera.transform.position = mid + new Vector3(-5.2f, 0.2f, 0f);
                    camera.transform.LookAt(mid);
                    RenderTo(camera, $"Screenshots/Review/clippose_{name}_side.jpg");
                }
                Shoot("rest");
                UnityEngine.Avatar avatar = AvatarClips.BuildAvatar(rig);
                var handler = new HumanPoseHandler(avatar, rig.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                Debug.Log($"[ClipCheck] rest pose as muscles: body {pose.bodyPosition} {pose.bodyRotation.eulerAngles}");
                for (int m = 0; m < pose.muscles.Length; m++)
                    if (Mathf.Abs(pose.muscles[m]) > 0.2f) Debug.Log($"[ClipCheck]   {HumanTrait.MuscleName[m]} = {pose.muscles[m]:0.00}");
                for (int m = 0; m < pose.muscles.Length; m++) pose.muscles[m] = 0f;
                handler.SetHumanPose(ref pose);
                Shoot("zero");
                handler.Dispose();
                Object.DestroyImmediate(avatar);

                // The clips alone first (none of the animator's own steps after them).
                rig.ResetPose();
                var bare = new AvatarClips(rig);
                if (!bare.Prepare()) throw new InvalidOperationException("clips didn't prepare");
                foreach (var (name, speed, steps) in new[] { ("bare_idle", 0f, 3), ("bare_walk", 1.4f, 12), ("bare_walk2", 1.4f, 19) })
                {
                    for (int i = 0; i < steps; i++) { rig.ResetPose(); bare.Evaluate(speed, 1f / 30f); }
                    Transform hips = rig[AvatarRig.Bone.Hips];
                    Vector3 hl = hips.InverseTransformPoint(rig[AvatarRig.Bone.HandL].position), hr = hips.InverseTransformPoint(rig[AvatarRig.Bone.HandR].position);
                    Vector3 fl = hips.InverseTransformPoint(rig[AvatarRig.Bone.FootL].position), fr = hips.InverseTransformPoint(rig[AvatarRig.Bone.FootR].position);
                    Debug.Log($"[ClipCheck] {name}: hands L {hl} R {hr}, feet L {fl} R {fr} (hips space: +z ahead)");
                    Shoot(name);
                }
                bare.Release();

                var animator = go.AddComponent<AvatarAnimator>();
                animator.Rig = rig;
                animator.UseClips = true;
                foreach (var (name, speed, steps) in new[] { ("idle", 0f, 1), ("walk", 1.4f, 1), ("walk2", 1.4f, 9), ("run", 4.5f, 1), ("run2", 4.5f, 6) })
                {
                    animator.Motion = new AvatarMotion { Velocity = Vector3.forward * speed, Grounded = true, Sprinting = speed > 3f };
                    for (int i = 0; i < 40 + steps; i++) animator.Tick(1f / 30f);
                    Shoot(name);
                }
                animator.SendMessage("OnDestroy", SendMessageOptions.DontRequireReceiver);
                Object.DestroyImmediate(go);
                Debug.Log("[ClipCheck] poses done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipCheck] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------ side by side

        public static void CompareBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int bi = Array.IndexOf(args, "-body");
                byte body = bi >= 0 && bi + 1 < args.Length ? byte.Parse(args[bi + 1], CultureInfo.InvariantCulture) : AvatarLook.Bodies.Lola;
                if (!AvatarClips.Available) throw new InvalidOperationException("no clips in Resources/Mixamo (Breathing Idle, Walking, Running)");
                Compare(body);
                Debug.Log("[ClipCheck] compare frames done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClipCheck] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Compare(byte body)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.farClipPlane = 2000f;
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 30f;
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            string folder = "Screenshots/ClipCompare";
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);

            // High over the sea (nothing in the way), far apart so neither is in the other's picture: each has its
            // own camera at the same place beside it, and the two pictures go side by side (left hand-made, right clips).
            var start = new Vector3(400f, 300f, 0f);
            var spawned = new List<GameObject>();
            AvatarAnimator Make(Vector3 at, bool clips)
            {
                var go = new GameObject(clips ? "Clips" : "HandMade");
                spawned.Add(go);
                go.transform.position = at;
                AvatarRig rig = Spawn(go, body);
                var animator = go.AddComponent<AvatarAnimator>();
                animator.Rig = rig;
                animator.UseClips = clips;
                return animator;
            }
            AvatarAnimator handMade = Make(start, false), clipped = Make(start + Vector3.right * 40f, true);

            // The route: (seconds, speed in m/s).
            (float until, float speed)[] route = { (2f, 0f), (6f, 1.4f), (10f, 5.5f), (13f, 1.4f), (15.5f, 0f) };
            const float dt = 1f / 30f;
            float z = 0f, speed = 0f;
            int frame = 0;
            for (float t = 0f; t < route[^1].until; t += dt, frame++)
            {
                float target = 0f;
                foreach (var leg in route) { if (t < leg.until) { target = leg.speed; break; } }
                speed = Mathf.MoveTowards(speed, target, dt * 4f);
                z += speed * dt;
                foreach (AvatarAnimator a in new[] { handMade, clipped })
                {
                    Vector3 p = a.transform.position;
                    a.transform.position = new Vector3(p.x, p.y, start.z + z);
                    a.Motion = new AvatarMotion { Velocity = Vector3.forward * speed, FacingYaw = 0f, Grounded = true, Sprinting = speed > 3f };
                    a.Tick(dt);
                }
                if (frame % 60 == 0)
                {
                    Transform thigh = clipped.Rig[AvatarRig.Bone.ThighL], hips = clipped.Rig[AvatarRig.Bone.Hips];
                    Debug.Log($"[ClipCheck] t {t:0.0} speed {speed:0.0}: clips {clipped.ClipDriven}, hips {hips.localPosition}, left thigh {thigh.localEulerAngles}");
                }
                // From the side and a little ahead, following each of them.
                foreach (AvatarAnimator a in new[] { handMade, clipped })
                {
                    Vector3 mid = a.transform.position + Vector3.up * 0.9f;
                    camera.transform.position = mid + new Vector3(-4.6f, 0.4f, 2.0f);
                    camera.transform.LookAt(mid + Vector3.forward * 0.2f);
                    Render(camera, $"{folder}/{(a == handMade ? "a" : "b")}{frame:0000}.jpg");
                }
            }
            foreach (GameObject go in spawned)
            {
                if (go.TryGetComponent(out AvatarAnimator a)) a.SendMessage("OnDestroy", SendMessageOptions.DontRequireReceiver);
                Object.DestroyImmediate(go);
            }
        }

        private static void RenderTo(Camera camera, string path) => Render(camera, path, 900, 1000);

        private static void Render(Camera camera, string path) => Render(camera, path, 960, 1080);

        private static void Render(Camera camera, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToJPG(90));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
