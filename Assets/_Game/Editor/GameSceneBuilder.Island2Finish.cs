using System;
using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 2 finished (Docs/Overnight-Prompt-Island-2.md, tasks 4 and 7-9), as one batch that can run again: it
    /// removes what it built last time (Environment/Island2Finish) and builds it afresh. Nothing else in the scene
    /// changes: the hotel (Shako's model), the bar, the pool and the dock stay as they are.
    ///  * The marina: a second pier on the east beach with the hotel's yacht (Tripo, island 3 batch) moored along it;
    ///    the yacht is the boat to Skull Cove in beat 2.7 (StoryDirector finds the Vehicle called "Yacht").
    ///  * The cushion jetty on the west beach: the inflatable cushion (Fun.CushionRide), the lifeguards' jet ski to tow
    ///    it, a sign and a course of four buoys out west, clear of the guests' swimming water and the main dock.
    ///  * Gala decorations (shown only on the gala night, World.NightGlow): a red carpet from the hotel's door, velvet
    ///    ropes, string lights over it, the champagne tower and two chandeliers in the casino.
    ///  * Palms along the beach and the cart road, flower beds by the hotel's front, hedges round the pool, lamp posts
    ///    along the road (lit at night only, emissive), signposts, and a "GRAND CORAL" arch over the dock.
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Island2FinishBatch</c>
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string Island2FinishName = "Island2Finish";
        private const string TripoYachtPath = "Assets/_Game/Art/Tripo/Island2/yacht.glb";

        // Where things go (world x/z). The beach's waterline runs about z -212 in the middle, curving back at the ends.
        private const float MarinaX = 80f, CushionJettyX = -45f;
        private static readonly Vector2[] CushionBuoys = { new(-62f, -172f), new(-95f, -145f), new(-62f, -115f), new(-32f, -145f) };
        private static readonly Vector3 HotelDoorFront = new(20f, 0f, -243.2f);
        private const float CarpetLength = 12f, CarpetWidth = 2.2f;
        private static readonly Vector3 CasinoCentre = new(2.2f, 0f, -253.5f);

        public static void Island2FinishBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            Physics.SyncTransforms();
            if (env.Find(Island2FinishName) is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(Island2FinishName).transform;
            root.SetParent(env, false);

            BuildMarina(root);
            BuildCushionJetty(root);
            BuildGalaDecor(root);
            BuildIsland2Greenery(root);
            BuildIsland2Lamps(root);
            BuildIsland2Signs(root);
            BuildDockArch(root);
            BuildForecourtFountain(root);
            BuildLoungerSeats(root);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            AssignSceneIds(scene); // the yacht, the cushion and the jet ski are networked scene objects
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Island2Finish] PASS: built {root.childCount} groups, {root.GetComponentsInChildren<Transform>().Length} objects");
        }

        // ------------------------------------------------------------------ ground and shore

        /// <summary>The top of whatever is under a point (terrain, paving, decks), looking down from <paramref name="from"/> m up.</summary>
        private static float SurfaceUnder(Vector3 p, float from = 3.5f)
        {
            var ray = new Ray(new Vector3(p.x, from, p.z), Vector3.down);
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, from + 20f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.point.y > best) best = hit.point.y;
            return float.IsNegativeInfinity(best) ? GroundUnder(p) : best;
        }

        /// <summary>Walking north from inland at <paramref name="x"/>: the z where the sand goes under the sea.</summary>
        private static float WaterlineZ(float x)
        {
            for (float z = -260f; z < -150f; z += 0.5f)
                if (GroundUnder(new Vector3(x, 0f, z)) < WaterLevel) return z;
            return -212f;
        }

        /// <summary>Something other than sand within <paramref name="radius"/> of a spot on the ground (a wall, a road sign...).</summary>
        private static bool Blocked(Vector3 feet, float radius)
        {
            foreach (Collider c in Physics.OverlapSphere(feet + Vector3.up * (radius + 0.3f), radius, ~0, QueryTriggerInteraction.Ignore))
                if (c.gameObject.name != "BeachTerrain" && !c.transform.IsChildOf(GameObject.Find("Environment/BeachTerrain").transform)) return true;
            return false;
        }

        private static bool NearCartRoad(Vector2 p, float distance)
        {
            for (int i = 0; i + 1 < CartRoad.Length; i++)
            {
                Vector2 a = CartRoad[i], b = CartRoad[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                if ((a + ab * t - p).magnitude < distance) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the marina and the yacht (task 8)

        private static void BuildMarina(Transform parent)
        {
            var root = new GameObject("Marina").transform;
            root.SetParent(parent, false);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            float shore = WaterlineZ(MarinaX);
            // From the sand (deck top level with it) 32 m out to sea.
            float start = shore - 7f;
            while (GroundUnder(new Vector3(MarinaX, 0f, start)) > 0.45f && start < shore) start += 0.5f;
            const float length = 40f, width = 2.6f, deckTop = 0.5f;
            Material planks = GetMaterial("MarinaDeck", new Color(0.62f, 0.47f, 0.32f), smoothness: 0.2f);
            Material posts = GetMaterial("MarinaPost", new Color(0.36f, 0.26f, 0.18f), smoothness: 0.15f);
            Material rope = GetMaterial("MarinaRope", new Color(0.9f, 0.85f, 0.7f));
            float mid = start + length * 0.5f;
            Primitive(PrimitiveType.Cube, "Deck", root, new Vector3(MarinaX, deckTop - 0.15f, mid), new Vector3(width, 0.3f, length), planks);
            for (float z = start + 2f; z <= start + length; z += 4f)
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = MarinaX + side * (width * 0.5f - 0.15f);
                    float bed = GroundUnder(new Vector3(x, 0f, z));
                    float top = deckTop + 0.35f;
                    Primitive(PrimitiveType.Cylinder, "Post", root, new Vector3(x, (top + bed) * 0.5f, z), new Vector3(0.28f, (top - bed) * 0.5f, 0.28f), posts, keepCollider: false);
                }
            // Mooring cleats along the yacht's side.
            for (int i = 0; i < 3; i++)
                Primitive(PrimitiveType.Cube, "Cleat", root, new Vector3(MarinaX + width * 0.5f - 0.2f, deckTop + 0.06f, start + 10f + i * 8f), new Vector3(0.12f, 0.12f, 0.4f), rope, keepCollider: false);
            (float yachtDeck, float aftZ) = BuildYacht(root, new Vector3(MarinaX + width * 0.5f, 0f, start + length - 7f));
            // Boarding: a platform at the yacht's deck height reaching just over the pier's edge (the hull's box starts
            // past it, so the yacht never rests on it), two steps up from the pier.
            float rise = yachtDeck - deckTop;
            var platformTop = yachtDeck - 0.04f;
            GameObject platform = Primitive(PrimitiveType.Cube, "BoardingPlatform", root, new Vector3(MarinaX + width * 0.5f - 0.45f, platformTop - 0.15f, aftZ),
                new Vector3(1.5f, 0.3f, 1.6f), planks);
            TagSurface(platform, SurfaceKind.Wood);
            for (int s = 1; s <= 2; s++)
            {
                float top = deckTop + rise * s / 3f;
                Primitive(PrimitiveType.Cube, "BoardingStep", root, new Vector3(MarinaX + width * 0.5f - 0.45f, (top + deckTop - 0.3f) * 0.5f, aftZ - 0.8f - (3 - s) * 0.45f),
                    new Vector3(1.5f, top - deckTop + 0.3f, 0.45f), planks);
            }
            Primitive(PrimitiveType.Cube, "PlatformBase", root, new Vector3(MarinaX + width * 0.5f - 0.6f, (platformTop - 0.3f + deckTop - 0.3f) * 0.5f, aftZ),
                new Vector3(1.2f, platformTop - deckTop, 1.6f), posts, keepCollider: false);
            Debug.Log($"[Island2Finish] marina: waterline z {shore:F1}, pier z {start:F1}..{start + length:F1} at x {MarinaX}, yacht deck {yachtDeck:F2}, boarding at z {aftZ:F1}, seabed under the yacht {GroundUnder(new Vector3(MarinaX + 4f, 0f, start + length - 7f)):F2}");
        }

        /// <summary>The hotel's yacht (the Tripo motor cruiser, 10 m long) against the pier's east side at <paramref name="pierEdge"/>.</summary>
        private static (float deck, float aftZ) BuildYacht(Transform parent, Vector3 pierEdge)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TripoYachtPath) ?? throw new InvalidOperationException("yacht.glb missing: " + TripoYachtPath);
            var root = new GameObject("Yacht");
            root.transform.SetParent(parent, false);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 3000f;
            body.linearDamping = 0.15f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // The model, its long side along the yacht's z (bow forward = north, out to sea), 10 m long.
            GameObject model = Object.Instantiate(prefab, root.transform, false);
            model.name = "Tripo_yacht";
            model.transform.localScale = Vector3.one * 10f;
            Bounds b0 = RendererBounds(model.transform);
            if (b0.size.x > b0.size.z) model.transform.localRotation = Quaternion.Euler(0f, YachtModelYaw, 0f);
            Bounds b = RendererBounds(model.transform);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z); // keel at the root, centred
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            b = RendererBounds(model.transform);

            // Measure the model where people stand (its points, along the middle: the main deck and the cabin's roof).
            Mesh hullMesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * model.GetComponentInChildren<MeshFilter>().transform.localToWorldMatrix;
            float length = b.size.z, beam = b.size.x;
            const float bin = 0.25f;
            int bins = Mathf.CeilToInt(length / bin) + 1;
            var top = new float[bins];
            foreach (Vector3 v in hullMesh.vertices)
            {
                Vector3 p = toRoot.MultiplyPoint3x4(v);
                if (Mathf.Abs(p.x) > 0.6f) continue;
                int i = Mathf.Clamp(Mathf.FloorToInt((p.z - b.min.z) / bin), 0, bins - 1);
                top[i] = Mathf.Max(top[i], p.y);
            }
            var samples = new List<(float z, float y)>();
            for (int i = 2; i < bins - 2; i++) if (top[i] > 0.3f) samples.Add((b.min.z + (i + 0.5f) * bin, top[i]));
            var decks = samples.Where(s => s.y > 0.3f).Select(s => s.y).OrderBy(y => y).ToList();
            float deck = Mathf.Max(1f, decks.Count > 0 ? decks[decks.Count / 5] : 1f);
            var cabin = samples.Where(s => s.y > deck + 1.2f).ToList();
            float cabinZ0 = cabin.Count > 0 ? cabin.Min(s => s.z) : 0f, cabinZ1 = cabin.Count > 0 ? cabin.Max(s => s.z) : 0f;
            float roof = cabin.Count > 0 ? cabin.Select(s => s.y).OrderBy(y => y).ElementAt(cabin.Count / 2) : deck;

            // Colliders: the hull up to the deck (what floats and what you stand on), the cabin as a block.
            var hull = new GameObject("Hull").AddComponent<BoxCollider>();
            hull.transform.SetParent(root.transform, false);
            hull.center = new Vector3(0f, deck * 0.5f, b.center.z);
            hull.size = new Vector3(beam * 0.86f, deck, length * 0.94f);
            TagSurface(hull.gameObject, SurfaceKind.Wood);
            if (cabin.Count > 0)
            {
                var block = new GameObject("Cabin").AddComponent<BoxCollider>();
                block.transform.SetParent(root.transform, false);
                block.center = new Vector3(0f, (deck + roof) * 0.5f, (cabinZ0 + cabinZ1) * 0.5f);
                block.size = new Vector3(beam * 0.62f, roof - deck, cabinZ1 - cabinZ0);
            }

            // The helm: a small console with a wheel on the aft deck (the driver stands at it, looking forward).
            float helmZ = cabin.Count > 0 ? cabinZ0 - 0.9f : b.min.z + 2f;
            Material teak = GetMaterial("YachtTeak", new Color(0.55f, 0.36f, 0.2f), smoothness: 0.45f);
            Material chrome = GetMaterial("Chrome", new Color(0.85f, 0.87f, 0.9f), 0.9f, 0.85f);
            Primitive(PrimitiveType.Cube, "HelmConsole", root.transform, new Vector3(0f, deck + 0.45f, helmZ + 0.35f), new Vector3(0.7f, 0.9f, 0.35f), teak, keepCollider: false);
            GameObject wheel = Primitive(PrimitiveType.Cylinder, "HelmWheel", root.transform, new Vector3(0f, deck + 1.05f, helmZ + 0.15f), new Vector3(0.5f, 0.02f, 0.5f), chrome, keepCollider: false);
            wheel.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            Transform P(string n, Vector3 p, Quaternion r)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.SetLocalPositionAndRotation(p, r);
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, deck + 0.55f, helmZ - 0.5f), Quaternion.identity);
            Transform gripL = P("GripLeft", new Vector3(-0.17f, deck + 1.12f, helmZ + 0.08f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform gripR = P("GripRight", new Vector3(0.17f, deck + 1.12f, helmZ + 0.08f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform thrust = P("Thrust", new Vector3(0f, 0.2f, b.min.z + 0.5f), Quaternion.identity);

            // Floats with about 0.9 m under water (the teal stripe at the waterline), of the model's whole height.
            float density = Mathf.Clamp(0.9f / Mathf.Max(1f, b.size.y), 0.08f, 0.4f);
            Vehicle yacht = FinishVehicle(root, "Yacht", "", seat, gripL, gripR, thrust, hull, density, thrustForce: 7f, maxSpeed: 13f, turnRate: 0.7f);
            root.transform.SetPositionAndRotation(new Vector3(pierEdge.x + beam * 0.5f + 0.1f, WaterLevel - 0.9f, pierEdge.z), Quaternion.identity);
            Debug.Log($"[Island2Finish] yacht: {length:F1} x {beam:F1} x {b.size.y:F1} m, deck {deck:F2}, cabin z {cabinZ0:F1}..{cabinZ1:F1} roof {roof:F2}, density {density:F2}, at {root.transform.position}");
            _ = yacht;
            return (root.transform.position.y + deck, root.transform.TransformPoint(new Vector3(0f, 0f, helmZ - 0.4f)).z);
        }

        /// <summary>How far the yacht model is turned to point its bow along +z (its length is along its x as imported).</summary>
        private const float YachtModelYaw = 90f;

        // ------------------------------------------------------------------ the cushion jetty and its course (task 4)

        private static void BuildCushionJetty(Transform parent)
        {
            var root = new GameObject("CushionJetty").transform;
            root.SetParent(parent, false);
            float shore = WaterlineZ(CushionJettyX);
            float start = shore - 6f;
            while (GroundUnder(new Vector3(CushionJettyX, 0f, start)) > 0.45f && start < shore) start += 0.5f;
            const float length = 24f, width = 2.2f, deckTop = 0.5f;
            Material planks = GetMaterial("MarinaDeck", new Color(0.62f, 0.47f, 0.32f), smoothness: 0.2f);
            Material posts = GetMaterial("MarinaPost", new Color(0.36f, 0.26f, 0.18f), smoothness: 0.15f);
            var deckGo = Primitive(PrimitiveType.Cube, "Deck", root, new Vector3(CushionJettyX, deckTop - 0.15f, start + length * 0.5f), new Vector3(width, 0.3f, length), planks);
            TagSurface(deckGo, SurfaceKind.Wood);
            for (float z = start + 2f; z <= start + length; z += 4f)
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = CushionJettyX + side * (width * 0.5f - 0.15f);
                    float bed = GroundUnder(new Vector3(x, 0f, z));
                    float top = deckTop + 0.35f;
                    Primitive(PrimitiveType.Cylinder, "Post", root, new Vector3(x, (top + bed) * 0.5f, z), new Vector3(0.24f, (top - bed) * 0.5f, 0.24f), posts, keepCollider: false);
                }
            // The sign at the land end.
            Signpost(root, "CushionSign", new Vector3(CushionJettyX + width * 0.5f + 0.6f, 0f, start - 0.5f), 180f, "CUSHION RIDE", "$40 A GUEST");

            // The cushion on the west side near the end, nose out to sea; the jet ski that tows it just ahead.
            float end = start + length;
            Vector3 cushionAt = OnWater(new Vector3(CushionJettyX - width * 0.5f - 1.9f, 0f, end - 3f));
            BuildJetSki(root, OnWater(new Vector3(cushionAt.x, 0f, end + 5f)), 0f, key: "", displayName: "Lifeguard Jet Ski");
            var home = new GameObject("CushionHome").transform;
            home.SetParent(root, false);
            home.position = cushionAt;
            var buoys = new List<Transform>();
            for (int i = 0; i < CushionBuoys.Length; i++)
            {
                Vector2 c = CushionBuoys[i];
                var buoy = new GameObject("CushionBuoy" + i).transform;
                buoy.SetParent(root, false);
                buoy.position = new Vector3(c.x, WaterLevel, c.y);
                if (PropModel("buoy", buoy, Vector3.zero, 0f, 2.2f) == null)
                    Primitive(PrimitiveType.Sphere, "Ball", buoy, Vector3.up * 0.3f, Vector3.one * 1.1f, GetMaterial("BuoyOrange", new Color(1f, 0.45f, 0.1f)), keepCollider: false);
                buoys.Add(buoy);
            }
            BuildCushion(root, cushionAt, home, buoys.ToArray());
        }

        private static void BuildCushion(Transform parent, Vector3 at, Transform home, Transform[] buoys)
        {
            GameObject prefab = TripoIsland2("inflatable_cushion") ?? throw new InvalidOperationException("inflatable_cushion.glb missing");
            var root = new GameObject("Cushion");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 90f;
            body.linearDamping = 0.3f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject model = Object.Instantiate(prefab, root.transform, false);
            model.name = "Tripo_inflatable_cushion";
            model.transform.localRotation = Quaternion.Euler(0f, CushionModelYaw, 0f);
            model.transform.localScale = Vector3.one * 1.15f;
            Bounds b = RendererBounds(model.transform);
            model.transform.position -= new Vector3(b.center.x - at.x, b.min.y - at.y, b.center.z - at.z);
            b = RendererBounds(model.transform);
            var hull = new GameObject("Hull").AddComponent<BoxCollider>();
            hull.transform.SetParent(root.transform, false);
            hull.center = new Vector3(0f, b.size.y * 0.2f, 0f);
            hull.size = new Vector3(b.size.x * 0.9f, b.size.y * 0.4f, b.size.z * 0.9f);
            Transform P(string n, Vector3 p)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                return tr;
            }
            // Two guests side by side on the floor of the cushion, backs to its backrest, facing the way it's towed.
            Transform r0 = P("RiderSpot0", new Vector3(-0.42f, CushionSitHeight, -0.25f));
            Transform r1 = P("RiderSpot1", new Vector3(0.42f, CushionSitHeight, -0.25f));
            Transform nose = P("Nose", new Vector3(0f, 0.45f, b.size.z * 0.5f));
            Transform seat = P("Seat", new Vector3(0f, CushionSitHeight + 0.5f, 0f));
            Transform thrust = P("Thrust", new Vector3(0f, 0f, -b.size.z * 0.5f));
            Vehicle vehicle = FinishVehicle(root, "Cushion", "", seat, seat, seat, thrust, hull, 0.25f, thrustForce: 0f, maxSpeed: 1f, turnRate: 0f);
            SetBool(vehicle, "_straddle", true);
            Object engine = new SerializedObject(vehicle).FindProperty("_engineAudio").objectReferenceValue;
            SetRef(vehicle, "_engineAudio", null);
            if (engine != null) Object.DestroyImmediate(engine);
            vehicle.gameObject.name = "Cushion";

            var line = root.AddComponent<LineRenderer>();
            line.positionCount = 9;
            line.widthMultiplier = 0.035f;
            line.sharedMaterial = GetMaterial("TowRope", new Color(0.85f, 0.78f, 0.55f));
            line.useWorldSpace = true;
            line.enabled = false;
            var ride = root.AddComponent<CushionRide>();
            SetRef(ride, "_vehicle", vehicle);
            SetRef(ride, "_nose", nose);
            SetRef(ride, "_rope", line);
            SetRefs(ride, "_riderSpots", r0, r1);
            SetRefs(ride, "_buoys", buoys.Cast<Object>().ToArray());
            SetRef(ride, "_home", home);
            GameObject npc = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            if (npc != null) SetRef(ride, "_npcPrefab", npc.GetComponent<StoryNpc>());
        }

        /// <summary>The cushion model turned so its backrest is at the back (-z) and its tow ring at the front.</summary>
        private const float CushionModelYaw = 180f;
        /// <summary>Where a seated guest's root goes, over the cushion's bottom.</summary>
        private const float CushionSitHeight = 0.25f;

        // ------------------------------------------------------------------ the gala (task 7): shown only on the gala night

        private static void BuildGalaDecor(Transform parent)
        {
            var root = new GameObject("GalaDecor").transform;
            root.SetParent(parent, false);
            var night = new List<GameObject>();
            var show = new GameObject("GalaOnly").transform; // everything, switched as one
            show.SetParent(root, false);
            night.Add(show.gameObject);

            // The red carpet from the hotel's door out over the forecourt, gold edged.
            Material red = GetMaterial("GalaCarpet", new Color(0.55f, 0.03f, 0.06f), smoothness: 0.05f);
            Material gold = GetMaterial("GalaGold", new Color(1f, 0.78f, 0.35f), 0.9f, 0.7f);
            for (int i = 0; i < 12; i++)
            {
                // In metre-long pieces, each on the ground under it (the forecourt steps down to the sand).
                float z = HotelDoorFront.z + 0.5f + i;
                float y = SurfaceUnder(new Vector3(HotelDoorFront.x, 0f, z)) + 0.012f;
                Primitive(PrimitiveType.Cube, "Carpet", show, new Vector3(HotelDoorFront.x, y, z), new Vector3(CarpetWidth, 0.02f, 1.02f), red, keepCollider: false);
                foreach (float side in new[] { -1f, 1f })
                    Primitive(PrimitiveType.Cube, "CarpetEdge", show, new Vector3(HotelDoorFront.x + side * (CarpetWidth * 0.5f + 0.04f), y, z), new Vector3(0.08f, 0.022f, 1.02f), gold, keepCollider: false);
            }
            // The velvet ropes along both sides (the Tripo stanchion sets).
            GameObject ropes = TripoIsland2("velvet_rope_stanchions");
            for (int i = 0; i < 3 && ropes != null; i++)
                foreach (float side in new[] { -1f, 1f })
                {
                    float z = HotelDoorFront.z + 2.5f + i * 3.4f;
                    var at = new Vector3(HotelDoorFront.x + side * (CarpetWidth * 0.5f + 0.55f), 0f, z);
                    at.y = SurfaceUnder(at);
                    GameObject set = Object.Instantiate(ropes, show, false);
                    set.name = "VelvetRopes";
                    set.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
                    set.transform.localScale = Vector3.one * 1.1f;
                    Bounds sb = RendererBounds(set.transform);
                    set.transform.position += new Vector3(at.x - sb.center.x, at.y - sb.min.y, at.z - sb.center.z);
                }
            // String lights over the carpet: gold poles at its corners, warm bulbs on three zigzag strings.
            float zEnd = HotelDoorFront.z + CarpetLength;
            var poles = new List<Vector3>();
            foreach (float z in new[] { HotelDoorFront.z + 1.2f, zEnd - 0.4f })
                foreach (float side in new[] { -1f, 1f })
                {
                    var at = new Vector3(HotelDoorFront.x + side * (CarpetWidth * 0.5f + 1.3f), 0f, z);
                    at.y = SurfaceUnder(at);
                    Primitive(PrimitiveType.Cylinder, "LightPole", show, at + Vector3.up * 1.6f, new Vector3(0.08f, 1.6f, 0.08f), gold, keepCollider: false);
                    poles.Add(at + Vector3.up * 3.15f);
                }
            Material bulb = GetMaterial("GalaBulb", new Color(1f, 0.85f, 0.55f), emission: new Color(3.2f, 2.1f, 0.9f));
            StringLights(show, "StringA", poles[0], poles[3], bulb);
            StringLights(show, "StringB", poles[1], poles[2], bulb);
            StringLights(show, "StringC", poles[2], poles[3], bulb);
            // Two warm lights under the strings (no shadows: cheap).
            foreach (float t in new[] { 0.3f, 0.75f })
            {
                var lightGo = new GameObject("GalaLight");
                lightGo.transform.SetParent(show, false);
                lightGo.transform.position = new Vector3(HotelDoorFront.x, SurfaceUnder(new Vector3(HotelDoorFront.x, 0f, HotelDoorFront.z + CarpetLength * t)) + 3f,
                    HotelDoorFront.z + CarpetLength * t);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 9f;
                l.intensity = 2.2f;
                l.color = new Color(1f, 0.8f, 0.55f);
                l.shadows = LightShadows.None;
            }

            // In the casino: the champagne tower on a gold-clothed round table by the door, two chandeliers over the floor.
            float floor = SurfaceUnder(CasinoCentre, 3.4f);
            var table = new Vector3(5.6f, floor, -258.6f);
            Primitive(PrimitiveType.Cylinder, "ChampagneTable", show, table + Vector3.up * 0.4f, new Vector3(1.1f, 0.4f, 1.1f), GetMaterial("GalaCloth", new Color(0.95f, 0.92f, 0.85f)));
            if (TripoIsland2("champagne_tower") is { } tower)
            {
                GameObject t = Object.Instantiate(tower, show, false);
                t.name = "ChampagneTower";
                t.transform.position = table;
                Bounds tb = RendererBounds(t.transform);
                t.transform.position += new Vector3(table.x - tb.center.x, table.y + 0.8f - tb.min.y, table.z - tb.center.z);
            }
            float ceiling = 1.5f + 3.45f;
            foreach (Vector3 c in new[] { new Vector3(-0.2f, 0f, -250.5f), new Vector3(4.2f, 0f, -256.5f) })
                if (TripoIsland2("crystal_chandelier") is { } chandelier)
                {
                    GameObject ch = Object.Instantiate(chandelier, show, false);
                    ch.name = "Chandelier";
                    ch.transform.localScale = Vector3.one * 0.85f;
                    ch.transform.position = c;
                    Bounds cb = RendererBounds(ch.transform);
                    ch.transform.position += new Vector3(c.x - cb.center.x, ceiling - cb.max.y, c.z - cb.center.z);
                }

            var glow = root.gameObject.AddComponent<NightGlow>();
            SetRefs(glow, "_night", night.Cast<Object>().ToArray());
            SetBool(glow, "_galaOnly", true);
            show.gameObject.SetActive(false);
        }

        /// <summary>A sagging string of bulbs between two points: one mesh for the bulbs, a thin wire.</summary>
        private static void StringLights(Transform parent, string name, Vector3 a, Vector3 b, Material bulb)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            int count = Mathf.Max(4, Mathf.RoundToInt(Vector3.Distance(a, b) / 0.55f));
            Vector3 P(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.45f);
            Mesh sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var parts = new List<CombineInstance>();
            for (int i = 1; i < count; i++)
                parts.Add(new CombineInstance { mesh = sphere, transform = Matrix4x4.TRS(P(i / (float)count) + Vector3.down * 0.06f, Quaternion.identity, Vector3.one * 0.09f) });
            var mesh = new Mesh { name = name + "_Bulbs" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject("Bulbs");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = bulb;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var wire = root.gameObject.AddComponent<LineRenderer>();
            wire.positionCount = 12;
            for (int i = 0; i < 12; i++) wire.SetPosition(i, P(i / 11f));
            wire.widthMultiplier = 0.015f;
            wire.useWorldSpace = true;
            wire.sharedMaterial = GetMaterial("GalaWire", new Color(0.08f, 0.07f, 0.06f));
            wire.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ palms, flowers, hedges (task 9)

        private static void BuildIsland2Greenery(Transform parent)
        {
            var root = new GameObject("Greenery").transform;
            root.SetParent(parent, false);
            var rng = new System.Random(2026);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int placed = 0, skipped = 0;
            void Palm(float x, float z)
            {
                var feet = new Vector3(x + R(-1f, 1f), 0f, z + R(-1f, 1f));
                feet.y = GroundUnder(feet);
                // On dry sand only, never on the road, a path or against a building.
                if (feet.y < 0.6f || NearCartRoad(new Vector2(feet.x, feet.z), 3.2f) || Blocked(feet, 1.2f)) { skipped++; return; }
                if (MeshyArt.Place("palm_tall", root, R(7f, 9.5f), feet, R(0f, 360f)) != null) placed++;
            }
            // The beach edge, away from the guests' towels and the docks.
            for (float x = -92f; x <= -52f; x += 10f) Palm(x, WaterlineZ(x) - 12f);
            for (float x = -36f; x <= -28f; x += 8f) Palm(x, WaterlineZ(x) - 13f);
            for (float x = 56f; x <= 72f; x += 8f) Palm(x, WaterlineZ(x) - 12f);
            // Along the cart road, on its outer side.
            for (float z = -245f; z >= -300f; z -= 14f) Palm(-75.5f, z);
            for (float x = -60f; x <= 55f; x += 16f) Palm(x, -311.5f);
            for (float z = -276f; z >= -300f; z -= 12f) Palm(67.5f, z);

            // Flower beds either side of the hotel's front, clear of the red carpet and the door.
            foreach (float x in new[] { 10.5f, 29.5f })
            {
                var at = new Vector3(x, 0f, -233.8f);
                at.y = SurfaceUnder(at);
                if (!Blocked(at, 0.9f)) BeachProp(root, "FlowerBed", "resort_planter", at, 0f);
            }
            // A low hedge round the pool's east and south sides (gaps to walk through).
            Material hedge = GetMaterial("Hedge", new Color(0.16f, 0.36f, 0.14f), smoothness: 0.08f);
            void Hedge(Vector3 a, Vector3 b)
            {
                Vector3 c = (a + b) * 0.5f;
                c.y = GroundUnder(c);
                float len = Vector3.Distance(a, b);
                GameObject h = Primitive(PrimitiveType.Cube, "Hedge", root, c + Vector3.up * 0.45f, new Vector3(0.75f, 0.9f, len), hedge);
                h.transform.rotation = Quaternion.LookRotation(b - a);
            }
            Hedge(new Vector3(79f, 0f, -240f), new Vector3(79f, 0f, -248f));
            Hedge(new Vector3(79f, 0f, -253f), new Vector3(79f, 0f, -264f));
            Hedge(new Vector3(52f, 0f, -266f), new Vector3(60f, 0f, -266f));
            Hedge(new Vector3(66f, 0f, -266f), new Vector3(78f, 0f, -266f));
            Debug.Log($"[Island2Finish] greenery: {placed} palms ({skipped} spots skipped)");
        }

        // ------------------------------------------------------------------ lamp posts along the road (task 9)

        private static void BuildIsland2Lamps(Transform parent)
        {
            var root = new GameObject("LampPosts").transform;
            root.SetParent(parent, false);
            Material bronze = GetMaterial("LampBronze", new Color(0.18f, 0.15f, 0.12f), 0.6f, 0.4f);
            Material dayBulb = GetMaterial("LampBulbDay", new Color(0.92f, 0.9f, 0.84f), smoothness: 0.6f);
            Material nightBulb = GetMaterial("LampBulbNight", new Color(1f, 0.85f, 0.6f), emission: new Color(4f, 2.8f, 1.4f));
            var day = new List<Object>();
            var night = new List<Object>();
            int n = 0;
            // Every ~18 m on the outer side of the road (inside for the stretch by the hotel).
            for (int i = 0; i + 1 < CartRoad.Length; i++)
            {
                Vector2 a = CartRoad[i], b = CartRoad[i + 1];
                Vector2 dir = (b - a).normalized, side = new Vector2(dir.y, -dir.x); // the road's right
                float len = (b - a).magnitude;
                for (float s = 4f; s < len - 2f; s += 18f)
                {
                    Vector2 p = a + dir * s + side * (CartRoadWidth * 0.5f + 0.8f);
                    var feet = new Vector3(p.x, 0f, p.y);
                    feet.y = GroundUnder(feet);
                    if (Blocked(feet, 0.5f)) continue;
                    var lamp = new GameObject("LampPost" + n++).transform;
                    lamp.SetParent(root, false);
                    lamp.position = feet;
                    Primitive(PrimitiveType.Cylinder, "Pole", lamp, new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), bronze);
                    Primitive(PrimitiveType.Cube, "Cap", lamp, new Vector3(0f, 3.55f, 0f), new Vector3(0.42f, 0.08f, 0.42f), bronze, keepCollider: false);
                    day.Add(Primitive(PrimitiveType.Sphere, "Globe", lamp, new Vector3(0f, 3.32f, 0f), Vector3.one * 0.36f, dayBulb, keepCollider: false));
                    GameObject lit = Primitive(PrimitiveType.Sphere, "GlobeLit", lamp, new Vector3(0f, 3.32f, 0f), Vector3.one * 0.36f, nightBulb, keepCollider: false);
                    lit.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lit.SetActive(false);
                    night.Add(lit);
                }
            }
            // Pool-side bollards (the same glow, low down) at the deck's corners.
            foreach (Vector2 p in new[] { new Vector2(47f, -239f), new Vector2(77f, -239f), new Vector2(47f, -263f), new Vector2(77f, -263f) })
            {
                var feet = new Vector3(p.x, 0f, p.y);
                feet.y = SurfaceUnder(feet);
                var post = new GameObject("PoolBollard").transform;
                post.SetParent(root, false);
                post.position = feet;
                Primitive(PrimitiveType.Cylinder, "Post", post, new Vector3(0f, 0.4f, 0f), new Vector3(0.18f, 0.4f, 0.18f), bronze);
                day.Add(Primitive(PrimitiveType.Cylinder, "Lens", post, new Vector3(0f, 0.86f, 0f), new Vector3(0.2f, 0.06f, 0.2f), dayBulb, keepCollider: false));
                GameObject lit = Primitive(PrimitiveType.Cylinder, "LensLit", post, new Vector3(0f, 0.86f, 0f), new Vector3(0.2f, 0.06f, 0.2f), nightBulb, keepCollider: false);
                lit.SetActive(false);
                night.Add(lit);
            }
            var glow = root.gameObject.AddComponent<NightGlow>();
            SetRefs(glow, "_night", night.ToArray());
            SetRefs(glow, "_day", day.ToArray());
            Debug.Log($"[Island2Finish] lamps: {n} lamp posts, 4 pool bollards");
        }

        // ------------------------------------------------------------------ signposts (task 9)

        private static void BuildIsland2Signs(Transform parent)
        {
            var root = new GameObject("Signposts").transform;
            root.SetParent(parent, false);
            Signpost(root, "SignCasino", new Vector3(5f, 0f, -232.5f), 180f, "CASINO", "IN THE HOTEL, WEST WING");
            Signpost(root, "SignBar", new Vector3(-38f, 0f, -238.5f), 180f, "CORAL BAR", null);
            Signpost(root, "SignGuns", new Vector3(-13.5f, 0f, -239.5f), 180f, "CORAL ARMS", "GUNS");
            Signpost(root, "SignPool", new Vector3(46.5f, 0f, -236f), 180f, "POOL", null);
            Signpost(root, "SignMarina", new Vector3(MarinaX - 3.5f, 0f, WaterlineZ(MarinaX) - 9f), 180f, "MARINA", null);
        }

        /// <summary>A small teal board with gold letters on a post (readable from both sides), feet on the ground.</summary>
        private static void Signpost(Transform parent, string name, Vector3 feet, float yaw, string title, string subtitle)
        {
            Material post = GetMaterial("SignPost", new Color(0.93f, 0.9f, 0.82f), smoothness: 0.3f);
            Material teal = GetMaterial("SignTeal", new Color(0.09f, 0.42f, 0.46f), smoothness: 0.35f);
            Color gold = new(1f, 0.82f, 0.4f);
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            feet.y = SurfaceUnder(feet);
            root.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            Primitive(PrimitiveType.Cylinder, "Post", root, new Vector3(0f, 1.05f, 0f), new Vector3(0.1f, 1.05f, 0.1f), post);
            Primitive(PrimitiveType.Cube, "Board", root, new Vector3(0f, 1.85f, 0f), new Vector3(1.5f, 0.55f, 0.06f), teal, keepCollider: false);
            foreach (float side in new[] { 1f, -1f })
            {
                TextMesh t = WorldText(root, "Title", new Vector3(0f, subtitle != null ? 1.92f : 1.85f, 0.035f * side), title, 80, 0.028f, gold);
                t.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
                if (subtitle == null) continue;
                TextMesh s = WorldText(root, "Subtitle", new Vector3(0f, 1.71f, 0.035f * side), subtitle, 60, 0.018f, gold);
                s.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ lounger seats (task 10)

        /// <summary>
        /// The pool's lounge sets (two loungers and a table each, GameSceneBuilder.TeamB) have no collision: an invisible
        /// box on each lounger's flat part, so guests sitting on them rest on the cushion (StoryDirector.Gala).
        /// Their backrests are at the set's -z (the model's +y as made in Blender).
        /// </summary>
        private static void BuildLoungerSeats(Transform parent)
        {
            var root = new GameObject("LoungerSeats").transform;
            root.SetParent(parent, false);
            Transform pool = GameObject.Find("Environment/Island2Pool")?.transform;
            if (pool == null) return;
            int n = 0;
            foreach (Transform set in pool)
            {
                if (!set.name.StartsWith("Lounge")) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    var seat = new GameObject("LoungerSeat").transform;
                    seat.SetParent(root, false);
                    seat.SetPositionAndRotation(set.TransformPoint(new Vector3(side * 1.2f, LoungerCushionTop - 0.05f, 0.25f)), set.rotation);
                    seat.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.7f, 0.1f, 1.7f);
                    n++;
                }
            }
            Debug.Log($"[Island2Finish] {n} lounger seats");
        }

        /// <summary>The loungers' cushion top over the set's root (the model stands 0.1 m up, its cushion top at 0.67).</summary>
        private const float LoungerCushionTop = 0.66f;

        // ------------------------------------------------------------------ the fountain on the forecourt (task 9)

        /// <summary>A round stone fountain in front of the casino wing: a basin with water, a column, two bowls.</summary>
        private static void BuildForecourtFountain(Transform parent)
        {
            var root = new GameObject("Fountain").transform;
            root.SetParent(parent, false);
            var at = new Vector3(2.5f, 0f, -238.5f);
            at.y = SurfaceUnder(at);
            if (Blocked(at + Vector3.up * 0.2f, 1.6f)) { Debug.LogWarning("[Island2Finish] no room for the fountain"); return; }
            root.position = at;
            Material stone = GetMaterial("FountainStone", new Color(0.93f, 0.88f, 0.78f), smoothness: 0.3f);
            Material gold = GetMaterial("GalaGold", new Color(1f, 0.78f, 0.35f), 0.9f, 0.7f);
            Primitive(PrimitiveType.Cylinder, "Basin", root, new Vector3(0f, 0.28f, 0f), new Vector3(3.6f, 0.28f, 3.6f), stone);
            Primitive(PrimitiveType.Cylinder, "BasinRim", root, new Vector3(0f, 0.58f, 0f), new Vector3(3.8f, 0.04f, 3.8f), stone, keepCollider: false);
            GameObject water = Primitive(PrimitiveType.Cylinder, "Water", root, new Vector3(0f, 0.57f, 0f), new Vector3(3.3f, 0.02f, 3.3f), PoolWaterMaterial(), keepCollider: false);
            water.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Primitive(PrimitiveType.Cylinder, "Column", root, new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 0.55f, 0.35f), stone, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "BowlLow", root, new Vector3(0f, 1.62f, 0f), new Vector3(1.5f, 0.07f, 1.5f), stone, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "Column2", root, new Vector3(0f, 1.95f, 0f), new Vector3(0.18f, 0.3f, 0.18f), stone, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "BowlHigh", root, new Vector3(0f, 2.28f, 0f), new Vector3(0.75f, 0.05f, 0.75f), stone, keepCollider: false);
            Primitive(PrimitiveType.Sphere, "Finial", root, new Vector3(0f, 2.48f, 0f), Vector3.one * 0.22f, gold, keepCollider: false);
            GameObject lowWater = Primitive(PrimitiveType.Cylinder, "BowlWater", root, new Vector3(0f, 1.695f, 0f), new Vector3(1.35f, 0.01f, 1.35f), PoolWaterMaterial(), keepCollider: false);
            lowWater.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ the arch over the dock (task 9)

        private static void BuildDockArch(Transform parent)
        {
            var root = new GameObject("DockArch").transform;
            root.SetParent(parent, false);
            Transform dock = GameObject.Find("Environment/HotelDock")?.transform;
            if (dock == null) return;
            // Over the dock's land end, where it meets the sand.
            float z = WaterlineZ(dock.position.x) - 2.5f;
            Material white = GetMaterial("ArchWhite", new Color(0.95f, 0.93f, 0.87f), smoothness: 0.3f);
            Material teal = GetMaterial("SignTeal", new Color(0.09f, 0.42f, 0.46f), smoothness: 0.35f);
            foreach (float side in new[] { -1f, 1f })
            {
                var feet = new Vector3(dock.position.x + side * 2.1f, 0f, z);
                feet.y = GroundUnder(feet);
                Primitive(PrimitiveType.Cube, "Pillar", root, feet + Vector3.up * 1.9f, new Vector3(0.45f, 3.8f + 0.6f, 0.45f), white);
            }
            float top = GroundUnder(new Vector3(dock.position.x, 0f, z)) + 3.9f;
            Primitive(PrimitiveType.Cube, "Beam", root, new Vector3(dock.position.x, top, z), new Vector3(5.1f, 0.7f, 0.5f), teal, keepCollider: false);
            foreach (float side in new[] { 1f, -1f })
            {
                TextMesh t = WorldText(root, "Name", new Vector3(dock.position.x, top, z + 0.27f * side), "GRAND CORAL", 90, 0.045f, new Color(1f, 0.82f, 0.4f));
                t.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
            }
        }
    }
}
