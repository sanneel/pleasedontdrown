using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The spots the user marked in red on screenshots (11 October 2026), fixed in the saved Game scene (the builder code
    /// does the same on a full rebuild):
    /// - The hotel's facade is now clean massing (ArtSource/Tools/hotel_massing.py): its walls use three of the five
    ///   materials, so every hotel renderer gets its materials again from the model, and the collision meshes are re-linked.
    /// - Milo's pier: the walkway's planks ran on under the cross dock in the same plane and flickered through it.
    /// - Every walk-in Tripo house (and the tavern): a doorstep closes the strip the doorway cut out of the porch deck, and
    ///   the porch collider is at the deck's height.
    /// Island two and three navigation is baked again. Batch: -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.FixMarkedSpotsBatch
    /// </summary>
    public static partial class GameSceneBuilder
    {
        [MenuItem("PLEASE DON'T DROWN/Fix marked spots (hotel, pier, shack doors)")]
        public static void FixMarkedSpots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RefreshCleanHotel();
            FixRentalPier();
            FixShackPorches();
            BakeNavMeshes(only: "Island2");
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[MarkedSpots] done, scene saved");
        }

        public static void FixMarkedSpotsBatch()
        {
            try { FixMarkedSpots(); }
            catch (Exception e)
            {
                Debug.LogError($"[MarkedSpots] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void RefreshCleanHotel()
        {
            LODGroup tower = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Single(g => g.name == "CentralTower");
            int renderers = 0;
            for (int i = 0; i < 3; i++)
            {
                GameObject model = LoadProp("resort_hotel_real_lod" + i) ?? throw new InvalidOperationException("Hotel LOD not imported: " + i);
                Transform level = tower.transform.Find("HotelLOD" + i) ?? throw new InvalidOperationException("No HotelLOD" + i);
                foreach (MeshFilter filter in level.GetComponentsInChildren<MeshFilter>())
                {
                    MeshFilter source = model.GetComponentsInChildren<MeshFilter>().Single(f => f.name == filter.name);
                    filter.sharedMesh = source.sharedMesh;
                    filter.GetComponent<MeshRenderer>().sharedMaterials =
                        source.GetComponent<MeshRenderer>().sharedMaterials.Select(RealHotelMaterial).ToArray();
                    renderers++;
                }
            }
            tower.RecalculateBounds();
            // ApplyRealisticHotel's collision: the clean details at full detail (LOD0), then the facade (LOD1).
            MeshCollider[] shells = tower.GetComponentsInChildren<MeshCollider>().Where(c => c.name == "HotelCollision").ToArray();
            if (shells.Length != 2) throw new InvalidOperationException("Expected two hotel collision shells, found " + shells.Length);
            if (shells[0].sharedMesh != null && !shells[0].sharedMesh.name.Contains("Windows")) shells = shells.Reverse().ToArray();
            shells[0].sharedMesh = MeshOf("resort_hotel_real_lod0", "HotelWindows0");
            shells[1].sharedMesh = MeshOf("resort_hotel_real_lod1", "HotelLOD1");
            Debug.Log($"[MarkedSpots] hotel: {renderers} renderers re-materialled, collision re-linked " +
                      $"({shells[1].sharedMesh.triangles.Length / 3} facade triangles)");
        }

        private static Mesh MeshOf(string model, string part) =>
            LoadProp(model).GetComponentsInChildren<MeshFilter>().Single(f => f.name == part).sharedMesh;

        private static void FixRentalPier()
        {
            Transform dock = GameObject.Find("Environment")?.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MilosRentalDock")
                             ?? throw new InvalidOperationException("No MilosRentalDock");
            Transform[] planks = dock.Cast<Transform>().Where(t => t.name == "PierPlank").OrderByDescending(t => t.localPosition.z).ToArray();
            if (planks.Length != 48 && planks.Length != PierPlanks) throw new InvalidOperationException("Unexpected pier planks: " + planks.Length);
            for (int i = PierPlanks; i < planks.Length; i++) Object.DestroyImmediate(planks[i].gameObject);
            Transform last = planks[PierPlanks - 1];
            last.localPosition = new Vector3(last.localPosition.x, last.localPosition.y, PierPlankZ(PierPlanks - 1));
            last.localScale = new Vector3(last.localScale.x, last.localScale.y, PierPlankDepth(PierPlanks - 1));
            Debug.Log($"[MarkedSpots] pier: {planks.Length - PierPlanks} planks under the cross dock removed, the last one fits the gap");
        }

        private static void FixShackPorches()
        {
            int houses = 0;
            foreach (Transform house in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                         .Where(t => t.name.StartsWith("PirateHouse_") || t.name == "PirateTavern").ToArray())
            {
                Transform model = house.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Tripo_pirate_"));
                if (model == null) continue;
                foreach (Transform old in house.Cast<Transform>().Where(t => t.name is "Doorstep" or "Porch" or "PorchFront").ToArray())
                    Object.DestroyImmediate(old.gameObject);
                string shack = model.name.Substring("Tripo_".Length);
                float scale = model.localScale.x;
                ShackRoom r = TripoShackRoom(shack);
                float porchEnd = r.porchZ > 0f ? r.porchZ * scale : TripoBounds(model, house).max.z - 0.15f;
                ShackPorch(house, r, scale, porchEnd);
                houses++;
            }
            Debug.Log($"[MarkedSpots] doorsteps and deck-high porches: {houses} houses");
        }
    }
}
