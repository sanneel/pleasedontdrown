using System;
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
using PleaseDontDrown.Net;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using SteamTransport = FishySteamworks.FishySteamworks;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Builds the M0 network test scene from code, so the setup is reproducible and reviewable.
    /// Menu: PLEASE DON'T DROWN > Rebuild M0 test scene. Batch: -executeMethod PleaseDontDrown.Editor.M0SceneBuilder.BuildBatch
    /// </summary>
    public static class M0SceneBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/Game.unity";
        private const string PrefabPath = "Assets/_Game/Player/Prefabs/M0_Player.prefab";
        private const string MaterialDir = "Assets/_Game/Data/Materials";

        [MenuItem("PLEASE DON'T DROWN/Rebuild M0 test scene")]
        private static void BuildFromMenu()
        {
            if (EditorUtility.DisplayDialog("Rebuild M0 scene", $"This overwrites {ScenePath} and the M0 player prefab.", "Rebuild", "Cancel"))
                Build();
        }

        public static void BuildBatch()
        {
            try
            {
                Build();
                Debug.Log("[M0] BUILD OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[M0] BUILD FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        /// <summary>Rebuilds the scene, then makes a Windows build in Builds/Win64 (used for multi-instance tests).</summary>
        public static void BuildPlayerBatch()
        {
            try
            {
                Build();
                const string exe = "Builds/Win64/PleaseDontDrown.exe";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new Exception($"Player build {report.summary.result} with {report.summary.totalErrors} errors");
                // Steam needs the app id next to the exe when not launched through Steam.
                File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(exe)!, "steam_appid.txt"), true);
                Debug.Log($"[M0] PLAYER BUILD OK: {exe} ({report.summary.totalSize / (1024 * 1024)} MB)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[M0] PLAYER BUILD FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            ConfigureProject();
            NetworkObject player = BuildPlayerPrefab();
            RefreshFishNetPrefabs();
            BuildScene(player);
            AssetDatabase.SaveAssets();
        }

        // ---------------- Project ----------------

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "PleaseDontDrown";   // decides the save folder under AppData/LocalLow
            PlayerSettings.productName = "PLEASE DON'T DROWN";
            PlayerSettings.bundleVersion = "0.0.1";
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

        // ---------------- Player prefab ----------------

        private static NetworkObject BuildPlayerPrefab()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)!);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "M0_Player";
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            var controller = go.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            var body = go.GetComponent<MeshRenderer>();
            body.sharedMaterial = GetMaterial("M0_Player", Color.white);

            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            Object.DestroyImmediate(visor.GetComponent<BoxCollider>());
            visor.transform.SetParent(go.transform, false);
            visor.transform.localPosition = new Vector3(0f, 0.55f, 0.33f);
            visor.transform.localScale = new Vector3(0.6f, 0.18f, 0.2f);
            visor.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("M0_Visor", new Color(0.1f, 0.1f, 0.12f));

            var tag = new GameObject("NameTag");
            tag.transform.SetParent(go.transform, false);
            tag.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            var text = tag.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.font = font;
            tag.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            text.text = "Lifeguard";
            text.fontSize = 64;
            text.characterSize = 0.05f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            var player = go.AddComponent<DebugCapsulePlayer>();
            SetRef(player, "_nameTag", text);
            SetRef(player, "_body", body);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            return saved.GetComponent<NetworkObject>();
        }

        // ---------------- Scene ----------------

        private static void BuildScene(NetworkObject playerPrefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Camera menuCam = Object.FindFirstObjectByType<Camera>();
            menuCam.name = "MenuCamera";
            menuCam.transform.SetPositionAndRotation(new Vector3(-14f, 9f, -12f), Quaternion.Euler(22f, 40f, 0f));
            menuCam.clearFlags = CameraClearFlags.SolidColor;
            menuCam.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            var cameras = new GameObject("SceneCameras").AddComponent<SceneCameras>();
            SetRef(cameras, "_menuCamera", menuCam);

            Light sun = Object.FindFirstObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = new Color(1f, 0.96f, 0.88f);

            BuildBeach();
            Transform[] spawns = BuildSpawnPoints();

            new GameObject("Steam").AddComponent<SteamBootstrap>();
            BuildNetworkManager(playerPrefab, spawns);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void BuildBeach()
        {
            var env = new GameObject("Environment").transform;
            Material sand = GetMaterial("M0_Sand", new Color(0.93f, 0.84f, 0.62f));
            Material wood = GetMaterial("M0_Wood", new Color(0.55f, 0.36f, 0.22f));
            Material red = GetMaterial("M0_RescueRed", new Color(0.86f, 0.16f, 0.13f));

            Block(env, "Sand", new Vector3(0f, -0.5f, 15f), new Vector3(80f, 1f, 30f), sand);
            Block(env, "Seabed", new Vector3(0f, -3f, -60f), new Vector3(300f, 1f, 120f), GetMaterial("M0_Seabed", new Color(0.62f, 0.56f, 0.42f)));

            // Water surface: visual only until M3 (no collider, so you can wade down to the seabed).
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Water";
            Object.DestroyImmediate(water.GetComponent<MeshCollider>());
            water.transform.SetParent(env, false);
            water.transform.position = new Vector3(0f, -0.35f, -60f);
            water.transform.localScale = new Vector3(30f, 1f, 12f);
            water.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("M0_Water", new Color(0.15f, 0.55f, 0.75f));

            // The saddest lifeguard shack imaginable.
            var shack = new GameObject("Station_Shack").transform;
            shack.SetParent(env, false);
            shack.position = new Vector3(0f, 0f, 10f);
            Block(shack, "Floor", new Vector3(0f, 0.2f, 0f), new Vector3(5f, 0.4f, 4f), wood);
            foreach (var p in new[] { new Vector3(-2.3f, 1.6f, -1.8f), new Vector3(2.3f, 1.6f, -1.8f), new Vector3(-2.3f, 1.4f, 1.8f), new Vector3(2.3f, 1.4f, 1.8f) })
                Block(shack, "Post", p, new Vector3(0.2f, 2.6f, 0.2f), wood);
            Block(shack, "Roof", new Vector3(0f, 2.9f, 0f), new Vector3(5.4f, 0.15f, 4.4f), red).transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);

            var sign = new GameObject("Sign").AddComponent<TextMesh>();
            sign.transform.SetParent(shack, false);
            sign.transform.localPosition = new Vector3(0f, 3.4f, -2.2f);
            sign.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            sign.GetComponent<MeshRenderer>().sharedMaterial = sign.font.material;
            sign.text = "LIFEGUARD (probably)";
            sign.fontSize = 80;
            sign.characterSize = 0.06f;
            sign.anchor = TextAnchor.MiddleCenter;
            sign.color = red.color;
            sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Lifeguard tower.
            var tower = new GameObject("Tower").transform;
            tower.SetParent(env, false);
            tower.position = new Vector3(12f, 0f, 4f);
            foreach (var p in new[] { new Vector3(-0.8f, 1.5f, -0.8f), new Vector3(0.8f, 1.5f, -0.8f), new Vector3(-0.8f, 1.5f, 0.8f), new Vector3(0.8f, 1.5f, 0.8f) })
                Block(tower, "Leg", p, new Vector3(0.15f, 3f, 0.15f), wood);
            Block(tower, "Platform", new Vector3(0f, 3.1f, 0f), new Vector3(2.2f, 0.2f, 2.2f), wood);
            Block(tower, "Ramp", new Vector3(0f, 1.5f, 2.6f), new Vector3(1f, 0.1f, 4f), wood).transform.localRotation = Quaternion.Euler(38f, 0f, 0f);
        }

        private static Transform[] BuildSpawnPoints()
        {
            var parent = new GameObject("SpawnPoints").transform;
            return Enumerable.Range(0, 4).Select(i =>
            {
                var t = new GameObject($"Spawn_{i}").transform;
                t.SetParent(parent, false);
                t.position = new Vector3(-3f + i * 2f, 1.2f, 15f);
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

            var mpSo = new SerializedObject(multipass);
            SerializedProperty list = Require(mpSo, "_transports");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = tugboat;
            list.GetArrayElementAtIndex(1).objectReferenceValue = steam;
            mpSo.ApplyModifiedPropertiesWithoutUndo();

            var steamSo = new SerializedObject(steam);
            Require(steamSo, "_peerToPeer").boolValue = true;
            steamSo.ApplyModifiedPropertiesWithoutUndo();

            var auth = go.AddComponent<LobbyAuthenticator>();
            SetRef(serverManager, "_authenticator", auth);

            var spawner = go.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            spawner.Spawns = spawns;
            EditorUtility.SetDirty(spawner);

            var lobby = go.AddComponent<SteamLobbyService>();
            var connection = go.AddComponent<ConnectionService>();
            SetRef(connection, "_networkManager", networkManager);
            SetRef(connection, "_multipass", multipass);
            SetRef(connection, "_lobby", lobby);

            var menu = go.AddComponent<DevConnectMenu>();
            SetRef(menu, "_connection", connection);
            SetRef(go.AddComponent<DevLaunchArgs>(), "_connection", connection);

            // Point the NetworkManager at FishNet's generated prefab collection.
            string guid = AssetDatabase.FindAssets("t:DefaultPrefabObjects").FirstOrDefault();
            if (guid != null)
            {
                var prefabs = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                var nmSo = new SerializedObject(networkManager);
                SerializedProperty prop = nmSo.FindProperty("_spawnablePrefabs") ?? nmSo.FindProperty("SpawnablePrefabs");
                if (prop != null)
                {
                    prop.objectReferenceValue = prefabs;
                    nmSo.ApplyModifiedPropertiesWithoutUndo();
                }
                else Debug.LogWarning("[M0] Could not find NetworkManager spawnable prefabs field; FishNet will assign it on play.");
            }
            else Debug.LogWarning("[M0] DefaultPrefabObjects asset not found yet.");
        }

        // ---------------- Helpers ----------------

        private static void RefreshFishNetPrefabs()
        {
            // FishNet regenerates its prefab list on import; force a full pass so the new player prefab is registered.
            Type generator = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("FishNet.Editing.PrefabCollectionGenerator.Generator"))
                .FirstOrDefault(t => t != null);
            MethodInfo full = generator?.GetMethod("GenerateFull", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (full == null)
            {
                Debug.LogWarning("[M0] FishNet prefab generator not found; relying on automatic generation.");
                return;
            }
            object[] args = full.GetParameters().Select(p => p.Name == "forced" ? (object)true : p.DefaultValue).ToArray();
            full.Invoke(null, args);
            AssetDatabase.Refresh();
        }

        private static GameObject Block(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPos;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return cube;
        }

        private static Material GetMaterial(string name, Color color)
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            EditorUtility.SetDirty(mat);
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
    }
}
