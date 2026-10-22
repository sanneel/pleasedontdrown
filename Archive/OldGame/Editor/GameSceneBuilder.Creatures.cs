using System;
using FishNet.Component.Transforming;
using FishNet.Object;
using PleaseDontDrown.Creatures;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The one creature prefab (Resources/Creature): parrots, monkeys, alligators and Old Snapper share it; the kind is
    /// set when it's spawned and every machine builds the body (Creatures/Creature.Body.cs). Building it doesn't touch
    /// the scene: the story loads it from Resources.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string CreaturePrefabPath = "Assets/_Game/Resources/Creature.prefab";

        [MenuItem("PLEASE DON'T DROWN/Build creature prefab")]
        public static void BuildCreaturePrefab()
        {
            var go = new GameObject("Creature");
            try
            {
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                go.AddComponent<NetworkObject>();
                var sync = go.AddComponent<NetworkTransform>();
                sync.SetSynchronizeScale(false);
                var creature = go.AddComponent<Creature>();
                SetRef(creature, "_audio", SpatialAudio(go, 3f, 60f));

                Color[] colours =
                {
                    new(0.2f, 0.65f, 0.25f), new(0.85f, 0.12f, 0.1f), new(1f, 0.8f, 0.15f), new(0.15f, 0.35f, 0.85f), // parrot
                    new(0.38f, 0.24f, 0.14f), new(0.86f, 0.68f, 0.52f),                                                  // monkey
                    new(0.22f, 0.27f, 0.16f), new(0.78f, 0.74f, 0.55f),                                                  // alligator
                    new(0.5f, 0.4f, 0.26f), new(0.5f, 0.52f, 0.36f),                                                     // turtle
                    new(0.95f, 0.93f, 0.85f), new(0.05f, 0.05f, 0.06f), new(0.7f, 0.25f, 0.28f), new(0.08f, 0.07f, 0.08f), // teeth, eye, mouth, hat
                    new(0.48f, 0.22f, 0.62f), new(0.86f, 0.55f, 0.75f), new(1f, 0.78f, 0.22f), new(0.93f, 0.88f, 0.72f), // octopus, chains, panama
                    new(1f, 0.85f, 0.2f)                                                                                   // glowing eyes
                };
                string[] names = Enum.GetNames(typeof(CreaturePaint));
                var paints = new Material[names.Length];
                for (int i = 0; i < names.Length; i++)
                    paints[i] = GetMaterial("Creature_" + names[i], colours[i], metallic: names[i] == "Gold" ? 0.8f : 0f,
                        smoothness: names[i] is "Eye" or "Gold" ? 0.85f : names[i] == "Shell" ? 0.3f : 0.15f,
                        emission: names[i] == "Glow" ? new Color(3f, 2.2f, 0.4f) : null);
                var so = new SerializedObject(creature);
                SerializedProperty list = Require(so, "_paints");
                list.arraySize = paints.Length;
                for (int i = 0; i < paints.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = paints[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                System.IO.Directory.CreateDirectory("Assets/_Game/Resources");
                PrefabUtility.SaveAsPrefabAsset(go, CreaturePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            RefreshFishNetPrefabs();
            Debug.Log("[Creatures] PASS: creature prefab built and registered: " + CreaturePrefabPath);
        }

        /// <summary>Batch: build the creature prefab.</summary>
        public static void BuildCreaturePrefabBatch() => BuildCreaturePrefab();
    }
}
