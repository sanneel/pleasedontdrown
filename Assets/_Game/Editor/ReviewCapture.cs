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
                if (p[0] == "fphands")
                {
                    // A trailing "nogun" hides the item, to look at the hands alone.
                    bool noGun = p[p.Length - 1] == "nogun";
                    if (noGun) p = p[..^1];
                    // A trailing "eye": x y z yaw is the player's eye, and the item sits at its hold pose in front of it
                    // (then a shot from that eye with FOV 74 is the first-person view).
                    bool fromEye = p[p.Length - 1] == "eye";
                    if (fromEye) p = p[..^1];
                    // Trying a hold pose before baking it: "at=x,y,z" (camera space) and "rot=pitch,yaw,roll" anywhere.
                    Vector3? tryAt = null, tryRot = null;
                    foreach (string token in p)
                    {
                        if (!token.StartsWith("at=") && !token.StartsWith("rot=")) continue;
                        string[] v = token.Substring(token.IndexOf('=') + 1).Split(',');
                        var vec = new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
                        if (token.StartsWith("at=")) tryAt = vec; else tryRot = vec;
                    }
                    p = System.Array.FindAll(p, t => !t.StartsWith("at=") && !t.StartsWith("rot="));
                    // fphands <prefab> x y z yaw: the item with our first-person hands on its grips (as when held).
                    var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Items/Prefabs/{p[1]}.prefab");
                    if (gunPrefab == null) { Debug.LogError($"[Review] no item {p[1]}"); continue; }
                    GameObject gun = Object.Instantiate(gunPrefab, new Vector3(F(2), F(3), F(4)), Quaternion.Euler(0f, F(5), 0f));
                    if (gun.TryGetComponent(out Rigidbody gunBody)) gunBody.isKinematic = true;
                    if (fromEye)
                    {
                        var held = gun.GetComponent<Items.Item>();
                        Quaternion view = Quaternion.Euler(0f, F(5), 0f);
                        Vector3 at = tryAt ?? held.HoldOffset;
                        Quaternion turn = tryRot is { } r ? Quaternion.Euler(r) : held.HoldRotation;
                        gun.transform.SetPositionAndRotation(new Vector3(F(2), F(3), F(4)) + view * at, view * turn);
                    }
                    AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
                    var holder = new GameObject("ReviewHands");
                    var eye = new GameObject("ReviewEye").AddComponent<Camera>();
                    eye.enabled = false;
                    eye.transform.SetPositionAndRotation(gun.transform.position, gun.transform.rotation);
                    var arms = holder.AddComponent<Player.FirstPersonArms>();
                    arms.Init(null, eye);
                    arms.Build(AvatarLook.Lifeguard);
                    var gunItem = gun.GetComponent<Items.Item>();
                    Transform gr = gunItem.GripRight, gl = gunItem.GripLeft;
                    HandPose handPose = gunItem.GripPose;
                    // Optional: fphands ... thumb index fingers (to try a grip pose before baking it).
                    if (p.Length > 8) handPose = new HandPose(F(8), F(6), 0f) { Index = F(7) };
                    // ... wrap: turns the right hand around the handle (palm toward the front strap), as if baked.
                    if (p.Length > 9 && gr != null)
                    {
                        Vector3 axis = -gr.right, pivot = gr.position - gr.up * 0.016f;
                        Quaternion turn = Quaternion.AngleAxis(F(9), axis);
                        gr.SetPositionAndRotation(pivot + turn * (gr.position - pivot), turn * gr.rotation);
                    }
                    arms.PlaceForReview(true, gr != null ? new HandGrip(gr.position, gr.forward, -gr.up, handPose) : null);
                    arms.PlaceForReview(false, gl != null ? new HandGrip(gl.position, gl.forward, -gl.up, handPose) : null);
                    if (noGun) foreach (Renderer r in gun.GetComponentsInChildren<Renderer>()) r.enabled = false;
                    continue;
                }
                if (p[0] == "item")
                {
                    // item <prefab or full .prefab path> x y z yaw [child to switch on...]: e.g. a gun with its scope and suppressor showing.
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[1].EndsWith(".prefab") ? p[1] : $"Assets/_Game/Items/Prefabs/{p[1]}.prefab");
                    if (prefab == null) { Debug.LogError($"[Review] no item {p[1]}"); continue; }
                    GameObject item = Object.Instantiate(prefab, new Vector3(F(2), F(3), F(4)), Quaternion.Euler(0f, F(5), 0f));
                    if (item.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
                    for (int k = 6; k < p.Length; k++)
                    {
                        if (p[k] == "markers")
                        {
                            // Coloured balls on the gun's named points: grips (red right / blue left), eye (green),
                            // muzzle (yellow), eject port (white); a short stick shows each grip's finger direction.
                            foreach (Transform t in item.GetComponentsInChildren<Transform>(true))
                            {
                                Color? c = t.name == "GripRight" ? Color.red : t.name == "GripLeft" ? Color.blue : t.name.StartsWith("Eye") ? Color.green
                                    : t.name.StartsWith("Muzzle") ? Color.yellow : t.name == "EjectPort" ? Color.white : null;
                                if (c == null) continue;
                                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                                Object.DestroyImmediate(ball.GetComponent<Collider>());
                                ball.transform.position = t.position;
                                ball.transform.localScale = Vector3.one * 0.012f;
                                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c.Value };
                                ball.GetComponent<Renderer>().sharedMaterial = mat;
                                if (!t.name.StartsWith("Grip")) continue;
                                GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                                Object.DestroyImmediate(stick.GetComponent<Collider>());
                                stick.transform.SetPositionAndRotation(t.position + t.forward * 0.03f, t.rotation);
                                stick.transform.localScale = new Vector3(0.003f, 0.003f, 0.06f);
                                stick.GetComponent<Renderer>().sharedMaterial = mat;
                            }
                            continue;
                        }
                        bool on = !p[k].StartsWith("-");
                        string part = p[k].TrimStart('-');
                        foreach (Transform t in item.GetComponentsInChildren<Transform>(true))
                            if (t.name == part) t.gameObject.SetActive(on);
                    }
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
        private static AvatarLook SandyBoss()
        {
            AvatarLook look = Story.StoryDirector.SandyLook;
            look.Body = AvatarLook.Bodies.SandyBoss;
            return look;
        }

        private static AvatarLook CodeBuilt(AvatarLook look)
        {
            look.Body = 0;
            return look;
        }

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
                "sandyboss" => SandyBoss(),
                "body" => new AvatarLook { Body = (byte)seed, Figure = (byte)(AvatarLook.Bodies.IsFeminine((byte)seed) ? 1 : 0) },
                "sandycode" => CodeBuilt(Story.StoryDirector.SandyLook),
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
            // "from>to@seconds": held in one pose, then caught that long into blending to the next (e.g. lie>sit@0.3).
            string blendTo = null;
            float blendTime = 0f;
            if (pose.Contains(">"))
            {
                string[] parts = pose.Split('>', '@');
                pose = parts[0];
                blendTo = parts[1];
                blendTime = parts.Length > 2 ? float.Parse(parts[2], CultureInfo.InvariantCulture) : 2f;
            }
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
                case "sitchair": m.Pose = AvatarPose.SitChair; break;
                case "happy": m.Mood = AvatarMood.Happy; m.Talking = true; break;
                default: if (PoseByName(pose) is { } held) m.Pose = held; break;
            }
            animator.Motion = m;
            // Face the camera first, so grips computed from "right" below are the avatar's right.
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 90; i++)
            {
                AvatarAnimator.TimeOverride = 100f + i / 30f;
                animator.Tick(1f / 30f);
            }
            // Optional 8th value: tick on this many more seconds (walk-cycle phases for a filmstrip).
            if (p.Length > 7)
            {
                int extra = Mathf.RoundToInt(F(7) * 30f);
                for (int i = 0; i < extra; i++)
                {
                    AvatarAnimator.TimeOverride = 103f + i / 30f;
                    animator.Tick(1f / 30f);
                }
            }
            if (blendTo != null && PoseByName(blendTo) is { } next)
            {
                m.Pose = next;
                animator.Motion = m;
                for (int i = 0; i < Mathf.RoundToInt(blendTime * 30f); i++)
                {
                    AvatarAnimator.TimeOverride = 103f + i / 30f;
                    animator.Tick(1f / 30f);
                }
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

        private static AvatarPose? PoseByName(string name) => name switch
        {
            "idle" or "normal" => AvatarPose.Normal,
            "lie" => AvatarPose.Lie,
            "liefront" => AvatarPose.LieFront,
            "sit" => AvatarPose.Sit,
            "sitchair" => AvatarPose.SitChair,
            "kneel" => AvatarPose.Kneel,
            "down" => AvatarPose.Down,
            "scared" => AvatarPose.Scared,
            "handsup" => AvatarPose.HandsUp,
            _ => null
        };

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
