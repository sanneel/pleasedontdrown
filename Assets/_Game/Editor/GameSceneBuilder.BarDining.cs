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
        private const float BarTableX = 10.36f, ArmchairSeat = 0.95f, BarTableTop = 0.74f;

        /// <summary>What the hotel bar sells (item, label, price).</summary>
        private static readonly (string item, string label, int price)[] HotelBarMenu =
            { ("Fries", "Fries", 3), ("Cola", "Cola", 2), ("Beer", "Beer", 4), ("Cocktail", "Cocktail", 8), ("Khinkali", "Khinkali", 6) };

        [MenuItem("PLEASE DON'T DROWN/Hotel bar: tables and khinkali")]
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
            AssignSceneIds(scene); // the new seats are networked scene objects
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BarDining] PASS: {seats.Count} table seats, menu of {HotelBarMenu.Length} (khinkali in)");
        }

        /// <summary>Batch: the same.</summary>
        public static void BuildBarDiningBatch() => BuildBarDining();

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
            seat.localPosition = new Vector3(0f, ArmchairSeat, 0.08f); // on the front of the cushion, toward the table (deep in it, the big armchair swallowed the body)
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
