using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The hotel bar's tables (the team, 2026-10-12): the bar model has four little tables, each with two armchairs
    /// facing each other across it. Each armchair is a seat you order from (<see cref="BarSeat"/>), so two lifeguards
    /// can sit across from each other and eat; the order is put down on the table in front of you. Also khinkali on
    /// the menu (model: ArtSource/Tools/model_khinkali.py).
    /// "Hotel bar: tables and khinkali" updates only these in the saved scene (no hotel rebuild: its look is fragile).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // Armchairs measured from the bar model (side render of the LOD2 GLB, bar space): x = +/-10.36, two per table.
        // Each faces its table: (chair z, table z).
        private static readonly (float chair, float table)[] BarTableChairs = { (2.47f, 4.02f), (5.58f, 4.02f), (-4.55f, -3.12f), (-1.7f, -3.12f) };
        /// <summary>A bar stool's seat point: on the cushion, a little back from the counter (on the middle the knees went into it).</summary>
        private static readonly Vector3 StoolSeat = new(0f, StoolCushion + 0.13f, -0.16f);
        private const float BarTableX = 10.36f, ArmchairSeat = 0.9f, BarTableTop = 0.74f;

        /// <summary>What the hotel bar sells (item, label, price).</summary>
        private static readonly (string item, string label, int price)[] HotelBarMenu =
            { ("Fries", "Fries", 3), ("Cola", "Cola", 2), ("Beer", "Beer", 4), ("Cocktail", "Cocktail", 8), ("Khinkali", "Khinkali", 6) };

        [MenuItem("PLEASE DON'T DROWN/Part A: scene additions (bar tables, khinkali, blackjack)")]
        public static void BuildBarDining()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

            // The khinkali item (and the catalog / network prefab list knowing it).
            Item khinkali = BuildKhinkali();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            var so = new SerializedObject(catalog);
            SerializedProperty list = Require(so, "_items");
            bool listed = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == khinkali) listed = true;
            if (!listed)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = khinkali;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
            }
            AssetDatabase.SaveAssets();
            RefreshFishNetPrefabs();

            Barista barista = Object.FindObjectsByType<Barista>(FindObjectsSortMode.None).FirstOrDefault(b => b.HasMenu);
            if (barista == null) throw new InvalidOperationException("No hotel bar barista with a menu in the scene.");
            Transform bar = barista.transform.parent, hotel = bar.parent;
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, $"Logs/Game-before-bar-tables-{DateTime.Now:yyyyMMdd-HHmmss}.unity", true);

            List<Object> seats = AddBarTables(bar, hotel, barista);
            SetHotelBarMenu(barista);
            // The stools' seats, back from the counter (stools built before this keep their old spot otherwise).
            Transform stoolRoot = hotel.Find("HotelBarStools");
            if (stoolRoot != null)
                foreach (Transform stool in stoolRoot)
                    if (stool.Find("Seat") is { } stoolSeat) stoolSeat.localPosition = StoolSeat;
            AddBlackjackGame();
            AddGolfCart(bar, hotel);
            AssignSceneIds(scene); // the new seats and the card table are networked scene objects
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BarDining] PASS: {seats.Count} table seats, menu of {HotelBarMenu.Length} (khinkali in), blackjack table playable");
        }

        /// <summary>Batch: the same.</summary>
        public static void BuildBarDiningBatch() => BuildBarDining();

        /// <summary>The casino's blackjack table plays (Fun.BlackjackTable): looking at its collision box opens the cards.</summary>
        private static void AddBlackjackGame()
        {
            Collider table = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == "BlackjackCollider");
            if (table == null) throw new InvalidOperationException("No BlackjackCollider in the scene (the hotel casino).");
            Transform old = table.transform.parent.Find("BlackjackGame");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("BlackjackGame");
            go.transform.SetParent(table.transform.parent, false);
            go.transform.SetPositionAndRotation(table.transform.position, table.transform.rotation);
            go.AddComponent<NetworkObject>();
            go.AddComponent<BlackjackTable>();
            ConfigureInteractable(go.AddComponent<Interactable>(), new[] { table }, new Renderer[0], 4f);
            Debug.Log($"[BarDining] blackjack table at {table.bounds.center:F1}, size {table.bounds.size:F1}");
        }

        /// <summary>
        /// The golf cart (A9): a land <see cref="Vehicle"/> by the hotel bar, the driver's bench seat and a passenger's
        /// beside it. A greybox body for now (the Tripo model is part C's): white chassis and roof, four wheels.
        /// </summary>
        private static void AddGolfCart(Transform bar, Transform hotel)
        {
            Transform old = hotel.Find("GolfCart");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            Vector3? spot = null;
            float spotYaw = 0f;
            foreach (Vector2 p in new[] { new Vector2(0f, -12f), new Vector2(6f, -12f), new Vector2(-6f, -12f), new Vector2(15f, -3f), new Vector2(-15f, -3f), new Vector2(0f, 12f) })
            {
                Vector3 at = bar.TransformPoint(new Vector3(p.x, 0f, p.y));
                if (!Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out RaycastHit ground, 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (ground.normal.y < 0.95f || ground.point.y < 0.6f) continue; // a slope, or at the water
                Vector3 centre = ground.point + Vector3.up * 0.9f;
                if (Physics.CheckBox(centre, new Vector3(1.0f, 0.6f, 1.6f), bar.rotation, ~0, QueryTriggerInteraction.Ignore)) continue; // something there
                spot = ground.point + Vector3.up * 0.05f;
                spotYaw = bar.eulerAngles.y + 90f;
                break;
            }
            if (spot == null) throw new InvalidOperationException("No flat, free sand by the hotel bar for the golf cart.");

            Material white = GetMaterial("CartWhite", new Color(0.95f, 0.95f, 0.92f));
            Material cream = GetMaterial("CartSeat", new Color(0.85f, 0.75f, 0.55f));
            Material black = GetMaterial("CartTyre", new Color(0.06f, 0.06f, 0.07f));
            Material green = GetMaterial("CartGreen", new Color(0.15f, 0.45f, 0.3f));
            var root = new GameObject("GolfCart");
            root.transform.SetParent(hotel, false);
            root.transform.SetPositionAndRotation(spot.Value, Quaternion.Euler(0f, spotYaw, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 300f;
            body.linearDamping = 0.2f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Transform r = root.transform;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", r, new Vector3(0f, 0.55f, 0f), new Vector3(1.3f, 0.5f, 2.4f), white);
            // It slides on its hull (the wheels are drawn): no friction, or the sand holds it like a brick. Grip, steering
            // and rolling to a stop are Vehicle.DriveOnLand's.
            hull.GetComponent<Collider>().sharedMaterial = GetPhysicsMaterial("CartSlide", 0f, 0f, PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);
            Primitive(PrimitiveType.Cube, "Bench", r, new Vector3(0f, 0.86f, -0.2f), new Vector3(1.15f, 0.12f, 0.55f), cream, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Backrest", r, new Vector3(0f, 1.15f, -0.48f), new Vector3(1.15f, 0.5f, 0.1f), cream, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Dash", r, new Vector3(0f, 0.95f, 0.7f), new Vector3(1.25f, 0.35f, 0.25f), green, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Roof", r, new Vector3(0f, 2.0f, -0.05f), new Vector3(1.4f, 0.06f, 2.0f), white);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -0.55f, 0.8f })
                    Primitive(PrimitiveType.Cube, "Post", r, new Vector3(x, 1.45f, z), new Vector3(0.05f, 1.1f, 0.05f), white, keepCollider: false);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -0.8f, 0.8f })
                    Primitive(PrimitiveType.Cylinder, "Wheel", r, new Vector3(x, 0.25f, z), new Vector3(0.5f, 0.1f, 0.5f), black, keepCollider: false)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Primitive(PrimitiveType.Cylinder, "SteeringWheel", r, new Vector3(-0.28f, 1.18f, 0.42f), new Vector3(0.36f, 0.02f, 0.36f), black, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            Transform P(string n, Vector3 p, Quaternion q)
            {
                var t = new GameObject(n).transform;
                t.SetParent(r, false);
                t.localPosition = p;
                t.localRotation = q;
                return t;
            }
            Transform seat = P("Seat", new Vector3(-0.28f, 0.92f, -0.22f), Quaternion.identity);
            Transform passenger = P("PassengerSeat", new Vector3(0.28f, 0.92f, -0.22f), Quaternion.identity);
            Transform gripL = P("GripLeft", new Vector3(-0.42f, 1.24f, 0.38f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform gripR = P("GripRight", new Vector3(-0.14f, 1.24f, 0.38f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform thrust = P("Thrust", new Vector3(0f, 0.25f, -1.0f), Quaternion.identity);
            Vehicle cart = FinishVehicle(root, "golf cart", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.3f,
                thrustForce: 6f, maxSpeed: 8f, turnRate: 1.4f);
            SetRefs(cart, "_backSeats", passenger);
            SetBool(cart, "_land", true);
            SetField(cart, "_sideGrip", p => p.floatValue = 9f);
            SetField(cart, "_reverseThrust", p => p.floatValue = 3f);
            Debug.Log($"[BarDining] golf cart at {spot.Value:F1}");
        }

        private static Item BuildKhinkali()
        {
            PhysicsMaterial paper = GetPhysicsMaterial("Paper", 0.4f, 0.1f, PhysicsMaterialCombine.Average);
            Material white = GetMaterial("PlateWhite", new Color(0.93f, 0.93f, 0.9f));
            return BuildItem("Khinkali", "Khinkali", 0.3f, new Vector3(0.22f, -0.3f, 0.5f), Vector3.zero, 0.8f, paper, root =>
            {
                Primitive(PrimitiveType.Cylinder, "Plate", root, new Vector3(0f, 0.02f, 0f), new Vector3(0.2f, 0.02f, 0.2f), white);
                DressProp(root, "khinkali");
            }, linearDamping: 0.2f, angularDamping: 0.5f, density: 0.5f, waterDrag: 1.4f, configure: go => BarFood(go, food: 0.45f, seconds: 2.6f));
        }

        /// <summary>The eight armchairs at the four tables, as seats you order from (replacing their plain collision).</summary>
        private static List<Object> AddBarTables(Transform bar, Transform hotel, Barista barista)
        {
            Transform old = hotel.Find("HotelBarTableSeats");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            // The armchairs' plain boxes: the seats bring their own (a box in front would catch the look first).
            foreach (Transform t in bar.Cast<Transform>().Where(t => t.name == "ClubChair").ToArray())
                Object.DestroyImmediate(t.gameObject);

            var root = new GameObject("HotelBarTableSeats").transform;
            root.SetParent(hotel, false);
            var seats = new List<Object>();
            int n = 0;
            foreach (float side in new[] { -1f, 1f })
                foreach (var (chair, table) in BarTableChairs)
                {
                    float yaw = table > chair ? 0f : 180f; // facing its table
                    Vector3 local = new(side * BarTableX, 0f, chair);
                    float towardSeat = Mathf.Sign(chair - table);
                    Vector3 serve = new(side * BarTableX, BarTableTop + 0.12f, table + towardSeat * 0.24f);
                    seats.Add(BuildTableSeat(root, bar, n++, local, yaw, serve, barista));
                }

            // The barista serves the stools and the tables alike.
            var stools = new SerializedObject(barista);
            SerializedProperty list = Require(stools, "_seats");
            var all = new List<Object>();
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is BarSeat s && s != null && s.transform.parent != null &&
                    s.transform.parent.name == "HotelBarStools") all.Add(s);
            all.AddRange(seats);
            SetRefs(barista, "_seats", all.ToArray());
            return seats;
        }

        /// <summary>One armchair at a table: a chair that never moves, the seat in its cushion, orders put down on the table.</summary>
        private static BarSeat BuildTableSeat(Transform parent, Transform bar, int index, Vector3 local, float yaw, Vector3 serveLocal, Barista barista)
        {
            var go = new GameObject("TableSeat" + index);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(bar.TransformPoint(local), bar.rotation * Quaternion.Euler(0f, yaw, 0f));
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            go.AddComponent<NetworkObject>();
            go.AddComponent<ItemSync>();

            var hull = new GameObject("Hull");
            hull.transform.SetParent(go.transform, false);
            var box = hull.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.65f, 0f);
            box.size = new Vector3(1.5f, 0.78f, 1.35f);
            var seat = new GameObject("Seat").transform;
            seat.SetParent(go.transform, false);
            seat.localPosition = new Vector3(0f, ArmchairSeat, 0.15f); // on the cushion (0.82 high, measured), near its front edge (0.38 ahead) so the knees bend over it: further back the short legs went into the cushion
            var serve = new GameObject("ServePoint").transform;
            serve.SetParent(bar, false);
            serve.localPosition = serveLocal;
            serve.SetParent(go.transform, true);

            var vehicle = go.AddComponent<Vehicle>();
            var so = new SerializedObject(vehicle);
            Require(so, "_displayName").stringValue = "armchair";
            Require(so, "_seat").objectReferenceValue = seat;
            Require(so, "_chair").boolValue = true;
            Require(so, "_thrust").floatValue = 0f;
            Require(so, "_reverseThrust").floatValue = 0f;
            Require(so, "_maxSpeed").floatValue = 1f;
            Require(so, "_turnRate").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(go.AddComponent<Interactable>(), new Collider[] { box }, new Renderer[0], 2.5f);

            var barSeat = go.AddComponent<BarSeat>();
            SetRef(barSeat, "_barista", barista);
            SetRef(barSeat, "_servePoint", serve);
            return barSeat;
        }

        private static void SetHotelBarMenu(Barista bar) => SetField(bar, "_menu", p =>
        {
            p.arraySize = HotelBarMenu.Length;
            for (int i = 0; i < HotelBarMenu.Length; i++)
            {
                SerializedProperty e = p.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Item").stringValue = HotelBarMenu[i].item;
                e.FindPropertyRelative("Label").stringValue = HotelBarMenu[i].label;
                e.FindPropertyRelative("Price").intValue = HotelBarMenu[i].price;
            }
        });
    }
}
