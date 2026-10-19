using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class SceneProbeMaps
    {
        /// <summary>
        /// What a model is like from above, in its own frame: -executeMethod PleaseDontDrown.Editor.SceneProbeMaps.HeightMapBatch
        /// -root Environment/Hotel/HotelBeachBar [-model ColouredTripoBar] -x -7,7 -z -3,7 -step 0.25 -from 3.4
        /// casts rays down at the model's own triangles (temporary mesh colliders, nothing saved) and logs a map of the
        /// first surface met: '.' floor (under 0.45 m above the root), ':' low (to 0.9), 'c' counter height (to 1.7),
        /// 'H' higher, ' ' nothing; plus the clear floor cells' coordinates, for putting people where there's room.
        /// </summary>
        public static void HeightMapBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Arg(string key, string fallback)
                {
                    int i = Array.IndexOf(args, key);
                    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
                }
                float[] Range(string key, string fallback) =>
                    Array.ConvertAll(Arg(key, fallback).Split(','), s => float.Parse(s, CultureInfo.InvariantCulture));
                EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
                var root = GameObject.Find(Arg("-root", "Environment/Hotel/HotelBeachBar"))?.transform
                           ?? throw new InvalidOperationException("no root");
                Transform model = root.Find(Arg("-model", "ColouredTripoBar")) ?? root;
                float[] xs = Range("-x", "-7,7"), zs = Range("-z", "-3,7");
                float step = float.Parse(Arg("-step", "0.25"), CultureInfo.InvariantCulture);
                float from = float.Parse(Arg("-from", "3.4"), CultureInfo.InvariantCulture);
                bool numeric = Arg("-numeric", "0") == "1"; // (heights in metres instead of letters)

                // Every renderer of the model (the first LOD only) as a collider of its own on a free layer.
                const int layer = 31;
                var made = new List<GameObject>();
                LODGroup lods = model.GetComponentInChildren<LODGroup>();
                var renderers = new List<Renderer>();
                if (lods != null) renderers.AddRange(lods.GetLODs()[0].renderers);
                else renderers.AddRange(model.GetComponentsInChildren<Renderer>());
                foreach (Renderer r in renderers)
                {
                    Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh == null) continue;
                    var go = new GameObject("_probe");
                    go.layer = layer;
                    go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    go.transform.localScale = r.transform.lossyScale;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    made.Add(go);
                }
                Physics.SyncTransforms();

                var map = new StringBuilder();
                var clear = new List<string>();
                for (float z = zs[1]; z >= zs[0] - 1e-4f; z -= step)
                {
                    map.Append($"{z,6:F2} ");
                    for (float x = xs[0]; x <= xs[1] + 1e-4f; x += step)
                    {
                        Vector3 top = root.TransformPoint(new Vector3(x, from, z));
                        char c = ' ';
                        if (Physics.Raycast(top, -root.up, out RaycastHit hit, from + 2f, 1 << layer, QueryTriggerInteraction.Ignore))
                        {
                            float h = root.InverseTransformPoint(hit.point).y;
                            c = h < 0.45f ? '.' : h < 0.9f ? ':' : h < 1.7f ? 'c' : 'H';
                            if (numeric) { map.Append($"{h,5:F2}"); continue; }
                            if (c == '.')
                            {
                                // Room for a person: nothing over the floor up to 2 m, half a metre round.
                                bool room = true;
                                foreach (var d in new[] { Vector3.zero, Vector3.right * .25f, Vector3.left * .25f, Vector3.forward * .25f, Vector3.back * .25f })
                                {
                                    Vector3 feet = root.TransformPoint(new Vector3(x, h + 0.05f, z) + d);
                                    if (Physics.Raycast(feet, root.up, 1.95f, 1 << layer, QueryTriggerInteraction.Ignore)) { room = false; break; }
                                }
                                if (room) clear.Add($"({x:F2},{h:F2},{z:F2})");
                                else c = '_';
                            }
                        }
                        if (numeric) { if (c == ' ') map.Append("    -"); continue; }
                        map.Append(c);
                    }
                    map.AppendLine();
                }
                foreach (GameObject go in made) Object.DestroyImmediate(go);
                Debug.Log($"[HeightMap] {root.name} x {xs[0]}..{xs[1]} (left to right), z {zs[1]} (top) .. {zs[0]}, step {step}\n{map}");
                Debug.Log($"[HeightMap] clear floor cells ({clear.Count}): {string.Join(" ", clear)}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HeightMap] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }
    }
}
