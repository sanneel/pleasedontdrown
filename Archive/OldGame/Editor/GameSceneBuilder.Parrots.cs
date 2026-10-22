using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.Fun;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 1's parrots (instead of seagulls): perches on the crowns of the palms round the station and on the beach
    /// hut's roof, and a little flock of macaws (red ones, plus green and blue repaints) that sit on them, chatter
    /// and fly off when you come close (Fun/Parrot.cs).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const int ParrotCount = 8;

        private static void BuildParrots(Transform env, Transform parent)
        {
            var root = new GameObject("Parrots").transform;
            root.SetParent(parent, false);
            var flock = root.gameObject.AddComponent<ParrotFlock>();
            SetField(flock, "_skyCentre", p => p.vector3Value = new Vector3(IslandCenter.x, 0f, IslandCenter.y - 8f));

            // Perches: the top of each palm's crown (the middle of its highest vertices), facing out over the beach.
            var perches = new List<Object>();
            foreach (Transform palm in env.GetComponentsInChildren<Transform>().Where(t => t.name == "Meshy_palm_tall"))
            {
                if (!PalmCrown(palm, out Vector3 top)) continue;
                perches.Add(Perch(root, $"Perch_Palm{perches.Count}", top, Random.Range(0f, 360f)));
            }
            Transform hut = env.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "BeachHut");
            if (hut != null && RendererBounds(hut, out Bounds b))
                perches.Add(Perch(root, "Perch_Hut", new Vector3(b.center.x, b.max.y - 0.02f, b.center.z), hut.eulerAngles.y));
            SetRefs(flock, "_perches", perches.ToArray());
            Debug.Log($"[Build] parrots: {ParrotCount} birds, {perches.Count} perches");

            var paints = new[]
            {
                null,
                new Dictionary<string, Material> { ["red"] = GetMaterial("ParrotGreen", new Color(0.2f, 0.72f, 0.3f)), ["orange"] = GetMaterial("ParrotLime", new Color(0.75f, 0.9f, 0.25f)) },
                new Dictionary<string, Material> { ["red"] = GetMaterial("ParrotBlue", new Color(0.15f, 0.5f, 0.95f)), ["orange"] = GetMaterial("ParrotGold", new Color(1f, 0.78f, 0.15f)) }
            };
            for (int i = 0; i < ParrotCount; i++)
            {
                var bird = new GameObject($"Parrot{i}").transform;
                bird.SetParent(root, false);
                bool flying = i % 4 == 3; // a couple already up in the air
                // The rest sit on the perches round the station from the start (spread through the list).
                Transform seat = !flying && perches.Count > 0 ? (Transform)perches[i * 5 % perches.Count] : null;
                if (seat != null) bird.SetPositionAndRotation(seat.position, seat.rotation);
                else bird.position = new Vector3(IslandCenter.x + Mathf.Cos(i) * 10f, 12f, IslandCenter.y - 8f + Mathf.Sin(i) * 10f);
                bird.localScale = Vector3.one * 1.8f; // big enough to spot on a 7 m palm
                var body = new GameObject("Body").transform;
                body.SetParent(bird, false);
                Dictionary<string, Material> paint = paints[i % paints.Length];
                PropModel("parrot", body, repaint: paint);
                Transform Wing(string name, float side)
                {
                    var pivot = new GameObject(name).transform;
                    pivot.SetParent(body, false);
                    pivot.localPosition = new Vector3(0.1f * side, 0.3f, -0.02f);
                    pivot.localScale = new Vector3(side, 1f, 1f); // one model, mirrored for the left
                    PropModel("parrot_wing", pivot, repaint: paint);
                    return pivot;
                }
                Transform left = Wing("WingL", -1f), right = Wing("WingR", 1f);
                var parrot = bird.gameObject.AddComponent<Parrot>();
                SetRef(parrot, "_flock", flock);
                SetRef(parrot, "_body", body);
                SetRef(parrot, "_wingL", left);
                SetRef(parrot, "_wingR", right);
                SetRef(parrot, "_audio", SpatialAudio(bird.gameObject, 3f, 45f));
                SetBool(parrot, "_startFlying", flying);
            }
        }

        private static Transform Perch(Transform root, string name, Vector3 at, float yaw)
        {
            var perch = new GameObject(name).transform;
            perch.SetParent(root, false);
            perch.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            return perch;
        }

        /// <summary>The middle of a palm's highest vertices (its crown, where the fronds meet), in world space.</summary>
        private static bool PalmCrown(Transform palm, out Vector3 top)
        {
            top = default;
            var points = new List<Vector3>();
            foreach (MeshFilter filter in palm.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                points.AddRange(filter.sharedMesh.vertices.Select(v => toWorld.MultiplyPoint3x4(v)));
            }
            if (points.Count == 0) return false;
            // The trunk's top: the highest points close to the trunk line, not the frond tips arching out.
            float maxY = points.Max(p => p.y);
            List<Vector3> high = points.Where(p => p.y > maxY - 1.2f).ToList();
            Vector3 middle = new Vector3(high.Average(p => p.x), 0f, high.Average(p => p.z));
            List<Vector3> core = high.Where(p => new Vector2(p.x - middle.x, p.z - middle.z).sqrMagnitude < 0.6f * 0.6f).ToList();
            if (core.Count == 0) core = high;
            top = new Vector3(middle.x, core.Max(p => p.y) - 0.05f, middle.z);
            return true;
        }

        private static bool RendererBounds(Transform t, out Bounds bounds)
        {
            bounds = default;
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return true;
        }
    }
}
