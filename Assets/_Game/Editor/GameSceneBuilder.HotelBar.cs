using System;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
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
    /// The hotel island's beach bar: the coloured Tripo bar (Art/Props/TripoBeachBar.prefab, 26 x 16 m, a U-shaped
    /// counter with twelve turquoise stools) west of the hotel on the plateau, facing the sea. Its deck stands on the
    /// highest sand under it with a wooden skirt down to the lowest, so it never floats or sinks on the raised island.
    /// A bartender behind the counter; sit on a stool (each a <see cref="Vehicle"/> chair with a <see cref="BarSeat"/>)
    /// and order fries, cola, beer or a cocktail from the menu for the team's money. Two guests sit at the bar.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector3 HotelBarLocal = new(-68f, 0f, -10f); // in the hotel's frame: ~18 m west of it

        // Stools measured from the bar model (top-down render + cushion vertices): front row facing the counter (-z),
        // the sides facing in toward the side counters. Cushion top 1.14 m over the model's base.
        private static readonly (Vector3 at, float yaw)[] HotelBarStools =
        {
            (new Vector3(3.42f, 0f, 5.25f), 180f), (new Vector3(1.11f, 0f, 5.25f), 180f), (new Vector3(-1.01f, 0f, 5.25f), 180f), (new Vector3(-3.38f, 0f, 5.25f), 180f),
            (new Vector3(5.82f, 0f, 0.46f), 270f), (new Vector3(5.82f, 0f, 1.43f), 270f), (new Vector3(5.82f, 0f, 2.65f), 270f), (new Vector3(5.82f, 0f, 3.99f), 270f),
            (new Vector3(-5.78f, 0f, 0.46f), 90f), (new Vector3(-5.78f, 0f, 1.43f), 90f),
        };
        // The last two stools on the west side: the guests'.
        private static readonly (Vector3 at, float yaw, byte body, string name)[] HotelBarGuests =
        {
            (new Vector3(-5.78f, 0f, 2.65f), 90f, AvatarLook.Bodies.GirlPink, "Kayla"),
            (new Vector3(-5.78f, 0f, 3.99f), 90f, AvatarLook.Bodies.GirlLavender, "Mia"),
        };
        private const float StoolCushion = 1.14f;
        private const float CounterTop = 1.54f;

        private static void BuildHotelBar(Transform hotel)
        {
            foreach (string old in new[] { "HotelBeachBar", "HotelBarStools", "HotelBarGuests" })
            {
                Transform t = hotel.Find(old);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var barPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropDir + "/TripoBeachBar.prefab");
            if (barPrefab == null) throw new InvalidOperationException("Coloured Tripo beach bar prefab missing (Art/Props/TripoBeachBar.prefab)");

            // Stand it on the highest sand under its footprint; a skirt hides the drop to the lowest.
            float high = float.MinValue, low = float.MaxValue;
            for (float x = -13f; x <= 13f; x += 1f)
                for (float z = -8f; z <= 8f; z += 1f)
                {
                    Vector3 w = hotel.TransformPoint(HotelBarLocal + new Vector3(x, 0f, z));
                    float g = BeachHeight(w.x, w.z);
                    high = Mathf.Max(high, g);
                    low = Mathf.Min(low, g);
                }
            var root = new GameObject("HotelBeachBar").transform;
            root.SetParent(hotel, false);
            Vector3 centre = hotel.TransformPoint(HotelBarLocal);
            root.SetPositionAndRotation(new Vector3(centre.x, high, centre.z), hotel.rotation);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(barPrefab, root);
            visual.name = "ColouredTripoBar";
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            float drop = high - low + 0.4f;
            Primitive(PrimitiveType.Cube, "DeckSkirt", root, new Vector3(0f, 0.2f - drop * 0.5f, 0f), new Vector3(25.8f, drop, 15.8f), wood);

            // Collision following the model: deck, the U of the counter (the rear aisle stays open), lounge chairs, posts.
            Collider(root, "ClubDeck", new Vector3(0, .13f, 0), new Vector3(24.3f, .26f, 14.8f));
            Collider(root, "ClubCounter", new Vector3(0, .90f, 4.6f), new Vector3(10.6f, 1.28f, .95f));
            foreach (float x in new[] { -4.7f, 4.7f })
                Collider(root, "ClubSideCounter", new Vector3(x, .90f, .4f), new Vector3(1.2f, 1.28f, 7.45f));
            foreach (float x in new[] { -10.36f, 10.36f })
                foreach (float z in new[] { -4.55f, -1.7f, 2.47f, 5.58f })
                    Collider(root, "ClubChair", new Vector3(x, .65f, z), new Vector3(1.7f, .78f, 1.7f));
            foreach (float x in new[] { -10.74f, 10.74f })
                foreach (float z in new[] { -6.65f, 0, 6.65f })
                    Collider(root, "ClubPost", new Vector3(x, 2.0f, z), new Vector3(.35f, 3.5f, .35f));
            Collider(root, "ClubRoof", new Vector3(0f, 6.2f, 0f), new Vector3(25f, .4f, 15.4f));

            // The menu board by the counter's front-left corner, facing the beach, on two legs.
            SignPicture(root, "BarMenu", new Vector3(-5.3f, 2.25f, 5.12f), 180f, new Vector2(1.6f, 1.2f), "bar_menu");
            Collider(root, "BarMenuBoard", new Vector3(-5.3f, 2.25f, 5.08f), new Vector3(1.7f, 1.3f, .06f));
            foreach (float side in new[] { -0.72f, 0.72f }) // on two legs down to the deck
                Primitive(PrimitiveType.Cube, "BarMenuLeg", root, new Vector3(-5.3f + side, 1.2f, 5.06f), new Vector3(.08f, 1.9f, .08f), wood);
            Primitive(PrimitiveType.Cube, "BarMenuFrame", root, new Vector3(-5.3f, 2.25f, 5.08f), new Vector3(1.72f, 1.32f, .05f), wood);

            // The bartender behind the counter, facing the front stools.
            root.gameObject.AddComponent<NetworkObject>();
            BuildBarista(root, AvatarLook.Bodies.Bartender);
            Transform barista = root.Find("Barista");
            barista.localPosition = new Vector3(.25f, .26f, 3.72f);
            for (int i = 0; i < 4; i++) root.Find("ServeSpot" + i).localPosition = new Vector3(HotelBarStools[i].at.x, CounterTop + .1f, 4.85f);
            root.Find("WipeFrom").localPosition = new Vector3(-.25f, CounterTop + .02f, 4.35f);
            root.Find("WipeTo").localPosition = new Vector3(.75f, CounterTop + .02f, 4.35f);
            barista.Find("Rag").localPosition = new Vector3(.3f, CounterTop - .25f, .7f);

            // The stools you sit on (each its own networked chair, beside the bar: not nested in its network object).
            var stools = new GameObject("HotelBarStools").transform;
            stools.SetParent(hotel, false);
            var seats = new List<Object>();
            for (int i = 0; i < HotelBarStools.Length; i++)
                seats.Add(BuildBarStool(stools, root, i, HotelBarStools[i].at, HotelBarStools[i].yaw, barista.GetComponent<Barista>()));
            var bar = barista.GetComponent<Barista>();
            SetRefs(bar, "_seats", seats.ToArray());
            SetField(bar, "_menu", p =>
            {
                (string item, string label, int price)[] menu = { ("Fries", "Fries", 3), ("Cola", "Cola", 2), ("Beer", "Beer", 4), ("Cocktail", "Cocktail", 8) };
                p.arraySize = menu.Length;
                for (int i = 0; i < menu.Length; i++)
                {
                    SerializedProperty e = p.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("Item").stringValue = menu[i].item;
                    e.FindPropertyRelative("Label").stringValue = menu[i].label;
                    e.FindPropertyRelative("Price").intValue = menu[i].price;
                }
            });

            // Two guests on the west stools, chatting over their drinks.
            GameObject npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            if (npcPrefab != null)
            {
                var guests = new GameObject("HotelBarGuests").transform;
                guests.SetParent(hotel, false);
                foreach (var g in HotelBarGuests)
                {
                    // Feet on the stool's rail: the sitting pose puts the hips 0.48 m over the feet.
                    Vector3 at = root.TransformPoint(g.at + new Vector3(0f, StoolCushion + 0.08f - 0.48f, 0f));
                    StoryNpc npc = PlaceNpc(npcPrefab, guests, "BarGuest_" + g.name, at, root.eulerAngles.y + g.yaw);
                    var customer = npc.gameObject.AddComponent<BarCustomer>();
                    SetField(customer, "_name", p => p.stringValue = g.name);
                    SetField(customer, "_body", p => p.intValue = g.body);
                }
            }
        }

        /// <summary>One stool: a chair that never moves, the seat at the cushion, its order put down on the counter in front.</summary>
        private static BarSeat BuildBarStool(Transform parent, Transform bar, int index, Vector3 local, float yaw, Barista barista)
        {
            var go = new GameObject("BarStool" + index);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(bar.TransformPoint(local), bar.rotation * Quaternion.Euler(0f, yaw, 0f));
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll; // it's a stool: whoever simulates it, it stays put
            go.AddComponent<NetworkObject>();
            go.AddComponent<ItemSync>();

            var hull = new GameObject("Hull");
            hull.transform.SetParent(go.transform, false);
            var box = hull.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, StoolCushion * 0.5f, 0f);
            box.size = new Vector3(0.5f, StoolCushion, 0.5f);
            var seat = new GameObject("Seat").transform;
            seat.SetParent(go.transform, false);
            seat.localPosition = new Vector3(0f, StoolCushion + 0.08f, 0f);
            // In front of the stool, on the counter (front row: 0.4 m forward; sides: over the side counter).
            var serve = new GameObject("ServePoint").transform;
            serve.SetParent(go.transform, false);
            serve.localPosition = new Vector3(0f, CounterTop + 0.1f, index < 4 ? 0.42f : 0.85f); // (the stool stands on the bar's base)

            var vehicle = go.AddComponent<Vehicle>();
            var so = new SerializedObject(vehicle);
            Require(so, "_displayName").stringValue = "bar stool";
            Require(so, "_seat").objectReferenceValue = seat;
            Require(so, "_chair").boolValue = true;
            Require(so, "_thrust").floatValue = 0f;
            Require(so, "_reverseThrust").floatValue = 0f;
            Require(so, "_maxSpeed").floatValue = 1f; // (never 0: it divides)
            Require(so, "_turnRate").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(go.AddComponent<Interactable>(), new Collider[] { box }, new Renderer[0], 2.5f);

            var barSeat = go.AddComponent<BarSeat>();
            SetRef(barSeat, "_barista", barista);
            SetRef(barSeat, "_servePoint", serve);
            return barSeat;
        }
    }
}
