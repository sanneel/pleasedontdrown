using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The pirate tavern's round tables become the Tripo treasure table (coins, dice, candles, bottles and mugs on a
    /// barrel-legged top, 1.7 m across; Art/Tripo/Island3/treasure_table.glb, made 2026-10-12). The tavern had two small
    /// greybox tables each side of the way in, 2.1 m apart: each pair becomes one treasure table in its middle with six
    /// stools round it. An edit to the saved scene; run again after a merge (it rebuilds what it made).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const float TreasureTableRadius = 0.85f, TreasureStoolRadius = 1.2f;
        private const int TreasureStools = 6;

        /// <summary>Batch: the treasure tables in the tavern.</summary>
        public static void TavernTreasureTablesBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject prefab = TripoPrefab("treasure_table");
            if (prefab == null) { Debug.LogError("[Tavern] no treasure_table model"); return; }
            Transform house = FindByPath("Environment/Island3/PirateTavern");
            if (house == null) { Debug.LogError("[Tavern] no tavern"); return; }
            var log = new List<string>();
            var tables = house.Cast<Transform>().Where(t => t.name is "RoundTable" or "TreasureTable").ToList();
            // A side of the room each (the greybox pairs share their x).
            foreach (var side in tables.GroupBy(t => Mathf.Round(t.localPosition.x)).ToList())
            {
                Vector3 centre = side.Aggregate(Vector3.zero, (s, t) => s + t.localPosition) / side.Count();
                float floor = side.Min(t => t.name == "RoundTable" ? t.localPosition.y - 0.76f : t.localPosition.y);
                var middle = new Vector2(centre.x, centre.z);
                Transform stoolModel = null;
                foreach (Transform t in house.Cast<Transform>().ToArray())
                {
                    Vector2 at = new(t.localPosition.x, t.localPosition.z);
                    if (t.name is "TableFoot" or "Candle" or "RoundTable" or "TreasureTable" && Vector2.Distance(at, middle) < 1.8f)
                        Object.DestroyImmediate(t.gameObject);
                    else if (t.name == "Stool" && Vector2.Distance(at, middle) < 2.6f)
                    {
                        if (stoolModel == null) { stoolModel = t; continue; }
                        Object.DestroyImmediate(t.gameObject);
                    }
                }
                var root = new GameObject("TreasureTable").transform;
                root.SetParent(house, false);
                root.localPosition = new Vector3(centre.x, floor, centre.z);
                root.localRotation = Quaternion.Euler(0f, centre.x > 0f ? 25f : 205f, 0f);
                GameObject model = Object.Instantiate(prefab, root, false);
                model.name = "Tripo_treasure_table";
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
                var top = root.gameObject.AddComponent<CapsuleCollider>(); // the round top and its barrels, solid
                top.direction = 1;
                top.radius = TreasureTableRadius - 0.05f;
                top.height = 0.68f;
                top.center = new Vector3(0f, 0.34f, 0f);
                if (stoolModel != null)
                    for (int k = 0; k < TreasureStools; k++)
                    {
                        float a = (k + 0.5f) * Mathf.PI * 2f / TreasureStools;
                        Transform stool = k == 0 ? stoolModel : Object.Instantiate(stoolModel.gameObject, house, false).transform;
                        stool.name = "Stool";
                        stool.localPosition = new Vector3(centre.x + Mathf.Cos(a) * TreasureStoolRadius, stoolModel.localPosition.y, centre.z + Mathf.Sin(a) * TreasureStoolRadius);
                    }
                log.Add($"{ScenePathOf(root)} at {root.position:F2} (from {side.Count()} tables), {TreasureStools} stools round it");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Tavern] PASS: {log.Count} treasure tables\n" + string.Join("\n", log));
        }
    }
}
