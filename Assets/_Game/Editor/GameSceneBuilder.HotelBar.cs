using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
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
        // Behind the counter (the model measured from above, SceneProbeMaps.HeightMapBatch at 10 cm): the staff floor is
        // 0.45 up, x -2.2..2.2, z -2.6..3.5; the front counter's top runs from z 3.53 (1.56 high there) to 4.85 over a
        // cabinet whose inside face is at z 3.6. The bartender stood at z 3.72 with her feet at the deck (0.26): in the
        // counter to the chest and sunk into the floor.
        private const float BarStaffFloor = 0.45f;
        private static readonly Vector3 HotelBaristaAt = new(.25f, BarStaffFloor, 3.25f);
        private const float BarWipeZ = 3.72f, BarWipeTop = 1.56f;

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

            TripoBarColliders(root);
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
            Transform barista = PlaceHotelBarista(root);

            // The stools you sit on, two guests on stools of their own (GameSceneBuilder.BarDining.cs), and the armchairs
            // at the four tables: seats too.
            var bar = barista.GetComponent<Barista>();
            SetRefs(bar, "_seats", BuildBarStools(root, hotel, bar).ToArray());
            AddBarTables(root, hotel, bar);
            SetHotelBarMenu(bar);
        }

        /// <summary>
        /// The coloured Tripo bar's solid parts where the model has them (measured from its triangles, 10 cm): the deck,
        /// the staff floor behind the counter, the U of the counter (the front, each side's tall counter and the lower work
        /// counter inside it), the shelves along the back either side of the rear aisle, the lounge chairs and the posts.
        /// The counter's box had stood half a metre too far out: an invisible wall before it, the bartender's half open.
        /// </summary>
        private static void TripoBarColliders(Transform root)
        {
            Collider(root, "ClubDeck", new Vector3(0, .13f, 0), new Vector3(24.3f, .26f, 14.8f));
            Collider(root, "ClubStaffFloor", new Vector3(-.1f, (.26f + BarStaffFloor) / 2f, .15f), new Vector3(4.5f, BarStaffFloor - .26f, 6.8f));
            Collider(root, "ClubCounter", new Vector3(0, .91f, 4.2f), new Vector3(10.1f, 1.3f, 1.3f));                  // z 3.55..4.85
            foreach (float side in new[] { -1f, 1f })
            {
                Collider(root, "ClubSideCounter", new Vector3(side * 4.15f, .91f, .45f), new Vector3(1.8f, 1.3f, 6.3f));  // x 3.25..5.05
                Collider(root, "ClubWorkCounter", new Vector3(side * 2.75f, .675f, .45f), new Vector3(.95f, .83f, 6.2f)); // 1.09 high
            }
            Collider(root, "ClubBackShelves", new Vector3(-3.025f, 1.68f, -2.9f), new Vector3(3.05f, 2.84f, .7f)); // x -4.55..-1.5
            Collider(root, "ClubBackShelves", new Vector3(2.75f, 1.68f, -2.9f), new Vector3(3.0f, 2.84f, .7f));    // x 1.25..4.25
            foreach (float x in new[] { -10.36f, 10.36f })
                foreach (float z in new[] { -4.55f, -1.7f, 2.47f, 5.58f })
                    Collider(root, "ClubChair", new Vector3(x, .65f, z), new Vector3(1.7f, .78f, 1.7f));
            foreach (float x in new[] { -10.74f, 10.74f })
                foreach (float z in new[] { -6.65f, 0, 6.65f })
                    Collider(root, "ClubPost", new Vector3(x, 2.0f, z), new Vector3(.35f, 3.5f, .35f));
        }

        /// <summary>The bartender on the staff floor a hand's breadth behind the counter, wiping just inside its edge
        /// (where her arm reaches), the rag put down there.</summary>
        private static Transform PlaceHotelBarista(Transform root)
        {
            Transform barista = root.Find("Barista");
            barista.localPosition = HotelBaristaAt;
            root.Find("WipeFrom").localPosition = new Vector3(.05f, BarWipeTop + .045f, BarWipeZ); // (the rag 3.5 cm under the palm)
            root.Find("WipeTo").localPosition = new Vector3(.6f, BarWipeTop + .045f, BarWipeZ);
            barista.Find("Rag").localPosition = new Vector3(.3f, BarWipeTop + .007f - HotelBaristaAt.y, BarWipeZ - HotelBaristaAt.z);
            return barista;
        }

        /// <summary>Batch: the saved scene's hotel bar given the colliders and the bartender's place above (the rest of the
        /// bar as it is). Run again after a merge.</summary>
        public static void HotelBarFixBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform root = FindByPath("Environment/Hotel/HotelBeachBar") ?? throw new InvalidOperationException("No hotel bar");
            int removed = 0;
            foreach (Transform t in root.Cast<Transform>().ToArray())
                if (t.name is "ClubDeck" or "ClubStaffFloor" or "ClubCounter" or "ClubSideCounter" or "ClubWorkCounter" or "ClubBackShelves"
                        or "ClubChair" or "ClubPost" && t.GetComponents<Component>().Length == 2 && t.GetComponent<BoxCollider>() != null)
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            TripoBarColliders(root);
            foreach (Transform t in root.Cast<Transform>().Where(t => t.name == "ClubChair").ToArray())
                Object.DestroyImmediate(t.gameObject); // (the armchairs are seats with boxes of their own: AddBarTables)
            Transform barista = PlaceHotelBarista(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[HotelBar] PASS: {removed} old colliders replaced; bartender at {barista.position:F2} (feet on the staff floor)");
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
