using System.Collections.Generic;
using System.IO;
using FishNet.Component.Transforming;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Story mode content (Docs/05-Story-Mode.md): story items, the NPC and shark prefabs, Sandy's Lost &amp; Found
    /// kiosk, the jet ski, the hotel island with its reception, infirmary and dock, the pirate boat and the director.
    /// Everything is greybox: generated models replace the visuals later, the named points stay.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string StoryPrefabDir = "Assets/_Game/Story/Prefabs";
        private const string NpcPrefabPath = StoryPrefabDir + "/StoryNpc.prefab";
        private const string SharkPrefabPath = StoryPrefabDir + "/Shark.prefab";

        // Island 1 places.
        private static readonly Vector3 KioskPosition = new(8.5f, 0f, 17.5f);
        private static readonly Vector3 RobberSpawn = new(-32f, 0f, 24f);
        private static readonly Vector3[] LostItemSpots = { new(6f, 0f, 3.5f), new(-6f, 0f, 3f), new(12f, 0f, 5f), new(-14f, 0f, 6f) };
        private static readonly Vector3 JetSkiDock1 = new(-5.8f, 0f, -9f);
        // Island 2 places (the hotel faces north, toward island 1).
        private static readonly Vector3 HotelCenter = new(20f, 0f, -244f);
        private static readonly Vector3 Dock2 = new(-2f, 0f, -210f);
        private static readonly Vector3 JetSkiDock2 = new(0.4f, 0f, -203f);
        private static readonly Vector3 Island2Arrival = new(0f, 0f, -205f);
        private static readonly Vector3 Island2Spawn = new(20f, 0.3f, -231f);
        private static readonly Vector3 PirateLanding = new(-8f, 0f, -219f);
        private static readonly Vector3 PirateBoatStart = new(-80f, 0f, -168f);
        private static readonly Vector3 PirateBoatParked = new(-150f, 0f, -330f);

        // =====================================================================
        // Items
        // =====================================================================

        private static IEnumerable<Object> BuildStoryItems(Mesh torus)
        {
            PhysicsMaterial rubber = GetPhysicsMaterial("Rubber", 0.3f, 0.8f, PhysicsMaterialCombine.Average);
            PhysicsMaterial wood = GetPhysicsMaterial("WoodPhysics", 0.1f, 0.6f, PhysicsMaterialCombine.Average);
            Material leather = GetMaterial("Leather", new Color(0.42f, 0.25f, 0.13f));
            Material black = GetMaterial("PhoneBlack", new Color(0.08f, 0.08f, 0.1f));
            Material screen = GetMaterial("PhoneScreen", new Color(0.25f, 0.55f, 0.95f), smoothness: 0.8f);
            Material gold = GetMaterial("Gold", new Color(0.95f, 0.75f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            Material lens = GetMaterial("Lens", new Color(0.1f, 0.12f, 0.16f), smoothness: 0.9f);
            Material pink = GetMaterial("FramePink", new Color(1f, 0.45f, 0.65f));
            Material baggie = GetMaterial("Baggie", new Color(0.95f, 0.95f, 0.9f), smoothness: 0.6f);
            Material orange = GetMaterial("FloatOrange", new Color(1f, 0.5f, 0.1f));
            Material steel = GetMaterial("Steel", new Color(0.55f, 0.57f, 0.6f), metallic: 0.7f, smoothness: 0.6f);
            Material gunMetal = GetMaterial("GunMetal", new Color(0.16f, 0.16f, 0.18f), metallic: 0.5f, smoothness: 0.5f);
            Material defibYellow = GetMaterial("DefibYellow", new Color(1f, 0.82f, 0.15f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));

            void Lost(GameObject go, int reward, bool evidence = false)
            {
                var so = new SerializedObject(go.AddComponent<LostItem>());
                Require(so, "_reward").intValue = reward;
                Require(so, "_evidence").boolValue = evidence;
                so.ApplyModifiedPropertiesWithoutUndo();
                SetBool(go.GetComponent<Item>(), "_pocketable", true);
                SetEnum(go.GetComponent<Item>(), "_grip", (int)ItemGrip.OneHand);
                SetRef(go.AddComponent<ImpactSound>(), "_audio", SpatialAudio(go, 1.5f, 20f));
            }
            Vector3 smallHold = new(0.2f, -0.28f, 0.5f);

            yield return BuildItem("Wallet", "Wallet", 0.25f, smallHold, new Vector3(-20f, 0f, 0f), 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Body", root, Vector3.zero, new Vector3(0.12f, 0.03f, 0.09f), leather);
                Primitive(PrimitiveType.Cube, "Stitch", root, new Vector3(0f, 0.016f, 0f), new Vector3(0.1f, 0.004f, 0.07f), gold, keepCollider: false);
            }, density: 0.6f, configure: go => Lost(go, 30));

            yield return BuildItem("Phone", "Phone", 0.2f, smallHold, new Vector3(-30f, 0f, 0f), 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Body", root, Vector3.zero, new Vector3(0.08f, 0.012f, 0.16f), black);
                Primitive(PrimitiveType.Cube, "Screen", root, new Vector3(0f, 0.0065f, 0f), new Vector3(0.07f, 0.002f, 0.14f), screen, keepCollider: false);
            }, density: 1.4f, configure: go => Lost(go, 40)); // phones sink: dive for it

            yield return BuildItem("Sunglasses", "Sunglasses", 0.08f, smallHold, Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Bridge", root, Vector3.zero, new Vector3(0.14f, 0.015f, 0.015f), pink);
                foreach (float side in new[] { -1f, 1f })
                    Primitive(PrimitiveType.Sphere, "Lens", root, new Vector3(0.045f * side, -0.012f, 0f), new Vector3(0.055f, 0.04f, 0.012f), lens, keepCollider: false);
            }, density: 0.8f, configure: go => Lost(go, 20));

            yield return BuildItem("Watch", "Watch", 0.12f, smallHold, Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cylinder, "Face", root, Vector3.zero, new Vector3(0.05f, 0.008f, 0.05f), gold);
                Primitive(PrimitiveType.Cube, "Band", root, new Vector3(0f, -0.004f, 0f), new Vector3(0.025f, 0.006f, 0.16f), black, keepCollider: false);
            }, density: 1.6f, configure: go => Lost(go, 35));

            yield return BuildItem("Baggie", "Baggie", 0.05f, smallHold, Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Bag", root, Vector3.zero, new Vector3(0.09f, 0.025f, 0.07f), baggie);
                Primitive(PrimitiveType.Cube, "Seal", root, new Vector3(0f, 0f, 0.036f), new Vector3(0.09f, 0.03f, 0.006f), red, keepCollider: false);
            }, density: 0.3f, configure: go => Lost(go, 25, evidence: true));

            yield return BuildItem("JetSkiKeys", "Jet Ski Keys", 0.1f, smallHold, Vector3.zero, 1f, rubber, root =>
            {
                Primitive(PrimitiveType.Cube, "Float", root, Vector3.zero, new Vector3(0.05f, 0.03f, 0.1f), orange);
                Primitive(PrimitiveType.Cube, "Key", root, new Vector3(0f, 0f, 0.09f), new Vector3(0.015f, 0.004f, 0.06f), steel, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Ring", root, new Vector3(0f, 0f, 0.055f), new Vector3(0.03f, 0.003f, 0.03f), steel, keepCollider: false);
            }, density: 0.3f, configure: go =>
            {
                SetBool(go.GetComponent<Item>(), "_pocketable", true);
                SetEnum(go.GetComponent<Item>(), "_grip", (int)ItemGrip.OneHand);
            });

            // Guns: pistol, SMG, shotgun, rifle, sniper (GameSceneBuilder.Weapons.cs).
            foreach (Object gun in BuildWeapons(wood)) yield return gun;
            yield return BuildKnife(wood);

            yield return BuildItem("Defibrillator", "Defibrillator", 2.5f, new Vector3(0.05f, -0.4f, 0.62f), Vector3.zero, 0.8f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Case", root, Vector3.zero, new Vector3(0.34f, 0.12f, 0.26f), defibYellow);
                Primitive(PrimitiveType.Cube, "Cross", root, new Vector3(0f, 0.061f, 0f), new Vector3(0.1f, 0.004f, 0.03f), red, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Cross2", root, new Vector3(0f, 0.061f, 0f), new Vector3(0.03f, 0.004f, 0.1f), red, keepCollider: false);
                foreach (float side in new[] { -1f, 1f })
                    Primitive(PrimitiveType.Cylinder, "Paddle", root, new Vector3(0.12f * side, 0.075f, 0.09f), new Vector3(0.07f, 0.015f, 0.07f), black, keepCollider: false);
            }, density: 0.7f, configure: go =>
            {
                SetBool(go.GetComponent<Item>(), "_pocketable", true);
                SetRef(go.AddComponent<Defibrillator>(), "_audio", SpatialAudio(go, 3f, 60f));
            });
        }

        // =====================================================================
        // Prefabs: story characters and the shark
        // =====================================================================

        private static void BuildStoryPrefabs()
        {
            Directory.CreateDirectory(StoryPrefabDir);

            // ---- a story character: host-moved, procedurally animated, talkable, punchable
            var root = new GameObject("StoryNpc");
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.32f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            // The head too: talking to someone behind a counter means looking at their face.
            var head = root.AddComponent<SphereCollider>();
            head.center = new Vector3(0f, 1.62f, 0f);
            head.radius = 0.3f;
            var nob = root.AddComponent<NetworkObject>();
            var nobSo = new SerializedObject(nob);
            Require(nobSo, "_preventDespawnOnDisconnect").boolValue = true;
            nobSo.ApplyModifiedPropertiesWithoutUndo();
            var sync = root.AddComponent<NetworkTransform>(); // the host moves it
            sync.SetSynchronizeScale(false);

            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(root.transform, false);
            avatarGo.AddComponent<SkinnedMeshRenderer>();
            var rig = avatarGo.AddComponent<AvatarRig>();
            SetRef(rig, "_material", AvatarMaterial());
            SetBool(rig, "_buildOnAwake", false);
            var animator = avatarGo.AddComponent<AvatarAnimator>();
            SetRef(animator, "_rig", rig);
            SetRef(avatarGo.AddComponent<AvatarJiggle>(), "_rig", rig);

            TextMesh tag = WorldText(root.transform, "NameTag", new Vector3(0f, 2.2f, 0f), "", 64, 0.04f, new Color(1f, 0.95f, 0.8f), onTop: true);
            tag.gameObject.SetActive(false);

            var npc = root.AddComponent<StoryNpc>();
            SetRef(npc, "_rig", rig);
            SetRef(npc, "_animator", animator);
            SetRef(npc, "_audio", SpatialAudio(root, 3f, 60f));
            SetRef(npc, "_nameTag", tag);
            ConfigureInteractable(root.AddComponent<Interactable>(), new Collider[] { capsule, head }, new Renderer[] { avatarGo.GetComponent<SkinnedMeshRenderer>() }, 3.4f);
            PrefabUtility.SaveAsPrefabAsset(root, NpcPrefabPath);
            Object.DestroyImmediate(root);

            // ---- the shark: grey body, fin out of the water, wagging tail
            Material grey = GetMaterial("SharkGrey", new Color(0.45f, 0.5f, 0.56f));
            Material belly = GetMaterial("SharkBelly", new Color(0.9f, 0.9f, 0.88f));
            Material eye = GetMaterial("PhoneBlack", new Color(0.08f, 0.08f, 0.1f));
            var shark = new GameObject("Shark");
            var sharkBody = shark.AddComponent<Rigidbody>();
            sharkBody.isKinematic = true;
            shark.AddComponent<NetworkObject>();
            var sharkSync = shark.AddComponent<NetworkTransform>();
            sharkSync.SetSynchronizeScale(false);
            GameObject torso = Primitive(PrimitiveType.Capsule, "Body", shark.transform, Vector3.zero, new Vector3(0.7f, 1.4f, 0.6f), grey);
            torso.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Capsule, "Belly", shark.transform, new Vector3(0f, -0.12f, 0.1f), new Vector3(0.6f, 1.1f, 0.45f), belly, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Fin", shark.transform, new Vector3(0f, 0.55f, 0.05f), new Vector3(0.08f, 0.62f, 0.45f), grey, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                Primitive(PrimitiveType.Sphere, "Eye", shark.transform, new Vector3(0.2f * side, 0.1f, 1.05f), Vector3.one * 0.07f, eye, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Flipper", shark.transform, new Vector3(0.45f * side, -0.2f, 0.3f), new Vector3(0.5f, 0.05f, 0.25f), grey, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(0f, 20f * side, -20f * side);
            }
            var tail = new GameObject("TailPivot").transform;
            tail.SetParent(shark.transform, false);
            tail.localPosition = new Vector3(0f, 0f, -1.3f);
            Primitive(PrimitiveType.Cube, "TailUp", tail, new Vector3(0f, 0.25f, -0.2f), new Vector3(0.06f, 0.55f, 0.3f), grey, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "TailDown", tail, new Vector3(0f, -0.15f, -0.15f), new Vector3(0.06f, 0.35f, 0.25f), grey, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            var sharkLogic = shark.AddComponent<Shark>();
            DressShark(shark, tail);
            SetRef(sharkLogic, "_tail", tail);
            SetRef(sharkLogic, "_audio", SpatialAudio(shark, 4f, 60f));
            PrefabUtility.SaveAsPrefabAsset(shark, SharkPrefabPath);
            Object.DestroyImmediate(shark);
        }

        // =====================================================================
        // Scene: story places
        // =====================================================================

        private static void BuildStoryWorld(Transform env)
        {
            // Load prefab refs after NewScene (see BuildScene).
            var npcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            var sharkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SharkPrefabPath);
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));

            var story = new GameObject("Story").transform;

            // ---------------------------------------------------------------- island 1: Sandy's Lost & Found kiosk
            // Sandy's Lost & Found is the old shack (door off): she sits inside at the window, you hand things in at
            // the window's shelf. Without the Meshy shack there's a stand-alone kiosk instead.
            Transform shack = env.Find("Station_Shack");
            LostAndFound lostAndFound;
            StoryNpc sandy;
            if (shack != null && shack.Find("FloorCollision") != null)
            {
                lostAndFound = BuildShackLostAndFound(shack, out Vector3 stool, out float stoolYaw);
                sandy = PlaceNpc(npcPrefab, story, "Sandy", stool, stoolYaw);
            }
            else
            {
                Vector3 kioskPos = OnGround(KioskPosition);
                float kioskYaw = Quaternion.LookRotation(new Vector3(0f, 0f, 15f) - new Vector3(kioskPos.x, 0f, kioskPos.z)).eulerAngles.y;
                Transform kiosk = BuildKiosk(env, kioskPos, kioskYaw, out lostAndFound);
                sandy = PlaceNpc(npcPrefab, story, "Sandy", OnGround(kiosk.TransformPoint(new Vector3(1.7f, 0f, 0.9f))), kioskYaw);
            }

            // The beach crowd: towels and umbrellas, sunbathers on them, people wading and swimming.
            Transform[] towels1 = BuildTowels(env, "Island1", Island1TowelXs, seaTowardPositiveZ: false, startZ: -15f, Island1Avoid(), seed: 11);
            BuildCrowd(story, "BeachCrowd_Island1", npcPrefab, towels1, new Vector2(-24f, 20f), new Vector2(-34f, -8f),
                new Vector2(-32f, 32f), new Vector2(-14f, 4f), swimmers: 7, waders: 3, seed: 1000);

            // Lost things turn up by the shore in front of the station (first spot) and next to people's towels.
            var lostSpots = new List<Transform> { Point(story, "LostItemSpot_0", OnGround(LostItemSpots[0]), 0f) };
            for (int i = 0; i < towels1.Length; i += 2)
                lostSpots.Add(Point(story, $"LostItemSpot_{lostSpots.Count}", OnGround(towels1[i].position + towels1[i].right * 1.1f), 0f));
            Transform robberSpawn = Point(story, "RobberSpawn", OnGround(RobberSpawn), 90f);

            // The robber's jet ski, tied up by the dock.
            Vehicle jetSki = BuildJetSki(env, OnWater(JetSkiDock1), 180f);

            // ---------------------------------------------------------------- island 2: the hotel
            BuildDock(env, "HotelDock", Dock2, 24f);
            Transform hotel = BuildHotel(env, npcPrefab, story, out StoryNpc receptionist, out ShopCounter reception, out Transform desk,
                out Transform infirmary, out Transform firstAid, out Transform hotelDoor);
            Transform jetSkiDock2 = Point(story, "JetSkiDock2", OnWater(JetSkiDock2), 180f);
            Transform arrival = Point(story, "Island2Arrival", Island2Arrival, 0f);
            Transform island2Spawn = Point(story, "Island2Spawn", Island2Spawn, 0f);
            Transform pirateLanding = Point(story, "PirateLanding", OnGround(PirateLanding), 0f);
            Transform pirateStart = Point(story, "PirateBoatStart", OnWater(PirateBoatStart),
                Quaternion.LookRotation(PirateLanding - PirateBoatStart).eulerAngles.y);
            Vehicle pirateBoat = BuildPirateBoat(env, OnWater(PirateBoatParked), 30f);
            Transform[] towels2 = BuildTowels(env, "Island2", Island2TowelXs, seaTowardPositiveZ: true, startZ: -195f, Island2Avoid(), seed: 22);
            BuildCrowd(story, "BeachCrowd_Island2", npcPrefab, towels2, new Vector2(-18f, 56f), new Vector2(-198f, -180f),
                new Vector2(-22f, 60f), new Vector2(-216f, -204f), swimmers: 5, waders: 2, seed: 2000);

            // ---------------------------------------------------------------- the director
            var directorGo = new GameObject("StoryDirector");
            directorGo.transform.SetParent(story, false);
            directorGo.AddComponent<NetworkObject>();
            var economy = directorGo.AddComponent<Economy>();
            AudioSource cash = directorGo.AddComponent<AudioSource>();
            cash.playOnAwake = false;
            cash.spatialBlend = 0f; // the till rings in everyone's ears
            SetRef(economy, "_audio", cash);
            directorGo.AddComponent<DialogueService>();
            var director = directorGo.AddComponent<StoryDirector>();
            SetRef(director, "_npcPrefab", npcPrefab.GetComponent<StoryNpc>());
            SetRef(director, "_sharkPrefab", sharkPrefab.GetComponent<Shark>());
            SetRef(director, "_sandy", sandy);
            SetRef(director, "_receptionist", receptionist);
            SetRef(director, "_lostAndFound", lostAndFound);
            SetRef(director, "_reception", reception);
            SetRef(director, "_receptionDesk", desk);
            SetRef(director, "_jetSki", jetSki);
            SetRef(director, "_jetSkiIsland2Dock", jetSkiDock2);
            SetRef(director, "_pirateBoat", pirateBoat);
            SetRef(director, "_pirateBoatStart", pirateStart);
            SetRef(director, "_pirateLanding", pirateLanding);
            SetRef(director, "_hotelDoor", hotelDoor);
            SetRef(director, "_infirmary", infirmary);
            SetRef(director, "_firstAid", firstAid);
            SetRef(director, "_island2Spawn", island2Spawn);
            SetRefs(director, "_lostItemSpots", lostSpots.ToArray());

            var so = new SerializedObject(director);
            // Island 1: everyone pulled out collapses and needs CPR (the design wants every CPR moment seen).
            SetIsland(Require(so, "_island1"), "The first island", new Vector2(-20f, 16f), new Vector2(-36f, -18f), Vector3.forward,
                seconds: 20f, condition: 60f, flatline: 0f, land: new Rect(-45f, 6f, 90f, 42f), robberSpawn, null, needsCpr: true);
            SetIsland(Require(so, "_island2"), "The hotel island", new Vector2(-20f, 58f), new Vector2(-194f, -181f), Vector3.back,
                seconds: 10f, condition: 45f, flatline: 12f, land: new Rect(-35f, -276f, 110f, 54f), null, arrival, needsCpr: false);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetIsland(SerializedProperty island, string name, Vector2 seaX, Vector2 seaZ, Vector3 shoreward,
            float seconds, float condition, float flatline, Rect land, Transform robberSpawn, Transform arrival, bool needsCpr)
        {
            island.FindPropertyRelative("Name").stringValue = name;
            island.FindPropertyRelative("SeaX").vector2Value = seaX;
            island.FindPropertyRelative("SeaZ").vector2Value = seaZ;
            island.FindPropertyRelative("Shoreward").vector3Value = shoreward;
            island.FindPropertyRelative("LandArea").rectValue = land;
            island.FindPropertyRelative("RobberSpawn").objectReferenceValue = robberSpawn;
            island.FindPropertyRelative("Arrival").objectReferenceValue = arrival;
            SerializedProperty profile = island.FindPropertyRelative("Profile");
            profile.FindPropertyRelative("Figure").intValue = -1;
            profile.FindPropertyRelative("SecondsToUnconscious").floatValue = seconds;
            profile.FindPropertyRelative("ConditionSeconds").floatValue = condition;
            profile.FindPropertyRelative("FlatlineAfter").floatValue = flatline;
            profile.FindPropertyRelative("BleedSeconds").floatValue = 60f;
            profile.FindPropertyRelative("Silent").boolValue = false;
            profile.FindPropertyRelative("NeedsCpr").boolValue = needsCpr;
        }

        // =====================================================================
        // Navigation
        // =====================================================================

        private const string NavMeshDir = "Assets/_Game/Data/Navigation";

        /// <summary>
        /// Bakes a navmesh per island from the scene's static colliders (terrain, buildings, counters, trunks, dock
        /// posts, rocks; nothing with a rigidbody) and adds a loader, so story characters walk around things.
        /// </summary>
        private static void BakeNavMeshes()
        {
            Directory.CreateDirectory(NavMeshDir);
            // Objects were created and moved by script: bring the physics scene up to date, or collecting the
            // colliders (a physics query) misses the ones built far from where they were created.
            Physics.SyncTransforms();
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.3f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.1f;
            (string name, Bounds bounds)[] areas =
            {
                ("Island1", new Bounds(new Vector3(0f, 0f, 8f), new Vector3(130f, 40f, 124f))),
                ("Island2", new Bounds(new Vector3(20f, 0f, -232f), new Vector3(144f, 40f, 124f))),
                ("DevIsland", new Bounds(new Vector3(-230f, 0f, -60f), new Vector3(110f, 40f, 90f)))
            };
            var baked = new List<Object>();
            foreach ((string name, Bounds bounds) in areas)
            {
                var sources = new List<NavMeshBuildSource>();
                UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
                sources.RemoveAll(s => s.component is Collider c && (c.attachedRigidbody != null || c.isTrigger));
                // Under a dock (the seabed between the posts) is off limits: swimmers and waders used to be routed
                // through there with their heads in the planks. The deck itself stays walkable (it's above the box).
                foreach (Collider deck in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                {
                    if (deck.name != "Deck" || deck.transform.parent == null || !deck.transform.parent.name.Contains("Dock")) continue;
                    if (!bounds.Intersects(deck.bounds)) continue;
                    Bounds under = deck.bounds;
                    float bottom = under.min.y - 0.02f;
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.ModifierBox,
                        area = 1, // Not Walkable
                        transform = Matrix4x4.TRS(new Vector3(under.center.x, bottom - 4f, under.center.z), Quaternion.identity, Vector3.one),
                        size = new Vector3(under.size.x + 0.8f, 8f, under.size.z + 0.8f)
                    });
                }
                NavMeshData data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                data.name = $"NavMesh_{name}";
                string path = $"{NavMeshDir}/{name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(data, path);
                baked.Add(data);
                Debug.Log($"[Build] navmesh {name}: {sources.Count} sources");
            }
            var loader = new GameObject("Navigation").AddComponent<NavMeshLoader>();
            SetRefs(loader, "_data", baked.ToArray());
        }

        // =====================================================================
        // The beach crowd
        // =====================================================================

        private static readonly float[] Island1TowelXs = { -41f, -37f, -33f, -29f, -25f, -21f, -17f, 15.5f, 19f, 22.5f, 26f, 30f, 34f, 38f, 42f };
        private static readonly float[] Island2TowelXs = { -24f, -19f, -13f, 6f, 10f, 14f, 26f, 30f, 34f, 39f, 44f, 49f };

        /// <summary>Things towels must keep clear of on island 1 (x, z, radius).</summary>
        private static List<Vector3> Island1Avoid()
        {
            var list = new List<Vector3>
            {
                new(0f, 10f, 5f), new(12f, 8f, 4f), new(-8f, 2f, 2.6f), new(-8f, 6f, 2.6f), new(0f, 15f, 4.5f),
                new(4.6f, 12.6f, 2f), new(-5f, 12.5f, 1.8f), new(-4f, 9.5f, 2.5f), new(-2.2f, 11.2f, 1.5f), new(15.5f, 5f, 1.2f),
                new(4.5f, 14f, 1.2f), new(LostItemSpots[0].x, LostItemSpots[0].z, 1.5f), new(RobberSpawn.x, RobberSpawn.z, 2f),
                new(DevPadOnIsland1.x, DevPadOnIsland1.z, 3f)
            };
            foreach (Vector2 palm in MeshyArt.PalmSpots) list.Add(new Vector3(palm.x, palm.y, 1.8f));
            return list;
        }

        private static List<Vector3> Island2Avoid() => new()
        {
            new(-2f, -214f, 3f), new(-2f, -219f, 3f), new(20f, -237f, 3f), new(PirateLanding.x, PirateLanding.z, 3f), new(Island2Spawn.x, Island2Spawn.z, 2.5f),
            new(PadOnIsland2.x, PadOnIsland2.z, 3f)
        };

        /// <summary>
        /// Towels along a beach: for each x, walk in from the sea to dry sand and go a few metres further; skip spots
        /// near buildings, palms and paths. Every other towel gets an umbrella. Returns the towel points
        /// (middle of the towel, forward = feet toward the sea).
        /// </summary>
        private static Transform[] BuildTowels(Transform env, string name, float[] xs, bool seaTowardPositiveZ, float startZ, List<Vector3> avoid, int seed)
        {
            Color[] towelColors =
            {
                new(0.95f, 0.35f, 0.35f), new(0.25f, 0.6f, 0.95f), new(1f, 0.82f, 0.25f), new(0.35f, 0.8f, 0.5f),
                new(0.95f, 0.5f, 0.8f), new(1f, 0.6f, 0.2f), new(0.6f, 0.45f, 0.9f), new(0.3f, 0.85f, 0.85f)
            };
            Color[] umbrellaColors = { new(0.95f, 0.3f, 0.25f), new(0.2f, 0.65f, 0.7f), new(1f, 0.85f, 0.3f), new(0.98f, 0.96f, 0.9f) };
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material pole = GetMaterial("UmbrellaPole", new Color(0.85f, 0.85f, 0.82f));
            var rng = new System.Random(seed);
            var root = new GameObject($"BeachTowels_{name}").transform;
            root.SetParent(env, false);
            float inlandStep = seaTowardPositiveZ ? -0.5f : 0.5f;
            var towels = new List<Transform>();
            int n = 0;
            foreach (float baseX in xs)
            {
                float x = baseX + (float)(rng.NextDouble() - 0.5) * 1.5f;
                float z = startZ;
                for (int i = 0; i < 400 && BeachHeight(x, z) < WaterLevel + 0.3f; i++) z += inlandStep;
                z += Mathf.Sign(inlandStep) * (3.5f + (float)rng.NextDouble() * 4.5f);
                float y = BeachHeight(x, z);
                if (Mathf.Abs(BeachHeight(x, z + 1f) - BeachHeight(x, z - 1f)) > 0.5f || Mathf.Abs(BeachHeight(x + 1f, z) - BeachHeight(x - 1f, z)) > 0.5f) continue;
                bool blocked = false;
                foreach (Vector3 a in avoid)
                    if (new Vector2(x - a.x, z - a.y).sqrMagnitude < (a.z + 1.3f) * (a.z + 1.3f)) blocked = true;
                if (blocked) continue;
                float yaw = (seaTowardPositiveZ ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 40f;
                var towel = new GameObject($"Towel_{n}").transform;
                towel.SetParent(root, false);
                towel.SetPositionAndRotation(new Vector3(x, y + 0.012f, z), Quaternion.Euler(0f, yaw, 0f));
                Material cloth = GetMaterial($"Towel{n % towelColors.Length}", towelColors[n % towelColors.Length]);
                Primitive(PrimitiveType.Cube, "Cloth", towel, Vector3.zero, new Vector3(0.95f, 0.02f, 1.95f), cloth, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Stripe", towel, new Vector3(0f, 0.004f, -0.7f), new Vector3(0.95f, 0.02f, 0.18f), white, keepCollider: false);
                if (n % 2 == 0)
                {
                    // Beach umbrella beside it: a pole you bump into, a canopy you don't.
                    var umbrella = new GameObject("Umbrella").transform;
                    umbrella.SetParent(towel, false);
                    umbrella.localPosition = new Vector3(1.05f, 0f, -0.3f);
                    Primitive(PrimitiveType.Cylinder, "Pole", umbrella, new Vector3(0f, 1.1f, 0f), new Vector3(0.07f, 1.1f, 0.07f), pole);
                    Material canopy = GetMaterial($"Umbrella{(n / 2) % umbrellaColors.Length}", umbrellaColors[(n / 2) % umbrellaColors.Length]);
                    Primitive(PrimitiveType.Sphere, "Canopy", umbrella, new Vector3(0f, 2.15f, 0f), new Vector3(2.3f, 0.45f, 2.3f), canopy, keepCollider: false)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 6f);
                }
                towels.Add(towel);
                n++;
            }
            Debug.Log($"[Build] {towels.Count} towels on {name}");
            return towels.ToArray();
        }

        private static void BuildCrowd(Transform parent, string name, GameObject npcPrefab, Transform[] towels, Vector2 swimX, Vector2 swimZ,
            Vector2 wadeX, Vector2 wadeZ, int swimmers, int waders, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<NetworkObject>();
            var crowd = go.AddComponent<BeachCrowd>();
            SetRef(crowd, "_npcPrefab", npcPrefab.GetComponent<StoryNpc>());
            SetRefs(crowd, "_towels", towels);
            var so = new SerializedObject(crowd);
            Require(so, "_swimX").vector2Value = swimX;
            Require(so, "_swimZ").vector2Value = swimZ;
            Require(so, "_wadeX").vector2Value = wadeX;
            Require(so, "_wadeZ").vector2Value = wadeZ;
            Require(so, "_swimmers").intValue = swimmers;
            Require(so, "_waders").intValue = waders;
            Require(so, "_seed").intValue = seed;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3 OnGround(Vector3 p) => new(p.x, BeachHeight(p.x, p.z), p.z);
        private static Vector3 OnWater(Vector3 p) => new(p.x, WaterLevel, p.z);

        private static Transform Point(Transform parent, string name, Vector3 position, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return t;
        }

        private static StoryNpc PlaceNpc(GameObject prefab, Transform parent, string name, Vector3 position, float yaw)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            go.name = $"Npc_{name}";
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go.GetComponent<StoryNpc>();
        }

        /// <summary>
        /// Turns the shack into Sandy's Lost &amp; Found: a counter plank over the shelf under the east window (hand
        /// things in here), a sign under the eaves, the rates, and a stool inside where Sandy sits looking out.
        /// Window position measured from a screenshot of the Meshy shack (4 m model units, scaled like the walls).
        /// </summary>
        private static LostAndFound BuildShackLostAndFound(Transform shack, out Vector3 stool, out float stoolYaw)
        {
            float k = ShackScale;
            float east = 1.30f * k, floor = 0.575f * k, windowZ = -0.30f * k;
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material seat = GetMaterial("StoolSeat", new Color(0.8f, 0.3f, 0.25f));

            // The counter: a plank over the window's own shelf (window opening y 2.13..3.21, shelf at 1.89).
            GameObject counter = Primitive(PrimitiveType.Cube, "LostAndFoundCounter", shack, new Vector3(east + 0.16f, 1.9f, windowZ), new Vector3(0.42f, 0.08f, 1.8f), wood);
            TagSurface(counter, SurfaceKind.Wood);
            counter.AddComponent<NetworkObject>();
            var lostAndFound = counter.AddComponent<LostAndFound>();
            var pay = new GameObject("PayPoint").transform;
            pay.SetParent(shack, false);
            pay.localPosition = new Vector3(east + 0.3f, 2.4f, windowZ);
            SetRef(lostAndFound, "_payPoint", pay);

            // Painted sign standing on the roof over the window, the price chalkboard under the counter. Both face east;
            // the pictures are painted by ArtSource/Tools/make_signs.py (engine text looked cheap, Meshy can't write).
            GameObject board = Primitive(PrimitiveType.Cube, "LostAndFoundSign", shack, new Vector3(east + 0.02f, 4.72f, windowZ), new Vector3(0.08f, 0.78f, 2.5f), wood, keepCollider: false);
            foreach (float side in new[] { -0.85f, 0.85f })
                Primitive(PrimitiveType.Cube, "SignPost", shack, new Vector3(east - 0.05f, 4.3f, windowZ + side), new Vector3(0.07f, 0.7f, 0.07f), wood, keepCollider: false);
            SignPicture(shack, "LostAndFoundSignFace", new Vector3(east + 0.068f, 4.72f, windowZ), -90f, new Vector2(2.5f, 0.78f), "lost_and_found_sign");
            Primitive(PrimitiveType.Cube, "PriceBoard", shack, new Vector3(east + 0.05f, 1.24f, windowZ), new Vector3(0.04f, 0.58f, 0.93f), wood, keepCollider: false);
            SignPicture(shack, "PriceBoardFace", new Vector3(east + 0.078f, 1.24f, windowZ), -90f, new Vector2(0.93f, 0.58f), "lost_and_found_prices");
            ConfigureInteractable(counter.AddComponent<Interactable>(), new[] { counter.GetComponent<Collider>() },
                new[] { counter.GetComponent<Renderer>(), board.GetComponent<Renderer>() }, 3.2f);

            // A raised booth floor under the window (0.4 m: characters step up onto it) so she looks out of it,
            // and her stool on it, facing the window (no collider: she sits on it, nobody trips on it).
            const float booth = 0.4f;
            GameObject boothFloor = Primitive(PrimitiveType.Cube, "BoothFloor", shack, new Vector3(east - 0.62f, floor + booth * 0.5f, windowZ), new Vector3(1.0f, booth, 1.5f), wood);
            TagSurface(boothFloor, SurfaceKind.Wood);
            // A bar stool: she sits up high with her feet on its footrest, head and shoulders in the window.
            const float barStool = 0.28f;
            var boothTop = new Vector3(east - 0.72f, floor + booth, windowZ);
            Primitive(PrimitiveType.Cylinder, "StoolSeat", shack, boothTop + Vector3.up * (0.45f + barStool), new Vector3(0.38f, 0.03f, 0.38f), seat, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "StoolLeg", shack, boothTop + Vector3.up * (0.45f + barStool) * 0.5f, new Vector3(0.07f, (0.45f + barStool) * 0.5f, 0.07f), wood, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "StoolFootrest", shack, boothTop + Vector3.up * (barStool - 0.02f), new Vector3(0.32f, 0.02f, 0.32f), wood, keepCollider: false);
            Vector3 stoolLocal = boothTop + Vector3.up * barStool; // her feet, on the footrest
            stool = shack.TransformPoint(stoolLocal);
            stoolYaw = shack.eulerAngles.y + 90f;
            return lostAndFound;
        }

        /// <summary>
        /// A painted picture (sign lettering, a chalkboard) on a quad, from Assets/_Game/Art/Signs/{texture}.png.
        /// The quad shows its face on its local -z, so yaw -90 faces +x (east).
        /// </summary>
        private static GameObject SignPicture(Transform parent, string name, Vector3 localPos, float yaw, Vector2 size, string texture)
        {
            string path = $"Assets/_Game/Art/Signs/{texture}.png";
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer
                && (importer.anisoLevel != 8 || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048))
            {
                importer.anisoLevel = 8; // read at a slant from the beach
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var picture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (picture == null) throw new FileNotFoundException("Sign picture missing (run ArtSource/Tools/make_signs.py)", path);
            Material mat = GetMaterial("Sign_" + texture, Color.white, smoothness: 0.15f);
            mat.SetTexture("_BaseMap", picture);
            mat.mainTexture = picture;
            EditorUtility.SetDirty(mat);
            GameObject quad = Primitive(PrimitiveType.Quad, name, parent, localPos, new Vector3(size.x, size.y, 1f), mat, keepCollider: false);
            quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return quad;
        }

        /// <summary>Sandy's kiosk: counter (hand things in here), back board, striped awning and a sign.</summary>
        private static Transform BuildKiosk(Transform env, Vector3 position, float yaw, out LostAndFound lostAndFound)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material teal = GetMaterial("KioskTeal", new Color(0.18f, 0.55f, 0.55f));
            Material cream = GetMaterial("SignBoard", new Color(0.95f, 0.93f, 0.85f));
            Material coral = GetMaterial("KioskCoral", new Color(1f, 0.5f, 0.4f));
            var kiosk = new GameObject("LostAndFoundKiosk").transform;
            kiosk.SetParent(env, false);
            kiosk.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            TagSurface(kiosk.gameObject, SurfaceKind.Wood);
            GameObject counter = Primitive(PrimitiveType.Cube, "Counter", kiosk, new Vector3(0f, 0.55f, 0f), new Vector3(2.2f, 1.1f, 0.7f), teal);
            Primitive(PrimitiveType.Cube, "CounterTop", kiosk, new Vector3(0f, 1.12f, 0.05f), new Vector3(2.35f, 0.06f, 0.85f), wood, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Back", kiosk, new Vector3(0f, 1.2f, -1f), new Vector3(2.4f, 2.4f, 0.1f), wood);
            foreach (float side in new[] { -1.15f, 1.15f })
                Primitive(PrimitiveType.Cube, "Post", kiosk, new Vector3(side, 1.25f, 0.35f), new Vector3(0.1f, 2.5f, 0.1f), wood);
            for (int i = 0; i < 5; i++)
                Primitive(PrimitiveType.Cube, "Awning", kiosk, new Vector3(-0.96f + i * 0.48f, 2.55f, -0.3f), new Vector3(0.48f, 0.06f, 1.6f), i % 2 == 0 ? coral : cream, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Bin", kiosk, new Vector3(-0.7f, 1.3f, -0.1f), new Vector3(0.5f, 0.3f, 0.4f), cream, keepCollider: false);
            // (TextMesh reads from its -z side: the front ones turn round to face the customers at +z.)
            WorldText(kiosk, "Sign", new Vector3(0f, 2.05f, -0.94f), "LOST & FOUND", 80, 0.036f, new Color(0.9f, 0.3f, 0.25f))
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            WorldText(kiosk, "SignBack", new Vector3(0f, 2.05f, -1.06f), "LOST & FOUND", 80, 0.036f, new Color(0.9f, 0.3f, 0.25f));
            WorldText(kiosk, "Rates", new Vector3(0f, 1.55f, -0.94f), "We pay for:\nwallets $30  phones $40\nwatches $35  sunglasses $20", 56, 0.02f, new Color(0.2f, 0.2f, 0.2f))
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            counter.AddComponent<NetworkObject>();
            lostAndFound = counter.AddComponent<LostAndFound>();
            var pay = new GameObject("PayPoint").transform;
            pay.SetParent(kiosk, false);
            pay.localPosition = new Vector3(0f, 1.6f, 0.2f);
            SetRef(lostAndFound, "_payPoint", pay);
            ConfigureInteractable(counter.AddComponent<Interactable>(), new[] { counter.GetComponent<Collider>() }, new[] { counter.GetComponent<Renderer>() }, 3.8f);
            return kiosk;
        }

        private static void BuildDock(Transform env, string name, Vector3 landEnd, float length, float yaw = 0f)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            var dock = new GameObject(name).transform;
            dock.SetParent(env, false);
            dock.SetPositionAndRotation(new Vector3(landEnd.x, 0f, landEnd.z), Quaternion.Euler(0f, yaw, 0f));
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3(0f, 0.175f, 0f), new Vector3(2.4f, 0.25f, length), wood);
            for (float z = -length * 0.5f + 1f; z <= length * 0.5f; z += 4f)
                foreach (float x in new[] { -1.1f, 1.1f })
                    Primitive(PrimitiveType.Cube, "Post", dock, new Vector3(x, -2.2f, z), new Vector3(0.22f, 4.8f, 0.22f), wood);
        }

        /// <summary>
        /// The Grand Coral Hotel (greybox): a two-storey block facing north with an open doorway. Inside, the reception
        /// desk (shop + receptionist) on the left, the infirmary bed on the right, the first-aid box by the door.
        /// </summary>
        private static Transform BuildHotel(Transform env, GameObject npcPrefab, Transform story, out StoryNpc receptionist, out ShopCounter reception,
            out Transform desk, out Transform infirmary, out Transform firstAid, out Transform door)
        {
            Material wall = GetMaterial("HotelWall", new Color(0.98f, 0.9f, 0.78f));
            Material trim = GetMaterial("HotelTrim", new Color(0.2f, 0.55f, 0.6f));
            Material floor = GetMaterial("HotelFloor", new Color(0.85f, 0.78f, 0.65f));
            Material roof = GetMaterial("HotelRoof", new Color(0.85f, 0.4f, 0.3f));
            Material glass = GetMaterial("HotelGlass", new Color(0.55f, 0.8f, 0.95f), smoothness: 0.9f);
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material sheet = GetMaterial("BedSheet", new Color(0.8f, 0.9f, 1f));

            var hotel = new GameObject("Hotel").transform;
            hotel.SetParent(env, false);
            hotel.position = new Vector3(HotelCenter.x, BeachHeight(HotelCenter.x, HotelCenter.z), HotelCenter.z);
            const float w = 24f, d = 12f, h = 3.2f, t = 0.3f, doorW = 2.6f;
            Primitive(PrimitiveType.Cube, "Floor", hotel, new Vector3(0f, 0.15f, 0f), new Vector3(w, 0.3f, d), floor);
            Primitive(PrimitiveType.Cube, "Step", hotel, new Vector3(0f, 0.08f, d * 0.5f + 0.6f), new Vector3(4f, 0.16f, 1.2f), floor);
            float y = 0.3f + h * 0.5f;
            Primitive(PrimitiveType.Cube, "WallBack", hotel, new Vector3(0f, y, -d * 0.5f), new Vector3(w, h, t), wall);
            Primitive(PrimitiveType.Cube, "WallLeft", hotel, new Vector3(-w * 0.5f, y, 0f), new Vector3(t, h, d), wall);
            Primitive(PrimitiveType.Cube, "WallRight", hotel, new Vector3(w * 0.5f, y, 0f), new Vector3(t, h, d), wall);
            float seg = (w - doorW) * 0.5f;
            Primitive(PrimitiveType.Cube, "WallFrontL", hotel, new Vector3(-(doorW + seg) * 0.5f, y, d * 0.5f), new Vector3(seg, h, t), wall);
            Primitive(PrimitiveType.Cube, "WallFrontR", hotel, new Vector3((doorW + seg) * 0.5f, y, d * 0.5f), new Vector3(seg, h, t), wall);
            Primitive(PrimitiveType.Cube, "Lintel", hotel, new Vector3(0f, 0.3f + h - 0.3f, d * 0.5f), new Vector3(doorW, 0.6f, t), trim);
            // Windows on the front (glass panes set into the wall).
            foreach (float x in new[] { -9f, -5f, 5f, 9f })
                Primitive(PrimitiveType.Cube, "Window", hotel, new Vector3(x, 1.9f, d * 0.5f + 0.16f), new Vector3(2f, 1.3f, 0.04f), glass, keepCollider: false);
            // Upper floor and roof (solid, not enterable yet).
            Primitive(PrimitiveType.Cube, "Ceiling", hotel, new Vector3(0f, 0.3f + h + 0.15f, 0f), new Vector3(w + 0.4f, 0.3f, d + 0.4f), trim);
            Primitive(PrimitiveType.Cube, "Upper", hotel, new Vector3(0f, 0.3f + h + 0.3f + 1.6f, 0f), new Vector3(w, 3.2f, d), wall);
            for (int i = 0; i < 6; i++)
                Primitive(PrimitiveType.Cube, "UpperWindow", hotel, new Vector3(-10f + i * 4f, 0.3f + h + 1.9f, d * 0.5f + 0.02f), new Vector3(1.6f, 1.3f, 0.04f), glass, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Roof", hotel, new Vector3(0f, 0.3f + h + 0.3f + 3.3f, 0f), new Vector3(w + 1f, 0.35f, d + 1f), roof);
            TextMesh name = WorldText(hotel, "Sign", new Vector3(0f, 0.3f + h + 0.15f, d * 0.5f + 0.25f), "GRAND CORAL HOTEL", 90, 0.06f, new Color(0.95f, 0.95f, 0.95f));
            name.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TagSurface(hotel.gameObject, SurfaceKind.Wood);

            // Reception desk (left of the door) with the shop and the receptionist behind it.
            GameObject deskGo = Primitive(PrimitiveType.Cube, "ReceptionDesk", hotel, new Vector3(-6f, 0.85f, 2.2f), new Vector3(3.2f, 1.1f, 0.8f), trim);
            Primitive(PrimitiveType.Cube, "DeskTop", hotel, new Vector3(-6f, 1.43f, 2.25f), new Vector3(3.4f, 0.06f, 1f), wood, keepCollider: false);
            TextMesh deskSign = WorldText(hotel, "DeskSign", new Vector3(-6f, 0.95f, 2.62f), "RECEPTION", 70, 0.03f, Color.white);
            deskSign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            deskGo.AddComponent<NetworkObject>();
            reception = deskGo.AddComponent<ShopCounter>();
            var shopSpawn = new GameObject("ShopSpawn").transform;
            shopSpawn.SetParent(hotel, false);
            shopSpawn.localPosition = new Vector3(-5.2f, 1.6f, 2.3f);
            SetRef(reception, "_spawnPoint", shopSpawn);
            var shopSo = new SerializedObject(reception);
            Require(shopSo, "_title").stringValue = "Grand Coral reception";
            SerializedProperty products = Require(shopSo, "_products");
            (string item, int price, string blurb)[] stock =
            {
                ("Knife", 80, "Left mouse stabs whatever is in front of you. Z / C change the skin."),
                ("Pistol", 250, "For when guests get attacked. Left mouse shoots, right mouse aims, R reloads."),
                ("SMG", 600, "Sprays. Hold the trigger. 30 rounds."),
                ("Shotgun", 700, "Eight pellets a shot. Knocks you back too."),
                ("Rifle", 900, "Full auto, accurate when you aim. Takes a 4x scope."),
                ("Sniper", 1200, "One shot, one pirate. 8x scope included."),
            };
            products.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++)
            {
                SerializedProperty product = products.GetArrayElementAtIndex(i);
                product.FindPropertyRelative("Item").stringValue = stock[i].item;
                product.FindPropertyRelative("Price").intValue = stock[i].price;
                product.FindPropertyRelative("Blurb").stringValue = stock[i].blurb;
            }
            shopSo.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(deskGo.AddComponent<Interactable>(), new[] { deskGo.GetComponent<Collider>() }, new[] { deskGo.GetComponent<Renderer>() }, 3f);
            desk = deskGo.transform;
            receptionist = PlaceNpc(npcPrefab, story, "Receptionist", hotel.TransformPoint(new Vector3(-6f, 0.3f, 0.9f)), 0f);

            // Infirmary corner (right): a bed under a red cross.
            GameObject bed = Primitive(PrimitiveType.Cube, "HospitalBed", hotel, new Vector3(8.5f, 0.65f, -4f), new Vector3(2.1f, 0.5f, 1f), white);
            Primitive(PrimitiveType.Cube, "Sheet", hotel, new Vector3(8.5f, 0.92f, -4f), new Vector3(2.05f, 0.06f, 0.95f), sheet, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Pillow", hotel, new Vector3(9.3f, 1f, -4f), new Vector3(0.4f, 0.12f, 0.7f), white, keepCollider: false);
            Primitive(PrimitiveType.Cube, "CrossV", hotel, new Vector3(8.5f, 2.4f, -5.84f), new Vector3(0.3f, 0.9f, 0.02f), red, keepCollider: false);
            Primitive(PrimitiveType.Cube, "CrossH", hotel, new Vector3(8.5f, 2.4f, -5.84f), new Vector3(0.9f, 0.3f, 0.02f), red, keepCollider: false);
            WorldText(hotel, "InfirmarySign", new Vector3(8.5f, 1.75f, -5.83f), "INFIRMARY", 70, 0.03f, red.color).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            bed.AddComponent<NetworkObject>();
            var bedLogic = bed.AddComponent<HospitalBed>();
            var patient = new GameObject("PatientPoint").transform;
            patient.SetParent(hotel, false);
            patient.localPosition = new Vector3(8.5f, 1f, -4f);
            patient.localRotation = Quaternion.LookRotation(Vector3.right); // head toward the pillow (+x)
            SetRef(bedLogic, "_patientPoint", patient);
            ConfigureInteractable(bed.AddComponent<Interactable>(), new[] { bed.GetComponent<Collider>() }, new[] { bed.GetComponent<Renderer>() }, 3f);
            infirmary = patient;

            // First-aid box and shelf, right of the door (the defibrillator is put here).
            Primitive(PrimitiveType.Cube, "FirstAidBox", hotel, new Vector3(3.2f, 2f, d * 0.5f - 0.25f), new Vector3(0.7f, 0.5f, 0.2f), white, keepCollider: false);
            Primitive(PrimitiveType.Cube, "FirstAidCross", hotel, new Vector3(3.2f, 2f, d * 0.5f - 0.36f), new Vector3(0.12f, 0.36f, 0.02f), red, keepCollider: false);
            Primitive(PrimitiveType.Cube, "FirstAidCross2", hotel, new Vector3(3.2f, 2f, d * 0.5f - 0.36f), new Vector3(0.36f, 0.12f, 0.02f), red, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Shelf", hotel, new Vector3(3.2f, 1.1f, d * 0.5f - 0.4f), new Vector3(0.9f, 0.06f, 0.5f), wood);
            firstAid = new GameObject("FirstAidShelf").transform;
            firstAid.SetParent(hotel, false);
            firstAid.localPosition = new Vector3(3.2f, 1.35f, d * 0.5f - 0.4f);

            door = new GameObject("HotelDoor").transform;
            door.SetParent(hotel, false);
            door.localPosition = new Vector3(0f, 0.3f, d * 0.5f + 2f);

            // A lamp so the lobby isn't a cave.
            var lamp = new GameObject("LobbyLight").AddComponent<Light>();
            lamp.transform.SetParent(hotel, false);
            lamp.transform.localPosition = new Vector3(0f, 3f, 0f);
            lamp.type = LightType.Point;
            lamp.range = 16f;
            lamp.intensity = 1.6f;
            lamp.color = new Color(1f, 0.92f, 0.8f);
            return hotel;
        }

        /// <summary>The robber's jet ski: hull, seat, handlebars; driven with W/S/A/D (keys needed).</summary>
        private static Vehicle BuildJetSki(Transform env, Vector3 position, float yaw, string key = "Jet Ski Keys", string displayName = "Jet Ski")
        {
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material dark = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));
            var root = new GameObject("JetSki");
            root.transform.SetParent(env, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 260f;
            body.linearDamping = 0.1f;
            body.angularDamping = 1.2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.95f, 0.45f, 2.7f), red);
            Primitive(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0f, 0.28f, 1.35f), new Vector3(0.8f, 0.3f, 0.7f), red, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Deck", root.transform, new Vector3(0f, 0.42f, -0.25f), new Vector3(0.82f, 0.1f, 1.7f), white, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Seat", root.transform, new Vector3(0f, 0.55f, -0.4f), new Vector3(0.42f, 0.18f, 1f), dark, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Column", root.transform, new Vector3(0f, 0.62f, 0.45f), new Vector3(0.12f, 0.4f, 0.12f), dark, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Handlebar", root.transform, new Vector3(0f, 0.82f, 0.55f), new Vector3(0.72f, 0.05f, 0.05f), dark, keepCollider: false);
            Transform P(string n, Vector3 p, Quaternion r)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                tr.localRotation = r;
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, 0.7f, -0.25f), Quaternion.identity);
            Transform gripL = P("GripLeft", new Vector3(-0.3f, 0.86f, 0.5f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
            Transform gripR = P("GripRight", new Vector3(0.3f, 0.86f, 0.5f), Quaternion.LookRotation(Vector3.forward, Vector3.up));
            Transform thrust = P("Thrust", new Vector3(0f, -0.05f, -1.35f), Quaternion.identity);
            DressJetSki(root);
            return FinishVehicle(root, displayName, key, seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.32f,
                thrustForce: 11f, maxSpeed: 17f, turnRate: 1.7f);
        }

        /// <summary>The pirates' boat (theirs until chapter 2.7): hull, deck, cabin, wheel, mast and a black flag.</summary>
        private static Vehicle BuildPirateBoat(Transform env, Vector3 position, float yaw)
        {
            Material hullMat = GetMaterial("PirateHull", new Color(0.3f, 0.2f, 0.13f));
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material black = GetMaterial("PhoneBlack", new Color(0.08f, 0.08f, 0.1f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            var root = new GameObject("PirateBoat");
            root.transform.SetParent(env, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 1500f;
            body.linearDamping = 0.1f;
            body.angularDamping = 1.5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(2.6f, 0.9f, 7f), hullMat);
            Primitive(PrimitiveType.Cube, "Bow", root.transform, new Vector3(0f, 0.45f, 3.7f), new Vector3(1.8f, 0.7f, 1.2f), hullMat, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            TagSurface(Primitive(PrimitiveType.Cube, "Deck", root.transform, new Vector3(0f, 0.8f, 0f), new Vector3(2.3f, 0.1f, 6.4f), wood), SurfaceKind.Wood);
            Primitive(PrimitiveType.Cube, "Cabin", root.transform, new Vector3(0f, 1.45f, -2.3f), new Vector3(1.6f, 1.2f, 1.6f), hullMat);
            Primitive(PrimitiveType.Cylinder, "Wheel", root.transform, new Vector3(0f, 1.55f, -1.2f), new Vector3(0.6f, 0.03f, 0.6f), wood, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Mast", root.transform, new Vector3(0f, 2.8f, 0.9f), new Vector3(0.15f, 4f, 0.15f), wood, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Flag", root.transform, new Vector3(0.65f, 4.3f, 0.9f), new Vector3(1.2f, 0.8f, 0.03f), black, keepCollider: false);
            Primitive(PrimitiveType.Sphere, "Skull", root.transform, new Vector3(0.65f, 4.35f, 0.88f), new Vector3(0.28f, 0.28f, 0.02f), white, keepCollider: false);
            Transform P(string n, Vector3 p, Quaternion r)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                tr.localRotation = r;
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, 1.35f, -1.9f), Quaternion.identity);
            Transform gripL = P("GripLeft", new Vector3(-0.2f, 1.62f, -1.12f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform gripR = P("GripRight", new Vector3(0.2f, 1.62f, -1.12f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform thrust = P("Thrust", new Vector3(0f, -0.1f, -3.5f), Quaternion.identity);
            // The model's floor sits low in its tubes: float higher so the water stays outside.
            float density = DressPirateBoat(root) ? 0.17f : 0.3f;
            return FinishVehicle(root, "Pirate Boat", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), density,
                thrustForce: 6f, maxSpeed: 11f, turnRate: 0.9f);
        }

        private static Vehicle FinishVehicle(GameObject root, string displayName, string key, Transform seat, Transform gripL, Transform gripR, Transform thrust,
            Collider hull, float density, float thrustForce, float maxSpeed, float turnRate)
        {
            root.AddComponent<NetworkObject>();
            var buoyancy = new SerializedObject(root.AddComponent<Buoyancy>());
            Require(buoyancy, "_density").floatValue = density;
            Require(buoyancy, "_waterDrag").floatValue = 0.5f;
            Require(buoyancy, "_waterAngularDrag").floatValue = 2.5f;
            buoyancy.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<SurfaceCrossing>();
            root.AddComponent<ItemSync>();
            var vehicle = root.AddComponent<Vehicle>();
            var so = new SerializedObject(vehicle);
            Require(so, "_displayName").stringValue = displayName;
            Require(so, "_keyItem").stringValue = key;
            Require(so, "_seat").objectReferenceValue = seat;
            Require(so, "_gripLeft").objectReferenceValue = gripL;
            Require(so, "_gripRight").objectReferenceValue = gripR;
            Require(so, "_thrustPoint").objectReferenceValue = thrust;
            Require(so, "_thrust").floatValue = thrustForce;
            Require(so, "_maxSpeed").floatValue = maxSpeed;
            Require(so, "_turnRate").floatValue = turnRate;
            Require(so, "_engineAudio").objectReferenceValue = SpatialAudio(root, 3f, 80f);
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(root.AddComponent<Interactable>(), new[] { hull }, root.GetComponentsInChildren<Renderer>(), 3.5f);
            // Parked (in the water or run up on the beach): story characters route around it.
            var box = (BoxCollider)hull;
            AddNavCarver(root, box.transform.localPosition, Vector3.Scale(box.size, box.transform.localScale) + new Vector3(0.3f, 0f, 0.3f));
            return vehicle;
        }

        /// <summary>
        /// A navmesh obstacle that cuts its footprint out of the navmesh while it stands still, so story characters
        /// route around it. Tall (down to the seabed) so a floating one carves the swimmers' navmesh too.
        /// </summary>
        public static void AddNavCarver(GameObject go, Vector3 center, Vector3 footprint)
        {
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(center.x, center.y - 3f, center.z);
            obstacle.size = new Vector3(footprint.x, 8f, footprint.z);
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingTimeToStationary = 0.5f;
            obstacle.carvingMoveThreshold = 0.3f;
        }
    }
}
