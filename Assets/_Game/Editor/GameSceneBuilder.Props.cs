using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The props modelled in code by ArtSource/Tools/model_props.py (Assets/_Game/Art/Props/*.glb): the things you
    /// carry, the lost things, the dock, rocks, buoys, towels, the bell, the signs and the hut's furniture. Each is
    /// written in the same space and size as the greybox it replaces, so it goes in unturned at scale 1; the greybox's
    /// colliders stay (that's what you walk on, grab and bump into) and only its looks go. Without the GLB the
    /// greybox stays as it was.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string PropDir = "Assets/_Game/Art/Props";

        private static GameObject LoadProp(string asset)
        {
            string path = $"{PropDir}/{asset}.glb";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null && File.Exists(path)) throw new InvalidDataException($"Prop GLB failed to import: {path}");
            return prefab;
        }

        /// <summary>
        /// The model as a child of <paramref name="parent"/>, with our own matte materials in its painted colours
        /// (<paramref name="repaint"/> swaps a named paint for another material: a towel's cloth, say). Null if the
        /// GLB isn't there.
        /// </summary>
        private static GameObject PropModel(string asset, Transform parent, Vector3 localPosition = default, float yaw = 0f, float scale = 1f,
            Dictionary<string, Material> repaint = null)
        {
            GameObject prefab = LoadProp(asset);
            if (prefab == null) return null;
            GameObject model = Object.Instantiate(prefab, parent, false);
            model.name = "Model_" + asset;
            model.transform.localPosition = localPosition;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = Vector3.one * scale;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = repaint != null && repaint.TryGetValue(materials[i].name, out Material other) ? other : PropPaint(materials[i]);
                renderer.sharedMaterials = materials;
            }
            return model;
        }

        /// <summary>One shared URP Lit material per paint name ("red", "wood_dark"...), matte; metal and glass get a little shine.</summary>
        private static Material PropPaint(Material imported)
        {
            string paint = imported.name;
            Color colour = imported.HasProperty("baseColorFactor") ? imported.GetColor("baseColorFactor")
                : imported.HasProperty("_BaseColor") ? imported.GetColor("_BaseColor") : imported.color;
            bool metal = paint is "gold" or "brass" or "brass_dark" or "steel";
            bool glossy = paint is "screen" or "screen_light" or "lens";
            return GetMaterial("Prop_" + paint, colour, metallic: metal ? 0.7f : 0f, smoothness: metal ? 0.55f : glossy ? 0.8f : 0.12f);
        }

        /// <summary>
        /// Swaps a greybox's looks for the model: boxes that are also colliders keep the collider, the purely
        /// decorative ones go. With <paramref name="fitCollider"/> the collider becomes a box round the model
        /// instead (for things whose greybox was a much simpler shape: glasses with their arms out, a watch strap).
        /// </summary>
        private static bool DressProp(Transform root, string asset, bool fitCollider = false, Dictionary<string, Material> repaint = null)
        {
            if (LoadProp(asset) == null) return false;
            var children = new List<Transform>();
            foreach (Transform child in root) children.Add(child);
            foreach (Transform child in children)
            {
                if (child.GetComponent<MeshRenderer>() == null) continue; // a grip point or some other marker
                if (fitCollider || child.GetComponent<Collider>() == null)
                {
                    Object.DestroyImmediate(child.gameObject);
                    continue;
                }
                Object.DestroyImmediate(child.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(child.GetComponent<MeshFilter>());
            }
            GameObject model = PropModel(asset, root, repaint: repaint);
            if (fitCollider)
            {
                // In the root's own space: the builder makes items at the origin, unturned.
                Bounds bounds = RendererBounds(model);
                var box = root.gameObject.AddComponent<BoxCollider>();
                box.center = root.InverseTransformPoint(bounds.center);
                box.size = bounds.size;
            }
            return true;
        }

        /// <summary>The model's renderers (for a hover outline that used to be the greybox's).</summary>
        private static Renderer[] PropRenderers(Transform root) => root.GetComponentsInChildren<Renderer>();
    }
}
