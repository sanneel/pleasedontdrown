using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        private static bool TryBuildTripoResort(Transform parent)
        {
            string sourceMaterial = "Assets/TripoModels/apartment_building_3d_model/Materials/apartment_building_3d_model.mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterial);
            string[] models = Enumerable.Range(0, 3).Select(i => $"Assets/_Game/Art/Props/resort_hotel_lod{i}.glb").ToArray();
            string[] reception = Enumerable.Range(0, 3).Select(i => $"Assets/_Game/Art/Props/resort_hotel_reception_lod{i}.glb").ToArray();
            string[] cleanReception = Enumerable.Range(0, 3).Select(i => $"Assets/_Game/Art/Props/resort_hotel_clean_reception_lod{i}.glb").ToArray();
            if (cleanReception.All(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) != null)) reception = cleanReception;
            if (source == null || models.Concat(reception).Any(p => AssetDatabase.LoadAssetAtPath<GameObject>(p) == null)) return false;
            var material = FinishedHotelMaterial();
            ImportedHotelBuilding(parent, "CentralTower", reception, material, new Vector3(0, 0, -4), 40, true);
            Debug.Log("[TripoHotel] Single optimized hotel placed; original FBX preserved.");
            return true;
        }

        private static void ImportedHotelBuilding(Transform parent, string name, string[] paths, Material material,
            Vector3 position, float height, bool hasReception)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false); root.localPosition = position;
            Bounds bounds = default;
            var lods = new LOD[3]; float[] transitions = { .28f, .075f, .012f };
            for (int i = 0; i < paths.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                var model = Object.Instantiate(asset, root); model.name = "HotelLOD" + i;
                if (i == 0) bounds = TripoHotelReview.ModelBounds(root);
                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) throw new InvalidOperationException("Hotel LOD has no renderers: " + paths[i]);
                foreach (var r in renderers)
                    r.sharedMaterials = paths[i].Contains("_clean_") ? r.sharedMaterials.Select(PropPaint).ToArray() : new[] { material };
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                lods[i] = new LOD(transitions[i], renderers);
            }
            float scale = height / bounds.size.y;
            root.localScale = Vector3.one * scale;
            var group = root.gameObject.AddComponent<LODGroup>(); group.SetLODs(lods); group.RecalculateBounds();
            float width = bounds.size.x * scale, depth = bounds.size.z * scale;
            if (hasReception)
            {
                // Collision surrounds the real lobby rather than sealing its entrance with a full tower box.
                Collider(parent, "HotelUpperShell", new Vector3(0, (height + 3.7f) * .5f, -7), new Vector3(width, height - 3.7f, 26));
                float wingWidth = (width - 24) * .5f;
                foreach (int side in new[] { -1, 1 })
                    Collider(parent, "HotelGroundSide", new Vector3(side * (12 + wingWidth * .5f), 1.85f, -7), new Vector3(wingWidth, 3.7f, 26));
                Collider(parent, "HotelGroundBack", new Vector3(0, 1.85f, -13.1f), new Vector3(24, 3.7f, 13.8f));
            }
            else
            {
                // Exclude the projecting entrance canopy from the solid guest-building footprint.
                Collider(parent, name + "Shell", position + new Vector3(0, height * .5f, -depth * .12f), new Vector3(width, height, depth * .7f));
            }
        }
    }
}
