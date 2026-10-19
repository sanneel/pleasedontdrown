using System.Collections.Generic;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The story's last items (Docs/Prompt-Shako-Chapter-4-Finale.md): the casino's <b>Money Sack</b> (3.9: carried from
    /// the gold vault to the pirate boat, two hands, fits a pocket slot so one lifeguard can take several) and the
    /// orange <b>Lost &amp; Found Tag</b> in Sandy's handwriting (3.10 on the boat; 4.3's evidence in her warehouse).
    /// Each keeps its look under a "Look" child (the boat's cargo copies it at runtime).
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Chapter4ItemsBatch</c> (prefabs + catalog +
    /// FishNet's prefab list; the scene isn't touched).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        public static void Chapter4ItemsBatch()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            var so = new SerializedObject(catalog);
            SerializedProperty list = Require(so, "_items");
            int added = 0;
            foreach (Object made in BuildChapter4Items())
            {
                bool listed = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == made) listed = true;
                if (listed) continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = made;
                added++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            RefreshFishNetPrefabs();
            Debug.Log($"[Chapter4Items] PASS: money sack and Lost & Found tag built ({added} new in the catalog)");
        }

        private static IEnumerable<Object> BuildChapter4Items()
        {
            PhysicsMaterial cloth = GetPhysicsMaterial("SackCloth", 0.6f, 0.05f, PhysicsMaterialCombine.Average);
            PhysicsMaterial paper = GetPhysicsMaterial("Paper", 0.4f, 0.1f, PhysicsMaterialCombine.Average);
            Material burlap = GetMaterial("SackBurlap", new Color(0.86f, 0.72f, 0.5f));
            Material twine = GetMaterial("SackTwine", new Color(0.36f, 0.26f, 0.15f));
            Material gold = GetMaterial("Gold", new Color(0.95f, 0.75f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            Material tagOrange = GetMaterial("FkTagOrange", new Color(1f, 0.52f, 0.12f));
            Material tagWhite = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));

            // A fat burlap sack tied at the neck, a gold "$" on its front, coins peeping out of the top.
            yield return BuildItem("MoneySack", "Money Sack", 7f, new Vector3(0f, -0.36f, 0.52f), Vector3.zero, 0.55f, cloth, root =>
            {
                var look = new GameObject("Look").transform;
                look.SetParent(root, false);
                GameObject body = Primitive(PrimitiveType.Sphere, "Body", look, new Vector3(0f, -0.02f, 0f), new Vector3(0.38f, 0.4f, 0.35f), burlap, keepCollider: false);
                _ = body;
                Primitive(PrimitiveType.Cylinder, "Neck", look, new Vector3(0f, 0.19f, 0f), new Vector3(0.14f, 0.05f, 0.14f), burlap, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Tie", look, new Vector3(0f, 0.175f, 0f), new Vector3(0.155f, 0.02f, 0.155f), twine, keepCollider: false);
                Primitive(PrimitiveType.Sphere, "Ruffle", look, new Vector3(0f, 0.25f, 0f), new Vector3(0.19f, 0.08f, 0.17f), burlap, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Coin", look, new Vector3(0.035f, 0.285f, 0.02f), new Vector3(0.07f, 0.008f, 0.07f), gold, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(20f, 0f, 15f);
                foreach (float side in new[] { 1f, -1f })
                {
                    TextMesh mark = WorldText(look, "Dollar", new Vector3(0f, 0f, 0.178f * side), "$", 90, 0.02f, new Color(1f, 0.8f, 0.25f));
                    mark.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
                    mark.fontStyle = FontStyle.Bold;
                }
                var col = root.gameObject.AddComponent<SphereCollider>();
                col.center = new Vector3(0f, 0.01f, 0f);
                col.radius = 0.185f;
            }, linearDamping: 0.3f, angularDamping: 1.2f, density: 0.85f, waterDrag: 1.6f, configure: go =>
            {
                Item item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                SetEnum(item, "_grip", (int)ItemGrip.TwoHands);
            });

            // A luggage tag: orange card with a white band, a hole and a loop of string.
            yield return BuildItem("LostTag", "Lost & Found Tag", 0.03f, new Vector3(0.2f, -0.28f, 0.5f), new Vector3(-20f, 0f, 0f), 0.8f, paper, root =>
            {
                var look = new GameObject("Look").transform;
                look.SetParent(root, false);
                Primitive(PrimitiveType.Cube, "Card", look, Vector3.zero, new Vector3(0.075f, 0.006f, 0.13f), tagOrange);
                Primitive(PrimitiveType.Cube, "Band", look, new Vector3(0f, 0.0035f, -0.015f), new Vector3(0.076f, 0.002f, 0.03f), tagWhite, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Hole", look, new Vector3(0f, 0.0035f, 0.05f), new Vector3(0.016f, 0.001f, 0.016f), twine, keepCollider: false);
                Primitive(PrimitiveType.Cube, "String", look, new Vector3(0f, 0.004f, 0.09f), new Vector3(0.004f, 0.003f, 0.08f), twine, keepCollider: false);
            }, density: 0.4f, configure: go =>
            {
                Item item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                SetEnum(item, "_grip", (int)ItemGrip.OneHand);
            });
        }
    }
}
