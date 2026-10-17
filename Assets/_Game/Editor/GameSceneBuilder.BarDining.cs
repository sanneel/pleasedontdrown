using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
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
        // The lounge's armchairs, measured on the bar model (rays on LOD0, bar space): x = +/-10.2, in pairs facing each
        // other; each one's cushion front edge (z) and which way it faces. Each pair's low table (ours, Art/Props/
        // bar_table.glb: the model's own stood 5-14 cm from one of them) goes halfway between, 28 cm clear of both.
        private static readonly (float edge, float facing)[] BarArmchairs = { (2.877f, 1f), (4.0f, -1f), (-3.825f, 1f), (-2.68f, -1f) };
        // The bar stools (Art/Props/bar_stool.glb, ArtSource/Tools/model_bar_stool.py), made for the players' goofy lifeguard:
        // the cushion 1.07 m over the deck (his waist then about level with the counter top, 1.55), its footrest ring where
        // his feet land with the thighs level, each stool 0.55 m back from the counter top's edge so his knees clear it.
        private const float BarStoolTop = 1.07f;                              // cushion top over its feet (the model's)
        private const float BarStoolSeat = BarDeckTop + BarStoolTop;          // ...in the bar's frame: the seat point
        private const float BarStoolFromCounter = 0.50f;                      // the counter top's edge this far ahead (in reach of a seated arm)
        private const float BarTableX = 10.2f, ArmchairSeat = 0.83f, ArmchairSeatZ = 0.2f, ArmchairEdge = 0.38f;
        private const float BarTableTop = BarDeckTop + 0.484f, BarTableWidth = 0.97f, BarTableDepth = 0.58f; // (model_bar_table.py)

        /// <summary>What the hotel bar sells (item, label, price).</summary>
        private static readonly (string item, string label, int price)[] HotelBarMenu =
            { ("Fries", "Fries", 3), ("Cola", "Cola", 2), ("Beer", "Beer", 4), ("Cocktail", "Cocktail", 8), ("Khinkali", "Khinkali", 6) };

        [MenuItem("PLEASE DON'T DROWN/Part A: scene additions (bar tables, khinkali, blackjack)")]
        public static void BuildBarDining()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);

            // The khinkali item (and the catalog / network prefab list knowing it); every bar dish eaten at the table.
            Item khinkali = BuildKhinkali();
            AddTableMeals();
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

            // The stools (new ones, back from the counter) and their guests, then the tables' armchairs.
            SetRefs(barista, "_seats", BuildBarStools(bar, hotel, barista).ToArray());
            List<Object> seats = AddBarTables(bar, hotel, barista);
            SetHotelBarMenu(barista);
            AddBlackjackGame();
            AddGolfCart(bar, hotel);
            AssignSceneIds(scene); // the new seats and the card table are networked scene objects
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BarDining] PASS: {seats.Count} table seats, menu of {HotelBarMenu.Length} (khinkali in), blackjack table playable");
        }

        /// <summary>Batch: the same.</summary>
        public static void BuildBarDiningBatch() => BuildBarDining();

        /// <summary>
        /// The bar stools and the two guests on theirs. The bar model's own stools (squashed with it to 1.2 x 0.7 m ovals
        /// right against the counter: nobody could sit on them like a person) are cut out of it by
        /// ArtSource/Tools/remove_bar_stools.py; these are Art/Props/bar_stool.glb, made to fit the players' body.
        /// </summary>
        private static List<Object> BuildBarStools(Transform bar, Transform hotel, Barista barista)
        {
            foreach (string old in new[] { "HotelBarStools", "HotelBarGuests" })
            {
                Transform t = hotel.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var stools = new GameObject("HotelBarStools").transform;
            stools.SetParent(hotel, false);
            var seats = new List<Object>();
            for (int i = 0; i < HotelBarStools.Length; i++)
                seats.Add(BuildBarStool(stools, bar, i, HotelBarStools[i].at, HotelBarStools[i].yaw, barista));
            for (int i = 0; i < 4; i++) // where the bartender stands to serve the front row
                if (bar.Find("ServeSpot" + i) is { } spot) spot.localPosition = new Vector3(HotelBarStools[i].at.x, CounterTop + .1f, 4.85f);

            // Two guests chatting over their drinks, sat on their stools as a player sits (AvatarPose.SitStool).
            GameObject npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            if (npcPrefab == null) return seats;
            var guests = new GameObject("HotelBarGuests").transform;
            guests.SetParent(hotel, false);
            foreach (var g in HotelBarGuests)
            {
                var stool = new GameObject("GuestStool_" + g.name).transform;
                stool.SetParent(guests, false);
                stool.SetPositionAndRotation(bar.TransformPoint(g.at), bar.rotation * Quaternion.Euler(0f, g.yaw, 0f));
                BarStoolLooks(stool);
                var seatPoint = new GameObject("Seat").transform; // (where she sits: for the review renders)
                seatPoint.SetParent(stool, false);
                seatPoint.localPosition = new Vector3(0f, BarStoolSeat, 0f);
                Vector3 at = bar.TransformPoint(g.at + Vector3.up * (BarStoolSeat - 0.5f)); // (the seat 0.5 m over the root)
                StoryNpc npc = PlaceNpc(npcPrefab, guests, "BarGuest_" + g.name, at, bar.eulerAngles.y + g.yaw);
                var customer = npc.gameObject.AddComponent<BarCustomer>();
                SetField(customer, "_name", p => p.stringValue = g.name);
                SetField(customer, "_body", p => p.intValue = g.body);
            }
            return seats;
        }

        /// <summary>A stool's model on the deck under <paramref name="stool"/> (which stands at the bar's base), and its collision.</summary>
        private static BoxCollider BarStoolLooks(Transform stool)
        {
            if (PropModel("bar_stool", stool, new Vector3(0f, BarDeckTop, 0f)) == null)
                throw new InvalidOperationException("Bar stool model missing (Art/Props/bar_stool.glb: ArtSource/Tools/model_bar_stool.py)");
            var hull = new GameObject("Hull");
            hull.transform.SetParent(stool, false);
            var box = hull.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, BarDeckTop + BarStoolTop * 0.5f, 0f);
            box.size = new Vector3(0.42f, BarStoolTop, 0.42f);
            return box;
        }

        /// <summary>
        /// The casino's blackjack table plays (Fun.BlackjackTable), on the felt: looking at its collision box deals and
        /// plays; the cards (Resources/Cards, ArtSource/Tools/make_cards.py) are laid on the table. The table's dressed-up
        /// fake cards and bet chips go (real ones are dealt now).
        /// </summary>
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
            var game = go.AddComponent<BlackjackTable>();
            // The felt: the collider's box stands 0.75 m out over the arc from the dealer's straight edge, the felt top
            // 0.906 m over the floor (GameSceneBuilder.HotelInterior: the half-moon table at t, the box at t + (0.75, 0.5, 0)).
            var felt = new GameObject("Felt").transform;
            felt.SetParent(go.transform, false);
            felt.localPosition = new Vector3(-0.75f, 0.406f, 0f);
            SetRef(game, "_felt", felt);
            SetRef(game, "_textMaterial", WorldTextMaterial());
            CardMaterial();
            ConfigureInteractable(go.AddComponent<Interactable>(), new[] { table }, new Renderer[0], 4f);
            // The dressing's fake hands and bets on the felt.
            int gone = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.name is not ("Card" or "CardPip" or "DealerCardUp" or "DealerCardDown" or "BetChip")) continue;
                Vector3 d = t.position - felt.position;
                if (new Vector2(d.x, d.z).magnitude < 1.7f && Mathf.Abs(d.y) < 0.2f) { Object.DestroyImmediate(t.gameObject); gone++; }
            }
            // (Most of the dressing is baked into the casino's Combined_* meshes: cut the bits lying on the felt out of them.)
            foreach (MeshFilter combined in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!combined.name.StartsWith("Combined_") || combined.sharedMesh == null) continue;
                Mesh mesh = combined.sharedMesh;
                Vector3[] v = mesh.vertices;
                bool OnFelt(int i)
                {
                    Vector3 w = combined.transform.TransformPoint(v[i]) - felt.position;
                    return new Vector2(w.x, w.z).magnitude < 1.4f && w.y > -0.004f && w.y < 0.006f;
                }
                bool changed = false;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    var keep = new List<int>(tris.Length);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        if (OnFelt(tris[t]) && OnFelt(tris[t + 1]) && OnFelt(tris[t + 2])) { gone++; continue; }
                        keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
                    }
                    if (keep.Count == tris.Length) continue;
                    mesh.SetTriangles(keep, sub);
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[BarDining] blackjack table at {table.bounds.center:F1}, size {table.bounds.size:F1}; {gone} fake cards and chips taken off it");
        }

        /// <summary>The cards' material (Resources/Cards/Card.mat): the atlas, its round corners cut out.</summary>
        private static void CardMaterial()
        {
            const string atlas = "Assets/_Game/Resources/Cards/cards.png", path = "Assets/_Game/Resources/Cards/Card.mat";
            if (AssetImporter.GetAtPath(atlas) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas) ?? throw new InvalidOperationException($"Card atlas missing ({atlas}: ArtSource/Tools/make_cards.py)");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", texture);
            mat.mainTexture = texture;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.3f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The golf cart (A9): a land <see cref="Vehicle"/> by the hotel bar, the driver's bench seat and a passenger's
        /// beside it. A greybox body for now (the Tripo model is part C's): white chassis and roof, four wheels.
        /// </summary>
        /// <summary>
        /// How far ahead of the cart's middle the steering wheel is: within reach of the lifeguard's short cartoon arms
        /// from the seat (at 0.42 they leant 45 degrees and still fell 13 cm short; see PatchGolfCartReach).
        /// </summary>
        public const float CartWheelZ = 0.22f;

        private static void AddGolfCart(Transform bar, Transform hotel)
        {
            Transform old = hotel.Find("GolfCart");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            Vector3? spot = null;
            float spotYaw = 0f;
            // In a bay of the parking by the hotel at the end of Shako's cart road (B10), facing back up the road.
            if (GameObject.Find("CartParkingHotel") is { } bay)
            {
                spot = bay.transform.TransformPoint(new Vector3(1.7f, 0f, 0f));
                spotYaw = bay.transform.eulerAngles.y;
            }
            // (Without it: flat, free sand by the bar.)
            foreach (Vector2 p in spot != null ? new Vector2[0] : new[] { new Vector2(0f, -12f), new Vector2(6f, -12f), new Vector2(-6f, -12f), new Vector2(15f, -3f), new Vector2(-15f, -3f), new Vector2(0f, 12f) })
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
            Primitive(PrimitiveType.Cylinder, "SteeringWheel", r, new Vector3(-0.28f, 1.22f, CartWheelZ), new Vector3(0.36f, 0.02f, 0.36f), black, keepCollider: false)
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
            Transform gripL = P("GripLeft", new Vector3(-0.4f, 1.24f, CartWheelZ - 0.03f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform gripR = P("GripRight", new Vector3(-0.16f, 1.24f, CartWheelZ - 0.03f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform thrust = P("Thrust", new Vector3(0f, 0.25f, -1.0f), Quaternion.identity);
            Vehicle cart = FinishVehicle(root, "golf cart", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.3f,
                thrustForce: 6f, maxSpeed: 8f, turnRate: 1.4f);
            SetRefs(cart, "_backSeats", passenger);
            SetBool(cart, "_land", true);
            SetField(cart, "_sideGrip", p => p.floatValue = 9f);
            SetField(cart, "_reverseThrust", p => p.floatValue = 3f);
            Debug.Log($"[BarDining] golf cart at {spot.Value:F1}");
        }

        /// <summary>
        /// The bar's dishes are eaten where they're put down (Fun.TableMeal): three bites or sips each, the khinkali one
        /// dumpling at a time off the plate (khinkali.glb's Khinkali0..2).
        /// </summary>
        private static void AddTableMeals()
        {
            foreach (string name in new[] { "Fries", "Cola", "Beer", "Cocktail", "Khinkali" })
            {
                string path = $"Assets/_Game/Items/Prefabs/{name}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException($"No {name} item ({path})");
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var meal = root.GetComponent<TableMeal>() ?? root.AddComponent<TableMeal>();
                    SetField(meal, "_bites", p => p.intValue = 3);
                    SetField(meal, "_pieceNames", p =>
                    {
                        p.arraySize = name == "Khinkali" ? 3 : 0;
                        for (int i = 0; i < p.arraySize; i++) p.GetArrayElementAtIndex(i).stringValue = "Khinkali" + i;
                    });
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
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
                for (int i = 0; i < BarArmchairs.Length; i++)
                {
                    var (edge, facing) = BarArmchairs[i];
                    float table = (edge + BarArmchairs[i ^ 1].edge) * 0.5f; // the pair's table, halfway between them
                    if (i % 2 == 0) BuildLoungeTable(root, bar, n / 2, new Vector3(side * BarTableX, 0f, table));
                    Vector3 local = new(side * BarTableX, 0f, edge - facing * ArmchairEdge); // (its point 0.38 behind the cushion's edge)
                    Vector3 serve = new(side * BarTableX, BarTableTop + 0.06f, table - facing * BarTableDepth * 0.3f); // on the table, its own half, near its edge (in reach, Fun.TableMeal)
                    seats.Add(BuildTableSeat(root, bar, n++, local, facing > 0f ? 0f : 180f, serve, barista));
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

        /// <summary>A low table between two armchairs: the model on the deck, and a box to bump into and put things on.</summary>
        private static void BuildLoungeTable(Transform parent, Transform bar, int index, Vector3 local)
        {
            var go = new GameObject("LoungeTable" + index);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(bar.TransformPoint(local), bar.rotation);
            if (PropModel("bar_table", go.transform, new Vector3(0f, BarDeckTop, 0f)) == null)
                throw new InvalidOperationException("Bar table model missing (Art/Props/bar_table.glb: ArtSource/Tools/model_bar_table.py)");
            TagSurface(go, SurfaceKind.Wood);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, (BarDeckTop + BarTableTop) * 0.5f, 0f);
            box.size = new Vector3(BarTableWidth, BarTableTop - BarDeckTop, BarTableDepth);
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
            // The whole sofa: arms, cushion (1.05 deep, its front edge 0.38 ahead) and back.
            box.center = new Vector3(0f, 0.65f, ArmchairEdge - 0.78f);
            box.size = new Vector3(2.4f, 0.78f, 1.56f);
            var seat = new GameObject("Seat").transform;
            seat.SetParent(go.transform, false);
            // On the cushion (0.82 high, measured), forward on it as at a table, its front edge (0.38 ahead) 0.18 ahead of
            // the seat: the knees bend over it and the legs hang in front (further back they went down through it).
            seat.localPosition = new Vector3(0f, ArmchairSeat, ArmchairSeatZ);
            var serve = new GameObject("ServePoint").transform;
            serve.SetParent(bar, false);
            serve.localPosition = serveLocal;
            serve.SetParent(go.transform, true);

            var vehicle = go.AddComponent<Vehicle>();
            var so = new SerializedObject(vehicle);
            Require(so, "_displayName").stringValue = "armchair";
            Require(so, "_seat").objectReferenceValue = seat;
            Require(so, "_chair").boolValue = true;
            Require(so, "_seatEdge").floatValue = ArmchairEdge - ArmchairSeatZ;
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
