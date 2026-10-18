using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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

            // High over the sea (nothing in the way), the two walking side by side along +Z.
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
            AvatarAnimator handMade = Make(start + Vector3.left * 0.7f, false), clipped = Make(start + Vector3.right * 0.7f, true);

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
                // From the side and a little ahead, following them.
                Vector3 mid = new(start.x, start.y + 0.9f, start.z + z);
                camera.transform.position = mid + new Vector3(-7.5f, 0.6f, 3.2f);
                camera.transform.LookAt(mid + Vector3.forward * 0.3f);
                Render(camera, $"{folder}/f{frame:0000}.jpg");
            }
            foreach (GameObject go in spawned) Object.DestroyImmediate(go);
        }

        private static void Render(Camera camera, string path)
        {
            const int w = 1280, h = 720;
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
