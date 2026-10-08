using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        [MenuItem("PLEASE DON'T DROWN/Polish single hotel and resort beach")]
        public static void PolishSingleResort()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var hotel = GameObject.Find("Environment/Hotel")?.transform;
            if (scene.path != ScenePath || hotel == null || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Game in edit mode before polishing the resort.");
            var buildings = hotel.GetComponentsInChildren<LODGroup>(true).Where(g => IsHotelBuilding(g) && g.gameObject.activeInHierarchy).ToArray();
            if (buildings.Length != 1) throw new InvalidOperationException($"Expected your one remaining hotel; found {buildings.Length}. No scene edits made.");
            foreach (string model in new[] { "resort_beach_bar", "resort_cabana", "resort_lounge_set", "resort_parasol", "resort_planter", "resort_reception_trim" })
                if (LoadProp(model) == null) throw new InvalidOperationException("Blender asset not imported: " + model);
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-single-resort-polish-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            foreach (var imported in scene.GetRootGameObjects())
                if (AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(imported)) == "Assets/TripoModels/CoralBeachClubEmblem/CoralBeachClubEmblem.obj")
                    Object.DestroyImmediate(imported);
            RefreshPolishedHotel(buildings[0]);
            foreach (var c in hotel.GetComponentsInChildren<BoxCollider>(true))
                if (c.name is "WestWingShell" or "EastWingShell" or "GardenWingShell")
                    if (hotel.GetComponentsInChildren<Transform>(true).All(t => t.name != c.name.Replace("Shell", "") || !t.gameObject.activeInHierarchy)) Object.DestroyImmediate(c.gameObject);
            // Overlay clean Blender-authored trim on the lobby; its existing story interactions and colliders remain.
            BuildFinishedHotelEntrance(hotel);
            BuildRichResortBeach(hotel);
            BakeNavMeshes(true); AssignSceneIds(scene);
            VerifyResort(hotel, GameObject.Find("Environment/BeachTerrain"));
            if (hotel.GetComponentsInChildren<LODGroup>(true).Count(g => IsHotelBuilding(g) && g.gameObject.activeInHierarchy) != 1) throw new InvalidOperationException("Hotel count changed");
            Physics.SyncTransforms();
            Vector3 barApproach = hotel.TransformPoint(new Vector3(-68, 1.7f, 3));
            if (Physics.Raycast(barApproach, Vector3.back, out var hit, 7, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Beach bar customer approach blocked by " + hit.collider.name);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BuildResortPlayer();
            Debug.Log("[ResortPolish] PASS: one hotel retained, Blender geometry/trim refreshed, richer beach and working large bar, Windows build succeeded.");
        }

        private static void RefreshPolishedHotel(LODGroup group)
        {
            bool reception = group.name == "CentralTower";
            var existing = group.GetLODs().SelectMany(l => l.renderers).FirstOrDefault(r => r != null);
            var material = existing != null ? existing.sharedMaterial : AssetDatabase.LoadAssetAtPath<Material>(MaterialDir + "/ResortTripoFacade.mat");
            if (material == null) throw new InvalidOperationException("Hotel material missing");
            material = FinishedHotelMaterial();
            var position = group.transform.localPosition; var rotation = group.transform.localRotation; var scale = group.transform.localScale;
            foreach (Transform child in group.transform.Cast<Transform>().ToArray())
                if (child.name.StartsWith("HotelLOD")) Object.DestroyImmediate(child.gameObject);
            var levels = new LOD[3]; float[] transitions = { .28f, .075f, .012f };
            for (int i = 0; i < 3; i++)
            {
                bool clean = reception && File.Exists(PropDir + "/resort_hotel_clean_reception_lod" + i + ".glb");
                string model = clean ? "resort_hotel_clean_reception_lod" + i : "resort_hotel_" + (reception ? "reception_" : "") + "lod" + i;
                var asset = LoadProp(model); if (asset == null) throw new InvalidOperationException("Hotel LOD missing: " + model);
                var copy = Object.Instantiate(asset, group.transform); copy.name = "HotelLOD" + i;
                var renderers = copy.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers)
                    renderer.sharedMaterials = clean ? renderer.sharedMaterials.Select(PropPaint).ToArray() : new[] { material };
                levels[i] = new LOD(transitions[i], renderers);
            }
            group.SetLODs(levels); group.RecalculateBounds();
            if (position != group.transform.localPosition || rotation != group.transform.localRotation || scale != group.transform.localScale)
                throw new InvalidOperationException("Hotel placement changed during polish");
        }

        private static void BuildRichResortBeach(Transform hotel)
        {
            var old = hotel.Find("ResortBeachLife"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("ResortBeachLife").transform; root.SetParent(hotel, false);
            var stone = GetMaterial("ResortStone", new Color(.78f, .73f, .62f));
            var ivory = GetMaterial("ResortIvory", new Color(.96f, .91f, .8f));
            var teal = GetMaterial("ResortTeal", new Color(.12f, .43f, .47f));
            var wood = GetMaterial("ResortDeckWood", new Color(.62f, .41f, .23f));
            ResortBox(root, "BeachPromenadeWest", new Vector3(-57, .04f, 1), new Vector3(70, .08f, 5), stone);
            ResortBox(root, "BeachPromenadeEast", new Vector3(62, .04f, 1), new Vector3(80, .08f, 5), stone);
            for (int i = 0; i < 4; i++)
            {
                float x = 42 + i * 15;
                BeachProp(root, "Cabana" + i, "resort_cabana", new Vector3(x, 0, -8));
                Collider(root, "CabanaDeck", new Vector3(x, .08f, -8), new Vector3(8, .16f, 7));
                foreach (float dx in new[] { -3.5f, 3.5f }) foreach (float dz in new[] { -3f, 3f })
                    Collider(root, "CabanaPost", new Vector3(x + dx, 1.8f, -8 + dz), new Vector3(.2f, 3.6f, .2f));
                // Daybed collision stays low enough to step over, and leaves the open front unobstructed.
                Collider(root, "Daybed", new Vector3(x, .35f, -9.2f), new Vector3(3.3f, .7f, 2.4f));
                var lounge = BeachProp(root, "Lounge" + i, "resort_lounge_set", new Vector3(x, 0, 9), i % 2 == 0 ? 0 : 12);
                foreach (float dx in new[] { -1.1f, 1.1f }) Collider(lounge.transform, "LoungeSeat", new Vector3(dx, .4f, 0), new Vector3(.9f, .8f, 2.3f));
                BeachProp(root, "Parasol" + i, "resort_parasol", new Vector3(x, 0, 9));
                Collider(root, "ParasolPole", new Vector3(x, 1.4f, 9), new Vector3(.1f, 2.8f, .1f));
            }
            foreach (float x in new[] { -103f, -88f, -37f, 34f, 106f })
            {
                ResortPalm(root, new Vector3(x, 0, -3), (int)x);
                BeachProp(root, "FlowerPlanter", "resort_planter", new Vector3(x + 3, 0, 1));
            }
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++)
                {
                    float x = side * (31 + i * 23);
                    BeachProp(root, "GardenSeat", "resort_lounge_set", new Vector3(x, 0, -41), 180);
                    BeachProp(root, "GardenPlanter", "resort_planter", new Vector3(x, 0, -35));
                }
            // Arrival planting frames the hotel's front without covering the dock approach.
            foreach (float x in new[] { -16f, 16f })
                for (int i = 0; i < 3; i++) BeachProp(root, "ArrivalPlanter", "resort_planter", new Vector3(x, 0, 11 + i * 5));
            BuildLargeResortBar(root);
            TextMesh sign = WorldText(root, "BeachClubSign", new Vector3(-68, 4.9f, -1.8f), "CORAL BEACH CLUB", 90, .085f, ivory.color);
            sign.transform.localRotation = Quaternion.Euler(0, 180, 0);
            // Warm lamps around the club's tables, with no dynamic shadows.
            foreach (float x in new[] { -78f, -58f })
            {
                var light = new GameObject("BeachClubLantern").AddComponent<Light>(); light.transform.SetParent(root, false);
                light.transform.localPosition = new Vector3(x, 3.4f, -9); light.type = LightType.Point;
                light.color = new Color(1, .72f, .38f); light.intensity = .8f; light.range = 9; light.shadows = LightShadows.None;
            }
        }

        private static GameObject BeachProp(Transform root, string name, string model, Vector3 position, float yaw = 0)
        {
            var marker = new GameObject(name).transform; marker.SetParent(root, false); marker.localPosition = position;
            marker.localRotation = Quaternion.Euler(0, yaw, 0);
            var visual = PropModel(model, marker); if (visual == null) throw new InvalidOperationException("Beach model missing: " + model);
            if (model == "resort_planter") Collider(marker, "PlantBox", new Vector3(0, .37f, 0), new Vector3(2, .74f, 1.15f));
            return marker.gameObject;
        }

        private static void BuildLargeResortBar(Transform parent)
        {
            var root = new GameObject("LargeBeachBar").transform; root.SetParent(parent, false); root.localPosition = new Vector3(-68, 0, -10);
            TagSurface(root.gameObject, SurfaceKind.Wood); PropModel("resort_beach_bar", root);
            var emblemAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TripoModels/CoralBeachClubEmblem/CoralBeachClubEmblem.obj");
            if (emblemAsset != null)
            {
                var emblem = Object.Instantiate(emblemAsset, root); emblem.name = "CoralBeachEmblem";
                emblem.transform.localPosition = new Vector3(0, 4.4f, 7.0f);
                foreach (var renderer in emblem.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = GetMaterial("ResortBrass", new Color(.75f, .57f, .25f), metallic: .65f, smoothness: .65f);
            }
            Collider(root, "ClubDeck", new Vector3(0, .08f, 0), new Vector3(26, .16f, 16));
            Collider(root, "ClubCounter", new Vector3(0, .67f, 5), new Vector3(18, 1.18f, .9f));
            Collider(root, "ClubBackShelf", new Vector3(0, 1.55f, -4.8f), new Vector3(17, 3.1f, .7f));
            Collider(root, "ClubRoof", new Vector3(0, 5.95f, 0), new Vector3(27, 1.1f, 17));
            foreach (float x in new[] { -10.6f, 10.6f }) foreach (float z in new[] { -1.2f, 1.4f })
                Collider(root, "ClubBench", new Vector3(x, .58f, z), new Vector3(1.8f, .85f, .85f));
            foreach (float x in new[] { -11.8f, 11.8f }) foreach (float z in new[] { -6.8f, 6.8f })
                Collider(root, "ClubPost", new Vector3(x, 2.25f, z), new Vector3(.25f, 4.5f, .25f));
            root.gameObject.AddComponent<NetworkObject>();
            BuildBarista(root);
            root.Find("Barista").localPosition = new Vector3(.25f, .158f, 4.4f);
            for (int i = 0; i < 4; i++) root.Find("ServeSpot" + i).localPosition = new Vector3(-2.4f + i * 1.6f, 1.36f, 5.15f);
            root.Find("WipeFrom").localPosition = new Vector3(-.25f, 1.19f, 4.78f);
            root.Find("WipeTo").localPosition = new Vector3(.75f, 1.19f, 4.78f);
            foreach (string item in new[] { "Beer", "Coconut" })
            {
                var rack = root.gameObject.AddComponent<ItemRack>(); var spots = new List<Object>();
                for (int i = 0; i < 3; i++)
                {
                    var spot = new GameObject(item + "Spot" + i).transform; spot.SetParent(root, false);
                    spot.localPosition = new Vector3((item == "Beer" ? -7 : 3) + i * 1.5f, 1.36f, 5); spots.Add(spot);
                }
                SetField(rack, "_itemName", p => p.stringValue = item); SetRefs(rack, "_spots", spots.ToArray());
                SetField(rack, "_worldCap", p => p.intValue = item == "Beer" ? 12 : 8);
            }
        }
    }

    [InitializeOnLoad]
    internal static class SingleResortPolishRequest
    {
        static SingleResortPolishRequest() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            const string request = "Logs/resort-single-polish-request.txt";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try
            {
                TripoHotelReview.ReportScene();
                GameSceneBuilder.PolishSingleResort();
                File.WriteAllText("Logs/resort-single-polish-result.txt", "PASS");
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Logs/resort-single-polish-result.txt", "FAIL " + e); }
        }
    }
}
