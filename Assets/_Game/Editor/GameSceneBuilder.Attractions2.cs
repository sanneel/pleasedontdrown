using System.Collections.Generic;
using System.IO;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// More of island 1's attractions: the banana boat the lifeguards' jet ski tows, the punch-the-bell strongman and
    /// the coconut shy by the basketball court (with its own throwing balls).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector3 BananaSpot = new(1.5f, 0f, -12.5f);
        private static readonly Vector3 StrongmanSpot = new(-2f, 0f, 20f);
        private static readonly Vector3 CoconutShySpot = new(3.5f, 0f, 29f); // beside the basketball court

        private static Vector3[] MoreAttractionSpots() => new[]
        {
            new Vector3(StrongmanSpot.x, StrongmanSpot.z, 2f), new Vector3(CoconutShySpot.x, CoconutShySpot.z, 3f),
            new Vector3(CoconutShySpot.x, CoconutShySpot.z - 4f, 2.5f)
        };

        private static void BuildMoreAttractions(Transform parent)
        {
            BuildBananaBoat(parent);
            BuildStrongman(parent);
            BuildCoconutShy(parent);
        }

        private static Item BuildShyBall()
        {
            PhysicsMaterial rubber = GetPhysicsMaterial("ShyBall", 0.35f, 0.6f, PhysicsMaterialCombine.Average);
            Material red = GetMaterial("ShyBallRed", new Color(0.85f, 0.15f, 0.12f));
            return BuildItem("ShyBall", "Shy Ball", 0.35f, new Vector3(0.12f, -0.25f, 0.55f), Vector3.zero, 1.3f, rubber, root =>
            {
                Primitive(PrimitiveType.Sphere, "Ball", root, Vector3.zero, Vector3.one * 0.11f, red);
            }, linearDamping: 0.05f, angularDamping: 0.2f, density: 0.4f, waterDrag: 0.8f,
                configure: go => SetBool(go.GetComponent<Item>(), "_pocketable", true));
        }

        // ------------------------------------------------------------------ banana boat

        private static void BuildBananaBoat(Transform parent)
        {
            // The lifeguards' own jet ski (no keys), parked just in front of the banana's nose: sit on it and the
            // banana hitches on. (The robber's jet ski, with keys, is still the story's way off the island.)
            BuildJetSki(parent, OnWater(BananaSpot + new Vector3(0f, 0f, -6.3f)), 180f, key: "", displayName: "Lifeguard Jet Ski");

            Material yellow = GetMaterial("BananaYellow", new Color(1f, 0.85f, 0.2f));
            Material rope = GetMaterial("TowRope", new Color(0.85f, 0.78f, 0.55f));
            var root = new GameObject("BananaBoat");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(OnWater(BananaSpot), Quaternion.Euler(0f, 180f, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 120f;
            body.linearDamping = 0.3f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.3f, 0.6f, 4.4f), yellow);
            Transform P(string n, Vector3 p)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, 0.72f, 1.2f));
            Transform gripL = P("GripLeft", new Vector3(-0.14f, 0.82f, 1.55f));
            Transform gripR = P("GripRight", new Vector3(0.14f, 0.82f, 1.55f));
            gripL.localRotation = gripR.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Transform thrust = P("Thrust", new Vector3(0f, 0f, -2f));
            Transform nose = P("Nose", new Vector3(0f, 0.75f, 2.45f));
            GameObject model = PropModel("banana_boat", root.transform);
            if (model != null)
            {
                Object.DestroyImmediate(hull.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(hull.GetComponent<MeshFilter>());
            }
            Vehicle vehicle = FinishVehicle(root, "Banana Boat", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.15f,
                thrustForce: 0f, maxSpeed: 1f, turnRate: 0f); // (max speed must not be 0: Vehicle divides by it)
            Object engine = new SerializedObject(vehicle).FindProperty("_engineAudio").objectReferenceValue;
            SetRef(vehicle, "_engineAudio", null);
            if (engine != null) Object.DestroyImmediate(engine);

            var line = root.AddComponent<LineRenderer>();
            line.positionCount = 9;
            line.widthMultiplier = 0.035f;
            line.sharedMaterial = rope;
            line.useWorldSpace = true;
            line.enabled = false;
            var banana = root.AddComponent<BananaBoat>();
            SetRef(banana, "_vehicle", vehicle);
            SetRef(banana, "_nose", nose);
            SetRef(banana, "_rope", line);
        }

        // ------------------------------------------------------------------ strongman

        private static void BuildStrongman(Transform parent)
        {
            var root = new GameObject("Strongman").transform;
            root.SetParent(parent, false);
            root.position = Ground(StrongmanSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the pad faces the station beach
            PropModel("high_striker", root);
            var baseBox = new GameObject("Base");
            baseBox.transform.SetParent(root, false);
            var box = baseBox.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.15f, 0.1f);
            box.size = new Vector3(1.2f, 0.3f, 1.4f);
            var pole = new GameObject("PoleCollider");
            pole.transform.SetParent(root, false);
            var poleBox = pole.AddComponent<BoxCollider>();
            poleBox.center = new Vector3(0f, 2.75f, -0.2f);
            poleBox.size = new Vector3(0.24f, 5f, 0.16f);
            // The pad: punch it (a damageable target).
            var pad = new GameObject("Pad");
            pad.transform.SetParent(root, false);
            pad.transform.localPosition = new Vector3(0f, 0.75f, 0.55f);
            pad.AddComponent<BoxCollider>().size = new Vector3(0.7f, 0.9f, 0.7f);
            var puckRail = new GameObject("PuckRail").transform;
            puckRail.SetParent(root, false);
            puckRail.localPosition = new Vector3(0f, 0.5f, -0.05f);
            GameObject puck = PropModel("striker_puck", puckRail);
            var dial = new GameObject("DialPivot").transform;
            dial.SetParent(root, false);
            dial.localPosition = new Vector3(0.75f, 1.45f, 0.645f);
            GameObject needle = PropModel("striker_needle", dial);
            root.gameObject.AddComponent<NetworkObject>();
            var game = root.gameObject.AddComponent<Strongman>();
            if (needle != null) SetRef(game, "_needle", needle.transform);
            if (puck != null) SetRef(game, "_puck", puck.transform);
            SetRef(game, "_audio", SpatialAudio(pad, 3f, 40f));
            var bell = new GameObject("Bell");
            bell.transform.SetParent(root, false);
            bell.transform.localPosition = new Vector3(0f, 5.3f, -0.2f);
            SetRef(game, "_bellAudio", SpatialAudio(bell, 6f, 90f));
        }

        // ------------------------------------------------------------------ coconut shy

        private static void BuildCoconutShy(Transform parent)
        {
            var root = new GameObject("CoconutShy").transform;
            root.SetParent(parent, false);
            root.position = Ground(CoconutShySpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the counter faces the station
            PropModel("coconut_shy", root);
            void Solid(string n, Vector3 c, Vector3 s)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root, false);
                go.transform.localPosition = c;
                go.AddComponent<BoxCollider>().size = s;
            }
            Solid("Counter", new Vector3(0f, 0.5f, 0.45f), new Vector3(3.1f, 1f, 0.55f));
            Solid("Backdrop", new Vector3(0f, 1.3f, -2.25f), new Vector3(3.1f, 2.6f, 0.1f));
            var posts = new List<Object>();
            foreach (float x in new[] { -0.9f, 0f, 0.9f })
            {
                Solid("Post", new Vector3(x, 0.65f, -1.6f), new Vector3(0.12f, 1.3f, 0.12f));
                Solid("Cup", new Vector3(x, 1.31f, -1.6f), new Vector3(0.16f, 0.02f, 0.16f));
                var top = new GameObject("PostTop").transform;
                top.SetParent(root, false);
                top.localPosition = new Vector3(x, 1.45f, -1.6f);
                posts.Add(top);
            }
            root.gameObject.AddComponent<NetworkObject>();
            var shy = root.gameObject.AddComponent<CoconutShy>();
            SetRefs(shy, "_posts", posts.ToArray());
            SetRef(shy, "_audio", SpatialAudio(root.gameObject, 3f, 40f));
            // Three balls on the counter, kept in stock.
            var rack = root.gameObject.AddComponent<ItemRack>();
            var spots = new List<Object>();
            for (int i = 0; i < 3; i++)
            {
                var spot = new GameObject($"BallSpot{i}").transform;
                spot.SetParent(root, false);
                spot.localPosition = new Vector3(-0.6f + i * 0.6f, 1.08f, 0.4f);
                spots.Add(spot);
            }
            SetField(rack, "_itemName", p => p.stringValue = "Shy Ball");
            SetRefs(rack, "_spots", spots.ToArray());
            SetField(rack, "_worldCap", p => p.intValue = 8);
        }
    }
}
