using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.World.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        // All guest wings are closed shells. The existing lobby remains the only interior.
        private static void BuildResortExterior(Transform hotel)
        {
            foreach (Transform child in hotel.Cast<Transform>().ToArray())
                if (child.name is "Upper" or "UpperWindow" or "Roof" or "Sign" or "ResortExterior")
                    Object.DestroyImmediate(child.gameObject);
            var root = new GameObject("ResortExterior").transform;
            root.SetParent(hotel, false);
            var ivory = GetMaterial("ResortIvory", new Color(.96f, .91f, .8f));
            var teal = GetMaterial("ResortTeal", new Color(.12f, .43f, .47f), smoothness: .35f);
            var glass = GetMaterial("ResortGlazing", new Color(.14f, .42f, .52f), smoothness: .85f);
            var stone = GetMaterial("ResortStone", new Color(.78f, .73f, .62f));
            var gold = GetMaterial("ResortBrass", new Color(.75f, .57f, .25f), metallic: .65f, smoothness: .65f);
            var pool = GetMaterial("ResortPoolWater", new Color(.05f, .65f, .75f), smoothness: .96f);
            var green = GetMaterial("ResortGarden", new Color(.25f, .43f, .22f));

            bool importedHotel = TryBuildTripoResort(root);
            if (!importedHotel)
            {
                ResortWing(root, "CentralTower", new Vector3(0, 0, -20), 44, 26, 10, ivory, teal, glass, gold);
            }
            var crownAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TripoModels/ResortCrown/ResortCrown.obj");
            if (!importedHotel && crownAsset != null)
            {
                var crown = Object.Instantiate(crownAsset, root);
                crown.name = "CoralCrown"; crown.transform.localPosition = new Vector3(0, 37, -20);
                crown.transform.localScale = Vector3.one * 3;
                foreach (var r in crown.GetComponentsInChildren<Renderer>()) r.sharedMaterial = gold;
            }

            // The old lobby roof sits beneath the tower. A broad canopy makes its entrance readable.
            ResortBox(root, "ArrivalCanopy", new Vector3(0, 5, 10), new Vector3(22, .5f, 11), ivory);
            foreach (float x in new[] { -9f, 9f })
                ResortBox(root, "CanopyColumn", new Vector3(x, 2.5f, 14), new Vector3(.65f, 5, .65f), teal);
            ResortBox(root, "EntranceWalk", new Vector3(0, .02f, 18), new Vector3(12, .04f, 24), stone);
            TextMesh sign = WorldText(root, "ResortSign", new Vector3(0, 7, 6.4f), "GRAND CORAL\n<size=48>ISLAND RESORT</size>", 90, .11f, gold.color);
            sign.transform.localRotation = Quaternion.Euler(0, 180, 0);

            // Pool terraces sit alongside the hotel, leaving the dock-to-reception route clear.
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 113;
                ResortBox(root, "PoolTerrace", new Vector3(x, .03f, -44), new Vector3(50, .06f, 86), stone);
                // Decorative raised pools avoid cutting the terrain. Pool-specific swimming can be added later.
                ResortBox(root, "PoolBasin", new Vector3(x, .075f, -44), new Vector3(29, .15f, 54), teal);
                ResortBox(root, "PoolWater", new Vector3(x, .83f, -44), new Vector3(28, .035f, 53), pool, false);
                foreach (float dx in new[] { -14.5f, 14.5f })
                    ResortBox(root, "PoolRim", new Vector3(x + dx, .48f, -44), new Vector3(1, .95f, 55), ivory);
                foreach (float dz in new[] { -27f, 27f })
                    ResortBox(root, "PoolRim", new Vector3(x, .48f, -44 + dz), new Vector3(30, .95f, 1), ivory);
                // Three broad steps let players climb the pool edge.
                for (int step = 0; step < 3; step++)
                    ResortBox(root, "PoolSteps", new Vector3(x, .15f * (step + 1), -14.5f - step * .7f), new Vector3(6, .3f * (step + 1), .75f), ivory);
                for (int i = 0; i < 7; i++)
                {
                    float z = -22 - i * 7;
                    foreach (float dx in new[] { -20f, 20f })
                    {
                        var chair = new GameObject("PoolLounger").transform;
                        chair.SetParent(root, false); chair.localPosition = new Vector3(x + dx, 0, z);
                        ResortBox(chair, "Seat", new Vector3(0, .45f, 0), new Vector3(.9f, .16f, 2), ivory, false);
                        var back = ResortBox(chair, "Back", new Vector3(0, .8f, -.8f), new Vector3(.9f, .13f, .8f), teal, false);
                        back.transform.localRotation = Quaternion.Euler(-35, 0, 0);
                        foreach (float end in new[] { -.7f, .7f })
                            ResortBox(chair, "Leg", new Vector3(0, .22f, end), new Vector3(.75f, .44f, .12f), teal, false);
                    }
                }
                for (int i = 0; i < 5; i++) ResortPalm(root, new Vector3(side * 143, 0, -14 - i * 19), i + side * 17);
            }
            // The much larger southern grounds have paths, gardens and shaded pavilions.
            ResortBox(root, "GardenPromenade", new Vector3(0, .025f, -148), new Vector3(10, .05f, 132), stone);
            foreach (float z in new[] { -96f, -151f, -206f })
            {
                ResortBox(root, "CrossWalk", new Vector3(0, .025f, z), new Vector3(232, .05f, 5), stone);
                foreach (int side in new[] { -1, 1 })
                {
                    ResortBox(root, "GardenLawn", new Vector3(side * 57, .015f, z + 21), new Vector3(91, .03f, 32), green, false);
                    ResortPavilion(root, new Vector3(side * 65, 0, z + 18), ivory, teal);
                    for (int i = 0; i < 4; i++) ResortPalm(root, new Vector3(side * (18 + i * 30), 0, z - 5), i + (int)z);
                }
            }
            for (int i = 0; i < 6; i++)
                foreach (int side in new[] { -1, 1 }) ResortPalm(root, new Vector3(side * 16, 0, -95 - i * 21), i * 31);
            // Use existing art, matching the rest of the game. Static garden trees have no network behaviours.
            CombineResortDetails(root);
            BuildFinishedHotelEntrance(hotel);
            if (LoadProp("resort_beach_bar") != null) BuildRichResortBeach(hotel);
        }

        private static GameObject ResortBox(Transform parent, string name, Vector3 p, Vector3 size, Material material, bool solid = true) =>
            Primitive(PrimitiveType.Cube, name, parent, p, size, material, solid);

        private static void ResortWing(Transform parent, string name, Vector3 position, float width, float depth, int floors,
            Material wall, Material trim, Material glass, Material brass)
        {
            var wing = new GameObject(name).transform; wing.SetParent(parent, false); wing.localPosition = position;
            float height = floors * 3.6f;
            ResortBox(wing, "ClosedGuestWing", new Vector3(0, height / 2, 0), new Vector3(width, height, depth), wall);
            ResortBox(wing, "RoofCrown", new Vector3(0, height + .45f, 0), new Vector3(width + 2, .9f, depth + 2), trim);
            ResortBox(wing, "CrownStripe", new Vector3(0, height + .94f, 0), new Vector3(width + 2.2f, .1f, depth + 2.2f), brass, false);
            // Front and rear balcony rows; ground floor is solid with decorative glazing.
            foreach (int side in new[] { -1, 1 })
                for (int floor = 0; floor < floors; floor++)
                {
                    float y = floor * 3.6f;
                    ResortBox(wing, "FloorBand", new Vector3(0, y + .2f, side * (depth / 2 + .65f)), new Vector3(width + .4f, .25f, 1.8f), wall, false);
                    int bays = Mathf.FloorToInt(width / 4);
                    for (int bay = 0; bay < bays; bay++)
                    {
                        float x = (bay - (bays - 1) * .5f) * 4;
                        ResortBox(wing, "RoomGlazing", new Vector3(x, y + 1.75f, side * (depth / 2 + .035f)), new Vector3(2.7f, 2.6f, .065f), glass, false);
                        ResortBox(wing, "WindowMullion", new Vector3(x, y + 1.75f, side * (depth / 2 + .09f)), new Vector3(.08f, 2.6f, .08f), trim, false);
                        ResortBox(wing, "BalconyDivider", new Vector3(x + 1.9f, y + 1.6f, side * (depth / 2 + .65f)), new Vector3(.13f, 3.2f, 1.6f), wall, false);
                        if (floor == 0) continue;
                        ResortBox(wing, "BalconyGlass", new Vector3(x, y + .75f, side * (depth / 2 + 1.45f)), new Vector3(3.65f, 1, .08f), glass, false);
                        ResortBox(wing, "BalconyRail", new Vector3(x, y + 1.27f, side * (depth / 2 + 1.45f)), new Vector3(3.8f, .09f, .13f), brass, false);
                    }
                }
            // End faces also have windows, so the resort reads correctly from the sea and gardens.
            foreach (int side in new[] { -1, 1 })
                for (int floor = 0; floor < floors; floor++)
                    for (float z = -depth / 2 + 3; z < depth / 2 - 1; z += 4)
                        ResortBox(wing, "EndWindow", new Vector3(side * (width / 2 + .035f), floor * 3.6f + 1.8f, z), new Vector3(.07f, 2.4f, 2.7f), glass, false);
            CombineResortDetails(wing);
        }

        private static void ResortPavilion(Transform parent, Vector3 position, Material wall, Material trim)
        {
            var pavilion = new GameObject("GardenPavilion").transform;
            pavilion.SetParent(parent, false); pavilion.localPosition = position;
            ResortBox(pavilion, "Deck", new Vector3(0, .1f, 0), new Vector3(10, .2f, 8), wall);
            ResortBox(pavilion, "ShadeRoof", new Vector3(0, 3.5f, 0), new Vector3(11, .3f, 9), trim);
            foreach (float x in new[] { -4f, 4f }) foreach (float z in new[] { -3f, 3f })
                ResortBox(pavilion, "Post", new Vector3(x, 1.8f, z), new Vector3(.22f, 3.4f, .22f), wall);
        }

        private static void ResortPalm(Transform root, Vector3 position, int seed)
        {
            var palm = MeshyArt.Place("palm_tall", root, 7.5f + Mathf.Abs(seed % 3), position, seed * 67);
            if (palm == null) throw new InvalidOperationException("Existing resort palm model missing");
            var trunk = palm.AddComponent<CapsuleCollider>();
            trunk.height = 6; trunk.radius = .24f; trunk.center = Vector3.up * 3;
        }

        // Bake thousands of facade details into a few persistent meshes per wing/material.
        // Keep solid building/pool colliders separate. Never combine trees, text or the lobby.
        private static void CombineResortDetails(Transform root)
        {
            var details = root.GetComponentsInChildren<MeshFilter>().Where(m =>
                m.sharedMesh != null && m.GetComponent<Collider>() == null && !m.name.StartsWith("Combined_") &&
                m.sharedMesh.name == "Cube").ToArray();
            foreach (var group in details.GroupBy(m => m.GetComponent<MeshRenderer>().sharedMaterial))
            {
                var mesh = new Mesh { name = root.name + "_" + group.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Select(m => new CombineInstance { mesh = m.sharedMesh, transform = root.worldToLocalMatrix * m.transform.localToWorldMatrix }).ToArray());
                string dir = MeshDir + "/Resort"; Directory.CreateDirectory(dir);
                string path = dir + "/" + mesh.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) { AssetDatabase.CreateAsset(mesh, path); existing = mesh; }
                else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing); }
                var go = new GameObject("Combined_" + group.Key.name); go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = existing; go.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
            }
            foreach (var detail in details) Object.DestroyImmediate(detail.gameObject);
        }

        [MenuItem("PLEASE DON'T DROWN/Expand island two resort")]
        public static void ExpandResort()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Game in edit mode before expanding the resort.");
            var hotel = GameObject.Find("Environment/Hotel")?.transform;
            var terrain = GameObject.Find("Environment/BeachTerrain");
            if (hotel == null || terrain == null) throw new InvalidOperationException("Hotel/terrain missing; no scene edits applied.");
            var importedCrown = GameObject.Find("ResortCrown");
            if (importedCrown != null) Object.DestroyImmediate(importedCrown);
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-resort-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var mesh = GetBeachMesh();
            terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
            terrain.GetComponent<MeshCollider>().sharedMesh = null;
            terrain.GetComponent<MeshCollider>().sharedMesh = mesh;
            var so = new SerializedObject(terrain.GetComponent<Seabed>());
            Require(so, "_grid").objectReferenceValue = mesh;
            Require(so, "_min").vector2Value = new Vector2(TerrainMinX, TerrainMinZ);
            Require(so, "_step").floatValue = TerrainStep;
            Require(so, "_countX").intValue = Mathf.RoundToInt((TerrainMaxX - TerrainMinX) / TerrainStep) + 1;
            Require(so, "_countZ").intValue = Mathf.RoundToInt((TerrainMaxZ - TerrainMinZ) / TerrainStep) + 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            BuildResortExterior(hotel);
            foreach (var imported in scene.GetRootGameObjects())
                if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(imported)) == TripoHotelReview.ModelPath)
                    Object.DestroyImmediate(imported);
            var boat = Object.FindObjectsByType<PleaseDontDrown.Vehicles.Vehicle>(FindObjectsSortMode.None).FirstOrDefault(v => v.name == "PirateBoat");
            if (boat != null) boat.transform.position = OnWater(PirateBoatParked);
            BakeNavMeshes(true);
            VerifyResort(hotel, terrain);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureResort();
            Selection.activeGameObject = hotel.gameObject;
            Debug.Log("[Resort] PASS: expanded island and hotel, reception retained; navigation and previews saved.");
        }

        private static void VerifyResort(Transform hotel, GameObject terrain)
        {
            if (hotel.Find("ReceptionDesk") == null || hotel.Find("HotelDoor") == null || hotel.Find("PatientPoint") == null)
                throw new InvalidOperationException("Reception story anchors missing");
            Physics.SyncTransforms();
            // Lobby entry must remain open below the canopy; the tower must not close its back half.
            Vector3 from = hotel.TransformPoint(new Vector3(0, 1.5f, 11));
            if (Physics.Raycast(from, Vector3.back, out var hit, 12, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Reception entrance blocked by " + hit.collider.name);
            foreach (Vector2 p in new[] { new Vector2(-115, -350), new Vector2(145, -350), new Vector2(20, -455) })
                if (BeachHeight(p.x, p.y) < -.05f) throw new InvalidOperationException("Resort grounds below sea level");
            if (terrain.GetComponent<MeshCollider>().sharedMesh != terrain.GetComponent<MeshFilter>().sharedMesh)
                throw new InvalidOperationException("Terrain collision/render mismatch");
        }

        private static void CaptureResort()
        {
            Directory.CreateDirectory("Screenshots/Review/Resort");
            var camera = new GameObject("Temporary resort review camera").AddComponent<Camera>();
            camera.fieldOfView = 60; camera.farClipPlane = 1200; camera.nearClipPlane = .1f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.depthTextureMode = DepthTextureMode.Depth;
            camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().requiresDepthTexture = true;
            // The ocean is generated only at runtime. Generate it temporarily using the same mesh and shader.
            var preview = new GameObject("Temporary preview ocean");
            var seabed = preview.AddComponent<Seabed>();
            EditorUtility.CopySerialized(GameObject.Find("Environment/BeachTerrain").GetComponent<Seabed>(), seabed);
            var water = preview.AddComponent<WaterSurface>();
            EditorUtility.CopySerialized(Object.FindObjectsByType<WaterSurface>(FindObjectsSortMode.None).First(w => w != water), water);
            var waterSo = new SerializedObject(water);
            waterSo.FindProperty("_gridSize").floatValue = 600; waterSo.FindProperty("_gridResolution").intValue = 240;
            waterSo.ApplyModifiedPropertiesWithoutUndo();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var oldSeabed = typeof(Seabed).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var oldParams = Shader.GetGlobalVector("_PDD_SeabedParams");
            var oldRect = Shader.GetGlobalVector("_PDD_SeabedRect");
            var oldTexture = Shader.GetGlobalTexture("_PDD_Seabed");
            float fog = RenderSettings.fogDensity;
            var poses = new[] {
                ("overview", new Vector3(255, 205, -115), new Vector3(20, 0, -345)),
                ("arrival", new Vector3(-12, 7, -183), new Vector3(20, 12, -267)),
                ("gardens", new Vector3(-147, 42, -423), new Vector3(20, 12, -300)),
                ("reception", new Vector3(20, 2, -233), new Vector3(17, 1.7f, -246)),
                ("beach-club", new Vector3(-90, 7, -224), new Vector3(-48, 2.6f, -254)),
                ("beach-cabanas", new Vector3(135, 6, -225), new Vector3(85, 1.5f, -252)) };
            try
            {
                typeof(Seabed).GetMethod("Awake", flags).Invoke(seabed, null);
                typeof(WaterSurface).GetMethod("Precompute", flags).Invoke(water, null);
                typeof(WaterSurface).GetMethod("BuildSurface", flags).Invoke(water, null);
                foreach (var pose in poses)
                {
                    // Aerial review uses less haze so the full layout is visible. Arrival retains the game's haze.
                    RenderSettings.fogDensity = pose.Item1 == "overview" ? .0012f : fog;
                    camera.transform.position = pose.Item2; camera.transform.LookAt(pose.Item3);
                    typeof(WaterSurface).GetMethod("LateUpdate", flags).Invoke(water, null);
                    foreach (Transform part in preview.transform) part.position = new Vector3(camera.transform.position.x, 0, camera.transform.position.z);
                    Shader.SetGlobalVector("_PDD_OceanCenter", new Vector4(camera.transform.position.x, camera.transform.position.z, 192, 294));
                    var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
                    var previous = RenderTexture.active;
                    var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    try {
                        camera.targetTexture = target; camera.Render(); camera.Render(); RenderTexture.active = target;
                        texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
                        File.WriteAllBytes("Screenshots/Review/Resort/" + pose.Item1 + ".png", texture.EncodeToPNG());
                    }
                    finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target); }
                }
            }
            finally
            {
                RenderSettings.fogDensity = fog;
                var texture = typeof(Seabed).GetField("_texture", flags).GetValue(seabed) as Texture2D;
                // Restore the static before destruction so this preview doesn't affect the live edit scene.
                typeof(Seabed).GetField("_instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, oldSeabed);
                Shader.SetGlobalVector("_PDD_SeabedParams", oldParams); Shader.SetGlobalVector("_PDD_SeabedRect", oldRect); Shader.SetGlobalTexture("_PDD_Seabed", oldTexture);
                if (texture != null) Object.DestroyImmediate(texture);
                foreach (var filter in preview.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(filter.sharedMesh);
                Object.DestroyImmediate(preview); Object.DestroyImmediate(camera.gameObject);
            }
        }

        public static void ReviewResort()
        {
            VerifyResort(GameObject.Find("Environment/Hotel").transform, GameObject.Find("Environment/BeachTerrain"));
            CaptureResort();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Resort] Visual and entrance checks passed.");
        }

        public static void BuildResortPlayer()
        {
            ReviewResort();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = "Builds/Win64/PleaseDontDrown.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Resort Windows build failed: " + report.summary.totalErrors + " errors");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[Resort] Windows build succeeded.");
        }
    }

    // One-shot request allows the already-open editor to apply the change without losing unsaved work.
    [InitializeOnLoad]
    internal static class ResortEditorRequest
    {
        static ResortEditorRequest() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            const string request = "Logs/resort-request.txt", result = "Logs/resort-result.txt";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (action == "build") GameSceneBuilder.BuildResortPlayer();
                else if (action == "review") GameSceneBuilder.ReviewResort();
                else GameSceneBuilder.ExpandResort();
                File.WriteAllText(result, "PASS: " + action);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText(result, "FAIL: " + e); }
        }
    }
}
