using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PleaseDontDrown.Story;
using PleaseDontDrown.Items;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        private static bool IsHotelBuilding(LODGroup g) => g.name is "CentralTower" or "WestWing" or "EastWing" or "GardenWing";

        // Shared by the full generator and the live scene update, so later rebuilds retain the finish.
        private static Material FinishedHotelMaterial()
        {
            const string folder = "Assets/TripoModels/apartment_building_3d_model/apartment_building_3d_model.fbm/";
            const string atlas = folder + "apartment_building_3d_model_basecolor.JPEG";
            var importer = AssetImporter.GetAtPath(atlas) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Hotel colour texture missing");
            var platform = importer.GetDefaultPlatformTextureSettings();
            bool reimport = importer.maxTextureSize != 4096 || importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8 ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.mipmapEnabled || !importer.sRGBTexture || platform.maxTextureSize != 4096;
            importer.maxTextureSize = 4096; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = true; importer.sRGBTexture = true;
            importer.mipMapBias = 0; importer.wrapMode = TextureWrapMode.Clamp;
            platform.maxTextureSize = 4096; platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
            if (reimport) importer.SaveAndReimport();
            var material = GetMaterial("ResortTripoFacade", Color.white, metallic: 0, smoothness: .16f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(atlas));
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(atlas));
            // The AI-generated normal/metallic maps make flat walls sparkle and wobble.
            // Keep the texture and architectural mesh; use stable matte facade shading.
            material.SetTexture("_MetallicGlossMap", null); material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.SetTexture("_BumpMap", null); material.DisableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale", 0);
            material.SetFloat("_SpecularHighlights", 0); material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.enableInstancing = true; EditorUtility.SetDirty(material);
            return material;
        }

        [MenuItem("PLEASE DON'T DROWN/Finish one hotel and open reception")]
        public static void FinishOneHotel()
        {
            var scene = SceneManager.GetActiveScene(); var hotel = GameObject.Find("Environment/Hotel")?.transform;
            if (scene.path != ScenePath || hotel == null || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Game in edit mode before finishing the hotel");
            var groups = hotel.GetComponentsInChildren<LODGroup>(true).Where(IsHotelBuilding).ToArray();
            var keep = groups.FirstOrDefault(g => g.name == "CentralTower");
            if (keep == null) throw new InvalidOperationException("Main hotel is missing; no scene edits made");
            for (int i = 0; i < 3; i++) if (LoadProp("resort_hotel_reception_lod" + i) == null) throw new InvalidOperationException("Hotel LOD not imported: " + i);
            for (int i = 0; i < 3; i++) if (LoadProp("resort_hotel_clean_reception_lod" + i) == null) throw new InvalidOperationException("Straightened hotel LOD not imported: " + i);
            if (LoadProp("resort_reception_trim") == null) throw new InvalidOperationException("Reception trim missing");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-hotel-finish-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            int removed = 0;
            foreach (var group in groups) if (group != keep) { Object.DestroyImmediate(group.gameObject); removed++; }
            // Tripo's bridge also adds raw FBX copies to the scene. They must not sit on top of game LODs.
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Reverse())
                {
                    if (t == null || t == hotel || t.IsChildOf(keep.transform)) continue;
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                    if (path.StartsWith("Assets/TripoModels/apartment_building_3d_model", StringComparison.Ordinal) && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    {
                        var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
                        if (instance != null) { Object.DestroyImmediate(instance); removed++; }
                    }
                }
            foreach (var t in hotel.GetComponentsInChildren<Transform>(true).ToArray())
                if (t != null && t.name is "WestWingShell" or "EastWingShell" or "GardenWingShell") Object.DestroyImmediate(t.gameObject);
            keep.gameObject.SetActive(true);
            keep.transform.localPosition = new Vector3(0, 0, -4); keep.transform.localRotation = Quaternion.identity;
            keep.transform.localScale = Vector3.one * (40 / .780731201f);
            RefreshPolishedHotel(keep);
            keep.fadeMode = LODFadeMode.None; keep.animateCrossFading = false; keep.ForceLOD(-1);
            RestoreMissingHotelReception(hotel);
            BuildFinishedHotelEntrance(hotel);
            CheckHotelEntranceGeometry(hotel, keep);
            VerifyResort(hotel, GameObject.Find("Environment/BeachTerrain"));
            if (hotel.GetComponentsInChildren<LODGroup>(true).Count(IsHotelBuilding) != 1) throw new InvalidOperationException("Extra hotel remains");
            BakeNavMeshes(true); AssignSceneIds(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            TripoHotelReview.ReportScene(); CaptureResort();
            File.WriteAllText("Logs/hotel-finish-result.txt", $"PASS: one hotel; removed {removed} extra hotel instances; clean facade and stable filtered textures; reception objects/links restored; lobby and entrance geometry/collision checks passed; navigation and scene saved.");
            Debug.Log("[HotelFinish] One polished hotel with a walkable reception entrance saved.");
        }

        private static void RestoreMissingHotelReception(Transform hotel)
        {
            var story = GameObject.Find("Story")?.transform;
            var director = Object.FindFirstObjectByType<StoryDirector>();
            if (story == null || director == null) throw new InvalidOperationException("Story root/director missing; cannot restore reception links");
            // Generate only a temporary hotel template, then transfer missing lobby pieces.
            // Retain the live entrance, patient and first-aid anchors and the existing story director.
            if (hotel.Find("ReceptionDesk") == null || hotel.Find("Floor") == null || hotel.Find("HospitalBed") == null)
            {
                var npc = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
                var templateEnv = new GameObject("TemporaryHotelTemplate").transform;
                var templateStory = new GameObject("TemporaryHotelStoryTemplate").transform;
                try
                {
                    var template = BuildHotel(templateEnv,npc,templateStory,out var receptionist,out var reception,out var desk,out var patient,out var firstAid,out var door);
                    foreach (Transform child in template.Cast<Transform>().ToArray())
                    {
                        if (child.name is "Upper" or "UpperWindow" or "Roof" or "Sign" or "Window") continue;
                        if (hotel.Find(child.name) == null) child.SetParent(hotel,false);
                    }
                    var existingNpc = Object.FindObjectsByType<StoryNpc>(FindObjectsSortMode.None)
                        .FirstOrDefault(n => n.name=="Receptionist" && !n.transform.IsChildOf(templateStory));
                    if (existingNpc == null)
                    {
                        receptionist.transform.SetParent(story,true);
                        receptionist.transform.position=hotel.TransformPoint(new Vector3(-6,.3f,.9f));
                        existingNpc = receptionist;
                    }
                    SetRef(director,"_receptionist",existingNpc);
                }
                finally { Object.DestroyImmediate(templateEnv.gameObject); Object.DestroyImmediate(templateStory.gameObject); }
            }
            var receptionDesk=hotel.Find("ReceptionDesk"); var patientPoint=hotel.Find("PatientPoint");
            var bed=hotel.Find("HospitalBed")?.GetComponent<HospitalBed>();
            if (bed != null) SetRef(bed,"_patientPoint",patientPoint);
            SetRef(director,"_receptionDesk",receptionDesk);
            SetRef(director,"_reception",receptionDesk.GetComponent<ShopCounter>());
            SetRef(receptionDesk.GetComponent<ShopCounter>(),"_spawnPoint",hotel.Find("ShopSpawn"));
            SetRef(director,"_hotelDoor",hotel.Find("HotelDoor"));
            SetRef(director,"_infirmary",patientPoint);
            SetRef(director,"_firstAid",hotel.Find("FirstAidShelf"));
        }

        private static void BuildFinishedHotelEntrance(Transform hotel)
        {
            foreach (string name in new[] { "ReceptionPolish", "HotelEntranceFinish" })
            { var old = hotel.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); }
            // Replace overlapping decorative panes and the old solid canopy with one clean entrance.
            foreach (Transform t in hotel.Cast<Transform>().ToArray()) if (t.name == "Window") Object.DestroyImmediate(t.gameObject);
            var exterior = hotel.Find("ResortExterior");
            if (exterior != null)
                foreach (Transform t in exterior.Cast<Transform>().ToArray())
                    if (t.name is "ArrivalCanopy" or "CanopyColumn") Object.DestroyImmediate(t.gameObject);
            var trim = new GameObject("ReceptionPolish").transform; trim.SetParent(hotel, false); PropModel("resort_reception_trim", trim);
            var root = new GameObject("HotelEntranceFinish").transform; root.SetParent(hotel, false);
            var ivory = GetMaterial("HotelFinishedIvory", new Color(.96f, .93f, .85f), smoothness: .12f);
            var timber = GetMaterial("HotelFinishedTimber", new Color(.30f, .20f, .12f), smoothness: .28f);
            var brass = GetMaterial("HotelFinishedBrass", new Color(.76f, .60f, .30f), metallic: .5f, smoothness: .45f);
            var stone = FinishedLobbyFloorMaterial();
            foreach (string name in new[] { "WallBack", "WallLeft", "WallRight", "WallFrontL", "WallFrontR", "Ceiling" })
                if (hotel.Find(name)?.GetComponent<Renderer>() is Renderer renderer) renderer.sharedMaterial = ivory;
            if (hotel.Find("Floor")?.GetComponent<Renderer>() is Renderer floor) floor.sharedMaterial = stone;
            if (hotel.Find("ReceptionDesk")?.GetComponent<Renderer>() is Renderer desk) desk.sharedMaterial = ivory;
            if (hotel.Find("DeskTop")?.GetComponent<Renderer>() is Renderer top) top.sharedMaterial = timber;
            // A 10m vestibule joins the imported front face to the original reception door.
            ResortBox(root, "EntranceDeck", new Vector3(0, .15f, 11), new Vector3(4, .30f, 10), stone);
            for (int i = 0; i < 3; i++)
                ResortBox(root, "EntranceStep", new Vector3(0, .05f * (3-i), 16.3f+i*.55f), new Vector3(4.4f, .1f*(3-i), .65f), ivory);
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(root, "VestibuleBase", new Vector3(side*2.2f, .65f, 11), new Vector3(.20f, .70f, 10), ivory);
                ResortBox(root, "VestibuleTopRail", new Vector3(side*2.2f, 1.1f, 11), new Vector3(.24f, .10f, 10), brass, false);
                ResortBox(root, "EntrancePortalJamb", new Vector3(side*2.2f, 2.05f, 16), new Vector3(.30f, 3.5f, .30f), ivory);
            }
            ResortBox(root, "EntrancePortalLintel", new Vector3(0, 3.75f, 16), new Vector3(4.7f, .30f, .35f), ivory);
            // Ceiling collision and real canopy support replace removed overlapping boxes.
            Collider(root, "ArrivalCanopyRoof", new Vector3(0, 5, 10), new Vector3(22.6f, .45f, 11.6f));
            foreach (float x in new[] { -9f, 9f }) Collider(root, "ArrivalCanopyColumn", new Vector3(x, 2.5f, 14), new Vector3(.71f, 5, .71f));
            for (int i = 0; i < 3; i++)
            {
                var light = new GameObject("WarmEntranceLight").AddComponent<Light>(); light.transform.SetParent(root, false);
                light.transform.localPosition = new Vector3(0, 3.2f, 8+i*3.5f); light.type = LightType.Point;
                light.color = new Color(1, .85f, .62f); light.intensity = .65f; light.range = 6; light.shadows = LightShadows.None;
            }
            var lobbyLight = hotel.Find("LobbyLight")?.GetComponent<Light>();
            if (lobbyLight != null) { lobbyLight.color = new Color(1, .88f, .73f); lobbyLight.intensity = 1.2f; lobbyLight.range = 17; }
        }

        private static Material FinishedLobbyFloorMaterial()
        {
            const string path = "Assets/_Game/Art/Props/hotel_limestone_tile.png";
            if (!File.Exists(path))
            {
                const int size = 256; var image = new Texture2D(size, size, TextureFormat.RGB24, false);
                var pixels = new Color32[size*size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    bool grout = x<2 || y<2 || x>=size-2 || y>=size-2;
                    byte value = (byte)(grout ? 184 : 222 + ((x*17+y*11)%3));
                    pixels[y*size+x] = new Color32(value, (byte)(value-6), (byte)(value-19), 255);
                }
                image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.filterMode != FilterMode.Trilinear || importer.anisoLevel != 8)
            { importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=8; importer.wrapMode=TextureWrapMode.Repeat; importer.SaveAndReimport(); }
            var material = GetMaterial("HotelFinishedLimestone", Color.white, smoothness: .12f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path)); material.SetTextureScale("_BaseMap", new Vector2(12,6));
            EditorUtility.SetDirty(material); return material;
        }

        private static void CheckHotelEntranceGeometry(Transform hotel, LODGroup keep)
        {
            // A collider-only check cannot detect an opaque imported wall. Test LOD0's
            // actual triangles as well, then remove the temporary mesh colliders.
            var temporary = keep.transform.Find("HotelLOD0").GetComponentsInChildren<MeshFilter>().Select(f =>
            { var collider=f.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=f.sharedMesh; return collider; }).ToArray();
            try
            {
                Physics.SyncTransforms();
                foreach (float x in new[] { -.65f, 0, .65f }) foreach (float y in new[] { 1.0f, 1.7f, 2.4f })
                {
                    Vector3 start=hotel.TransformPoint(new Vector3(x,y,17.5f)); Vector3 direction=-hotel.forward;
                    if (Physics.SphereCast(start,.30f,direction,out var hit,16,~0,QueryTriggerInteraction.Ignore))
                        throw new InvalidOperationException($"Hotel entrance blocked at x={x}, y={y} by {hit.collider.name}");
                }
            }
            finally { foreach(var collider in temporary) Object.DestroyImmediate(collider); }
        }
    }

    [InitializeOnLoad]
    internal static class HotelFinishRequest
    {
        static HotelFinishRequest() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            const string path="Logs/hotel-finish-request.txt";
            if (!File.Exists(path)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(path);
            try { GameSceneBuilder.FinishOneHotel(); }
            catch(Exception e) { Debug.LogException(e); File.WriteAllText("Logs/hotel-finish-result.txt","FAIL "+e); }
        }
    }
}
