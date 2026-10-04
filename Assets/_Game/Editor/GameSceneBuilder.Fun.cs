using System.Collections.Generic;
using FishNet.Object;
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
        private static readonly Vector3 HutSpot = new(21f, 0f, 19f);

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
                SetBool(go.GetComponent<Item>(), "_pocketable", true);
                SetRef(go.AddComponent<BallSounds>(), "_audio", SpatialAudio(go, 1.5f, 30f));
                // Continuous, not Continuous Speculative: speculative contacts ghost-bounced fast balls off the thin
                // rim before they got there (long shots went in 1 time in 10). The rim is static, so Continuous sweeps it.
                go.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Continuous;
            });
            yield return ball;
        }

        private static void BuildIsland1Fun(Transform env)
        {
            var fun = new GameObject("Island1Fun").transform;
            fun.SetParent(env, false);
            BuildHoop(fun);
            BuildRingTable(fun);
            BuildBeachHut(fun);
            BuildAttractions(fun); // trampolines, cannon, zipline, diving board, flamingo, banana boat
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

        // ------------------------------------------------------------------ the beach hut

        private static void BuildBeachHut(Transform parent)
        {
            var root = new GameObject("BeachHut").transform;
            root.SetParent(parent, false);
            root.position = Ground(HutSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the door faces the sea
            TagSurface(root.gameObject, SurfaceKind.Wood);
            GameObject model = PropModel("beach_hut", root);
            Transform wobble = model != null ? model.transform : root;

            // Walls you can't walk through (the model has no colliders of its own); a doorway in the front.
            void Wall(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                go.AddComponent<BoxCollider>().size = size;
            }
            Wall("Deck", new Vector3(0f, 0.075f, 0f), new Vector3(2.9f, 0.15f, 2.9f));
            Wall("WallBack", new Vector3(0f, 1.35f, -1.3f), new Vector3(2.7f, 2.4f, 0.1f));
            Wall("WallLeft", new Vector3(-1.3f, 1.35f, 0f), new Vector3(0.1f, 2.4f, 2.7f));
            Wall("WallRight", new Vector3(1.3f, 1.35f, 0f), new Vector3(0.1f, 2.4f, 2.7f));
            Wall("WallFrontL", new Vector3(-0.875f, 1.35f, 1.3f), new Vector3(0.85f, 2.4f, 0.1f));
            Wall("WallFrontR", new Vector3(0.875f, 1.35f, 1.3f), new Vector3(0.85f, 2.4f, 0.1f));
            Wall("Lintel", new Vector3(0f, 2.4f, 1.3f), new Vector3(0.9f, 0.3f, 0.1f));
            Wall("Roof", new Vector3(0f, 2.95f, 0f), new Vector3(2.9f, 0.8f, 2.9f));

            var spec = new MeshyArt.DoorSpec { Hinge = new Vector3(-0.45f, 0.15f, 1.3f), Width = 0.9f, Height = 2.1f, LeafDirection = 1f, WallFacing = 1f };
            BuildDoor(root, "HutDoor", spec, new Color(1f, 0.55f, 0.72f), new Color(0.97f, 0.97f, 0.95f), planks: true);

            var inside = new GameObject("Inside").transform;
            inside.SetParent(root, false);
            inside.localPosition = new Vector3(0f, 0.16f, -0.4f);
            var outside = new GameObject("Outside").transform;
            outside.SetParent(root, false);
            outside.localPosition = new Vector3(0f, 0f, 2.4f);
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
    }
}
