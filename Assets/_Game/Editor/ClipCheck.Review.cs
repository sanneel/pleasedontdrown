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
                            // (CPR: from across the tourist, facing the rescuer, as KissCheck's "pump_across".)
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
