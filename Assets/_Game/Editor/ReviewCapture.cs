using System;
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
    /// Renders review shots of the saved Game scene in batch mode (no player build needed).
    /// -executeMethod PleaseDontDrown.Editor.ReviewCapture.CaptureBatch -shots Screenshots/shots.txt
    /// Each line of the shots file: <c>name px py pz tx ty tz [fov]</c>, or <c>name top cx cz size</c> for an
    /// orthographic top-down view. Images go to Screenshots/Review/ (ignored by Git).
    /// Runtime-only things (the ocean mesh, players) are not in the saved scene and don't show.
    /// </summary>
    public static class ReviewCapture
    {
        public static void CaptureBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-shots");
                string file = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Screenshots/shots.txt";
                Capture(File.ReadAllLines(file));
                Debug.Log("[Review] captures complete");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Review] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Capture(string[] lines)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Review");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.farClipPlane = 2000f;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                float F(int k) => float.Parse(p[k], CultureInfo.InvariantCulture);
                if (p[0] == "avatar")
                {
                    SpawnAvatar(p);
                    continue;
                }
                RenderSettings.fog = p[1] != "top" && p[1] != "ortho"; // haze would hide a map view
                if (p[1] == "ortho")
                {
                    // name ortho cx cy cz yaw size: horizontal orthographic view (size = visible height in metres).
                    camera.orthographic = true;
                    camera.orthographicSize = F(6) * 0.5f;
                    Quaternion rot = Quaternion.Euler(0f, F(5), 0f);
                    camera.transform.SetPositionAndRotation(new Vector3(F(2), F(3), F(4)) - rot * Vector3.forward * (p.Length > 7 ? F(7) : 6f), rot);
                }
                else if (p[1] == "top")
                {
                    camera.orthographic = true;
                    camera.orthographicSize = F(4) * 0.5f;
                    camera.transform.SetPositionAndRotation(new Vector3(F(2), 300f, F(3)), Quaternion.Euler(90f, 0f, 0f));
                }
                else
                {
                    camera.orthographic = false;
                    camera.fieldOfView = p.Length > 7 ? F(7) : 55f;
                    camera.transform.position = new Vector3(F(1), F(2), F(3));
                    camera.transform.LookAt(new Vector3(F(4), F(5), F(6)));
                }
                Render(camera, p[0]);
            }
        }

        /// <summary>
        /// <c>avatar &lt;lifeguard|tourist:seed|random:seed&gt; x y z yaw [pose]</c> places a posed character for the next shots.
        /// Poses: idle walk run crouch jump swim tread dive hold carry charge throw eat cpr wave interact.
        /// </summary>
        private static void SpawnAvatar(string[] p)
        {
            float F(int k) => float.Parse(p[k], CultureInfo.InvariantCulture);
            string[] lookSpec = p[1].Split(':');
            int seed = lookSpec.Length > 1 ? int.Parse(lookSpec[1]) : 0;
            AvatarLook look = lookSpec[0] switch
            {
                "tourist" => AvatarLook.RandomTourist(seed),
                "woman" => AvatarLook.RandomTourist(seed, 1),
                "man" => AvatarLook.RandomTourist(seed, 0),
                "random" => AvatarLook.Random(new System.Random(seed)),
                "sandy" => Story.StoryDirector.SandyLook,
                "receptionist" => Story.StoryDirector.ReceptionistLook,
                "robber" => Story.StoryDirector.RobberLook,
                "pirate" => Story.StoryDirector.PirateLook(seed),
                _ => AvatarLook.Lifeguard
            };
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            var go = new GameObject("ReviewAvatar");
            go.transform.position = new Vector3(F(2), F(3), F(4));
            var rig = go.AddComponent<AvatarRig>();
            rig.Build(look);
            var animator = go.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            string pose = p.Length > 6 ? p[6] : "idle";
            float yaw = F(5);
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var m = new AvatarMotion { FacingYaw = yaw, Grounded = true };
            Vector3 chest = go.transform.position + Vector3.up * 1.2f;
            switch (pose)
            {
                case "walk": m.Velocity = forward * 2.2f; break;
                case "run": m.Velocity = forward * 7f; m.Sprinting = true; break;
                case "crouch": m.Crouch = 1f; break;
                case "jump": m.Grounded = false; m.Velocity = forward * 3f; break;
                case "swim": m.Swimming = true; m.Velocity = forward * 3f; break;
                case "tread": m.Swimming = true; break;
                case "dive": m.Swimming = true; m.Underwater = true; m.LookPitch = 35f; m.Velocity = forward * 2.5f; break;
                case "hold":
                    m.Holding = true; m.TwoHanded = true;
                    m.GripLeft = new HandGrip(chest + forward * 0.5f - go.transform.right * 0.3f, forward, go.transform.right, HandPose.BoxGrip);
                    m.GripRight = new HandGrip(chest + forward * 0.5f + go.transform.right * 0.3f, forward, -go.transform.right, HandPose.BoxGrip);
                    break;
                case "carry":
                    m.Holding = true; m.CarryingPerson = true;
                    m.GripLeft = new HandGrip(chest + forward * 0.45f - go.transform.right * 0.3f - Vector3.up * 0.2f, forward, Vector3.up, HandPose.Carry);
                    m.GripRight = new HandGrip(chest + forward * 0.45f + go.transform.right * 0.3f - Vector3.up * 0.25f, forward, Vector3.up, HandPose.Carry);
                    break;
                case "charge": m.Charge = 1f; break;
                case "eat": m.Eating = true; break;
                case "cpr": m.Cpr = true; m.CprPoint = go.transform.position + forward * 0.6f + Vector3.up * 0.2f; break;
                case "down": m.Pose = AvatarPose.Down; m.Mood = AvatarMood.Hurt; break;
                case "kneel": m.Pose = AvatarPose.Kneel; m.Mood = AvatarMood.Scared; break;
                case "scared": m.Pose = AvatarPose.Scared; m.Mood = AvatarMood.Scared; break;
                case "handsup": m.Pose = AvatarPose.HandsUp; break;
                case "seated": m.Seated = true; break;
                case "happy": m.Mood = AvatarMood.Happy; m.Talking = true; break;
            }
            animator.Motion = m;
            // Face the camera first, so grips computed from "right" below are the avatar's right.
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 90; i++)
            {
                AvatarAnimator.TimeOverride = 100f + i / 30f;
                animator.Tick(1f / 30f);
            }
            AvatarGesture gesture = pose switch { "throw" => AvatarGesture.Throw, "wave" => AvatarGesture.Wave, "interact" => AvatarGesture.Interact, _ => AvatarGesture.None };
            if (gesture != AvatarGesture.None)
            {
                animator.Play(gesture);
                AvatarAnimator.TimeOverride = 103f + (gesture == AvatarGesture.Wave ? 0.6f : 0.12f);
                animator.Tick(1f / 30f);
            }
            AvatarAnimator.TimeOverride = null;
        }

        private static void Render(Camera camera, string name)
        {
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            texture.Apply();
            File.WriteAllBytes($"Screenshots/Review/{name}.jpg", texture.EncodeToJPG(88));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
