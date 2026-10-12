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

        // The stools (GameSceneBuilder.BarDining.BuildBarStools): front row facing the counter (-z), the sides facing in
        // toward the side counters, each 0.55 m back from the counter top's edge (front z 4.84, sides x +/-5.02).
        private static readonly (Vector3 at, float yaw)[] HotelBarStools =
        {
            (new Vector3(3.4f, 0f, 5.34f), 180f), (new Vector3(1.12f, 0f, 5.34f), 180f), (new Vector3(-1.12f, 0f, 5.34f), 180f), (new Vector3(-3.4f, 0f, 5.34f), 180f),
            (new Vector3(5.52f, 0f, 0.5f), 270f), (new Vector3(5.52f, 0f, 1.65f), 270f), (new Vector3(5.52f, 0f, 2.8f), 270f), (new Vector3(5.52f, 0f, 3.95f), 270f),
            (new Vector3(-5.52f, 0f, 0.5f), 90f), (new Vector3(-5.52f, 0f, 1.65f), 90f),
        };
        // The last two stools on the west side: the guests'.
        private static readonly (Vector3 at, float yaw, byte body, string name)[] HotelBarGuests =
        {
            (new Vector3(-5.52f, 0f, 2.8f), 90f, AvatarLook.Bodies.GirlPink, "Kayla"),
            (new Vector3(-5.52f, 0f, 3.95f), 90f, AvatarLook.Bodies.GirlLavender, "Mia"),
        };
        private const float BarDeckTop = 0.266f; // the bar model's deck (measured)
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
            root.Find("WipeFrom").localPosition = new Vector3(-.25f, CounterTop + .02f, 4.35f);
            root.Find("WipeTo").localPosition = new Vector3(.75f, CounterTop + .02f, 4.35f);
            barista.Find("Rag").localPosition = new Vector3(.3f, CounterTop - .25f, .7f);

            // The stools you sit on, two guests on stools of their own (GameSceneBuilder.BarDining.cs), and the armchairs
            // at the four tables: seats too.
            var bar = barista.GetComponent<Barista>();
            SetRefs(bar, "_seats", BuildBarStools(root, hotel, bar).ToArray());
            AddBarTables(root, hotel, bar);
            SetHotelBarMenu(bar);
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

            BoxCollider box = BarStoolLooks(go.transform);
            var seat = new GameObject("Seat").transform;
            seat.SetParent(go.transform, false);
            seat.localPosition = new Vector3(0f, BarStoolSeat, 0f);
            // In front of the stool, on the counter (its edge half a metre ahead): the plate (0.1 round) just in from the
            // edge, where a seated arm reaches it (Fun.TableMeal; further in, the goofy body's short arms fell short).
            var serve = new GameObject("ServePoint").transform;
            serve.SetParent(go.transform, false);
            serve.localPosition = new Vector3(0f, CounterTop + 0.1f, BarStoolFromCounter + 0.11f);

            var vehicle = go.AddComponent<Vehicle>();
            var so = new SerializedObject(vehicle);
            Require(so, "_displayName").stringValue = "bar stool";
            Require(so, "_seat").objectReferenceValue = seat;
            Require(so, "_chair").boolValue = true;
            Require(so, "_stool").boolValue = true;
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
