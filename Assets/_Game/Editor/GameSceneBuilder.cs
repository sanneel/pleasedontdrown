using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Client;
using FishNet.Managing.Server;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Net;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
using SteamTransport = FishySteamworks.FishySteamworks;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Generates the prototype Game scene and Player prefab from code, so the setup is reproducible and reviewable.
    /// Menu: PLEASE DON'T DROWN > Rebuild Game scene.
    /// Batch: -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.BuildBatch (or BuildPlayerBatch for a Windows build).
    /// NOTE: rebuilding overwrites manual edits to Game.unity. Once the level is hand-authored this becomes a prefab/tool builder only.
    /// </summary>
    public static class GameSceneBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/Game.unity";
        private const string PlayerPrefabPath = "Assets/_Game/Player/Prefabs/Player.prefab";
        private const string MaterialDir = "Assets/_Game/Data/Materials";
        private const string PhysicsDir = "Assets/_Game/Data/Physics";
        private const string MeshDir = "Assets/_Game/Data/Meshes";
        private const string ItemPrefabDir = "Assets/_Game/Items/Prefabs";
        private const string ItemCatalogPath = "Assets/_Game/Data/ItemCatalog.asset";
        private const string OutlineShaderPath = "Assets/_Game/Data/Shaders/Outline.shader";
        private const string OutlineLayerName = "Outlined";
        private const string OutlineFeatureName = "HoverOutline";
        private const string BuildExe = "Builds/Win64/PleaseDontDrown.exe";

        private static Font BuiltinFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [MenuItem("PLEASE DON'T DROWN/Rebuild Game scene")]
        private static void BuildFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild Game scene", $"This overwrites {ScenePath} and the Player prefab.", "Rebuild", "Cancel"))
                Build();
        }

        public static void BuildBatch() => RunBatch(Build, "BUILD");

        public static void BuildPlayerBatch() => RunBatch(() =>
        {
            Build();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildExe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception($"Player build {report.summary.result} with {report.summary.totalErrors} errors");
            // Steam needs the app id next to the exe when not launched through Steam.
            File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(BuildExe)!, "steam_appid.txt"), true);
            Debug.Log($"[Build] Player: {BuildExe} ({report.summary.totalSize / (1024 * 1024)} MB)");
        }, "PLAYER BUILD");

        private static void RunBatch(Action action, string label)
        {
            try
            {
                action();
                Debug.Log($"[Build] {label} OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build] {label} FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            ConfigureProject();
            int outlineLayer = EnsureLayer(OutlineLayerName);
            SetupOutlineRendering(outlineLayer);
            NetworkObject player = BuildPlayerPrefab();
            ItemCatalog catalog = BuildItems();
            RefreshFishNetPrefabs();
            BuildScene(player, catalog);
            AssetDatabase.SaveAssets();
        }

        // =====================================================================
        // Project settings
        // =====================================================================

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "PleaseDontDrown";   // decides the save folder under AppData/LocalLow
            PlayerSettings.productName = "PLEASE DON'T DROWN";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.runInBackground = true;          // two local instances must both keep ticking
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            // 1 = Input System package only.
            Object projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (projectSettings != null)
            {
                var so = new SerializedObject(projectSettings);
                SerializedProperty input = so.FindProperty("activeInputHandler");
                if (input != null && input.intValue != 1)
                {
                    input.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;
                slot.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            throw new InvalidOperationException($"No free user layer for '{layerName}'.");
        }

        // =====================================================================
        // Hover outline: RenderObjects pass that redraws the "Outlined" layer with an inverted-hull shader
        // =====================================================================

        private static void SetupOutlineRendering(int layer)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(OutlineShaderPath);
            if (shader == null) throw new FileNotFoundException("Outline shader missing", OutlineShaderPath);

            Material outline = LoadOrCreateMaterial("Outline", shader);
            outline.SetColor("_OutlineColor", new Color(1f, 0.86f, 0.25f));
            outline.SetFloat("_OutlineWidth", 0.0035f);
            EditorUtility.SetDirty(outline);

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null) EnsureOutlineFeature(data, layer, outline);
            }
        }

        private static void EnsureOutlineFeature(UniversalRendererData data, int layer, Material material)
        {
            var feature = data.rendererFeatures.OfType<RenderObjects>().FirstOrDefault(f => f.name == OutlineFeatureName);
            bool isNew = feature == null;
            if (isNew)
            {
                feature = ScriptableObject.CreateInstance<RenderObjects>();
                feature.name = OutlineFeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
            }

            feature.settings.passTag = OutlineFeatureName;
            feature.settings.Event = RenderPassEvent.AfterRenderingOpaques;
            feature.settings.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            feature.settings.filterSettings.LayerMask = 1 << layer;
            feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            feature.settings.overrideMaterial = material;
            feature.settings.overrideMaterialPassIndex = 0;
            EditorUtility.SetDirty(feature);

            if (isNew)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var so = new SerializedObject(data);
                SerializedProperty features = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }

        // =====================================================================
        // Player prefab
        // =====================================================================

        private static NetworkObject BuildPlayerPrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath)!);

            var root = new GameObject("Player");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.35f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;

            root.AddComponent<NetworkObject>();
            var rootSync = root.AddComponent<NetworkTransform>();   // position + yaw, client authoritative
            rootSync.SetSynchronizeScale(false);

            GameObject body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f),
                GetMaterial("Player_Body", Color.white), keepCollider: false);

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.65f, 0f);
            var headSync = head.gameObject.AddComponent<NetworkTransform>(); // local height (crouch) + pitch
            headSync.SetSynchronizeScale(false);
            GameObject visor = Primitive(PrimitiveType.Cube, "Visor", head, new Vector3(0f, 0.02f, 0.3f), new Vector3(0.5f, 0.14f, 0.14f),
                GetMaterial("Player_Visor", new Color(0.08f, 0.08f, 0.1f)), keepCollider: false);
            GameObject cap = Primitive(PrimitiveType.Cube, "Cap", head, new Vector3(0f, 0.14f, 0.1f), new Vector3(0.64f, 0.08f, 0.74f),
                GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f)), keepCollider: false);

            TextMesh nameTag = WorldText(root.transform, "NameTag", new Vector3(0f, 2.2f, 0f), "Lifeguard", 64, 0.045f, Color.white);
            nameTag.gameObject.SetActive(false);

            // Owner-only systems are saved DISABLED so they never run (or register commands) on remote copies.
            var motor = root.AddComponent<PlayerMotor>();
            var look = root.AddComponent<PlayerLook>();
            var interactor = root.AddComponent<PlayerInteractor>();
            var hands = root.AddComponent<PlayerHands>(); // runs on every machine (places held items)
            var hub = root.AddComponent<PlayerHub>();
            motor.enabled = look.enabled = interactor.enabled = false;
            SetRef(hands, "_hub", hub);
            SetRef(hands, "_head", head);
            SetRef(hub, "_hands", hands);

            SetRef(motor, "_head", head);
            SetRef(look, "_head", head);
            SetRef(look, "_motor", motor);
            SetRef(interactor, "_hub", hub);
            SetRef(hub, "_motor", motor);
            SetRef(hub, "_look", look);
            SetRef(hub, "_interactor", interactor);
            SetRef(hub, "_head", head);
            SetRef(hub, "_body", body.transform);
            SetRef(hub, "_bodyRenderer", body.GetComponent<Renderer>());
            SetRef(hub, "_nameTag", nameTag);
            SetRefs(hub, "_selfHiddenRenderers", body.GetComponent<Renderer>(), visor.GetComponent<Renderer>(), cap.GetComponent<Renderer>());

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return saved.GetComponent<NetworkObject>();
        }

        // =====================================================================
        // Scene
        // =====================================================================

        // =====================================================================
        // Items
        // =====================================================================

        private static ItemCatalog BuildItems()
        {
            Directory.CreateDirectory(ItemPrefabDir);
            PhysicsMaterial bouncy = GetPhysicsMaterial("Bouncy", 0.78f, 0.35f, PhysicsMaterialCombine.Maximum);
            PhysicsMaterial rubber = GetPhysicsMaterial("Rubber", 0.3f, 0.8f, PhysicsMaterialCombine.Average);
            PhysicsMaterial wood = GetPhysicsMaterial("WoodPhysics", 0.1f, 0.6f, PhysicsMaterialCombine.Average);

            Material crateWood = GetMaterial("CrateWood", new Color(0.72f, 0.52f, 0.3f));
            Material crateBand = GetMaterial("CrateBand", new Color(0.45f, 0.3f, 0.17f));
            Material white = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material yellow = GetMaterial("BallYellow", new Color(1f, 0.83f, 0.2f));
            Material blue = GetMaterial("CoolerBlue", new Color(0.18f, 0.45f, 0.85f));
            Material dark = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));

            Item crate = BuildItem("Crate", "Crate", 8f, new Vector3(0f, -0.55f, 1.05f), Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Box", root, Vector3.zero, Vector3.one * 0.6f, crateWood);
                Primitive(PrimitiveType.Cube, "BandTop", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.62f, 0.07f, 0.62f), crateBand, keepCollider: false);
                Primitive(PrimitiveType.Cube, "BandBottom", root, new Vector3(0f, -0.2f, 0f), new Vector3(0.62f, 0.07f, 0.62f), crateBand, keepCollider: false);
            });

            Item ball = BuildItem("BeachBall", "Beach Ball", 0.4f, new Vector3(0.32f, -0.36f, 0.85f), Vector3.zero, 1f, bouncy, root =>
            {
                Primitive(PrimitiveType.Sphere, "Ball", root, Vector3.zero, Vector3.one * 0.55f, red);
                Primitive(PrimitiveType.Cylinder, "Band", root, Vector3.zero, new Vector3(0.56f, 0.06f, 0.56f), white, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Band2", root, Vector3.zero, new Vector3(0.56f, 0.06f, 0.56f), yellow, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }, linearDamping: 0.5f, angularDamping: 0.9f); // rolls ~10-15 m after a sprint kick instead of forever

            Mesh torus = GetTorusMesh("Torus", 0.28f, 0.075f);
            Item ring = BuildItem("LifeRing", "Life Ring", 1.2f, new Vector3(0.45f, -0.45f, 1.0f), new Vector3(70f, -30f, 0f), 1.1f, rubber, root =>
            {
                var go = new GameObject("Ring");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = torus;
                go.AddComponent<MeshRenderer>().sharedMaterial = red;
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = torus;
                col.convex = true;
                for (int i = 0; i < 4; i++)
                {
                    float angle = 45f + i * 90f;
                    float rad = angle * Mathf.Deg2Rad;
                    Primitive(PrimitiveType.Cube, "Tape", root, new Vector3(Mathf.Cos(rad) * 0.28f, 0f, Mathf.Sin(rad) * 0.28f),
                        new Vector3(0.165f, 0.165f, 0.06f), white, keepCollider: false).transform.localRotation = Quaternion.Euler(0f, -angle, 0f);
                }
            }, linearDamping: 0.1f, angularDamping: 0.2f);

            Item cooler = BuildItem("Cooler", "Cooler", 4f, new Vector3(0.05f, -0.58f, 0.95f), Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Body", root, Vector3.zero, new Vector3(0.55f, 0.36f, 0.36f), blue);
                Primitive(PrimitiveType.Cube, "Lid", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.57f, 0.07f, 0.38f), white, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Handle", root, new Vector3(0f, 0.25f, 0f), new Vector3(0.3f, 0.04f, 0.05f), dark, keepCollider: false);
            });

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, ItemCatalogPath);
            }
            SetRefs(catalog, "_items", crate, ball, ring, cooler);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static Item BuildItem(string file, string displayName, float mass, Vector3 holdOffset, Vector3 holdEuler, float throwStrength,
            PhysicsMaterial physics, Action<Transform> buildVisual, float linearDamping = 0.05f, float angularDamping = 0.1f)
        {
            var root = new GameObject(file);
            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = linearDamping;
            body.angularDamping = angularDamping;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            buildVisual(root.transform);
            foreach (Collider c in root.GetComponentsInChildren<Collider>())
                c.sharedMaterial = physics;

            var nob = root.AddComponent<NetworkObject>();
            var nobSo = new SerializedObject(nob);
            Require(nobSo, "_preventDespawnOnDisconnect").boolValue = true; // items outlive the player who last touched them
            nobSo.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<ItemSync>();
            var item = root.AddComponent<Item>();
            var itemSo = new SerializedObject(item);
            Require(itemSo, "_displayName").stringValue = displayName;
            Require(itemSo, "_holdOffset").vector3Value = holdOffset;
            Require(itemSo, "_holdEuler").vector3Value = holdEuler;
            Require(itemSo, "_throwStrength").floatValue = throwStrength;
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            var interactable = root.AddComponent<Interactable>(); // colliders + renderers: all children
            var interactableSo = new SerializedObject(interactable);
            Require(interactableSo, "_maxDistance").floatValue = 3f;
            interactableSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{ItemPrefabDir}/{file}.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<Item>();
        }

        private static void PlaceItems(ItemCatalog catalog)
        {
            var parent = new GameObject("Items").transform;
            void Place(string itemName, Vector3 position, float yaw = 0f)
            {
                Item prefab = catalog.Find(itemName);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent.gameObject.scene);
                instance.transform.SetParent(parent, true);
                instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            }

            Place("Crate", new Vector3(-3.8f, 0.3f, 9.2f), 8f);
            Place("Crate", new Vector3(-3.8f, 0.92f, 9.2f), 20f);
            Place("Crate", new Vector3(-4.6f, 0.3f, 9.8f), -12f);
            Place("Beach Ball", new Vector3(4.5f, 0.3f, 14f));
            Place("Life Ring", new Vector3(1f, 1.4f, 11.3f));
            Place("Life Ring", new Vector3(11f, 0.1f, 6.5f), 30f);
            Place("Cooler", new Vector3(-1.9f, 0.6f, 10.2f), 15f);
        }

        // =====================================================================
        // Scene
        // =====================================================================

        private static void BuildScene(NetworkObject playerPrefab, ItemCatalog catalog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera menuCam = Object.FindFirstObjectByType<Camera>();
            menuCam.name = "MenuCamera";
            menuCam.transform.SetPositionAndRotation(new Vector3(-14f, 9f, -12f), Quaternion.Euler(22f, 40f, 0f));
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            SetRef(new GameObject("SceneCameras").AddComponent<SceneCameras>(), "_menuCamera", menuCam);

            Light sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = new Color(1f, 0.96f, 0.88f);

            Transform env = new GameObject("Environment").transform;
            BuildBeach(env);
            BuildStation(env);
            PlaceItems(catalog);
            Transform[] spawns = BuildSpawnPoints();

            new GameObject("Steam").AddComponent<SteamBootstrap>();
            SetRef(new GameObject("GameContent").AddComponent<GameContent>(), "_items", catalog);
            var ui = new GameObject("UI");
            ui.AddComponent<PlayerHud>();
            ui.AddComponent<DevConsole>();
            ui.AddComponent<DevTools>();
            ui.AddComponent<ItemDebugView>();
            BuildNetworkManager(playerPrefab, spawns);

            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void BuildBeach(Transform env)
        {
            Primitive(PrimitiveType.Cube, "Sand", env, new Vector3(0f, -0.5f, 15f), new Vector3(80f, 1f, 30f), GetMaterial("Sand", new Color(0.93f, 0.84f, 0.62f)));
            // Seabed under everything, so anything thrown past the sand still lands somewhere.
            Primitive(PrimitiveType.Cube, "Seabed", env, new Vector3(0f, -3f, -20f), new Vector3(300f, 1f, 300f), GetMaterial("Seabed", new Color(0.62f, 0.56f, 0.42f)));
            // Water: visual only until M3 (no collider, so you can wade down to the seabed).
            GameObject water = Primitive(PrimitiveType.Plane, "Water", env, new Vector3(0f, -0.35f, -60f), new Vector3(30f, 1f, 12f),
                GetMaterial("Water", new Color(0.15f, 0.55f, 0.75f)), keepCollider: false);
            water.transform.position = new Vector3(0f, -0.35f, -60f);
        }

        private static void BuildStation(Transform env)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material brass = GetMaterial("Brass", new Color(0.85f, 0.65f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            Material dark = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));

            // The saddest lifeguard shack imaginable.
            var shack = new GameObject("Station_Shack").transform;
            shack.SetParent(env, false);
            shack.position = new Vector3(0f, 0f, 10f);
            Primitive(PrimitiveType.Cube, "Floor", shack, new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.4f, 4f), wood);
            foreach (Vector3 p in new[] { new Vector3(-2.3f, 1.6f, -1.8f), new Vector3(2.3f, 1.6f, -1.8f), new Vector3(-2.3f, 1.4f, 1.8f), new Vector3(2.3f, 1.4f, 1.8f) })
                Primitive(PrimitiveType.Cube, "Post", shack, p, new Vector3(0.2f, 2.6f, 0.2f), wood);
            Primitive(PrimitiveType.Cube, "Roof", shack, new Vector3(0f, 2.9f, 0f), new Vector3(5.4f, 0.15f, 4.4f), red).transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Counter", shack, new Vector3(0f, 0.85f, 1.3f), new Vector3(3.2f, 0.9f, 0.6f), wood);

            TextMesh roofSign = WorldText(shack, "RoofSign", new Vector3(0f, 3.4f, -2.2f), "LIFEGUARD (probably)", 80, 0.06f, red.color);
            roofSign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Shack light: bulb + point light, switched from a box on the front-left post.
            GameObject bulb = Primitive(PrimitiveType.Sphere, "Bulb", shack, new Vector3(0f, 2.55f, 0f), Vector3.one * 0.22f,
                GetMaterial("Bulb", new Color(1f, 0.95f, 0.85f), emission: new Color(1f, 0.78f, 0.45f) * 3f), keepCollider: false);
            var lamp = new GameObject("LampLight").AddComponent<Light>();
            lamp.transform.SetParent(bulb.transform, false);
            lamp.type = LightType.Point;
            lamp.range = 8f;
            lamp.intensity = 2.5f;
            lamp.color = new Color(1f, 0.82f, 0.6f);

            var lightSwitch = new GameObject("LightSwitch");
            lightSwitch.transform.SetParent(shack, false);
            lightSwitch.transform.localPosition = new Vector3(-2.3f, 1.35f, -1.66f);
            GameObject plate = Primitive(PrimitiveType.Cube, "Plate", lightSwitch.transform, Vector3.zero, new Vector3(0.16f, 0.24f, 0.06f), dark);
            lightSwitch.AddComponent<NetworkObject>();
            var toggle = lightSwitch.AddComponent<ToggleLight>();
            SetRefs(toggle, "_lights", lamp);
            SetRefs(toggle, "_bulbs", bulb.GetComponent<Renderer>());
            SetRef(toggle, "_audio", SpatialAudio(lightSwitch, 2f, 20f));
            ConfigureInteractable(lightSwitch.AddComponent<Interactable>(), new[] { plate.GetComponent<Collider>() }, new[] { plate.GetComponent<Renderer>() }, 2.8f);

            // Alarm bell on a post next to the shack.
            var bellRoot = new GameObject("AlarmBell");
            bellRoot.transform.SetParent(shack, false);
            bellRoot.transform.localPosition = new Vector3(3.3f, 0f, -2.4f);
            Primitive(PrimitiveType.Cube, "Post", bellRoot.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.12f, 2.3f, 0.12f), wood);
            Primitive(PrimitiveType.Cube, "Arm", bellRoot.transform, new Vector3(-0.22f, 2.25f, 0f), new Vector3(0.5f, 0.06f, 0.06f), dark);
            var pivot = new GameObject("SwingPivot").transform;
            pivot.SetParent(bellRoot.transform, false);
            pivot.localPosition = new Vector3(-0.42f, 2.22f, 0f);
            GameObject bell = Primitive(PrimitiveType.Cylinder, "Bell", pivot, new Vector3(0f, -0.16f, 0f), new Vector3(0.28f, 0.16f, 0.28f), brass);
            GameObject clapper = Primitive(PrimitiveType.Sphere, "Clapper", pivot, new Vector3(0f, -0.33f, 0f), Vector3.one * 0.08f, dark, keepCollider: false);
            bellRoot.AddComponent<NetworkObject>();
            var stationBell = bellRoot.AddComponent<StationBell>();
            SetRef(stationBell, "_swingPivot", pivot);
            SetRef(stationBell, "_audio", SpatialAudio(bellRoot, 4f, 90f));
            ConfigureInteractable(bellRoot.AddComponent<Interactable>(), new[] { bell.GetComponent<Collider>() },
                new[] { bell.GetComponent<Renderer>(), clapper.GetComponent<Renderer>() }, 3f);

            // Sign by the path from the spawn area.
            var sign = new GameObject("WelcomeSign");
            sign.transform.SetParent(env, false);
            sign.transform.position = new Vector3(-5f, 0f, 12.5f);
            sign.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Primitive(PrimitiveType.Cube, "Post", sign.transform, new Vector3(0f, 0.8f, 0f), new Vector3(0.12f, 1.6f, 0.12f), wood);
            GameObject board = Primitive(PrimitiveType.Cube, "Board", sign.transform, new Vector3(0f, 1.75f, 0f), new Vector3(1.9f, 0.9f, 0.08f), GetMaterial("SignBoard", new Color(0.95f, 0.93f, 0.85f)));
            WorldText(sign.transform, "Text", new Vector3(0f, 1.75f, -0.05f), "PLEASE DON'T DROWN\nLifeguard Station", 72, 0.028f, red.color);
            var readable = sign.AddComponent<ReadableSign>();
            var readableSo = new SerializedObject(readable);
            SerializedProperty lines = Require(readableSo, "_lines");
            string[] text =
            {
                "PLEASE DON'T DROWN Lifeguard Station. Est. yesterday.",
                "Days since last incident: 0",
                "Equipment: 1 rusty board, 2 life rings, 0 idea what we're doing.",
                "Management is not responsible for sharks, octopuses or Kevin."
            };
            lines.arraySize = text.Length;
            for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
            readableSo.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(sign.AddComponent<Interactable>(), new[] { board.GetComponent<Collider>() }, new[] { board.GetComponent<Renderer>() }, 3f);

            // Lifeguard tower.
            var tower = new GameObject("Tower").transform;
            tower.SetParent(env, false);
            tower.position = new Vector3(12f, 0f, 4f);
            foreach (Vector3 p in new[] { new Vector3(-0.8f, 1.5f, -0.8f), new Vector3(0.8f, 1.5f, -0.8f), new Vector3(-0.8f, 1.5f, 0.8f), new Vector3(0.8f, 1.5f, 0.8f) })
                Primitive(PrimitiveType.Cube, "Leg", tower, p, new Vector3(0.15f, 3f, 0.15f), wood);
            Primitive(PrimitiveType.Cube, "Platform", tower, new Vector3(0f, 3.1f, 0f), new Vector3(2.2f, 0.2f, 2.2f), wood);
            Primitive(PrimitiveType.Cube, "Ramp", tower, new Vector3(0f, 1.5f, 2.6f), new Vector3(1f, 0.1f, 4f), wood).transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
        }

        private static Transform[] BuildSpawnPoints()
        {
            var parent = new GameObject("SpawnPoints").transform;
            return Enumerable.Range(0, 4).Select(i =>
            {
                var t = new GameObject($"Spawn_{i}").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(-3f + i * 2f, 0.1f, 15f);
                t.rotation = Quaternion.Euler(0f, 180f, 0f);
                return t;
            }).ToArray();
        }

        private static void BuildNetworkManager(NetworkObject playerPrefab, Transform[] spawns)
        {
            var go = new GameObject("NetworkManager");
            var networkManager = go.AddComponent<NetworkManager>();
            var transportManager = GetOrAdd<TransportManager>(go);
            var serverManager = GetOrAdd<ServerManager>(go);
            GetOrAdd<ClientManager>(go);

            var multipass = go.AddComponent<Multipass>();
            var tugboat = go.AddComponent<Tugboat>();
            var steam = go.AddComponent<SteamTransport>();
            transportManager.Transport = multipass;
            EditorUtility.SetDirty(transportManager);
            SetRefs(multipass, "_transports", tugboat, steam);

            var steamSo = new SerializedObject(steam);
            Require(steamSo, "_peerToPeer").boolValue = true;
            steamSo.ApplyModifiedPropertiesWithoutUndo();

            SetRef(serverManager, "_authenticator", go.AddComponent<LobbyAuthenticator>());

            var spawner = go.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            spawner.Spawns = spawns;
            EditorUtility.SetDirty(spawner);

            var lobby = go.AddComponent<SteamLobbyService>();
            var connection = go.AddComponent<ConnectionService>();
            SetRef(connection, "_networkManager", networkManager);
            SetRef(connection, "_multipass", multipass);
            SetRef(connection, "_lobby", lobby);
            SetRef(go.AddComponent<DevConnectMenu>(), "_connection", connection);
            SetRef(go.AddComponent<DevLaunchArgs>(), "_connection", connection);

            // Point the NetworkManager at FishNet's generated prefab collection.
            string guid = AssetDatabase.FindAssets("t:DefaultPrefabObjects").FirstOrDefault();
            if (guid == null)
            {
                Debug.LogWarning("[Build] DefaultPrefabObjects asset not found yet.");
                return;
            }
            var nmSo = new SerializedObject(networkManager);
            SerializedProperty prop = nmSo.FindProperty("_spawnablePrefabs") ?? nmSo.FindProperty("SpawnablePrefabs");
            if (prop == null)
            {
                Debug.LogWarning("[Build] NetworkManager spawnable prefabs field not found; FishNet assigns it on play.");
                return;
            }
            prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
            nmSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // =====================================================================
        // FishNet helpers
        // =====================================================================

        private static void RefreshFishNetPrefabs()
        {
            MethodInfo full = FindFishNetType("FishNet.Editing.PrefabCollectionGenerator.Generator")
                ?.GetMethod("GenerateFull", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (full == null)
            {
                Debug.LogWarning("[Build] FishNet prefab generator not found; relying on automatic generation.");
                return;
            }
            full.Invoke(null, full.GetParameters().Select(p => p.Name == "forced" ? (object)true : p.DefaultValue).ToArray());
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Scene NetworkObjects need unique scene ids. FishNet makes them on validate with a throttle, which a
        /// script that creates several objects at once can outrun, so force a full pass before saving.
        /// </summary>
        private static void AssignSceneIds(UnityEngine.SceneManagement.Scene scene)
        {
            MethodInfo create = typeof(NetworkObject).GetMethod("CreateSceneId", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(UnityEngine.SceneManagement.Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
            if (create == null)
            {
                Debug.LogWarning("[Build] NetworkObject.CreateSceneId not found; scene ids rely on FishNet's automatic pass.");
                return;
            }
            object[] args = { scene, true, 0 };
            create.Invoke(null, args);
            Debug.Log($"[Build] Scene ids assigned ({args[2]} changed).");
        }

        private static Type FindFishNetType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        // =====================================================================
        // Generic helpers
        // =====================================================================

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static TextMesh WorldText(Transform parent, string name, Vector3 localPos, string text, int fontSize, float characterSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = BuiltinFont;
            go.GetComponent<MeshRenderer>().sharedMaterial = BuiltinFont.material;
            mesh.text = text;
            mesh.fontSize = fontSize;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            return mesh;
        }

        private static AudioSource SpatialAudio(GameObject host, float minDistance, float maxDistance)
        {
            var audio = host.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = minDistance;
            audio.maxDistance = maxDistance;
            return audio;
        }

        private static void ConfigureInteractable(Interactable interactable, Collider[] colliders, Renderer[] outline, float maxDistance)
        {
            SetRefs(interactable, "_colliders", colliders.Cast<Object>().ToArray());
            SetRefs(interactable, "_outlineRenderers", outline.Cast<Object>().ToArray());
            var so = new SerializedObject(interactable);
            Require(so, "_maxDistance").floatValue = maxDistance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material GetMaterial(string name, Color color, float metallic = 0f, float smoothness = 0.25f, Color? emission = null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            Material mat = LoadOrCreateMaterial(name, lit);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static PhysicsMaterial GetPhysicsMaterial(string name, float bounciness, float friction, PhysicsMaterialCombine bounceCombine)
        {
            Directory.CreateDirectory(PhysicsDir);
            string path = $"{PhysicsDir}/{name}.asset";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.bounciness = bounciness;
            mat.dynamicFriction = friction;
            mat.staticFriction = friction;
            mat.bounceCombine = bounceCombine;
            mat.frictionCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Torus in the XZ plane (hole along Y). Updated in place so references keep working across rebuilds.</summary>
        private static Mesh GetTorusMesh(string name, float radius, float tube, int radialSegments = 32, int tubeSegments = 14)
        {
            Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = name };
            mesh.Clear();

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= radialSegments; i++)
            {
                float u = i / (float)radialSegments * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(u) * radius, 0f, Mathf.Sin(u) * radius);
                for (int j = 0; j <= tubeSegments; j++)
                {
                    float v = j / (float)tubeSegments * Mathf.PI * 2f;
                    var normal = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                    vertices.Add(center + normal * tube);
                    normals.Add(normal);
                    uvs.Add(new Vector2(i / (float)radialSegments, j / (float)tubeSegments));
                }
            }
            int ring = tubeSegments + 1;
            for (int i = 0; i < radialSegments; i++)
            {
                for (int j = 0; j < tubeSegments; j++)
                {
                    int a = i * ring + j, b = (i + 1) * ring + j;
                    // Clockwise when seen from outside (Unity's front face).
                    triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        // Explicit checks: Unity's fake-null objects don't work with "??".
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static SerializedProperty Require(SerializedObject so, string name) =>
            so.FindProperty(name) ?? throw new MissingFieldException(so.targetObject.GetType().Name, name);

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            Require(so, field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetRefs(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty list = Require(so, field);
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
