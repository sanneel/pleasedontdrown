using System;
using System.IO;
using PleaseDontDrown.Story;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Meshy props that keep a painted texture: cleaned and re-baked by ArtSource/Tools/polish_prop.py (one mesh,
    /// standing on y 0, a tidy colour texture and sometimes a normal map). Here they get a plain matte material and
    /// go into the scene or into Resources. Without the GLB the greybox stays.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string RobberBackpackPath = "Assets/_Game/Resources/" + RobberBag.ResourceName + ".prefab";
        private static readonly string[] UmbrellaModels = { "umbrella_red", "umbrella_teal", "umbrella_yellow" };

        /// <summary>
        /// The model with our own material on it: URP Lit, the GLB's colour texture (and normal map if it has one),
        /// matte. The importer's materials carry metal/roughness values that make cloth shine.
        /// </summary>
        private static GameObject PlaceMeshyProp(string asset, Transform parent, string name)
        {
            GameObject prefab = LoadMeshyModel(asset);
            if (prefab == null) return null;
            var model = Object.Instantiate(prefab, parent, false);
            model.name = name;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = MeshyPropMaterial(asset, renderer.sharedMaterial);
            return model;
        }

        private static Material MeshyPropMaterial(string asset, Material imported)
        {
            Texture colour = null, normal = null;
            foreach (string property in imported.GetTexturePropertyNames())
            {
                Texture texture = imported.GetTexture(property);
                if (texture == null) continue;
                if (property.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0 || property == "_BumpMap") normal = texture;
                else if (property.IndexOf("baseColor", StringComparison.OrdinalIgnoreCase) >= 0 || property == "_BaseMap" || property == "_MainTex") colour = texture;
            }
            Material mat = LoadOrCreateMaterial("Meshy_" + asset, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", colour);
            mat.mainTexture = colour;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetTexture("_BumpMap", normal);
            if (normal != null) mat.EnableKeyword("_NORMALMAP");
            else mat.DisableKeyword("_NORMALMAP");
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            if (colour == null) Debug.LogWarning($"[Build] {asset}: no colour texture found on {imported.name}");
            return mat;
        }

        /// <summary>The thief's backpack, saved where StoryNpc finds it at run time (Resources).</summary>
        private static void BuildRobberBackpack()
        {
            var root = new GameObject(RobberBag.ResourceName);
            if (PlaceMeshyProp("robber_backpack", root.transform, "Model") != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RobberBackpackPath)!);
                PrefabUtility.SaveAsPrefabAsset(root, RobberBackpackPath);
                Debug.Log("[Build] robber backpack: Meshy model");
            }
            else AssetDatabase.DeleteAsset(RobberBackpackPath); // back to the code-built bag
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// A beach umbrella (2.35 m, striped; red, teal or yellow by <paramref name="index"/>) with a thin collider
        /// on its pole: you bump into the pole, not the canopy. False when the models aren't there.
        /// </summary>
        private static bool MeshyUmbrella(Transform parent, int index)
        {
            GameObject model = PlaceMeshyProp(UmbrellaModels[index % UmbrellaModels.Length], parent, "Model");
            if (model == null) return false;
            Debug.Log($"[Build] umbrella {index} at {parent.position}");
            // Each one turned and tipped a little differently, as people plant them.
            model.transform.localRotation = Quaternion.Euler(0f, index * 47f, 0f) * Quaternion.Euler(0f, 0f, 3f + index % 3 * 2f);
            var pole = parent.gameObject.AddComponent<CapsuleCollider>();
            pole.direction = 1;
            pole.radius = 0.05f;
            pole.height = 2.1f;
            pole.center = new Vector3(0f, 1.05f, 0f);
            return true;
        }
    }
}
