using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 1's attractions (besides the hoop, the ring table and the hut in GameSceneBuilder.Fun.cs): two
    /// trampolines, a human cannon on wheels that fires you (or a friend you carried over) out over the sea, a
    /// diving board off the dock and an inflatable flamingo you can paddle about on (a Meshy model). (The zipline is
    /// out for now.) Models: ArtSource/Tools/model_props.py and Art/Meshy/flamingo.glb.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector3[] TrampolineSpots = { new(-20f, 0f, 14f), new(-24.5f, 0f, 18.5f) };
        private static readonly Vector3 CannonSpot = new(28f, 0f, 7f);
        private static readonly Vector3 ZiplineTowerSpot = new(35f, 0f, 7f);
        private static readonly Vector3 ZiplinePostSpot = new(35f, 0f, -30f);
        private static readonly Vector3 DivingBoardSpot = new(-9.15f, 0.3f, -17.2f); // off the dock's side near its end (the end stays clear to run off)
        private static readonly Vector3 FlamingoSpot = new(-17f, 0f, -6f);

        /// <summary>Where the attractions stand (x, z, radius): towels keep clear.</summary>
        private static Vector3[] AttractionSpots() => new[]
        {
            new Vector3(TrampolineSpots[0].x, TrampolineSpots[0].z, 2.2f), new Vector3(TrampolineSpots[1].x, TrampolineSpots[1].z, 2.2f),
            new Vector3(CannonSpot.x, CannonSpot.z, 2.6f), new Vector3(HoopSpot.x, HoopSpot.z, 3f), new Vector3(HutSpot.x, HutSpot.z, 3f),
            new Vector3(RingTableSpot.x, RingTableSpot.z, 1.8f)
        };

        private static void BuildAttractions(Transform parent)
        {
            foreach (Vector3 spot in TrampolineSpots) BuildTrampoline(parent, spot);
            BuildCannon(parent);
            // BuildZipline(parent); // taken out for now (2026-10-05); the code and models stay for when it comes back
            BuildDivingBoard(parent);
            BuildFlamingo(parent);
            BuildMoreAttractions(parent); // banana boat (GameSceneBuilder.Attractions2.cs)
        }

        private static void BuildTrampoline(Transform parent, Vector3 at)
        {
            var root = new GameObject("Trampoline").transform;
            root.SetParent(parent, false);
            root.position = Ground(at);
            GameObject model = PropModel("trampoline", root);
            // The mat stands on its frame: a solid ring to land on round the edge, the bouncy trigger over the mat.
            var solid = new GameObject("Frame");
            solid.transform.SetParent(root, false);
            var box = solid.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.36f, 0f);
            box.size = new Vector3(2.4f, 0.72f, 2.4f);
            var pad = new GameObject("Bounce");
            pad.transform.SetParent(root, false);
            pad.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            pad.AddComponent<BoxCollider>().size = new Vector3(2.3f, 0.5f, 2.3f);
            var bounce = pad.AddComponent<BouncePad>();
            SetField(bounce, "_launch", p => p.floatValue = 14f);
            if (model != null) SetRef(bounce, "_squash", model.transform);
            SetRef(bounce, "_audio", SpatialAudio(pad, 2f, 30f));
        }

        private static void BuildCannon(Transform parent)
        {
            var root = new GameObject("HumanCannon").transform;
            root.SetParent(parent, false);
            root.position = Ground(CannonSpot);
            root.rotation = Quaternion.Euler(0f, 160f, 0f); // out over the sea
            // The carriage turns on its wheels (the turret), the barrel tips on its trunnions.
            var turret = new GameObject("Turret").transform;
            turret.SetParent(root, false);
            PropModel("human_cannon", turret);
            var body = new GameObject("Body");
            body.transform.SetParent(turret, false);
            var box = body.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.7f, -0.1f);
            box.size = new Vector3(1.3f, 1.4f, 2.0f);
            var wheels = new List<Object>();
            var radii = new List<float>();
            foreach (float side in new[] { -1f, 1f })
            foreach ((string model, Vector3 at, float radius) in new[] { ("cannon_wheel", new Vector3(0.68f, 0.48f, -0.5f), 0.48f), ("cannon_wheel_small", new Vector3(0.68f, 0.32f, 0.55f), 0.32f) })
            {
                var wheel = new GameObject("Wheel").transform;
                wheel.SetParent(turret, false);
                wheel.localPosition = new Vector3(at.x * side, at.y, at.z);
                PropModel(model, wheel);
                wheels.Add(wheel);
                radii.Add(radius);
            }
            var barrel = new GameObject("Barrel").transform;
            barrel.SetParent(turret, false);
            barrel.localPosition = new Vector3(0f, 1.0f, 0f);
            barrel.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            PropModel("cannon_barrel", barrel);
            var barrelBody = new GameObject("BarrelBody");
            barrelBody.transform.SetParent(barrel, false);
            var barrelBox = barrelBody.AddComponent<BoxCollider>();
            barrelBox.center = new Vector3(0f, 0f, 0.55f);
            barrelBox.size = new Vector3(0.8f, 0.8f, 2.9f);
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(barrel, false);
            mouth.localPosition = new Vector3(0f, 0f, 2.0f);
            root.gameObject.AddComponent<NetworkObject>();
            var cannon = root.gameObject.AddComponent<HumanCannon>();
            SetRef(cannon, "_turret", turret);
            SetRef(cannon, "_barrel", barrel);
            SetRefs(cannon, "_wheels", wheels.ToArray());
            SetField(cannon, "_wheelRadii", p =>
            {
                p.arraySize = radii.Count;
                for (int i = 0; i < radii.Count; i++) p.GetArrayElementAtIndex(i).floatValue = radii[i];
            });
            SetRef(cannon, "_mouth", mouth);
            SetField(cannon, "_power", p => p.floatValue = 28f); // ~25 m out: deep enough for a splash rating
            SetRef(cannon, "_audio", SpatialAudio(root.gameObject, 4f, 80f));
            ConfigureInteractable(root.gameObject.AddComponent<Interactable>(), new Collider[] { box, barrelBox }, root.GetComponentsInChildren<Renderer>(), 3f);
        }

        /// <summary>A wooden platform 4.5 m up with a ramp behind it, and the cable out to a post in the sea.</summary>
        private static void BuildZipline(Transform parent)
        {
            Material wood = GetMaterial("ZiplineWood", new Color(0.55f, 0.38f, 0.22f));
            Material rope = GetMaterial("ZiplineCable", new Color(0.25f, 0.25f, 0.27f));
            var root = new GameObject("Zipline").transform;
            root.SetParent(parent, false);
            root.position = Ground(ZiplineTowerSpot);
            root.rotation = Quaternion.Euler(0f, 180f, 0f); // the cable runs out to sea (local +z)
            TagSurface(root.gameObject, SurfaceKind.Wood);
            const float h = 4.5f;
            Primitive(PrimitiveType.Cube, "Platform", root, new Vector3(0f, h, 0f), new Vector3(2.4f, 0.2f, 2.4f), wood);
            foreach (float x in new[] { -1.1f, 1.1f })
            foreach (float z in new[] { -1.1f, 1.1f })
                Primitive(PrimitiveType.Cube, "Leg", root, new Vector3(x, h * 0.5f, z), new Vector3(0.2f, h, 0.2f), wood);
            foreach (float x in new[] { -1.15f, 1.15f })
                Primitive(PrimitiveType.Cube, "Rail", root, new Vector3(x, h + 0.55f, 0f), new Vector3(0.08f, 0.08f, 2.4f), wood, keepCollider: false);
            // The ramp up (behind, inland): 35 degrees like the tower's.
            float rampLen = h / Mathf.Sin(35f * Mathf.Deg2Rad);
            float run = h / Mathf.Tan(35f * Mathf.Deg2Rad);
            GameObject ramp = Primitive(PrimitiveType.Cube, "Ramp", root, new Vector3(0f, h * 0.5f, -1.2f - run * 0.5f), new Vector3(1.1f, 0.12f, rampLen), wood);
            ramp.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            for (int i = 1; i < 12; i++) // slats so it reads as steps
                Primitive(PrimitiveType.Cube, "Slat", ramp.transform, new Vector3(0f, 0.6f, -0.5f + i / 12f), new Vector3(1f, 0.6f, 0.02f), wood, keepCollider: false);
            // The gantry and the cable's top end over the front edge.
            Primitive(PrimitiveType.Cube, "Gantry", root, new Vector3(0f, h + 2.8f, 1.1f), new Vector3(1.6f, 0.15f, 0.15f), wood, keepCollider: false);
            foreach (float x in new[] { -0.75f, 0.75f })
                Primitive(PrimitiveType.Cube, "GantryPost", root, new Vector3(x, h + 1.4f, 1.1f), new Vector3(0.12f, 2.8f, 0.12f), wood, keepCollider: false);
            var start = new GameObject("CableTop").transform;
            start.SetParent(root, false);
            start.localPosition = new Vector3(0f, h + 2.65f, 1.3f);

            // The far post in the sea.
            Vector3 postAt = new Vector3(ZiplinePostSpot.x, BeachHeight(ZiplinePostSpot.x, ZiplinePostSpot.z) - 0.2f, ZiplinePostSpot.z);
            var post = new GameObject("ZiplinePost").transform;
            post.SetParent(parent, false);
            post.position = postAt;
            PropModel("zipline_post", post);
            post.gameObject.AddComponent<CapsuleCollider>().height = 7f;
            post.GetComponent<CapsuleCollider>().center = new Vector3(0f, 3.5f, 0f);
            post.GetComponent<CapsuleCollider>().radius = 0.18f;
            var end = new GameObject("CableEnd").transform;
            end.SetParent(post, false);
            end.localPosition = new Vector3(0f, 6.5f, 0f);

            // The cable itself: a thin stretched cylinder from end to end.
            Vector3 a = start.position, b = end.position;
            GameObject cable = Primitive(PrimitiveType.Cylinder, "Cable", parent, (a + b) * 0.5f, new Vector3(0.03f, Vector3.Distance(a, b) * 0.5f, 0.03f), rope, keepCollider: false);
            cable.transform.position = (a + b) * 0.5f;
            cable.transform.rotation = Quaternion.FromToRotation(Vector3.up, b - a);

            // The handle, waiting at the top: Interact with it to ride.
            var handle = new GameObject("Handle").transform;
            handle.SetParent(root, false);
            handle.position = a;
            handle.rotation = Quaternion.LookRotation(b - a);
            GameObject handleModel = PropModel("zipline_handle", handle);
            var grab = new GameObject("Grab");
            grab.transform.SetParent(handle, false);
            grab.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            var grabBox = grab.AddComponent<BoxCollider>();
            grabBox.size = new Vector3(0.8f, 0.9f, 0.5f);
            grabBox.isTrigger = false;
            var zip = root.gameObject.AddComponent<Zipline>();
            SetRef(zip, "_start", start);
            SetRef(zip, "_end", end);
            SetRef(zip, "_handle", handle);
            SetRef(zip, "_audio", SpatialAudio(handle.gameObject, 3f, 50f));
            ConfigureInteractable(root.gameObject.AddComponent<Interactable>(), new Collider[] { grabBox },
                handleModel != null ? handleModel.GetComponentsInChildren<Renderer>() : new Renderer[0], 3.2f);
        }

        private static void BuildDivingBoard(Transform parent)
        {
            var root = new GameObject("DivingBoard").transform;
            root.SetParent(parent, false);
            root.position = DivingBoardSpot;
            root.rotation = Quaternion.Euler(0f, -90f, 0f); // out over the deep water beside the dock
            TagSurface(root.gameObject, SurfaceKind.Wood);
            GameObject model = PropModel("diving_board", root);
            var board = new GameObject("Board");
            board.transform.SetParent(root, false);
            var box = board.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.45f, 1.3f);
            box.size = new Vector3(0.55f, 0.08f, 2.6f);
            // The springy tip: step off the end and it throws you up and out.
            var pad = new GameObject("Spring");
            pad.transform.SetParent(root, false);
            pad.transform.localPosition = new Vector3(0f, 0.75f, 2.2f);
            pad.AddComponent<BoxCollider>().size = new Vector3(0.6f, 0.5f, 0.9f);
            var bounce = pad.AddComponent<BouncePad>();
            SetField(bounce, "_launch", p => p.floatValue = 11f);
            SetField(bounce, "_push", p => p.vector3Value = new Vector3(0f, 0f, 3.5f));
            if (model != null) SetRef(bounce, "_squash", model.transform);
            SetRef(bounce, "_audio", SpatialAudio(pad, 2f, 30f));
        }

        /// <summary>The inflatable flamingo: a slow paddling "vehicle" (no keys, no engine), sit on its back ring and steer by the neck.</summary>
        private static void BuildFlamingo(Transform parent)
        {
            Material pink = GetMaterial("FlamingoPink", new Color(1f, 0.55f, 0.7f));
            var root = new GameObject("Flamingo");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(OnWater(FlamingoSpot), Quaternion.Euler(0f, 200f, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 70f;
            body.linearDamping = 0.4f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.8f, 0.5f, 1.8f), pink);
            Primitive(PrimitiveType.Cube, "Neck", root.transform, new Vector3(0f, 1.1f, 0.55f), new Vector3(0.3f, 1.2f, 0.3f), pink, keepCollider: false);
            Transform P(string n, Vector3 p)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, 0.55f, -0.45f));
            Transform gripL = P("GripLeft", new Vector3(-0.13f, 0.95f, 0.42f));
            Transform gripR = P("GripRight", new Vector3(0.13f, 0.95f, 0.42f));
            gripL.localRotation = gripR.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Transform thrust = P("Thrust", new Vector3(0f, 0f, -0.9f));
            GameObject prefab = LoadMeshyModel("flamingo");
            if (prefab != null)
            {
                SwapGreybox(root.transform, prefab, Vector3.zero, "Hull");
                ColliderBox(root.transform, "NeckCollider", new Vector3(0f, 1.1f, 0.55f), new Vector3(0.35f, 1.3f, 0.35f));
            }
            Vehicle vehicle = FinishVehicle(root, "Flamingo", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.12f,
                thrustForce: 3.5f, maxSpeed: 4f, turnRate: 1.3f);
            Object engine = new SerializedObject(vehicle).FindProperty("_engineAudio").objectReferenceValue;
            SetRef(vehicle, "_engineAudio", null); // no engine: it's paddled
            if (engine != null) Object.DestroyImmediate(engine);
        }
    }
}
