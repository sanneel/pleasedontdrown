using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Dev;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Story;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The dev island (Dev/DevIsland.cs): ~110 m west of island 1. Its east beach faces island 1: arrival pad, the big
    /// sign, the rescue test board by the water and a dock with a key-less jet ski. Inland: the armory counter and the
    /// gun table on the firing line of a range that shoots west (targets at 10, 25, 30 moving, 50 m on the sand, 100 and
    /// 150 m on platforms in the sea), a row of every item, and the model gallery on pedestals.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector2 DevIslandCenter = new(-230f, -60f);
        private static readonly Vector2 DevIslandHalfSize = new(42f, 30f);
        private const float DevIslandCornerRadius = 20f;
        private static readonly Vector3 DevPadOnIsland1 = new(-16f, 0f, 17f);
        private static readonly Vector3 DevArrival = new(-199.5f, 0.3f, -60f);
        private static readonly Vector3 PadOnIsland2 = new(8f, 0f, -229f);
        private const float DevFiringLineX = -207f;

        /// <summary>The dev island's shore coordinate (4 at the edge, growing inland), a flat sandy block.</summary>
        private static float DevIslandShore(float x, float z)
        {
            float wobble = 1.5f * Mathf.Sin(z * 0.08f + 0.7f) + (Mathf.PerlinNoise(x * 0.04f + 21f, z * 0.04f + 9f) - 0.5f) * 3f;
            return 4f - BoxDistanceOut(x, z, DevIslandCenter, DevIslandHalfSize, DevIslandCornerRadius) + wobble;
        }

        private static void BuildDevIsland(Transform env)
        {
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material plywood = GetMaterial("Plywood", new Color(0.86f, 0.72f, 0.5f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material stone = GetMaterial("Pedestal", new Color(0.82f, 0.8f, 0.76f));
            Material padGlow = GetMaterial("PadGlow", new Color(0.55f, 0.35f, 1f), emission: new Color(0.9f, 0.5f, 2.2f));
            Material metal = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));

            var root = new GameObject("DevIsland").transform;
            root.SetParent(env, false);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            var controller = new GameObject("DevIslandController");
            controller.transform.SetParent(root, false);
            controller.transform.position = new Vector3(DevIslandCenter.x, 0f, DevIslandCenter.y);
            controller.AddComponent<NetworkObject>();
            var dev = controller.AddComponent<DevIsland>();

            // ---------------------------------------------------------------- getting there and back
            Transform arrival = Point(root, "DevArrival", DevArrival, -90f); // looking west, into the island
            Transform home = Point(root, "DevHome", OnGround(DevPadOnIsland1 + new Vector3(2.2f, 0f, 0f)) + Vector3.up * 0.3f, 90f);
            Transform hotel = Point(root, "HotelArrival", OnGround(PadOnIsland2 + new Vector3(2.2f, 0f, 0f)) + Vector3.up * 0.3f, 180f);
            // A travel pad on every island (E: a list of the other islands).
            TeleportPadAt(root, "TravelPad_Island1", OnGround(DevPadOnIsland1), 90f, Destination.StationBeach, padGlow);
            TeleportPadAt(root, "TravelPad_Island2", OnGround(PadOnIsland2), 0f, Destination.HotelIsland, padGlow);
            TeleportPadAt(root, "TravelPad_Dev", OnGround(new Vector3(-200.5f, 0f, -66.5f)), -90f, Destination.DevIsland, padGlow);
            DevSign(root, "DevIslandSign", OnGround(new Vector3(-195f, 0f, -51f)), 90f, new Vector2(5f, 1.56f), "dev_island", 1.4f);

            // ---------------------------------------------------------------- armory and the range
            var shelves = new List<(string item, Transform spot)>();
            GameObject table = Primitive(PrimitiveType.Cube, "GunTable", root, new Vector3(DevFiringLineX + 1f, 0.45f, -47f), new Vector3(0.9f, 0.9f, 14f), wood);
            TagSurface(table, SurfaceKind.Wood);
            string[] guns = { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper", "Knife" };
            for (int i = 0; i < guns.Length; i++)
                shelves.Add((guns[i], Point(root, $"Shelf_{guns[i]}", new Vector3(DevFiringLineX + 1f, 1.02f, -52.5f + i * 2.6f), -90f)));
            DevSign(root, "RangeSign", new Vector3(DevFiringLineX + 2.5f, 0f, -47f), 90f, new Vector2(3.2f, 1f), "dev_range", 2.6f);

            GameObject counter = Primitive(PrimitiveType.Cube, "ArmoryCounter", root, new Vector3(-203.5f, 0.55f, -36f), new Vector3(3.2f, 1.1f, 0.8f), metal);
            TagSurface(counter, SurfaceKind.Wood);
            counter.AddComponent<NetworkObject>();
            var armory = counter.AddComponent<ShopCounter>();
            var armorySpawn = new GameObject("ArmorySpawn").transform;
            armorySpawn.SetParent(root, false);
            armorySpawn.position = new Vector3(-203.5f, 1.4f, -36f);
            SetRef(armory, "_spawnPoint", armorySpawn);
            var armorySo = new SerializedObject(armory);
            Require(armorySo, "_title").stringValue = "Dev armory";
            Require(armorySo, "_startsOpen").boolValue = true;
            Require(armorySo, "_free").boolValue = true;
            SerializedProperty products = Require(armorySo, "_products");
            products.arraySize = guns.Length;
            for (int i = 0; i < guns.Length; i++)
            {
                SerializedProperty product = products.GetArrayElementAtIndex(i);
                product.FindPropertyRelative("Item").stringValue = guns[i];
                product.FindPropertyRelative("Price").intValue = 0;
                product.FindPropertyRelative("Blurb").stringValue = "Free here. Hold a gun and open this again to fit parts.";
            }
            armorySo.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(counter.AddComponent<Interactable>(), new[] { counter.GetComponent<Collider>() }, new[] { counter.GetComponent<Renderer>() }, 3f);
            DevSign(root, "ArmorySign", new Vector3(-203.5f, 0f, -36.6f), 180f, new Vector2(2.4f, 0.75f), "dev_armory", 2.2f);

            // Targets: sideways lanes so no target hides another. 100 and 150 m stand on platforms in the sea.
            (float metres, float z, float travel)[] targets = { (10f, -53f, 0f), (25f, -50f, 0f), (50f, -47f, 0f), (30f, -41f, 3f), (100f, -50f, 0f), (150f, -44f, 0f) };
            foreach ((float metres, float z, float travel) in targets)
            {
                float x = DevFiringLineX - metres;
                bool sea = metres >= 100f;
                Vector3 ground = sea ? new Vector3(x, WaterLevel + 0.35f, z) : OnGround(new Vector3(x, 0f, z));
                if (sea)
                {
                    Primitive(PrimitiveType.Cube, $"Platform{metres}", root, ground - Vector3.up * 0.18f, new Vector3(2.4f, 0.36f, 2.4f), wood);
                    foreach (float px in new[] { -1f, 1f })
                        foreach (float pz in new[] { -1f, 1f })
                            Primitive(PrimitiveType.Cube, "Post", root, ground + new Vector3(px, -4f, pz), new Vector3(0.2f, 7.6f, 0.2f), wood);
                }
                Target(root, $"Target{metres}m", ground, travel, plywood, red, white, wood);
                if (travel == 0f)
                    DevSign(root, $"Distance{metres}", ground + new Vector3(0.2f, 0f, 1.1f), 90f, new Vector2(0.8f, 0.4f), $"dev_range_{metres}", 0.25f);
            }

            // ---------------------------------------------------------------- every item, restocked
            string[] items = { "Coconut", "LifeRing", "Crate", "Cooler", "BeachBall", "Defibrillator", "Wallet", "Phone", "Sunglasses", "Watch", "Baggie", "JetSkiKeys" };
            for (int i = 0; i < items.Length; i++)
            {
                var spot = new Vector3(-212f - i * 2.2f, 0f, -66f);
                GameObject pallet = Primitive(PrimitiveType.Cube, "Pallet", root, OnGround(spot) + Vector3.up * 0.06f, new Vector3(1f, 0.12f, 1f), wood);
                TagSurface(pallet, SurfaceKind.Wood);
                shelves.Add((items[i], Point(root, $"Shelf_{items[i]}", OnGround(spot) + Vector3.up * 0.5f, 0f)));
            }
            DevSign(root, "ItemsSign", OnGround(new Vector3(-224f, 0f, -63.5f)), 180f, new Vector2(2f, 0.62f), "dev_items", 1.2f);

            // ---------------------------------------------------------------- the model gallery
            // The generated models in a row; west of the item shelves, one row per tourist with its look-alikes (#1..#10).
            var models = new List<(string name, byte body, bool feminine, Vector3 at)>
            {
                ("Sandy", AvatarLook.Bodies.Sandy, true, new Vector3(-208f, 0f, -80f)),
                ("Sandy (boss)", AvatarLook.Bodies.SandyBoss, true, new Vector3(-212.5f, 0f, -80f)),
                ("Tourist: sunburnt dad", AvatarLook.Bodies.TouristBuddy, false, new Vector3(-217f, 0f, -80f)),
            };
            // The tourist women in a row, then the dad's look-alikes.
            string[] women = { "Lola", "Red bikini", "Blonde", "Redhead", "Black hair", "Pink bikini", "Lavender bikini" };
            for (int w = 0; w < women.Length; w++)
                models.Add(($"Tourist: {women[w]}", AvatarLook.Bodies.TouristWomen[w], true, new Vector3(-241f - w * 2.6f, 0f, -62f)));
            models.Add(("Barista", AvatarLook.Bodies.BaristaGirl, true, new Vector3(-241f - women.Length * 2.6f, 0f, -62f)));
            models.Add(("Bartender", AvatarLook.Bodies.Bartender, true, new Vector3(-241f - (women.Length + 1) * 2.6f, 0f, -62f)));
            for (int n = 1; n <= AvatarLook.Bodies.VariantsPerBase; n++)
                models.Add(($"Sunburnt dad #{n}", AvatarLook.Bodies.Variant(AvatarLook.Bodies.TouristBuddy, n), false,
                    new Vector3(-241f - (n - 1) * 2.6f, 0f, -66.5f)));
            var gallery = new List<(string name, byte body, bool feminine, StoryNpc npc)>();
            for (int i = 0; i < models.Count; i++)
            {
                Vector3 at = OnGround(models[i].at);
                GameObject pedestal = Primitive(PrimitiveType.Cylinder, "Pedestal", root, at + Vector3.up * 0.15f, new Vector3(1.4f, 0.15f, 1.4f), stone);
                TagSurface(pedestal, SurfaceKind.Rock);
                StoryNpc npc = PlaceNpc(npcPrefab, root, "Gallery_" + i, at + Vector3.up * 0.32f, 0f);
                gallery.Add((models[i].name, models[i].body, models[i].feminine, npc));
            }
            DevSign(root, "GallerySign", OnGround(new Vector3(-221.5f, 0f, -84f)), 0f, new Vector2(3.2f, 1f), "dev_gallery", 2.4f);

            // ---------------------------------------------------------------- rescue tests by the water
            Vector3 boardAt = OnGround(new Vector3(-195.5f, 0f, -73f));
            var board = new GameObject("RescueBoard").transform;
            board.SetParent(root, false);
            board.SetPositionAndRotation(boardAt, Quaternion.Euler(0f, -90f, 0f)); // faces west: you look out to sea pressing
            Primitive(PrimitiveType.Cube, "Panel", board, new Vector3(0f, 1.25f, 0f), new Vector3(3.4f, 1.3f, 0.12f), wood);
            foreach (float side in new[] { -1.6f, 1.6f })
                Primitive(PrimitiveType.Cube, "Post", board, new Vector3(side, 0.95f, 0.1f), new Vector3(0.14f, 1.9f, 0.14f), wood);
            DevSign(board, "RescueSign", Vector3.zero, 0f, new Vector2(2.6f, 0.8f), "dev_rescue", 2.05f, posts: false);
            (DevAction action, string label, Color color)[] buttons =
            {
                (DevAction.DrowningWoman, "Drowning woman (in the sea)", new Color(0.25f, 0.55f, 1f)),
                (DevAction.DrowningMan, "Drowning man (in the sea)", new Color(0.2f, 0.4f, 0.9f)),
                (DevAction.SilentWoman, "Silent drowning (no shouting)", new Color(0.4f, 0.75f, 1f)),
                (DevAction.CprWoman, "Unconscious woman on the sand (CPR)", new Color(1f, 0.5f, 0.7f)),
                (DevAction.CprMan, "Unconscious man on the sand (CPR)", new Color(0.9f, 0.4f, 0.6f)),
                (DevAction.Flatline, "No pulse (needs the defibrillator)", new Color(1f, 0.8f, 0.2f)),
                (DevAction.Shark, "Shark (bites someone in the water)", new Color(0.55f, 0.6f, 0.65f)),
                (DevAction.Robber, "Robber (runs off with nothing)", new Color(0.3f, 0.3f, 0.32f)),
                (DevAction.Money, "+$1000", new Color(0.3f, 0.85f, 0.35f)),
                (DevAction.ClearTourists, "Clear the tourists", new Color(0.9f, 0.9f, 0.9f)),
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                float bx = -1.3f + (i % 5) * 0.65f, by = i < 5 ? 1.5f : 1.0f;
                DevButtonAt(board, $"Button_{buttons[i].action}", new Vector3(bx, by, 0.07f), buttons[i].action, buttons[i].label,
                    GetMaterial($"DevButton{i}", buttons[i].color), metal);
            }
            Transform seaSpot = Point(root, "DevSeaSpot", new Vector3(-172f, WaterLevel, -73f), 0f);
            Transform beachSpot = Point(root, "DevBeachSpot", OnGround(new Vector3(-199.5f, 0f, -77f)), 0f);

            // ---------------------------------------------------------------- dock and a jet ski anyone can ride
            BuildDock(env, "DevDock", new Vector3(-183f, 0f, -88f), 18f, 90f);
            BuildJetSki(env, OnWater(new Vector3(-176f, 0f, -85.6f)), 90f, key: "", displayName: "Dev Jet Ski");

            // ---------------------------------------------------------------- wiring
            var so = new SerializedObject(dev);
            Require(so, "_arrival").objectReferenceValue = arrival;
            Require(so, "_home").objectReferenceValue = home;
            Require(so, "_hotel").objectReferenceValue = hotel;
            Require(so, "_seaSpot").objectReferenceValue = seaSpot;
            Require(so, "_beachSpot").objectReferenceValue = beachSpot;
            SerializedProperty shelfList = Require(so, "_shelves");
            shelfList.arraySize = shelves.Count;
            for (int i = 0; i < shelves.Count; i++)
            {
                shelfList.GetArrayElementAtIndex(i).FindPropertyRelative("Item").stringValue = shelves[i].item;
                shelfList.GetArrayElementAtIndex(i).FindPropertyRelative("Spot").objectReferenceValue = shelves[i].spot;
            }
            SerializedProperty galleryList = Require(so, "_gallery");
            galleryList.arraySize = gallery.Count;
            for (int i = 0; i < gallery.Count; i++)
            {
                SerializedProperty m = galleryList.GetArrayElementAtIndex(i);
                m.FindPropertyRelative("Name").stringValue = gallery[i].name;
                m.FindPropertyRelative("Body").intValue = gallery[i].body;
                m.FindPropertyRelative("Feminine").boolValue = gallery[i].feminine;
                m.FindPropertyRelative("Npc").objectReferenceValue = gallery[i].npc;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A painted board on two posts; it faces <paramref name="facing"/> (the side readers stand on).</summary>
        private static void DevSign(Transform parent, string name, Vector3 ground, float facing, Vector2 size, string texture, float bottom,
            bool posts = true)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            var sign = new GameObject(name).transform;
            sign.SetParent(parent, false);
            sign.localPosition = ground;
            sign.localRotation = Quaternion.Euler(0f, facing, 0f);
            float cy = bottom + size.y * 0.5f;
            Primitive(PrimitiveType.Cube, "Board", sign, new Vector3(0f, cy, 0f), new Vector3(size.x + 0.1f, size.y + 0.1f, 0.08f), wood);
            if (posts)
            {
                float top = cy + size.y * 0.5f;
                foreach (float side in new[] { -1f, 1f })
                    Primitive(PrimitiveType.Cube, "Post", sign, new Vector3(side * (size.x * 0.5f - 0.2f), top * 0.5f, -0.09f), new Vector3(0.12f, top, 0.12f), wood);
            }
            SignPicture(sign, "Face", new Vector3(0f, cy, 0.046f), 180f, size, texture);
        }

        private static void TeleportPadAt(Transform parent, string name, Vector3 ground, float facing, Destination here, Material glow)
        {
            Material ring = GetMaterial("PadRing", new Color(0.25f, 0.2f, 0.35f));
            var pad = new GameObject(name).transform;
            pad.SetParent(parent, false);
            pad.SetPositionAndRotation(ground, Quaternion.Euler(0f, facing, 0f));
            GameObject disc = Primitive(PrimitiveType.Cylinder, "Disc", pad, new Vector3(0f, 0.05f, 0f), new Vector3(1.8f, 0.05f, 1.8f), ring);
            Primitive(PrimitiveType.Cylinder, "Glow", pad, new Vector3(0f, 0.11f, 0f), new Vector3(1.4f, 0.02f, 1.4f), glow, keepCollider: false);
            var teleport = pad.gameObject.AddComponent<TeleportPad>();
            var so = new SerializedObject(teleport);
            Require(so, "_here").enumValueIndex = (int)here;
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(pad.gameObject.AddComponent<Interactable>(), new[] { disc.GetComponent<Collider>() },
                pad.GetComponentsInChildren<Renderer>(), 3.5f);
            DevSign(pad, "Sign", new Vector3(0f, 0f, -1.3f), 0f, new Vector2(1.6f, 0.5f), "dev_travel", 1.3f);
        }

        /// <summary>A range target: a plywood torso and head with a bullseye on a post, hinged at the foot to fall over.</summary>
        private static void Target(Transform parent, string name, Vector3 ground, float travel, Material plywood, Material red, Material white, Material wood)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, 90f, 0f)); // faces east, up the range
            root.AddComponent<NetworkObject>();
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            Primitive(PrimitiveType.Cube, "Post", hinge, new Vector3(0f, 0.3f, -0.03f), new Vector3(0.08f, 0.6f, 0.06f), wood);
            GameObject torso = Primitive(PrimitiveType.Cube, "Torso", hinge, new Vector3(0f, 0.98f, 0f), new Vector3(0.56f, 0.76f, 0.04f), plywood);
            GameObject head = Primitive(PrimitiveType.Cylinder, "Head", hinge, new Vector3(0f, 1.53f, 0f), new Vector3(0.3f, 0.02f, 0.3f), plywood);
            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            (float size, Material mat)[] rings = { (0.34f, red), (0.24f, white), (0.14f, red), (0.05f, white) };
            for (int i = 0; i < rings.Length; i++)
            {
                GameObject ringGo = Primitive(PrimitiveType.Cylinder, "Ring", hinge, new Vector3(0f, 1.02f, 0.021f + i * 0.002f), new Vector3(rings[i].size, 0.001f, rings[i].size), rings[i].mat, keepCollider: false);
                ringGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            TagSurface(root, SurfaceKind.Wood);
            var dummy = root.AddComponent<TargetDummy>();
            var so = new SerializedObject(dummy);
            Require(so, "_hinge").objectReferenceValue = hinge;
            Require(so, "_travel").vector3Value = new Vector3(0f, 0f, travel);
            Require(so, "_audio").objectReferenceValue = SpatialAudio(root, 3f, 60f);
            so.ApplyModifiedPropertiesWithoutUndo();
            _ = torso;
        }

        private static void DevButtonAt(Transform parent, string name, Vector3 local, DevAction action, string label, Material cap, Material body)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = local;
            root.AddComponent<NetworkObject>();
            GameObject box = Primitive(PrimitiveType.Cube, "Box", root.transform, Vector3.zero, new Vector3(0.36f, 0.36f, 0.1f), body);
            GameObject capGo = Primitive(PrimitiveType.Cylinder, "Cap", root.transform, new Vector3(0f, 0f, 0.07f), new Vector3(0.24f, 0.03f, 0.24f), cap, keepCollider: false);
            capGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var button = root.AddComponent<DevButton>();
            var so = new SerializedObject(button);
            Require(so, "_action").enumValueIndex = (int)action;
            Require(so, "_label").stringValue = label;
            Require(so, "_cap").objectReferenceValue = capGo.transform;
            Require(so, "_audio").objectReferenceValue = SpatialAudio(root, 2f, 20f);
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(root.AddComponent<Interactable>(), new[] { box.GetComponent<Collider>() }, new[] { box.GetComponent<Renderer>(), capGo.GetComponent<Renderer>() }, 3f);
        }
    }
}
