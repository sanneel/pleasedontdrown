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
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Net;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
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
    public static partial class GameSceneBuilder
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
            WriteBuildStamp();
            int outlineLayer = EnsureLayer(OutlineLayerName);
            SetupOutlineRendering(outlineLayer);
            BuildPlayerPrefab();
            BuildItems();
            BuildStoryPrefabs();
            AssetDatabase.SaveAssets();
            RefreshFishNetPrefabs();
            BuildScene();
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

        /// <summary>
        /// New stamp on every rebuild of the networked content; see NetVersion. Builds and editors made from
        /// different generations refuse to connect to each other instead of failing with mismatched scene objects.
        /// </summary>
        private static void WriteBuildStamp()
        {
            const string path = "Assets/_Game/Resources/BuildStamp.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            AssetDatabase.ImportAsset(path);
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
            // Physics body (How to Fish style): a rigidbody capsule that shoves items and can be knocked around.
            var physicsBody = root.AddComponent<Rigidbody>();
            physicsBody.mass = 75f;
            physicsBody.linearDamping = 0f;
            physicsBody.angularDamping = 0f;
            physicsBody.freezeRotation = true;      // facing lives on the head, the body never rotates
            physicsBody.interpolation = RigidbodyInterpolation.Interpolate;
            physicsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            // No friction: the motor controls speed itself, and walls shouldn't grab you when you slide along them.
            capsule.sharedMaterial = GetPhysicsMaterial("PlayerBody", 0f, 0f, PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);

            root.AddComponent<NetworkObject>();
            var rootSync = root.AddComponent<NetworkTransform>();   // position only, client authoritative
            rootSync.SetSynchronizeRotation(false);
            rootSync.SetSynchronizeScale(false);

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.65f, 0f);
            var headSync = head.gameObject.AddComponent<NetworkTransform>(); // local height (crouch) + pitch
            headSync.SetSynchronizeScale(false);

            // The cartoon body (built at runtime from the player's look) and its procedural animator.
            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(root.transform, false);
            avatarGo.AddComponent<SkinnedMeshRenderer>();
            var rig = avatarGo.AddComponent<AvatarRig>();
            SetRef(rig, "_material", AvatarMaterial());
            var animator = avatarGo.AddComponent<AvatarAnimator>();
            SetRef(animator, "_rig", rig);
            SetRef(avatarGo.AddComponent<AvatarJiggle>(), "_rig", rig);

            TextMesh nameTag = WorldText(root.transform, "NameTag", new Vector3(0f, 2.2f, 0f), "Lifeguard", 64, 0.045f, Color.white, onTop: true);
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
            var splash = root.AddComponent<SurfaceCrossing>(); // feet hitting the water
            var splashSo = new SerializedObject(splash);
            Require(splashSo, "_localPoint").vector3Value = new Vector3(0f, 0.15f, 0f);
            Require(splashSo, "_minDownSpeed").floatValue = 3f;
            splashSo.ApplyModifiedPropertiesWithoutUndo();
            SetRef(hub, "_hands", hands);
            var vitals = root.AddComponent<PlayerVitals>(); // food meter (owner)
            SetRef(vitals, "_hub", hub);
            SetRef(hub, "_vitals", vitals);
            var avatarDriver = root.AddComponent<PlayerAvatar>(); // every machine: animates the body from synced state
            SetRef(avatarDriver, "_hub", hub);
            SetRef(avatarDriver, "_rig", rig);
            SetRef(avatarDriver, "_animator", animator);
            SetRef(hub, "_avatar", avatarDriver);

            var combat = root.AddComponent<Combat.PlayerCombat>(); // punches, getting knocked about
            SetRef(combat, "_hub", hub);
            SetRef(combat, "_audio", SpatialAudio(root, 2f, 30f));

            var steps = root.AddComponent<PlayerFootsteps>(); // everyone's footsteps, on every machine
            SetRef(steps, "_hub", hub);
            AudioSource stepAudio = SpatialAudio(root, 2f, 30f);
            stepAudio.volume = 0.8f;
            SetRef(steps, "_audio", stepAudio);

            SetRef(motor, "_head", head);
            SetRef(look, "_head", head);
            SetRef(look, "_motor", motor);
            SetRef(interactor, "_hub", hub);
            SetRef(hub, "_motor", motor);
            SetRef(hub, "_look", look);
            SetRef(hub, "_interactor", interactor);
            SetRef(hub, "_head", head);
            SetRef(hub, "_nameTag", nameTag);

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

            Item crate = BuildItem("Crate", "Crate", 8f, new Vector3(0f, -0.45f, 0.84f), Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Box", root, Vector3.zero, Vector3.one * 0.6f, crateWood);
                Primitive(PrimitiveType.Cube, "BandTop", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.62f, 0.07f, 0.62f), crateBand, keepCollider: false);
                Primitive(PrimitiveType.Cube, "BandBottom", root, new Vector3(0f, -0.2f, 0f), new Vector3(0.62f, 0.07f, 0.62f), crateBand, keepCollider: false);
            }, density: 0.55f, waterDrag: 1.4f, configure: go => AddNavCarver(go, Vector3.zero, new Vector3(0.75f, 0f, 0.75f)));

            Item ball = BuildItem("BeachBall", "Beach Ball", 0.4f, new Vector3(0.1f, -0.34f, 0.72f), Vector3.zero, 1f, bouncy, root =>
            {
                Primitive(PrimitiveType.Sphere, "Ball", root, Vector3.zero, Vector3.one * 0.55f, red);
                Primitive(PrimitiveType.Cylinder, "Band", root, Vector3.zero, new Vector3(0.56f, 0.06f, 0.56f), white, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Band2", root, Vector3.zero, new Vector3(0.56f, 0.06f, 0.56f), yellow, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }, linearDamping: 0.5f, angularDamping: 0.9f, density: 0.1f, waterDrag: 0.8f, // rolls ~10-15 m after a sprint kick instead of forever
                configure: go => SetBool(go.GetComponent<Item>(), "_pocketable", true));

            Mesh torus = GetTorusMesh("Torus", 0.28f, 0.075f);
            Item ring = BuildItem("LifeRing", "Life Ring", 1.2f, new Vector3(0.36f, -0.42f, 0.84f), new Vector3(70f, -30f, 0f), 1.1f, rubber, root =>
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
            }, linearDamping: 0.1f, angularDamping: 0.2f, density: 0.25f, waterDrag: 1.1f,
                configure: go =>
                {
                    go.AddComponent<Floatable>(); // tourists in the water grab it
                    SetEnum(go.GetComponent<Item>(), "_grip", (int)ItemGrip.OneHand);
                    SetBool(go.GetComponent<Item>(), "_pocketable", true);
                    // Right fist round the tube on the ring's outer edge (the edge that ends up at your right).
                    var grip = new GameObject("GripRight").transform;
                    grip.SetParent(go.transform, false);
                    grip.localPosition = new Vector3(0.28f + 0.075f + 0.018f, 0f, 0f);
                    grip.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.right); // fingers through the ring, palm on the tube
                    SetRef(go.GetComponent<Item>(), "_gripRight", grip);
                });

            Item cooler = BuildItem("Cooler", "Cooler", 4f, new Vector3(0.02f, -0.46f, 0.76f), Vector3.zero, 1f, wood, root =>
            {
                Primitive(PrimitiveType.Cube, "Body", root, Vector3.zero, new Vector3(0.55f, 0.36f, 0.36f), blue);
                Primitive(PrimitiveType.Cube, "Lid", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.57f, 0.07f, 0.38f), white, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Handle", root, new Vector3(0f, 0.25f, 0f), new Vector3(0.3f, 0.04f, 0.05f), dark, keepCollider: false);
            }, density: 0.4f, waterDrag: 1.2f, configure: go => AddNavCarver(go, Vector3.zero, new Vector3(0.7f, 0f, 0.5f)));

            Material husk = GetMaterial("Coconut", new Color(0.45f, 0.28f, 0.15f));
            Material huskDark = GetMaterial("CoconutDark", new Color(0.2f, 0.12f, 0.07f));
            Item coconut = BuildItem("Coconut", "Coconut", 1.1f, new Vector3(0.24f, -0.3f, 0.55f), new Vector3(-20f, 0f, 0f), 1f, wood, root =>
            {
                Primitive(PrimitiveType.Sphere, "Husk", root, Vector3.zero, new Vector3(0.2f, 0.23f, 0.2f), husk);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f * Mathf.Deg2Rad;
                    Primitive(PrimitiveType.Sphere, "Eye", root, new Vector3(Mathf.Cos(a) * 0.035f, 0.1f, Mathf.Sin(a) * 0.035f), Vector3.one * 0.03f, huskDark, keepCollider: false);
                }
            }, linearDamping: 0.1f, angularDamping: 0.4f, density: 0.55f, waterDrag: 1.0f, configure: go =>
            {
                Item item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                SetEnum(item, "_grip", (int)ItemGrip.OneHand);
                AudioSource audio = SpatialAudio(go, 1.5f, 25f);
                SetRef(go.AddComponent<Edible>(), "_audio", audio);
                SetRef(go.AddComponent<ImpactSound>(), "_audio", audio);
            });

            Item tourist = BuildTourist(torus);

            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, ItemCatalogPath);
            }
            var items = new List<Object> { crate, ball, ring, cooler, coconut, tourist };
            items.AddRange(BuildStoryItems(torus));
            SetRefs(catalog, "_items", items.ToArray());
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static Item BuildItem(string file, string displayName, float mass, Vector3 holdOffset, Vector3 holdEuler, float throwStrength,
            PhysicsMaterial physics, Action<Transform> buildVisual, float linearDamping = 0.05f, float angularDamping = 0.1f,
            float density = 0.5f, float waterDrag = 1.2f, Action<GameObject> configure = null)
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

            var buoyancy = root.AddComponent<Buoyancy>();
            var buoyancySo = new SerializedObject(buoyancy);
            Require(buoyancySo, "_density").floatValue = density;
            Require(buoyancySo, "_waterDrag").floatValue = waterDrag;
            buoyancySo.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<SurfaceCrossing>();

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
            configure?.Invoke(root);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{ItemPrefabDir}/{file}.prefab");
            Object.DestroyImmediate(root);
            return saved.GetComponent<Item>();
        }

        /// <summary>
        /// A tourist: torso body (the networked Item, with the head on it) plus four limb bodies that VictimBody joints
        /// on at runtime. Colours are neutral here; each instance is painted from its synced seed.
        /// </summary>
        private static Item BuildTourist(Mesh torus)
        {
            PhysicsMaterial flesh = GetPhysicsMaterial("Flesh", 0.05f, 0.7f, PhysicsMaterialCombine.Minimum);

            var root = new GameObject("Tourist");
            var body = root.AddComponent<Rigidbody>();
            body.mass = 30f;
            body.linearDamping = 0.1f;
            body.angularDamping = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var torsoCol = root.AddComponent<CapsuleCollider>();
            torsoCol.radius = 0.2f;
            torsoCol.height = 0.72f;
            torsoCol.sharedMaterial = flesh;
            var headCol = root.AddComponent<SphereCollider>();
            headCol.center = new Vector3(0f, 0.52f, 0f);
            headCol.radius = 0.15f;
            headCol.sharedMaterial = flesh;

            // Visuals without colliders under one transform (squished during CPR): a cartoon body built from the
            // tourist's synced seed at runtime; its arms and legs follow the physics limbs below.
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(visual, false);
            avatarGo.transform.localPosition = new Vector3(0f, -1.22f, 0f);
            avatarGo.AddComponent<SkinnedMeshRenderer>(); // exists up front so the hover outline finds it
            var rig = avatarGo.AddComponent<AvatarRig>();
            SetRef(rig, "_material", AvatarMaterial());
            var rigSo = new SerializedObject(rig);
            Require(rigSo, "_buildOnAwake").boolValue = false;
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            // Limbs: pivot at the shoulder / hip, hanging straight down (VictimBody's rest pose).
            void Limb(string limbName, Vector3 pivot, float mass, float length, float radius, bool arm)
            {
                var limb = new GameObject(limbName);
                limb.transform.SetParent(root.transform, false);
                limb.transform.localPosition = pivot;
                var rb = limb.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var col = limb.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, -length * 0.5f, 0f);
                col.radius = radius;
                col.height = length;
                col.sharedMaterial = flesh;
                var limbFloat = new SerializedObject(limb.AddComponent<Buoyancy>());
                Require(limbFloat, "_density").floatValue = 0.97f;
                Require(limbFloat, "_waterDrag").floatValue = 2.2f;
                limbFloat.ApplyModifiedPropertiesWithoutUndo();
            }
            Limb("ArmL", new Vector3(-0.27f, 0.26f, 0f), 3f, 0.6f, 0.06f, true);
            Limb("ArmR", new Vector3(0.27f, 0.26f, 0f), 3f, 0.6f, 0.06f, true);
            Limb("LegL", new Vector3(-0.1f, -0.33f, 0f), 6f, 0.8f, 0.075f, false);
            Limb("LegR", new Vector3(0.1f, -0.33f, 0f), 6f, 0.8f, 0.075f, false);

            var nob = root.AddComponent<NetworkObject>();
            var nobSo = new SerializedObject(nob);
            Require(nobSo, "_preventDespawnOnDisconnect").boolValue = true;
            nobSo.ApplyModifiedPropertiesWithoutUndo();

            var buoyancy = new SerializedObject(root.AddComponent<Buoyancy>());
            Require(buoyancy, "_density").floatValue = 0.95f;
            Require(buoyancy, "_waterDrag").floatValue = 1.8f;
            buoyancy.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<SurfaceCrossing>();
            root.AddComponent<ItemSync>();

            var item = root.AddComponent<Item>();
            var itemSo = new SerializedObject(item);
            Require(itemSo, "_displayName").stringValue = "Tourist";
            Require(itemSo, "_pickUpVerb").stringValue = "Carry";
            Require(itemSo, "_carryMass").floatValue = 12f;   // the water (and adrenaline) carries most of it
            Require(itemSo, "_throwStrength").floatValue = 1f;
            Require(itemSo, "_pickupRange").floatValue = 3.4f;
            Require(itemSo, "_grip").enumValueIndex = (int)ItemGrip.Person;
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            var interactable = new SerializedObject(root.AddComponent<Interactable>());
            Require(interactable, "_maxDistance").floatValue = 3.2f;
            interactable.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<VictimBrain>();
            var victimBody = GetOrAdd<VictimBody>(root); // VictimBrain's RequireComponent already added it
            SetRef(victimBody, "_visual", visual);
            SetRef(victimBody, "_avatar", rig);
            var jiggle = avatarGo.AddComponent<AvatarJiggle>();
            SetRef(jiggle, "_rig", rig);
            SetRef(victimBody, "_jiggle", jiggle);
            // Lying on the sand (collapsed, sitting after a rescue): walkers go round, not over them.
            var carver = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            carver.shape = UnityEngine.AI.NavMeshObstacleShape.Box; // turns with the body: lies along it on the sand
            carver.center = new Vector3(0f, -0.25f, 0f);
            carver.size = new Vector3(0.75f, 2.1f, 0.65f);
            carver.carving = true;
            carver.carveOnlyStationary = true;
            carver.carvingTimeToStationary = 0.8f;
            carver.carvingMoveThreshold = 0.3f;
            SetRef(victimBody, "_audio", SpatialAudio(root, 3f, 70f));

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{ItemPrefabDir}/Tourist.prefab");
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
            Place("Life Ring", new Vector3(0.9f, 1.05f, 9.3f)); // inside the shack
            Place("Life Ring", new Vector3(15.5f, 0.1f, 5f), 30f);
            Place("Cooler", new Vector3(-2.2f, 0.4f, 11.2f), 15f);
            // Already floating in the sea.
            Place("Crate", new Vector3(4f, 0.5f, -14f), 35f);
            Place("Life Ring", new Vector3(-3f, 0.2f, -17f));
            Place("Beach Ball", new Vector3(1f, 0.3f, -9f));
        }

        // =====================================================================
        // Scene
        // =====================================================================

        private static void BuildScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // Load assets only AFTER NewScene: opening a scene unloads unreferenced assets, and a reference taken
            // before that points at a destroyed object that saves as "None" (this broke the spawn command).
            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);

            Camera menuCam = Object.FindFirstObjectByType<Camera>();
            menuCam.name = "MenuCamera";
            menuCam.transform.SetPositionAndRotation(new Vector3(-14f, 9f, -12f), Quaternion.Euler(22f, 40f, 0f));
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            SetRef(new GameObject("SceneCameras").AddComponent<SceneCameras>(), "_menuCamera", menuCam);

            // Linear fog must be on in the saved scene, or builds strip the fog shader variants that
            // UnderwaterFx relies on (distance haze above water, thick fog below).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.85f, 0.95f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 520f;

            Light sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = new Color(1f, 0.96f, 0.88f);

            Transform env = new GameObject("Environment").transform;
            BuildBeach(env);
            BuildStation(env);
            foreach (GameObject palm in MeshyArt.Palms(env, BeachHeight))
                MakeShakeable(palm);
            PlaceItems(catalog);
            BuildDrillBoard(env);
            BuildStoryWorld(env);
            Transform[] spawns = BuildSpawnPoints();

            // Rescues: drills now, the emergency director later.
            var rescue = new GameObject("Rescue");
            rescue.AddComponent<NetworkObject>();
            rescue.AddComponent<RescueService>();

            new GameObject("Steam").AddComponent<SteamBootstrap>();
            var content = new GameObject("GameContent").AddComponent<GameContent>();
            SetRef(content, "_items", catalog);
            SetRef(content, "_avatarMaterial", AvatarMaterial());
            SetRef(content, "_worldTextMaterial", WorldTextMaterial());
            if (catalog == null) throw new InvalidOperationException("Item catalog missing after scene creation");
            var ui = new GameObject("UI");
            ui.AddComponent<PlayerHud>();
            ui.AddComponent<DevConsole>();
            ui.AddComponent<DevTools>();
            ui.AddComponent<ItemDebugView>();
            ui.AddComponent<RescueHud>();
            ui.AddComponent<Story.StoryHud>();
            ui.AddComponent<AvatarCustomizer>();
            BuildNetworkManager(playerPrefab, spawns);
            BakeNavMeshes(); // last: everything that blocks walking is in the scene now

            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void BuildBeach(Transform env)
        {
            Material sand = GetMaterial("Sand", new Color(0.93f, 0.84f, 0.62f));
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material rock = GetMaterial("Rock", new Color(0.5f, 0.5f, 0.52f), smoothness: 0.1f);
            Material buoyYellow = GetMaterial("BallYellow", new Color(1f, 0.83f, 0.2f));

            // Terrain: dry beach (y = 0) shelving into the sea toward -z.
            var terrain = new GameObject("BeachTerrain");
            terrain.transform.SetParent(env, false);
            Mesh mesh = GetBeachMesh();
            terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
            terrain.AddComponent<MeshRenderer>().sharedMaterial = sand;
            terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
            var seabed = new SerializedObject(terrain.AddComponent<Seabed>()); // calms the waves over the shallows and the island
            Require(seabed, "_grid").objectReferenceValue = mesh;
            Require(seabed, "_min").vector2Value = new Vector2(TerrainMinX, TerrainMinZ);
            Require(seabed, "_step").floatValue = TerrainStep;
            Require(seabed, "_countX").intValue = Mathf.RoundToInt((TerrainMaxX - TerrainMinX) / TerrainStep) + 1;
            Require(seabed, "_countZ").intValue = Mathf.RoundToInt((TerrainMaxZ - TerrainMinZ) / TerrainStep) + 1;
            seabed.ApplyModifiedPropertiesWithoutUndo();

            // The ocean: waves (CPU + shader), host-synced wave size, underwater camera effects, splashes.
            var ocean = new GameObject("Ocean");
            var surface = ocean.AddComponent<WaterSurface>();
            SetRef(surface, "_material", LoadOrCreateMaterial("Ocean", AssetDatabase.LoadAssetAtPath<Shader>(OceanShaderPath)));
            ocean.AddComponent<NetworkObject>();
            ocean.AddComponent<OceanState>();
            ocean.AddComponent<UnderwaterFx>();
            SetRef(ocean.AddComponent<SplashFx>(), "_particleMaterial", GetSplashMaterial());

            // Dock to run and jump off (climb back up with Jump at the edge).
            var dock = new GameObject("Dock").transform;
            dock.SetParent(env, false);
            dock.position = new Vector3(-8f, 0f, 0f);
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3(0f, 0.175f, -6.5f), new Vector3(2.4f, 0.25f, 25f), wood); // land end at z=6, a step-able 0.3 m above the sand
            for (float z = 2f; z >= -18f; z -= 4f)
                foreach (float x in new[] { -1.1f, 1.1f })
                    Primitive(PrimitiveType.Cube, "Post", dock, new Vector3(x, -2.2f, z), new Vector3(0.22f, 4.8f, 0.22f), wood);

            // Rocks to swim to.
            GameObject rockA = Primitive(PrimitiveType.Sphere, "Rock", env, new Vector3(14f, -3.2f, -30f), new Vector3(7f, 5f, 6f), rock);
            rockA.transform.rotation = Quaternion.Euler(8f, 30f, -5f);
            TagSurface(rockA, SurfaceKind.Rock);
            GameObject rockB = Primitive(PrimitiveType.Sphere, "Rock", env, new Vector3(-20f, -4.5f, -44f), new Vector3(9f, 6f, 7f), rock);
            rockB.transform.rotation = Quaternion.Euler(-6f, 70f, 4f);
            TagSurface(rockB, SurfaceKind.Rock);

            // Swim-zone buoy line.
            var buoys = new GameObject("SwimZoneBuoys").transform;
            buoys.SetParent(env, false);
            for (int i = -4; i <= 4; i++)
            {
                GameObject buoy = Primitive(PrimitiveType.Sphere, "Buoy", buoys, new Vector3(i * 6f, WaterLevel, -40f), Vector3.one * 0.5f, buoyYellow, keepCollider: false);
                var bob = buoy.AddComponent<WaveBobber>();
                var bobSo = new SerializedObject(bob);
                Require(bobSo, "_heightOffset").floatValue = 0.1f;
                bobSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private const float WaterLevel = -0.35f;
        private const string OceanShaderPath = "Assets/_Game/Data/Shaders/Ocean.shader";

        // The island: a rounded rectangle of sand whose long side (the station beach) faces the open sea toward -z.
        private static readonly Vector2 IslandCenter = new(0f, 38f);
        private static readonly Vector2 IslandHalfSize = new(80f, 34f);
        private const float IslandCornerRadius = 28f;
        private const float TerrainMinX = -160f, TerrainMaxX = 160f, TerrainMinZ = -380f, TerrainMaxZ = 150f, TerrainStep = 2f;

        // The hotel island (chapter 2), ~200 m south across the channel; its beach faces island 1.
        private static readonly Vector2 Island2Center = new(20f, -250f);
        private static readonly Vector2 Island2HalfSize = new(60f, 30f);
        private const float Island2CornerRadius = 24f;

        /// <summary>Signed distance outside a rounded box (negative inside).</summary>
        private static float BoxDistanceOut(float x, float z, Vector2 center, Vector2 half, float radius)
        {
            var p = new Vector2(Mathf.Abs(x - center.x), Mathf.Abs(z - center.y));
            Vector2 q = p - (half - Vector2.one * radius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return outside + inside - radius;
        }

        /// <summary>Island 2's shore coordinate (same meaning as <see cref="ShoreCoordinate"/>: 4 at the edge, growing inland).</summary>
        private static float Island2Shore(float x, float z)
        {
            float wobble = 2.5f * Mathf.Sin(x * 0.05f + 1.3f) + (Mathf.PerlinNoise(x * 0.03f + 11f, z * 0.03f + 5f) - 0.5f) * 6f;
            return 4f - BoxDistanceOut(x, z, Island2Center, Island2HalfSize, Island2CornerRadius) + wobble;
        }

        /// <summary>
        /// Distance inland from the island's edge in "profile" metres (the old straight beach used z here, so the
        /// station beach keeps exactly its shape). Grows toward the middle of the island, negative out at sea.
        /// </summary>
        private static float ShoreCoordinate(float x, float z)
        {
            var p = new Vector2(Mathf.Abs(x - IslandCenter.x), Mathf.Abs(z - IslandCenter.y));
            Vector2 q = p - (IslandHalfSize - Vector2.one * IslandCornerRadius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            float distanceOut = outside + inside - IslandCornerRadius; // rounded-box signed distance, + outside
            float front = IslandCenter.y - IslandHalfSize.y;          // z of the station beach's edge
            // Curvy coast: the old gentle wave along the station beach, plus lumpier bays away from the station.
            float wild = Mathf.Clamp01((Mathf.Abs(x) - 25f) / 30f + Mathf.Clamp01((z - 20f) / 20f));
            float wobble = 4f * Mathf.Sin(x * 0.045f) + wild * (Mathf.PerlinNoise(x * 0.025f + 3.7f, z * 0.025f + 1.3f) - 0.5f) * 14f;
            return front - distanceOut + wobble;
        }

        /// <summary>
        /// Beach height at a point: flat sand inland, a curvy shoreline all round, shelving to ~9 m deep offshore.
        /// Two islands: the station island and the hotel island to the south (flat, no dunes, so the hotel sits level).
        /// </summary>
        private static float BeachHeight(float x, float z) =>
            Mathf.Max(ProfileHeight(ShoreCoordinate(x, z), x, z, true), ProfileHeight(Island2Shore(x, z), x, z, false));

        private static float ProfileHeight(float shore, float x, float z, bool dunesInland)
        {
            (float z, float h)[] profile = { (-170f, -9f), (-90f, -8f), (-45f, -4.5f), (-20f, -2.3f), (-6f, -0.9f), (4f, 0f), (100f, 0f) };
            float h = profile[0].h;
            for (int i = 0; i < profile.Length - 1; i++)
            {
                if (shore < profile[i].z || shore > profile[i + 1].z) continue;
                float t = Mathf.InverseLerp(profile[i].z, profile[i + 1].z, shore);
                h = Mathf.Lerp(profile[i].h, profile[i + 1].h, Mathf.SmoothStep(0f, 1f, t));
                break;
            }
            if (shore > profile[^1].z) h = 0f;
            if (shore < profile[0].z) h = profile[0].h;
            // Dunes inland, gentle ripples on the seabed.
            float dunes = dunesInland ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 36f, shore)) * (Mathf.PerlinNoise(x * 0.04f + 10f, z * 0.04f) * 2.2f) : 0f;
            float ripples = shore < -2f ? (Mathf.PerlinNoise(x * 0.15f, z * 0.15f) - 0.5f) * 0.3f : 0f;
            return h + dunes + ripples;
        }

        private static Mesh GetBeachMesh()
        {
            const float minX = TerrainMinX, maxX = TerrainMaxX, minZ = TerrainMinZ, maxZ = TerrainMaxZ, step = TerrainStep;
            int nx = Mathf.RoundToInt((maxX - minX) / step) + 1;
            int nz = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = minX + ix * step, z = minZ + iz * step;
                    vertices[iz * nx + ix] = new Vector3(x, BeachHeight(x, z), z);
                    uvs[iz * nx + ix] = new Vector2(x, z) * 0.25f;
                }
            }
            var triangles = new int[(nx - 1) * (nz - 1) * 6];
            int t = 0;
            for (int iz = 0; iz < nz - 1; iz++)
            {
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    int a = iz * nx + ix, b = a + nx;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + 1;
                    triangles[t++] = a + 1; triangles[t++] = b; triangles[t++] = b + 1;
                }
            }

            Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/BeachTerrain.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "BeachTerrain" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Soft round particle for splashes (URP Particles/Unlit, alpha blended).</summary>
        private static Material GetSplashMaterial()
        {
            string texPath = $"{MaterialDir}/SoftDot.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                const int size = 64;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));
                    }
                tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }

            Material mat = LoadOrCreateMaterial("SplashParticle", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);      // transparent
            mat.SetFloat("_Blend", 0f);        // alpha
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void BuildStation(Transform env)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            Material brass = GetMaterial("Brass", new Color(0.85f, 0.65f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            Material dark = GetMaterial("DarkMetal", new Color(0.18f, 0.18f, 0.2f));

            // The saddest lifeguard shack imaginable.
            var shack = new GameObject("Station_Shack").transform;
            TagSurface(shack.gameObject, SurfaceKind.Wood);
            shack.SetParent(env, false);
            shack.position = new Vector3(0f, 0f, 10f);
            Primitive(PrimitiveType.Cube, "Floor", shack, new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.4f, 4f), wood);
            foreach (Vector3 p in new[] { new Vector3(-2.3f, 1.6f, -1.8f), new Vector3(2.3f, 1.6f, -1.8f), new Vector3(-2.3f, 1.4f, 1.8f), new Vector3(2.3f, 1.4f, 1.8f) })
                Primitive(PrimitiveType.Cube, "Post", shack, p, new Vector3(0.2f, 2.6f, 0.2f), wood);
            Primitive(PrimitiveType.Cube, "Roof", shack, new Vector3(0f, 2.9f, 0f), new Vector3(5.4f, 0.15f, 4.4f), red).transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Counter", shack, new Vector3(0f, 0.85f, 1.3f), new Vector3(3.2f, 0.9f, 0.6f), wood);

            // The Meshy shack, scaled up so a lifeguard fits through its door; walk in and switch the light on.
            bool meshyShack = MeshyArt.Shack(shack, ShackScale, out MeshyArt.DoorSpec shackDoor);
            if (meshyShack)
            {
                // Sandy's Lost & Found now: the door is off its hinges, only the frame round the doorway stays.
                BuildDoorway(shack, "ShackDoorway", shackDoor, new Color(0.36f, 0.23f, 0.14f));
                // A shelf along the back wall with a radio and the first-aid kit.
                float k = ShackScale;
                Primitive(PrimitiveType.Cube, "Shelf", shack, new Vector3(0.28f * k, 0.575f * k + 0.95f, -1.18f * k + 0.25f), new Vector3(1.4f, 0.06f, 0.42f), wood);
                Primitive(PrimitiveType.Cube, "ShelfLegL", shack, new Vector3(0.28f * k - 0.62f, 0.575f * k + 0.47f, -1.18f * k + 0.25f), new Vector3(0.06f, 0.95f, 0.36f), wood);
                Primitive(PrimitiveType.Cube, "ShelfLegR", shack, new Vector3(0.28f * k + 0.62f, 0.575f * k + 0.47f, -1.18f * k + 0.25f), new Vector3(0.06f, 0.95f, 0.36f), wood);
                Primitive(PrimitiveType.Cube, "Radio", shack, new Vector3(0.28f * k - 0.3f, 0.575f * k + 1.08f, -1.18f * k + 0.25f), new Vector3(0.36f, 0.2f, 0.16f), dark, keepCollider: false);
                Primitive(PrimitiveType.Cube, "FirstAid", shack, new Vector3(0.28f * k + 0.3f, 0.575f * k + 1.06f, -1.18f * k + 0.25f), new Vector3(0.3f, 0.16f, 0.2f),
                    GetMaterial("FirstAidGreen", new Color(0.2f, 0.62f, 0.32f)), keepCollider: false);
                Primitive(PrimitiveType.Cube, "FirstAidCross", shack, new Vector3(0.28f * k + 0.3f, 0.575f * k + 1.06f, -1.18f * k + 0.25f + 0.101f), new Vector3(0.12f, 0.04f, 0.01f),
                    GetMaterial("White", new Color(0.95f, 0.95f, 0.95f)), keepCollider: false);
                Primitive(PrimitiveType.Cube, "FirstAidCross2", shack, new Vector3(0.28f * k + 0.3f, 0.575f * k + 1.06f, -1.18f * k + 0.25f + 0.101f), new Vector3(0.04f, 0.12f, 0.01f),
                    GetMaterial("White", new Color(0.95f, 0.95f, 0.95f)), keepCollider: false);
            }

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
            if (meshyShack)
            {
                // Inside: the lamp hangs from the ceiling, the switch is on the wall by the door.
                float k = ShackScale;
                lightSwitch.transform.localPosition = new Vector3(0.95f * k, 0.575f * k + 1.3f, 0.65f * k - 0.1f);
                bulb.transform.localPosition = new Vector3(0.28f * k, 2.55f * k - 0.3f, -0.26f * k);
                lamp.range = 6f;
                roofSign.transform.localPosition = new Vector3(0.12f * k, 2.55f * k + 0.35f, 0.65f * k + 0.45f);
                roofSign.characterSize = 0.04f;
            }
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

            // Lifeguard tower: door, window and ramp face the sea (run straight down the ramp into the water).
            var tower = new GameObject("Tower").transform;
            TagSurface(tower.gameObject, SurfaceKind.Wood);
            tower.SetParent(env, false);
            tower.position = new Vector3(12f, 0f, 8f);
            tower.rotation = Quaternion.Euler(0f, 180f, 0f);
            foreach (Vector3 p in new[] { new Vector3(-0.8f, 1.5f, -0.8f), new Vector3(0.8f, 1.5f, -0.8f), new Vector3(-0.8f, 1.5f, 0.8f), new Vector3(0.8f, 1.5f, 0.8f) })
                Primitive(PrimitiveType.Cube, "Leg", tower, p, new Vector3(0.15f, 3f, 0.15f), wood);
            Primitive(PrimitiveType.Cube, "Platform", tower, new Vector3(0f, 3.1f, 0f), new Vector3(2.2f, 0.2f, 2.2f), wood);
            Primitive(PrimitiveType.Cube, "Ramp", tower, new Vector3(0f, 1.5f, 2.6f), new Vector3(1f, 0.1f, 4f), wood).transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
            if (MeshyArt.Tower(tower, TowerScale, out MeshyArt.DoorSpec towerDoor))
            {
                BuildDoor(tower, "TowerDoor", towerDoor, new Color(0.47f, 0.35f, 0.28f), new Color(0.93f, 0.93f, 0.9f), planks: false);
                // A stool to sit on and watch the water.
                float k = TowerScale, deck = 2.285f * k;
                Primitive(PrimitiveType.Cylinder, "StoolSeat", tower, new Vector3(0.45f, deck + 0.62f, -1.1f * k), new Vector3(0.38f, 0.03f, 0.38f), wood);
                Primitive(PrimitiveType.Cylinder, "StoolLeg", tower, new Vector3(0.45f, deck + 0.3f, -1.1f * k), new Vector3(0.08f, 0.3f, 0.08f), wood);
            }
        }

        /// <summary>A palm you can shake for coconuts (Interact on the trunk).</summary>
        private static void MakeShakeable(GameObject palm)
        {
            palm.AddComponent<NetworkObject>();
            var tree = palm.AddComponent<PalmTree>();
            Transform model = palm.transform.childCount > 0 ? palm.transform.GetChild(0) : null;
            SetRef(tree, "_model", model);
            SetRef(tree, "_audio", SpatialAudio(palm, 3f, 35f));
            var so = new SerializedObject(tree);
            Require(so, "_crownHeight").floatValue = palm.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).DefaultIfEmpty(6f).Max() - palm.transform.position.y - 1.4f;
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(palm.AddComponent<Interactable>(), palm.GetComponents<Collider>(),
                palm.GetComponentsInChildren<Renderer>(), 3f);
        }

        private const float ShackScale = 1.45f;
        private const float TowerScale = 1.2f;

        /// <summary>
        /// A hinged, networked door in a structure's doorway, plus a frame around the gap that hides the cut edges
        /// of the Meshy wall. <paramref name="planks"/> adds a board-and-brace pattern (old shack door).
        /// </summary>
        private static void BuildDoor(Transform parent, string name, MeshyArt.DoorSpec spec, Color leafColor, Color frameColor, bool planks)
        {
            Material leafMat = GetMaterial(name + "Leaf", leafColor);
            Material trimMat = GetMaterial(name + "Frame", frameColor);
            Material darker = GetMaterial(name + "Boards", leafColor * 0.78f);
            Material brass = GetMaterial("Brass", new Color(0.85f, 0.65f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            float w = spec.Width, h = spec.Height, dir = spec.LeafDirection;
            const float thick = 0.06f;

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Hinge;
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);

            GameObject leaf = Primitive(PrimitiveType.Cube, "Leaf", hinge, new Vector3(dir * w * 0.5f, h * 0.5f, 0f), new Vector3(w - 0.02f, h - 0.02f, thick), leafMat);
            TagSurface(leaf, SurfaceKind.Wood);
            var extras = new List<Renderer> { leaf.GetComponent<Renderer>() };
            if (planks)
            {
                for (int i = 1; i < 4; i++)
                    extras.Add(Primitive(PrimitiveType.Cube, "Groove", hinge, new Vector3(dir * w * i / 4f, h * 0.5f, spec.WallFacing * thick * 0.5f),
                        new Vector3(0.015f, h - 0.06f, 0.01f), darker, keepCollider: false).GetComponent<Renderer>());
                foreach (float y in new[] { 0.22f, 0.78f })
                    extras.Add(Primitive(PrimitiveType.Cube, "Brace", hinge, new Vector3(dir * w * 0.5f, h * y, spec.WallFacing * (thick * 0.5f + 0.012f)),
                        new Vector3(w - 0.1f, 0.1f, 0.025f), darker, keepCollider: false).GetComponent<Renderer>());
            }
            foreach (float side in new[] { 1f, -1f })
                extras.Add(Primitive(PrimitiveType.Sphere, "Knob", hinge, new Vector3(dir * (w - 0.1f), h * 0.47f, side * (thick * 0.5f + 0.025f)),
                    Vector3.one * 0.06f, brass, keepCollider: false).GetComponent<Renderer>());

            DoorFrame(root.transform, spec, trimMat);

            root.AddComponent<NetworkObject>();
            var door = root.AddComponent<Door>();
            SetRef(door, "_hinge", hinge);
            SetRef(door, "_audio", SpatialAudio(root, 2f, 25f));
            ConfigureInteractable(root.AddComponent<Interactable>(), new[] { leaf.GetComponent<Collider>() }, extras.ToArray(), 2.8f);
        }

        /// <summary>Frame around a doorway gap (hides the cut edges of the Meshy wall), outside the door's swing.</summary>
        private static void DoorFrame(Transform root, MeshyArt.DoorSpec spec, Material trimMat)
        {
            const float trim = 0.09f;
            float w = spec.Width, h = spec.Height, dir = spec.LeafDirection, mid = dir * w * 0.5f;
            Primitive(PrimitiveType.Cube, "JambHinge", root, new Vector3(-dir * trim * 0.5f, h * 0.5f, 0f), new Vector3(trim, h + trim, 0.26f), trimMat, keepCollider: false);
            Primitive(PrimitiveType.Cube, "JambLatch", root, new Vector3(dir * (w + trim * 0.5f), h * 0.5f, 0f), new Vector3(trim, h + trim, 0.26f), trimMat, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Head", root, new Vector3(mid, h + trim * 0.5f, 0f), new Vector3(w + trim * 2f, trim, 0.26f), trimMat, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Sill", root, new Vector3(mid, 0.01f, 0f), new Vector3(w + trim * 2f, 0.02f, 0.26f), trimMat, keepCollider: false);
        }

        /// <summary>An open doorway: just the frame, no door (walk straight in).</summary>
        private static void BuildDoorway(Transform parent, string name, MeshyArt.DoorSpec spec, Color frameColor)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = spec.Hinge;
            DoorFrame(root.transform, spec, GetMaterial(name + "Frame", frameColor));
        }

        /// <summary>Red board by the spawn: starts a rescue drill (a tourist in trouble out in the water).</summary>
        private static void BuildDrillBoard(Transform env)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            Material red = GetMaterial("RescueRed", new Color(0.86f, 0.16f, 0.13f));
            var board = new GameObject("DrillBoard");
            board.transform.SetParent(env, false);
            board.transform.position = new Vector3(4.6f, 0f, 12.6f);
            board.transform.rotation = Quaternion.Euler(0f, 135f, 0f); // text faces the spawn area
            Primitive(PrimitiveType.Cube, "Post", board.transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.12f, 1.5f, 0.12f), wood);
            GameObject panel = Primitive(PrimitiveType.Cube, "Panel", board.transform, new Vector3(0f, 1.6f, 0f), new Vector3(1.5f, 0.8f, 0.08f), red);
            TextMesh text = WorldText(board.transform, "Text", new Vector3(0f, 1.6f, -0.05f), "RESCUE DRILL\n<size=44>throws a tourist in the sea</size>", 72, 0.028f, Color.white);
            text.richText = true;
            board.AddComponent<DrillBoard>();
            ConfigureInteractable(board.AddComponent<Interactable>(), new[] { panel.GetComponent<Collider>() }, new[] { panel.GetComponent<Renderer>() }, 3f);
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

        /// <param name="onTop">Drawn over everything (name tags); otherwise hidden behind walls like any object.</param>
        private static TextMesh WorldText(Transform parent, string name, Vector3 localPos, string text, int fontSize, float characterSize, Color color, bool onTop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = BuiltinFont;
            go.GetComponent<MeshRenderer>().sharedMaterial = onTop ? BuiltinFont.material : WorldTextMaterial();
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

        private static void TagSurface(GameObject go, SurfaceKind kind)
        {
            var so = new SerializedObject(go.AddComponent<SurfaceType>());
            Require(so, "_kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static PhysicsMaterial GetPhysicsMaterial(string name, float bounciness, float friction, PhysicsMaterialCombine bounceCombine,
            PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average)
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
            mat.frictionCombine = frictionCombine;
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

        /// <summary>Depth-tested 3D text; GameContent keeps its font texture current at runtime.</summary>
        private static Material WorldTextMaterial()
        {
            const string path = "Assets/_Game/Data/Shaders/WorldText.shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) throw new FileNotFoundException("WorldText shader missing", path);
            Material mat = LoadOrCreateMaterial("WorldText", shader);
            mat.mainTexture = BuiltinFont.material.mainTexture;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The one material every character uses (vertex colours + toon light).</summary>
        public static Material AvatarMaterial()
        {
            const string path = "Assets/_Game/Data/Shaders/Avatar.shader";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) throw new FileNotFoundException("Avatar shader missing", path);
            Material mat = LoadOrCreateMaterial("Avatar", shader);
            mat.SetFloat("_Ambient", 0.55f);
            mat.SetFloat("_Rim", 0.28f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
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

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            Require(so, field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            Require(so, field).enumValueIndex = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

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
