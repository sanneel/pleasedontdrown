using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Items;
using PleaseDontDrown.Story;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 1's fun and games: a basketball (item), the beach basketball hoop behind the station with its ball rack
    /// and three-point dots, a table of rescue rings by the tower that keeps itself stocked, and the pink-and-white
    /// beach hut (with a real door) for the hut gag. Models: ArtSource/Tools/model_props.py.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector3 HoopSpot = new(-6f, 0f, 30f);
        private static readonly Vector3 RingTableSpot = new(15.5f, 0f, 5.2f);
        private static readonly Vector3 HutSpot = new(21f, 0f, 19f); // the toilets
        private static readonly Vector3 BarSpot = new(28.5f, 0f, 17f);

        private static IEnumerable<Object> BuildFunItems()
        {
            PhysicsMaterial bounce = GetPhysicsMaterial("Basketball", 0.8f, 0.6f, PhysicsMaterialCombine.Maximum);
            Material orange = GetMaterial("BasketballOrange", new Color(0.95f, 0.48f, 0.12f));
            Item ball = BuildItem("Basketball", BasketballHoop.BallName, 0.6f, new Vector3(0.12f, -0.3f, 0.6f), Vector3.zero, 1.25f, bounce, root =>
            {
                Primitive(PrimitiveType.Sphere, "Ball", root, Vector3.zero, Vector3.one * 0.24f, orange);
                DressProp(root, "basketball");
            }, linearDamping: 0.08f, angularDamping: 0.3f, density: 0.3f, waterDrag: 0.8f, configure: go =>
            {
                var item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                AudioSource ballAudio = SpatialAudio(go, 1.5f, 30f);
                SetRef(go.AddComponent<BallSounds>(), "_audio", ballAudio);
                // How you really hold a basketball: the shooting hand behind and under it, fingers spread up its
                // back; the guide hand on its side. (Item space: +z away from you, as held.)
                Transform Grip(string n, Vector3 outward, Vector3 fingersRough)
                {
                    var g = new GameObject(n).transform;
                    g.SetParent(go.transform, false);
                    Vector3 o = outward.normalized;
                    g.localPosition = o * 0.125f;
                    g.localRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fingersRough, o).normalized, o); // palm (-up) on the ball
                    return g;
                }
                SetRef(item, "_gripRight", Grip("GripRight", new Vector3(0.3f, -0.45f, -0.84f), new Vector3(0f, 1f, 0.35f)));
                SetRef(item, "_gripLeft", Grip("GripLeft", new Vector3(-1f, 0.12f, -0.1f), new Vector3(0f, 0.7f, 1f)));
                SetField(item, "_gripPose", p =>
                {
                    p.FindPropertyRelative("Index").floatValue = p.FindPropertyRelative("Middle").floatValue =
                        p.FindPropertyRelative("Ring").floatValue = p.FindPropertyRelative("Pinky").floatValue = 0.28f;
                    p.FindPropertyRelative("Thumb").floatValue = 0.2f;
                    p.FindPropertyRelative("Spread").floatValue = 0.55f;
                });
                // Dribbling while you walk or run with it (the look of it; the ball in your hands stays put).
                Transform model = go.transform.Find("Model_basketball");
                if (model != null)
                {
                    var dribble = go.AddComponent<BasketballDribble>();
                    SetRef(dribble, "_item", item);
                    SetRef(dribble, "_visual", model);
                    SetRef(dribble, "_audio", ballAudio);
                }
                // Continuous, not Continuous Speculative: speculative contacts ghost-bounced fast balls off the thin
                // rim before they got there (long shots went in 1 time in 10). The rim is static, so Continuous sweeps it.
                go.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Continuous;
            });
            yield return ball;

            // A cold beer from the beach bar: drink it like food (hold Secondary), burp about ten seconds later.
            PhysicsMaterial glass = GetPhysicsMaterial("BottleGlass", 0.15f, 0.5f, PhysicsMaterialCombine.Average);
            Material amber = GetMaterial("BeerBottle", new Color(0.7f, 0.4f, 0.1f));
            Item beer = BuildItem("Beer", "Beer", 0.5f, new Vector3(0.22f, -0.28f, 0.5f), new Vector3(-8f, 0f, 0f), 1f, glass, root =>
            {
                Primitive(PrimitiveType.Capsule, "Bottle", root, Vector3.zero, new Vector3(0.083f, 0.156f, 0.083f), amber);
                DressProp(root, "beer_bottle");
            }, linearDamping: 0.1f, angularDamping: 0.4f, density: 0.7f, waterDrag: 1.0f, configure: go =>
            {
                Item item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                SetEnum(item, "_grip", (int)ItemGrip.OneHand);
                AudioSource audio = SpatialAudio(go, 1.5f, 25f);
                var drink = go.AddComponent<Edible>();
                SetRef(drink, "_audio", audio);
                SetField(drink, "_food", p => p.floatValue = 0.12f);
                SetField(drink, "_seconds", p => p.floatValue = 2.6f);
                SetBool(drink, "_drink", true);
                SetField(drink, "_burpAfter", p => p.floatValue = 10f);
                SetField(drink, "_lip", p => p.vector3Value = new Vector3(0f, 0.156f, 0f));
                go.AddComponent<BottleHold>();
                // The right hand round the bottle's belly: palm on its right side, a little toward you, fingers wrapped
                // round the front (tried side by side in ReviewCapture "bottle:yaw:1": 30 degrees was the one).
                var grip = new GameObject("GripRight").transform;
                grip.SetParent(go.transform, false);
                Quaternion round = Quaternion.Euler(0f, 30f, 0f);
                Vector3 outward = round * Vector3.right;
                grip.localPosition = outward * 0.036f + new Vector3(0f, -0.045f, 0f);
                grip.localRotation = Quaternion.LookRotation(round * Vector3.forward, outward);
                SetRef(item, "_gripRight", grip);
                SetField(item, "_gripPose", p =>
                {
                    p.FindPropertyRelative("Index").floatValue = 0.82f;
                    p.FindPropertyRelative("Middle").floatValue = 0.86f;
                    p.FindPropertyRelative("Ring").floatValue = 0.9f;
                    p.FindPropertyRelative("Pinky").floatValue = 0.94f;
                    p.FindPropertyRelative("Thumb").floatValue = 0.7f;
                    p.FindPropertyRelative("Spread").floatValue = 0f;
                });
                SetRef(go.AddComponent<ImpactSound>(), "_audio", audio);
            });
            yield return beer;
        }

        private static void BuildIsland1Fun(Transform env)
        {
            var fun = new GameObject("Island1Fun").transform;
            fun.SetParent(env, false);
            BuildHoop(fun);
            BuildRingTable(fun);
            BuildBeachToilets(fun);
            BuildBeachBar(fun);
            BuildAttractions(fun); // trampolines, cannon, diving board, flamingo, banana boat
            BuildParrots(env, fun); // instead of seagulls (GameSceneBuilder.Parrots.cs)
        }

        private static Vector3 Ground(Vector3 p) => new(p.x, BeachHeight(p.x, p.z), p.z);

        private static void SetField(Object target, string field, System.Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            set(Require(so, field));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ basketball

        /// <summary>
        /// The hoop faces the beach (the backboard toward the land). Colliders: the base, the pole, the backboard and
        /// a ring of 16 thin capsules for the rim (static, so the ball's Continuous Speculative sweep catches them at
        /// any throwing speed). Scoring is BasketballHoop's geometric check, not a trigger.
        /// </summary>
        private static void BuildHoop(Transform parent)
        {
            var root = new GameObject("BasketballHoop").transform;
            root.SetParent(parent, false);
            root.position = Ground(HoopSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            Material steel = GetMaterial("HoopSteel", new Color(0.2f, 0.32f, 0.62f));
            Material white = GetMaterial("HoopBoard", Color.white);
            PhysicsMaterial boardBounce = GetPhysicsMaterial("Backboard", 0.55f, 0.4f, PhysicsMaterialCombine.Average);
            PhysicsMaterial rimBounce = GetPhysicsMaterial("Rim", 0.45f, 0.3f, PhysicsMaterialCombine.Average);

            Primitive(PrimitiveType.Cube, "Base", root, new Vector3(0f, 0.15f, -1f), new Vector3(0.7f, 0.3f, 0.7f), steel);
            Primitive(PrimitiveType.Cube, "Pole", root, new Vector3(0f, 1.9f, -1f), new Vector3(0.14f, 3.4f, 0.14f), steel);
            GameObject board = Primitive(PrimitiveType.Cube, "Backboard", root, new Vector3(0f, 3.43f, -0.03f), new Vector3(1.8f, 1.05f, 0.05f), white);
            board.GetComponent<Collider>().sharedMaterial = boardBounce;
            var rimCentre = new GameObject("RimCentre").transform;
            rimCentre.SetParent(root, false);
            rimCentre.localPosition = new Vector3(0f, 3.05f, 0.38f);
            const int pieces = 16;
            const float rimR = 0.23f, tube = 0.012f;
            for (int i = 0; i < pieces; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / pieces;
                var piece = new GameObject($"Rim{i}");
                piece.transform.SetParent(rimCentre, false);
                piece.transform.localPosition = new Vector3(Mathf.Cos(a) * rimR, 0f, Mathf.Sin(a) * rimR);
                piece.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                var capsule = piece.AddComponent<CapsuleCollider>();
                capsule.direction = 2; // along the rim
                capsule.radius = tube;
                capsule.height = 2f * Mathf.PI * rimR / pieces + tube * 2f;
                capsule.sharedMaterial = rimBounce;
            }
            // The models: hoop and net (the net is its own model so it can kick when the ball drops through).
            GameObject hoopModel = PropModel("basketball_hoop", root);
            if (hoopModel != null) // the model draws it all: the greybox stays only as colliders
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                    if (!r.transform.IsChildOf(hoopModel.transform))
                    {
                        if (r.TryGetComponent(out MeshFilter filter)) Object.DestroyImmediate(filter);
                        Object.DestroyImmediate(r);
                    }
            GameObject net = PropModel("basketball_net", rimCentre);

            root.gameObject.AddComponent<NetworkObject>();
            var hoop = root.gameObject.AddComponent<BasketballHoop>();
            SetRef(hoop, "_rim", rimCentre);
            if (net != null) SetRef(hoop, "_net", net.transform);
            SetRef(hoop, "_audio", SpatialAudio(rimCentre.gameObject, 3f, 50f));

            // Three balls to shoot with, kept in stock at the foot of the hoop.
            var rack = root.gameObject.AddComponent<ItemRack>();
            var spots = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                var spot = new GameObject($"BallSpot{i}").transform;
                spot.SetParent(root, false);
                Vector3 local = new Vector3((i - 1) * 0.7f, 0f, 1.6f);
                Vector3 world = root.TransformPoint(local);
                spot.position = Ground(world) + Vector3.up * 0.15f;
                spots.Add(spot);
            }
            SetField(rack, "_itemName", p => p.stringValue = BasketballHoop.BallName);
            SetRefs(rack, "_spots", spots.ToArray());
            SetField(rack, "_worldCap", p => p.intValue = 6);

            // The three-point line: white dots on the sand, 6 m out round the hoop.
            Material line = GetMaterial("CourtLine", new Color(0.97f, 0.97f, 0.95f));
            Vector3 c = root.TransformPoint(new Vector3(0f, 0f, 0.38f));
            for (int i = 0; i <= 24; i++)
            {
                float a = Mathf.Lerp(-80f, 80f, i / 24f) * Mathf.Deg2Rad;
                Vector3 p = c + root.rotation * new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 6f;
                p = Ground(p) + Vector3.up * 0.02f;
                GameObject dot = Primitive(PrimitiveType.Cylinder, "ThreePointDot", parent, Vector3.zero, new Vector3(0.18f, 0.01f, 0.18f), line, keepCollider: false);
                dot.transform.position = p;
            }
        }

        // ------------------------------------------------------------------ rescue rings

        /// <summary>A low table of rescue rings by the tower; throw one to somebody in trouble and they kick in on it.</summary>
        private static void BuildRingTable(Transform parent)
        {
            var root = new GameObject("RingTable").transform;
            root.SetParent(parent, false);
            root.position = Ground(RingTableSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            Material wood = GetMaterial("RingTableWood", new Color(0.55f, 0.38f, 0.22f));
            Primitive(PrimitiveType.Cube, "Top", root, new Vector3(0f, 0.5f, 0f), new Vector3(2.5f, 0.08f, 0.85f), wood);
            foreach (float x in new[] { -1.1f, 1.1f })
            foreach (float z in new[] { -0.3f, 0.3f })
                Primitive(PrimitiveType.Cube, "Leg", root, new Vector3(x, 0.25f, z), new Vector3(0.08f, 0.5f, 0.08f), wood);
            root.gameObject.AddComponent<NetworkObject>();
            var rack = root.gameObject.AddComponent<ItemRack>();
            var spots = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                var spot = new GameObject($"RingSpot{i}").transform;
                spot.SetParent(root, false);
                spot.localPosition = new Vector3((i - 1) * 0.78f, 0.66f, 0f);
                spots.Add(spot);
            }
            SetField(rack, "_itemName", p => p.stringValue = "Life Ring");
            SetRefs(rack, "_spots", spots.ToArray());
            SetField(rack, "_worldCap", p => p.intValue = 9);
        }

        // ------------------------------------------------------------------ the beach toilets

        /// <summary>
        /// The public toilets (Art/Props/beach_toilets.glb): two cubicles, the right one shut and OCCUPIED, the left one
        /// with a real door. That's where a grateful tourist drags her hero after the kiss of life (LoveHut, the gag in
        /// StoryDirector.Gags): the whole block shakes and squeaks while Sandy pretends she saw nothing.
        /// </summary>
        private static void BuildBeachToilets(Transform parent)
        {
            var root = new GameObject("BeachToilets").transform;
            root.SetParent(parent, false);
            root.position = Ground(HutSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the doors face the sea
            TagSurface(root.gameObject, SurfaceKind.Wood);
            GameObject model = PropModel("beach_toilets", root);
            Transform wobble = model != null ? model.transform : root;

            // Walls you can't walk through (the model has no colliders of its own); the left cubicle's doorway open.
            void Wall(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                go.AddComponent<BoxCollider>().size = size;
            }
            const float w = 2.3f, d = 1.35f, t = 0.14f;
            Wall("Slab", new Vector3(0f, 0.075f, 0.15f), new Vector3(2f * w + 0.5f, 0.15f, 2f * d + 0.6f));
            Wall("WallBack", new Vector3(0f, 1.325f, -d), new Vector3(2f * w + t, 2.35f, t));
            Wall("WallLeft", new Vector3(-w, 1.325f, 0f), new Vector3(t, 2.35f, 2f * d + t));
            Wall("WallRight", new Vector3(w, 1.325f, 0f), new Vector3(t, 2.35f, 2f * d + t));
            Wall("WallMiddle", new Vector3(0f, 1.325f, 0f), new Vector3(t, 2.35f, 2f * d));
            Wall("FrontLeft", new Vector3(-1.95f, 1.325f, d), new Vector3(0.7f, 2.35f, t));
            Wall("FrontMiddle", new Vector3(0f, 1.325f, d), new Vector3(1.4f, 2.35f, t));
            Wall("FrontRight", new Vector3(1.95f, 1.325f, d), new Vector3(0.7f, 2.35f, t));
            Wall("Lintel", new Vector3(-1.15f, 2.375f, d), new Vector3(0.9f, 0.25f, t));
            Wall("ShutDoor", new Vector3(1.15f, 1.325f, d), new Vector3(0.9f, 2.35f, t)); // OCCUPIED
            Wall("Roof", new Vector3(0f, 2.75f, 0.3f), new Vector3(2f * w + 0.4f, 0.3f, 2f * d + 0.9f));
            Wall("Basin", new Vector3(w + 0.3f, 0.5f, 0.3f), new Vector3(0.45f, 1f, 0.45f));
            Wall("Bin", new Vector3(w + 0.43f, 0.33f, -0.55f), new Vector3(0.5f, 0.66f, 0.5f));

            var spec = new MeshyArt.DoorSpec { Hinge = new Vector3(-1.6f, 0.15f, d), Width = 0.9f, Height = 2.1f, LeafDirection = 1f, WallFacing = 1f };
            BuildDoor(root, "ToiletDoor", spec, new Color(0.2f, 0.72f, 0.7f), new Color(0.08f, 0.45f, 0.47f), planks: true);

            var inside = new GameObject("Inside").transform;
            inside.SetParent(root, false);
            inside.localPosition = new Vector3(-1.15f, 0.16f, -0.3f);
            var outside = new GameObject("Outside").transform;
            outside.SetParent(root, false);
            outside.localPosition = new Vector3(-1.15f, 0f, 2.4f);
            outside.localRotation = Quaternion.Euler(0f, 180f, 0f); // facing the door
            outside.position = Ground(outside.position);

            root.gameObject.AddComponent<NetworkObject>();
            var hut = root.gameObject.AddComponent<LoveHut>();
            SetRef(hut, "_door", root.GetComponentInChildren<Door>());
            SetRef(hut, "_outside", outside);
            SetRef(hut, "_inside", inside);
            SetRef(hut, "_wobble", wobble);
            SetRef(hut, "_audio", SpatialAudio(root.gameObject, 4f, 45f));
        }

        // ------------------------------------------------------------------ the beach bar

        /// <summary>
        /// The beach bar (Art/Props/beach_bar.glb): a thatched tiki bar facing the sea, stools along the counter and
        /// coconut drinks on it (an ItemRack keeps three there: pick one up and drink it).
        /// </summary>
        private static void BuildBeachBar(Transform parent)
        {
            var root = new GameObject("BeachBar").transform;
            root.SetParent(parent, false);
            root.position = Ground(BarSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the counter faces the sea
            TagSurface(root.gameObject, SurfaceKind.Wood);
            PropModel("beach_bar", root);
            void Solid(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                go.AddComponent<BoxCollider>().size = size;
            }
            Solid("Deck", new Vector3(0f, 0.075f, 0f), new Vector3(4.6f, 0.15f, 3.4f));
            Solid("Counter", new Vector3(0f, 0.62f, 1.2f), new Vector3(4.3f, 1.14f, 0.72f));
            Solid("BackShelf", new Vector3(0f, 1.2f, -1.32f), new Vector3(3.8f, 2.4f, 0.42f));
            Solid("Roof", new Vector3(0f, 3.0f, 0f), new Vector3(5.4f, 0.5f, 4.2f));
            foreach (float x in new[] { -2.1f, 2.1f })
                foreach (float z in new[] { -1.5f, 1.5f })
                    Solid("Post", new Vector3(x, 1.4f, z), new Vector3(0.18f, 2.6f, 0.18f));
            for (int i = 0; i < 4; i++)
                Solid("Stool", new Vector3(-1.5f + i, 0.4f, 1.85f), new Vector3(0.22f, 0.8f, 0.22f)); // (slim: you can step between them to the counter)
            Solid("Menu", new Vector3(-2.45f, 0.7f, 1.4f), new Vector3(0.1f, 1.4f, 0.7f));
            BuildBarista(root);

            root.gameObject.AddComponent<NetworkObject>();
            var rack = root.gameObject.AddComponent<ItemRack>();
            var spots = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                var spot = new GameObject($"DrinkSpot{i}").transform;
                spot.SetParent(root, false);
                spot.localPosition = new Vector3(-0.7f + i * 0.7f, 1.3f, 1.15f);
                spots.Add(spot);
            }
            SetField(rack, "_itemName", p => p.stringValue = "Coconut");
            SetRefs(rack, "_spots", spots.ToArray());
            SetField(rack, "_worldCap", p => p.intValue = 8);

            // Two beers always waiting at the ends of the counter (more from the barista).
            var beerRack = root.gameObject.AddComponent<ItemRack>();
            var beerSpots = new List<Object>();
            foreach (float x in new[] { -1.05f, 1.1f })
            {
                var spot = new GameObject("BeerSpot").transform;
                spot.SetParent(root, false);
                spot.localPosition = new Vector3(x, 1.36f, 1.15f);
                beerSpots.Add(spot);
            }
            SetField(beerRack, "_itemName", p => p.stringValue = "Beer");
            SetRefs(beerRack, "_spots", beerSpots.ToArray());
            SetField(beerRack, "_worldCap", p => p.intValue = 12);
        }

        /// <summary>
        /// The barista behind the counter (Fun/Barista): a stand-in body for now (his own model is coming), wiping the
        /// counter with a rag; Interact orders a beer, put down in front of whichever stool you're at.
        /// </summary>
        private static void BuildBarista(Transform bar)
        {
            var root = new GameObject("Barista").transform;
            root.SetParent(bar, false);
            root.localPosition = new Vector3(0.25f, 0.158f, 0.6f); // on the deck right behind the counter, facing the stools
            var capsule = root.gameObject.AddComponent<CapsuleCollider>();
            capsule.height = 1.85f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.92f, 0f);

            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(root, false);
            avatarGo.AddComponent<SkinnedMeshRenderer>();
            var rig = avatarGo.AddComponent<AvatarRig>();
            SetRef(rig, "_material", AvatarMaterial());
            SetBool(rig, "_buildOnAwake", false);
            var animator = avatarGo.AddComponent<AvatarAnimator>();
            SetRef(animator, "_rig", rig);
            SetRef(avatarGo.AddComponent<AvatarJiggle>(), "_rig", rig);

            Transform Marker(string name, Vector3 local)
            {
                var t = new GameObject(name).transform;
                t.SetParent(bar, false);
                t.localPosition = local;
                return t;
            }
            var serve = new List<Object>();
            for (int i = 0; i < 4; i++) serve.Add(Marker($"ServeSpot{i}", new Vector3(-1.5f + i, 1.36f, 1.38f)));
            Transform wipeFrom = Marker("WipeFrom", new Vector3(-0.25f, 1.19f, 0.98f));
            Transform wipeTo = Marker("WipeTo", new Vector3(0.75f, 1.19f, 0.98f));
            GameObject rag = Primitive(PrimitiveType.Cube, "Rag", root, new Vector3(0.3f, 1.03f, 0.38f), new Vector3(0.15f, 0.014f, 0.11f),
                GetMaterial("BarRag", new Color(0.92f, 0.9f, 0.84f)), keepCollider: false);

            var barista = root.gameObject.AddComponent<Barista>();
            SetRef(barista, "_rig", rig);
            SetRef(barista, "_animator", animator);
            SetRef(barista, "_audio", SpatialAudio(root.gameObject, 3f, 40f));
            SetRefs(barista, "_serveSpots", serve.ToArray());
            SetRef(barista, "_wipeFrom", wipeFrom);
            SetRef(barista, "_wipeTo", wipeTo);
            SetRef(barista, "_rag", rag.transform);
            ConfigureInteractable(root.gameObject.AddComponent<Interaction.Interactable>(), new Collider[] { capsule },
                new Renderer[] { avatarGo.GetComponent<SkinnedMeshRenderer>() }, 3.6f);
        }
    }
}
