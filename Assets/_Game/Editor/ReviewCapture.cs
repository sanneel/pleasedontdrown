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
            camera.nearClipPlane = 0.02f; // face close-ups (the kiss of life) from a few centimetres away
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
                if (p[0] == "clear")
                {
                    // Everyone posed so far goes: the next poses can stand on the same spot.
                    foreach (GameObject spawned in Spawned) if (spawned != null) Object.DestroyImmediate(spawned);
                    Spawned.Clear();
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
                    // ... "hand=size" (x life size), "grip=dx,dy,dz" / "lgrip=dx,dy,dz" (move the right / left grip, gun space).
                    Vector3? tryAt = null, tryRot = null;
                    Vector3 moveGrip = Vector3.zero, moveLeft = Vector3.zero;
                    foreach (string token in p)
                    {
                        if (token.StartsWith("hand=")) Player.FirstPersonArms.HandSize = float.Parse(token.Substring(5), CultureInfo.InvariantCulture);
                        if (!token.StartsWith("at=") && !token.StartsWith("rot=") && !token.StartsWith("grip=") && !token.StartsWith("lgrip=")) continue;
                        string[] v = token.Substring(token.IndexOf('=') + 1).Split(',');
                        var vec = new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
                        if (token.StartsWith("at=")) tryAt = vec;
                        else if (token.StartsWith("rot=")) tryRot = vec;
                        else if (token.StartsWith("grip=")) moveGrip = vec;
                        else moveLeft = vec;
                    }
                    p = System.Array.FindAll(p, t => !t.Contains("="));
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
                    if (gr != null) gr.localPosition += moveGrip;
                    if (gl != null) gl.localPosition += moveLeft;
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
                    HandPose leftPose = p.Length > 8 ? handPose : gunItem.GripPoseLeft;
                    arms.PlaceForReview(false, gl != null ? new HandGrip(gl.position, gl.forward, -gl.up, leftPose) : null);
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

        /// <summary>goofy:head:belly:nose:eyes:teeth:hat:glasses:face (numbers; missing ones 0), on the uniform's colours.</summary>
        private static AvatarLook Goofy(string[] spec)
        {
            int At(int i) => spec.Length > i && int.TryParse(spec[i], out int v) ? v : 0;
            AvatarLook look = AvatarLook.Lifeguard;
            look.HeadSize = (byte)At(1);
            look.Belly = (byte)At(2);
            look.Nose = (byte)At(3);
            look.Eyes = (byte)At(4);
            look.Teeth = (byte)At(5);
            look.Hat = (HatStyle)At(6);
            look.Glasses = (GlassesStyle)At(7);
            look.Face = (FacialHair)At(8);
            look.HatColor = 6;
            return look;
        }

        private static AvatarLook CodeBuilt(AvatarLook look)
        {
            look.Body = 0;
            return look;
        }

        private static readonly System.Collections.Generic.List<GameObject> Spawned = new();

        private static void SpawnAvatar(string[] p)
        {
            float F(int k) => float.Parse(p[k], CultureInfo.InvariantCulture);
            string[] lookSpec = p[1].Split(':');
            int seed = lookSpec.Length > 1 && int.TryParse(lookSpec[1], out int parsed) ? parsed : 0;
            AvatarLook look = lookSpec[0] switch
            {
                "tourist" => AvatarLook.RandomTourist(seed),
                "woman" => AvatarLook.RandomTourist(seed, 1),
                "man" => AvatarLook.RandomTourist(seed, 0),
                "random" => AvatarLook.Random(new System.Random(seed)),
                "goofy" => Goofy(lookSpec),
                "sandy" => Story.StoryDirector.SandyLook,
                "sandyboss" => SandyBoss(),
                "body" => new AvatarLook { Body = (byte)seed, Figure = (byte)(AvatarLook.Bodies.IsFeminine((byte)seed) ? 1 : 0) },
                "sandycode" => CodeBuilt(Story.StoryDirector.SandyLook),
                "receptionist" => Story.StoryDirector.ReceptionistLook,
                "robber" => Story.StoryDirector.RobberLook,
                "barista" => Fun.Barista.MockLook,
                "pirate" => Story.StoryDirector.PirateLook(seed),
                _ => AvatarLook.Lifeguard
            };
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            var go = new GameObject("ReviewAvatar");
            Spawned.Add(go);
            // y "g": standing on whatever ground is there (slopes, steps).
            float groundY = 0f;
            if (p[3] == "g")
            {
                Physics.SyncTransforms();
                if (Physics.Raycast(new Vector3(F(2), 60f, F(4)), Vector3.down, out RaycastHit ground, 200f, ~0, QueryTriggerInteraction.Ignore)) groundY = ground.point.y;
                else Debug.LogWarning($"[Review] no ground under {p[2]}, {p[4]}");
            }
            go.transform.position = new Vector3(F(2), p[3] == "g" ? groundY : F(3), F(4));
            var rig = go.AddComponent<AvatarRig>();
            rig.Build(look);
            if (lookSpec[0] == "robber")
            {
                Story.RobberBag.Wear(Story.RobberBag.Create(), rig); // as in the story
                if (look.Body != AvatarLook.Bodies.Robber) Story.RobberDisguise.Create(rig);
            }
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
            if (p[3] == "g") Debug.Log($"[Review] avatar {p[1]} on the ground at y {groundY:0.00}");
            // "ride:N": on seat N of the banana boat exactly as in the game (Vehicle.GlueDriver puts the feet half a
            // metre under the seat; Vehicle.GetGrips puts the fists on its handles). The x y z yaw given are ignored.
            if (pose.StartsWith("ride:") && RideSeat(int.Parse(pose.Substring(5)), out Transform rideSeat, out Transform rideL, out Transform rideR))
            {
                yaw = rideSeat.eulerAngles.y;
                forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                go.transform.SetPositionAndRotation(rideSeat.position - Vector3.up * 0.5f, Quaternion.Euler(0f, yaw, 0f));
                m.FacingYaw = yaw;
                m.Seated = m.Straddle = true;
                m.Holding = m.TwoHanded = true;
                m.GripLeft = new HandGrip(rideL.position, rideL.forward, -rideL.up, HandPose.Fist);
                m.GripRight = new HandGrip(rideR.position, rideR.forward, -rideR.up, HandPose.Fist);
                pose = "ride";
            }
            switch (pose)
            {
                case "walk": m.Velocity = forward * 2.2f; break;
                case "run": m.Velocity = forward * 7f; m.Sprinting = true; break;
                case "crouch": m.Crouch = 1f; break;
                case "jump": m.Grounded = false; m.Velocity = forward * 3f; break;
                case "swim": m.Swimming = true; m.Velocity = forward * 3f; break;
                case "breast": m.Swimming = true; m.Velocity = forward * 1.2f; animator.Breaststroke = true; break;
                case "tread": m.Swimming = true; break;
                case "dive": m.Swimming = true; m.Underwater = true; m.LookPitch = 35f; m.Velocity = forward * 2.5f; break;
                case "hold":
                    m.Holding = true; m.TwoHanded = true;
                    m.GripLeft = new HandGrip(chest + forward * 0.5f - go.transform.right * 0.3f, forward, go.transform.right, HandPose.BoxGrip);
                    m.GripRight = new HandGrip(chest + forward * 0.5f + go.transform.right * 0.3f, forward, -go.transform.right, HandPose.BoxGrip);
                    break;
                case "carry":
                case "carrypair":
                    // As PlayerAvatar.CarryPoses: arms out under a lifeguard lying across them.
                    m.Holding = true; m.CarryingPerson = true;
                    Vector3 r = go.transform.right;
                    m.GripLeft = new HandGrip(chest + forward * 0.32f + r * 0.16f - Vector3.up * 0.47f, -r, -forward, HandPose.Carry);
                    m.GripRight = new HandGrip(chest + forward * 0.24f + r * 0.3f - Vector3.up * 0.12f, -r, -forward, HandPose.Carry);
                    if (pose == "carrypair")
                    {
                        // ...and the one carried, lying back where PlayerCarry.HoldPoint puts them, head to the left.
                        Vector3 inArms = go.transform.position + Quaternion.Euler(0f, yaw, 0f) * Player.PlayerCarry.HoldOffset;
                        SpawnAvatar(new[] { "avatar", p[1], inArms.x.ToString(CultureInfo.InvariantCulture), inArms.y.ToString(CultureInfo.InvariantCulture),
                            inArms.z.ToString(CultureInfo.InvariantCulture), (yaw + 180f).ToString(CultureInfo.InvariantCulture), "carried" });
                    }
                    break;
                case "carried": m.Pose = AvatarPose.Carried; m.Mood = AvatarMood.Scared; m.Grounded = false; break;
                case "pump": m.Cpr = true; m.CprPoint = go.transform.position + forward * 0.6f + Vector3.up * 0.2f; break;
                case "charge": m.Charge = 1f; break;
                case "eat": m.Eating = true; break;
                case "kiss":
                case "zap":
                case "cpr": m.Cpr = true; m.CprPoint = go.transform.position + forward * 0.6f + Vector3.up * 0.2f; break;
                case "down": m.Pose = AvatarPose.Down; m.Mood = AvatarMood.Hurt; break;
                case "kneel": m.Pose = AvatarPose.Kneel; m.Mood = AvatarMood.Scared; break;
                case "scared": m.Pose = AvatarPose.Scared; m.Mood = AvatarMood.Scared; break;
                case "handsup": m.Pose = AvatarPose.HandsUp; break;
                case "seated": m.Seated = true; break;
                case "straddle": m.Seated = true; m.Straddle = true; break;
                case "fly": m.Flying = true; m.Grounded = false; m.Velocity = forward * 14f; break;
                case "star": m.StarJump = true; m.Grounded = false; m.Velocity = Vector3.up * 6f; break;
                case "jumpshot": m.Grounded = false; break;
                case "sitchair": m.Pose = AvatarPose.SitChair; break;
                case "happy": m.Mood = AvatarMood.Happy; m.Talking = true; break;
                case "talk": m.Talking = true; animator.Lively = true; break;
                case "blink": m.Mood = AvatarMood.Hurt; break; // eyes nearly shut
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
            // "bottle" / "drink": a beer in the right hand as someone else sees it (BottleHold's remote offset from the
            // eyes, shrunk like PlayerHands does), or tipped up at the lips (PlayerHands' drinking), the hand on its grip.
            if (pose.StartsWith("bottle") || pose.StartsWith("drink"))
            {
                // bottle:<fingers yaw>:<palm 1 in, -1 out>:<grip height>: try a grip before baking it.
                string[] gripTry = pose.Split(':');
                var beerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Beer.prefab");
                GameObject beer = Object.Instantiate(beerPrefab);
                Spawned.Add(beer);
                if (beer.TryGetComponent(out Rigidbody beerBody)) beerBody.isKinematic = true;
                var beerItem = beer.GetComponent<Items.Item>();
                var drink = beer.GetComponent<Items.Edible>();
                Quaternion view = Quaternion.Euler(0f, yaw, 0f);
                Vector3 eye = go.transform.position + Vector3.up * 1.5f;
                bool drinking = pose.StartsWith("drink");
                if (gripTry.Length > 2)
                {
                    float gy = float.Parse(gripTry[1], CultureInfo.InvariantCulture), flip = float.Parse(gripTry[2], CultureInfo.InvariantCulture);
                    float gh = gripTry.Length > 3 ? float.Parse(gripTry[3], CultureInfo.InvariantCulture) : -0.045f;
                    Vector3 outward = Quaternion.Euler(0f, gy, 0f) * new Vector3(1f, 0f, 0f);
                    Transform gt = beerItem.GripRight;
                    gt.localPosition = outward * 0.036f + new Vector3(0f, gh, 0f);
                    gt.localRotation = Quaternion.LookRotation(Quaternion.Euler(0f, gy, 0f) * Vector3.forward * flip, outward);
                }
                m.Holding = true;
                m.Eating = m.Drinking = drinking;
                for (int i = 0; i < 45; i++)
                {
                    if (drinking)
                    {
                        Quaternion tipped = Quaternion.Euler(-119f, 0f, 0f);
                        beer.transform.rotation = view * tipped;
                        beer.transform.position = animator.Lips - beer.transform.rotation * drink.Lip;
                    }
                    else
                    {
                        beer.transform.SetPositionAndRotation(eye + view * Vector3.Scale(new Vector3(0.26f, -0.74f, 0.5f), new Vector3(0.9f, 0.85f, 0.66f)),
                            view * Quaternion.Euler(-6f, 0f, -4f));
                    }
                    Transform g = beerItem.GripRight;
                    m.GripRight = new HandGrip(g.position, g.forward, -g.up, beerItem.GripPose);
                    animator.Motion = m;
                    AvatarAnimator.TimeOverride = 104f + i / 30f;
                    animator.Tick(1f / 30f);
                }
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
            // Idle habits, caught in the middle: scratch hips stretch shield wipe.
            AvatarAnimator.IdleAct act = pose switch
            {
                "scratch" => AvatarAnimator.IdleAct.ScratchHead, "hips" => AvatarAnimator.IdleAct.HandsOnHips, "stretch" => AvatarAnimator.IdleAct.Stretch,
                "shield" => AvatarAnimator.IdleAct.ShieldEyes, "wipe" => AvatarAnimator.IdleAct.WipeBrow, _ => AvatarAnimator.IdleAct.None
            };
            if (act != AvatarAnimator.IdleAct.None)
            {
                AvatarAnimator.TimeOverride = 110f;
                animator.Play(act, 2.4f);
                for (int i = 0; i < 36; i++)
                {
                    AvatarAnimator.TimeOverride = 110f + i / 30f;
                    animator.Tick(1f / 30f);
                }
            }
            AvatarGesture gesture = pose switch
            {
                "throw" => AvatarGesture.Throw, "wave" => AvatarGesture.Wave, "interact" => AvatarGesture.Interact, "punch" => AvatarGesture.Punch,
                "kiss" => AvatarGesture.Breath, "zap" => AvatarGesture.Zap, "shoot" => AvatarGesture.Shoot, "jumpshot" => AvatarGesture.JumpShot,
                "pump" => AvatarGesture.Pump, "burp" => AvatarGesture.Burp, "burpwind" => AvatarGesture.Burp, _ => AvatarGesture.None
            };
            if (gesture != AvatarGesture.None)
            {
                // Caught part-way through (the punch at full reach, the kiss with the lips down).
                float into = gesture switch { AvatarGesture.Wave => 0.6f, AvatarGesture.Punch => 0.24f, AvatarGesture.Breath => 0.55f, AvatarGesture.Zap => 0.3f, AvatarGesture.Shoot => 0.04f, AvatarGesture.JumpShot => 0.3f, AvatarGesture.Pump => 0.06f, AvatarGesture.Burp => pose == "burpwind" ? 0.2f : 0.55f, _ => 0.12f };
                float now = AvatarAnimator.TimeOverride ?? 103f;
                AvatarAnimator.TimeOverride = now;
                Vector3 point = gesture switch
                {
                    AvatarGesture.Punch => go.transform.position + forward * 0.75f + Vector3.up * 1.45f,
                    AvatarGesture.Breath => go.transform.position + forward * 0.62f + Vector3.up * 0.22f,
                    AvatarGesture.Zap => go.transform.position + forward * 0.6f + Vector3.up * 0.2f,
                    _ => Vector3.zero
                };
                animator.Play(gesture, point);
                AvatarAnimator.TimeOverride = now + into;
                animator.Tick(1f / 30f);
            }
            AvatarAnimator.TimeOverride = null;
            if (pose == "shut") rig.SetExpression(0f, 0.1f); // eyes closed (the painted closed-eyes face), as a tourist out cold
            if (pose == "fists")
            {
                rig.LeftHand?.Pose(HandPose.Fist);
                rig.RightHand?.Pose(HandPose.Fist);
            }
        }

        /// <summary>Seat N of the banana boat in the scene (0 the front) and the handles its rider holds.</summary>
        private static bool RideSeat(int index, out Transform seat, out Transform gripL, out Transform gripR)
        {
            seat = gripL = gripR = null;
            foreach (Vehicles.Vehicle v in Object.FindObjectsByType<Vehicles.Vehicle>(FindObjectsSortMode.None))
            {
                if (!v.Straddle) continue;
                Transform root = v.transform;
                if (index == 0)
                {
                    seat = root.Find("Seat");
                    gripL = root.Find("GripLeft");
                    gripR = root.Find("GripRight");
                }
                else
                {
                    int k = 0;
                    foreach (Transform child in root)
                        if (child.name == "BackSeat" && ++k == index)
                        {
                            seat = child;
                            gripL = child.Find("GripLeft");
                            gripR = child.Find("GripRight");
                        }
                }
                if (seat != null && gripL != null && gripR != null) return true;
            }
            Debug.LogError($"[Review] no banana seat {index}");
            return false;
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
