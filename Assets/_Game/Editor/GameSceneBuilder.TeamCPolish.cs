using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The user's screenshots of 2026-10-13, as small edits to the saved Game scene (Docs/18: A8/C7, B4, C4, C5):
    /// the cage-fight ring out of the tavern onto the grass beside it; the castle's shut gate fitted into its arch;
    /// clean, joined curtain walls instead of the Tripo wall pieces; the river cut through to the sea; island 3's coconut
    /// palms gone (jungle trees where the user marked) and a few back on island 1. Each batch saves the scene.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector2 OctagonOutsideAt = new(67.5f, -964.5f); // the grass west of the tavern

        /// <summary>Batch: all of the 2026-10-13 fixes below, in order (the river before the plants are settled).</summary>
        public static void TeamCPolishAllBatch()
        {
            TeamCRingOutsideBatch();
            TeamCGateDoorBatch();
            TeamCCastleWallsBatch();
            TeamCRiverMouthBatch();
            TeamCJunglePalmsBatch();
            TeamBIsland1PalmsBatch();
        }

        // ================================================================== the wood grain

        private const string WoodGrainPath = "Assets/_Game/Art/Wood/WoodGrain.png";

        /// <summary>A tileable plank grain (light grey: the material's colour tints it), made once and kept as a PNG.
        /// Lines run along v, so a plank's v goes along its length.</summary>
        private static Texture2D WoodGrain()
        {
            if (!File.Exists(WoodGrainPath))
            {
                const int size = 512;
                var pixels = new Color32[size * size];
                float Wave(float t) => Mathf.Sin(t * Mathf.PI * 2f);
                (float u, float v, float r)[] knots = { (0.23f, 0.31f, 0.035f), (0.71f, 0.77f, 0.028f), (0.52f, 0.06f, 0.022f) };
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = x / (float)size, v = y / (float)size;
                        // Whole-number frequencies only: the pattern wraps at the edges.
                        float warp = 0.05f * Wave(v * 3f + 0.3f) + 0.025f * Wave(v * 7f + u * 2f) + 0.012f * Wave(v * 17f + u * 3f);
                        float rings = 0.5f + 0.5f * Wave(u * 19f + warp * 9f);
                        float fine = 0.5f + 0.5f * Wave(u * 83f + 0.4f * Wave(v * 5f + u * 4f));
                        float value = 0.72f + 0.17f * Mathf.Pow(rings, 1.6f) + 0.07f * fine;
                        foreach ((float ku, float kv, float kr) in knots)
                        {
                            float du = Mathf.Abs(u - ku); du = Mathf.Min(du, 1f - du);
                            float dv = Mathf.Abs(v - kv); dv = Mathf.Min(dv, 1f - dv);
                            float d = Mathf.Sqrt(du * du + dv * dv * 0.18f) / kr;
                            if (d < 1.6f) value *= Mathf.Lerp(0.62f, 1f, Mathf.SmoothStep(0f, 1f, d / 1.6f)) + 0.08f * Wave(d * 2.2f);
                        }
                        byte b = (byte)Mathf.Clamp(value * 255f, 0f, 255f);
                        pixels[y * size + x] = new Color32(b, (byte)(b * 0.97f), (byte)(b * 0.92f), 255);
                    }
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                Directory.CreateDirectory(Path.GetDirectoryName(WoodGrainPath)!);
                File.WriteAllBytes(WoodGrainPath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(WoodGrainPath);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(WoodGrainPath);
            if (importer.wrapMode != TextureWrapMode.Repeat || importer.anisoLevel != 4 || importer.maxTextureSize != 512)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 512;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(WoodGrainPath);
        }

        /// <summary>A lit material with the plank grain, tinted <paramref name="colour"/>.</summary>
        private static Material WoodMaterial(string name, Color colour, float smoothness = 0.16f)
        {
            Material mat = GetMaterial(name, colour, smoothness: smoothness);
            mat.SetTexture("_BaseMap", WoodGrain());
            mat.SetTextureScale("_BaseMap", Vector2.one);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================== a small mesh builder

        /// <summary>Flat-shaded prisms into one mesh with sub-meshes; uv is planar: 1.2 m per tile across, 3 m along y.</summary>
        private sealed class MeshBuild
        {
            private readonly List<Vector3> _v = new(), _n = new();
            private readonly List<Vector2> _uv = new();
            private readonly List<int>[] _t;

            public MeshBuild(int subMeshes) => _t = Enumerable.Range(0, subMeshes).Select(_ => new List<int>()).ToArray();

            private static Vector2 Planar(Vector3 p, Vector3 n)
            {
                Vector3 a = new(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x / 1.2f, p.z / 1.2f);
                return a.x >= a.z ? new Vector2(p.z / 1.2f, p.y / 3f) : new Vector2(p.x / 1.2f, p.y / 3f);
            }

            private void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
                if (Vector3.Dot(n, outward) < 0f) (b, d) = (d, b);
                n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
                n.Normalize();
                int k = _v.Count;
                foreach (Vector3 p in new[] { a, b, c, d })
                {
                    _v.Add(p); _n.Add(n); _uv.Add(Planar(p, n));
                }
                _t[sub].AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }

            /// <summary>A prism from xL to xR, z0 to z1, its bottom and top edges sloping (heights at xL and xR).</summary>
            public void Prism(int sub, float xL, float xR, float botL, float botR, float topL, float topR, float z0, float z1)
            {
                Vector3 bl0 = new(xL, botL, z0), br0 = new(xR, botR, z0), tr0 = new(xR, topR, z0), tl0 = new(xL, topL, z0);
                Vector3 bl1 = new(xL, botL, z1), br1 = new(xR, botR, z1), tr1 = new(xR, topR, z1), tl1 = new(xL, topL, z1);
                Vector3 up = new Vector3(-(topR - topL), xR - xL, 0f).normalized, down = new Vector3(botR - botL, -(xR - xL), 0f).normalized;
                Quad(sub, bl0, br0, tr0, tl0, Vector3.back);
                Quad(sub, bl1, br1, tr1, tl1, Vector3.forward);
                Quad(sub, bl0, tl0, tl1, bl1, Vector3.left);
                Quad(sub, br0, tr0, tr1, br1, Vector3.right);
                Quad(sub, tl0, tr0, tr1, tl1, up);
                Quad(sub, bl0, br0, br1, bl1, down);
            }

            public void Box(int sub, Vector3 min, Vector3 max) => Prism(sub, min.x, max.x, min.y, min.y, max.y, max.y, min.z, max.z);

            public Mesh Save(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv);
                mesh.subMeshCount = _t.Length;
                for (int i = 0; i < _t.Length; i++) mesh.SetTriangles(_t[i], i);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                string dir = MeshDir + "/Island3";
                Directory.CreateDirectory(dir);
                string path = $"{dir}/{name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        // ================================================================== measuring a Tripo model's openings

        private const int ProbeLayer = 31;

        /// <summary>Gives every mesh under a root a temporary collider on <see cref="ProbeLayer"/>, so rays measure the
        /// model alone; disposing takes them away again.</summary>
        private sealed class MeshProbe : IDisposable
        {
            private readonly List<(MeshCollider collider, int layer)> _added = new();
            private readonly bool _backfaces;

            public MeshProbe(Transform root)
            {
                foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh == null) continue;
                    var c = f.gameObject.AddComponent<MeshCollider>();
                    c.sharedMesh = f.sharedMesh;
                    _added.Add((c, f.gameObject.layer));
                    f.gameObject.layer = ProbeLayer;
                }
                _backfaces = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
            }

            public float? Cast(Vector3 from, Vector3 dir, float max) =>
                Physics.Raycast(from, dir, out RaycastHit hit, max, 1 << ProbeLayer, QueryTriggerInteraction.Ignore) ? hit.distance : null;

            public void Dispose()
            {
                foreach ((MeshCollider c, int layer) in _added)
                {
                    if (c == null) continue;
                    c.gameObject.layer = layer;
                    Object.DestroyImmediate(c);
                }
                Physics.queriesHitBackfaces = _backfaces;
            }
        }

        /// <summary>Batch: logs the gatehouse passage's width and top at depths from the yard side (dz -2.4 .. -1.2).</summary>
        public static void TeamCGateProbeBatch() => RunBatch(() =>
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform castle = FindByPath("Environment/Island3/PirateCastle");
            Transform gatehouse = castle.Find("Tripo_castle_gatehouse");
            if (castle.Find("GateShut") is { } shut) Object.DestroyImmediate(shut.gameObject);
            GameObject prefab = TripoPrefab("castle_gatehouse");
            foreach (MeshFilter f in gatehouse.GetComponentsInChildren<MeshFilter>())
            {
                string path = AnimationUtility.CalculateTransformPath(f.transform, gatehouse);
                Transform original = path.Length == 0 ? prefab.transform : prefab.transform.Find(path);
                if (original != null && original.TryGetComponent(out MeshFilter of)) f.sharedMesh = of.sharedMesh;
            }
            const float Z = 20f, G = 0.3f;
            var sb = new StringBuilder();
            using (var probe = new MeshProbe(gatehouse))
                for (float dz = -2.4f; dz <= -1.19f; dz += 0.05f)
                {
                    Vector3 At(float x, float y, float z) => castle.TransformPoint(new Vector3(x, y, z));
                    float? up = probe.Cast(At(0f, G + 0.3f, Z + dz), castle.up, 20f);
                    float? fwd = probe.Cast(At(0f, G + 3f, Z + dz), castle.forward, 6f);
                    sb.Append($"dz {dz,5:0.00}: top {(up.HasValue ? (0.3f + up.Value).ToString("0.00") : "open")} ahead {(fwd.HasValue ? fwd.Value.ToString("0.00") : "-")} |");
                    for (float y = G + 0.5f; y < G + 9f; y += 0.5f)
                    {
                        float? l = probe.Cast(At(0f, y, Z + dz), -castle.right, 8f), r = probe.Cast(At(0f, y, Z + dz), castle.right, 8f);
                        sb.Append($" {y - G:0.0}:{(l.HasValue ? l.Value.ToString("0.00") : "-")}/{(r.HasValue ? r.Value.ToString("0.00") : "-")}");
                    }
                    sb.AppendLine();
                }
            Debug.Log("[TeamC] gate probe (scene not saved)\n" + sb);
        });

        private static void SaveLog(string file, List<string> log, string title)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllLines($"Logs/{file}", log);
            Debug.Log($"[TeamC] PASS: {title}\n" + string.Join("\n", log));
        }

        private static void RunBatch(Action body)
        {
            try { body(); }
            catch (Exception e)
            {
                Debug.LogError($"[TeamC] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static float Island3GroundAt(Transform island, float x, float z)
        {
            var ground = island.Find("Island3Ground").GetComponent<MeshCollider>();
            return ground.Raycast(new Ray(new Vector3(x, 120f, z), Vector3.down), out RaycastHit hit, 250f) ? hit.point.y : BeachHeight(x, z);
        }

        // ================================================================== C7: the ring next to the tavern

        /// <summary>Batch: the octagon out of the tavern onto the grass beside it (the user: "what's a ring doing in a
        /// bar"); the tavern gets its tables and stools back.</summary>
        public static void TeamCRingOutsideBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            Transform tavern = island.Find("PirateTavern") ?? throw new InvalidOperationException("No tavern");
            var log = new List<string>();
            if (tavern.Find("Octagon") is { } inside) { Object.DestroyImmediate(inside.gameObject); log.Add("octagon taken out of the tavern"); }
            log.Add($"tavern tables: {RestoreTavernTables(tavern)} put back");
            Transform cage = OctagonByTavern(island, tavern, log);
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SaveLog("teamc-ring.txt", log, $"C7 octagon outside at {cage.position}");
        });

        /// <summary>The tavern's round tables and stools, if the room has none (the octagon had taken their place).</summary>
        private static int RestoreTavernTables(Transform tavern)
        {
            if (tavern.Cast<Transform>().Any(t => t.name == "RoundTable")) return 0;
            ShackRoom r = TripoShackRoom(TripoTavern);
            const float t = 0.12f; // TripoHouse's wall thickness
            float x0 = r.x0 + t, x1 = r.x1 - t, z0 = r.z0 + t, z1 = r.z1 - t;
            int before = tavern.childCount;
            TavernTables(tavern, x0, x1, z1, r.floor, r.doorX, z0 + 2.2f,
                GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f)),
                GetMaterial("TavernCounterTop", new Color(0.24f, 0.14f, 0.08f), smoothness: 0.35f),
                GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f)),
                GetMaterial("TavernStool", new Color(0.36f, 0.22f, 0.12f)));
            return tavern.Cast<Transform>().Skip(before).Count(c => c.name == "RoundTable");
        }

        /// <summary>
        /// The octagon on a raised plank stage on the grass west of the tavern, its open side toward the tavern's door,
        /// steps up to it there, torches round it (island 3 is always night). The plants in its way go.
        /// </summary>
        private static Transform OctagonByTavern(Transform island, Transform tavern, List<string> log)
        {
            if (island.Find("Octagon") is { } old) Object.DestroyImmediate(old.gameObject);
            const float R = 3.1f, Stage = R + 0.45f;
            Vector2 at = OctagonOutsideAt;
            float high = float.MinValue, low = float.MaxValue;
            for (int i = 0; i < 16; i++)
                foreach (float rr in new[] { 0f, Stage * 0.5f, Stage })
                {
                    float a = i * Mathf.PI / 8f;
                    float g = Island3GroundAt(island, at.x + Mathf.Sin(a) * rr, at.y + Mathf.Cos(a) * rr);
                    high = Mathf.Max(high, g);
                    low = Mathf.Min(low, g);
                }
            float top = high + 0.22f;
            ShackRoom room = TripoShackRoom(TripoTavern);
            Vector3 door = tavern.TransformPoint(new Vector3(room.doorX, room.floor, room.z1 + 1.5f));
            float yaw = Mathf.Atan2(door.x - at.x, door.z - at.y) * Mathf.Rad2Deg;
            Transform cage = BuildOctagon(island, new Vector3(at.x, top, at.y), yaw);
            log.Add($"octagon at ({at.x}, {top:0.00}, {at.y}), gate toward the tavern door (yaw {yaw:0}), ground {low:0.00}..{high:0.00}");

            // The stage under it: planks round, its edge a dark skirt down into the ground.
            Material plank = WoodMaterial("RingStagePlank", new Color(0.62f, 0.45f, 0.3f));
            Material skirt = WoodMaterial("RingStageSkirt", new Color(0.34f, 0.23f, 0.15f));
            float bottom = low - 0.35f;
            Primitive(PrimitiveType.Cylinder, "StageSkirt", cage, new Vector3(0f, (bottom - top) * 0.5f - 0.02f, 0f),
                new Vector3(Stage * 2f, (top - bottom) * 0.5f, Stage * 2f), skirt, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "StageDeck", cage, new Vector3(0f, -0.01f, 0f), new Vector3(Stage * 2f - 0.1f, 0.012f, Stage * 2f - 0.1f), plank, keepCollider: false);
            var body = new GameObject("StageCollider");
            body.transform.SetParent(cage, false);
            foreach (float turn in new[] { 0f, 45f })
            {
                var box = new GameObject("StageBox").AddComponent<BoxCollider>();
                box.transform.SetParent(body.transform, false);
                box.transform.localRotation = Quaternion.Euler(0f, turn + 22.5f, 0f);
                box.center = new Vector3(0f, (bottom - top) * 0.5f, 0f);
                box.size = new Vector3(Stage * 1.84f, top - bottom, Stage * 1.84f);
            }
            TagSurface(body, SurfaceKind.Wood);

            // Steps up at the open side, down to the ground there.
            float gateGround = Island3GroundAt(island, at.x + Mathf.Sin(yaw * Mathf.Deg2Rad) * (Stage + 0.8f), at.y + Mathf.Cos(yaw * Mathf.Deg2Rad) * (Stage + 0.8f));
            int steps = Mathf.CeilToInt((top - gateGround) / 0.2f) - 1;
            for (int s = 0; s < steps; s++)
            {
                float stepTop = top - (s + 1) * (top - gateGround) / (steps + 1);
                ResortBox(cage, "Step", new Vector3(0f, (stepTop + gateGround - 0.3f) * 0.5f - top, Stage + 0.15f + s * 0.32f),
                    new Vector3(2.4f, stepTop - gateGround + 0.3f, 0.34f), skirt);
            }
            log.Add($"stage {top - low:0.00} m over the lowest ground, {steps} steps");

            // Torches round it, clear of the way in.
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material flame = GetMaterial("TorchFlame", new Color(1f, 0.55f, 0.15f), emission: new Color(3f, 1.3f, 0.3f));
            foreach (float a in new[] { 62f, 150f, 210f, 298f })
            {
                Vector3 local = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (Stage + 0.7f);
                Vector3 world = cage.TransformPoint(local);
                float g = Island3GroundAt(island, world.x, world.z);
                Torch(cage, new Vector3(local.x, g - top - 0.1f, local.z), wood, flame);
            }

            // Plants in the way go.
            int cleared = 0;
            foreach (string group in new[] { "Palms", "Jungle" })
                if (island.Find(group) is { } plants)
                    foreach (Transform p in plants.Cast<Transform>().ToArray())
                        if (new Vector2(p.position.x - at.x, p.position.z - at.y).magnitude < Stage + 2.5f)
                        {
                            Object.DestroyImmediate(p.gameObject);
                            cleared++;
                        }
            log.Add($"{cleared} plants cleared for it");
            return cage;
        }

        /// <summary>The cage (C7): an 8-sided ring 6.2 m across, canvas floor with a red ring, padded posts, padded
        /// lower walls with bars above; its local +z side is open (the way in).</summary>
        private static Transform BuildOctagon(Transform parent, Vector3 centre, float yaw)
        {
            const float R = 3.1f, PostH = 1.95f; // corner radius, cage height
            Material canvas = GetMaterial("OctagonCanvas", new Color(0.86f, 0.82f, 0.72f), smoothness: 0.1f);
            Material pad = GetMaterial("OctagonPad", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.35f);
            Material red = GetMaterial("OctagonRed", new Color(0.62f, 0.07f, 0.06f), smoothness: 0.3f);
            Material wire = GetMaterial("OctagonWire", new Color(0.2f, 0.2f, 0.21f), 0.7f, 0.4f);
            var cage = new GameObject("Octagon").transform;
            cage.SetParent(parent, false);
            cage.SetPositionAndRotation(centre, Quaternion.Euler(0f, yaw, 0f));
            TagSurface(cage.gameObject, SurfaceKind.Wood);
            GameObject Part(PrimitiveType type, string n, Vector3 local, Vector3 scale, Material m, bool solid = false) =>
                Primitive(type, n, cage, local, scale, m, solid);
            Part(PrimitiveType.Cylinder, "Canvas", new Vector3(0f, 0.03f, 0f), new Vector3(R * 2f * 0.91f, 0.03f, R * 2f * 0.91f), canvas); // inside the flat sides
            Part(PrimitiveType.Cylinder, "CanvasRing", new Vector3(0f, 0.062f, 0f), new Vector3(R * 1.5f, 0.002f, R * 1.5f), red);
            Part(PrimitiveType.Cylinder, "CanvasInner", new Vector3(0f, 0.064f, 0f), new Vector3(R * 1.42f, 0.002f, R * 1.42f), canvas);
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f; // flat sides face local ±x and ±z
                corners[i] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R;
                Part(PrimitiveType.Cylinder, "Post", corners[i] + Vector3.up * PostH * 0.5f, new Vector3(0.16f, PostH * 0.5f, 0.16f), pad, solid: true);
                Part(PrimitiveType.Cylinder, "PostPad", corners[i] + Vector3.up * 1.2f, new Vector3(0.24f, 0.45f, 0.24f), red);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 8], mid = (a + b) * 0.5f;
                if (mid.z > R * 0.8f) continue; // the way in, open
                float len = Vector3.Distance(a, b) - 0.16f;
                var side = new GameObject("Side").transform;
                side.SetParent(cage, false);
                side.localPosition = mid;
                side.localRotation = Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, 90f, 0f); // x along the side
                Primitive(PrimitiveType.Cube, "Pad", side, new Vector3(0f, 0.45f, 0f), new Vector3(len, 0.9f, 0.14f), pad, keepCollider: false);
                Primitive(PrimitiveType.Cube, "PadStripe", side, new Vector3(0f, 0.86f, 0f), new Vector3(len, 0.06f, 0.16f), red, keepCollider: false);
                for (int k = 1; k < 7; k++) // the fence over the pad: thin uprights and two rails
                    Primitive(PrimitiveType.Cube, "Wire", side, new Vector3(-len * 0.5f + k * len / 7f, 1.42f, 0f), new Vector3(0.025f, 1.06f, 0.025f), wire, keepCollider: false);
                foreach (float y in new[] { 1.4f, PostH - 0.03f })
                    Primitive(PrimitiveType.Cube, "Rail", side, new Vector3(0f, y, 0f), new Vector3(len, 0.04f, 0.04f), wire, keepCollider: false);
                var block = side.gameObject.AddComponent<BoxCollider>();
                block.center = new Vector3(0f, PostH * 0.5f, 0f);
                block.size = new Vector3(len, PostH, 0.2f);
            }
            return cage;
        }

        // ================================================================== C5: the castle gate in its arch

        /// <summary>Batch: the shut castle gate rebuilt inside the gatehouse's passage, cut to its arch (the user: the
        /// door was a big flat board standing in front of the arch).</summary>
        public static void TeamCGateDoorBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform castle = FindByPath("Environment/Island3/PirateCastle") ?? throw new InvalidOperationException("No castle");
            var log = new List<string>();
            ShutCastleGate(castle, log);
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SaveLog("teamc-gate.txt", log, "C5 gate fitted to the arch");
        });

        /// <summary>
        /// The shut gate: two plank leaves in the gatehouse passage just inside its outer arch ring, and a second pair in
        /// the yard-side arch (in front of the model's flat beige panel there). Their tops follow the arches measured on
        /// the model; iron straps with studs, an iron rim along the arch, ring handles. One collider over the passage up to
        /// the greybox arch above (nobody walks or climbs in).
        /// </summary>
        private static void ShutCastleGate(Transform castle, List<string> log)
        {
            foreach (string old in new[] { "GateShut", "GateShutInner" })
                if (castle.Find(old) is { } t) Object.DestroyImmediate(t.gameObject);
            const float Z = 20f; // the castle's north wall line
            Transform gatehouse = castle.Find("Tripo_castle_gatehouse");
            RestoreTripoMeshes(gatehouse, "castle_gatehouse");
            (Transform outer, float w) = GateLeaves(castle, gatehouse, "GateShut", Z + 1.0f, 0.07f, false, log);
            var blocker = outer.gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, 7.1f * 0.5f, 0f);
            blocker.size = new Vector3(2f * w + 0.3f, 7.1f, 0.5f);
            if (gatehouse != null) GateLeaves(castle, gatehouse, "GateShutInner", Z - 1.8f, 0.05f, true, log);
        }

        /// <summary>Puts a Tripo model's own meshes back (an earlier fix had cut a copy).</summary>
        private static void RestoreTripoMeshes(Transform model, string name)
        {
            GameObject prefab = model == null ? null : TripoPrefab(name);
            if (prefab == null) return;
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>())
            {
                string path = AnimationUtility.CalculateTransformPath(f.transform, model);
                Transform original = path.Length == 0 ? prefab.transform : prefab.transform.Find(path);
                if (original != null && original.TryGetComponent(out MeshFilter of) && of.sharedMesh != null) f.sharedMesh = of.sharedMesh;
            }
        }

        /// <summary>One pair of arched leaves at castle-local depth <paramref name="z"/>, fitted to the opening measured
        /// there (<paramref name="widestSide"/>: the arch by the wider side's ray, where the other side hits clutter).</summary>
        private static (Transform root, float halfWidth) GateLeaves(Transform castle, Transform gatehouse, string name, float z, float half, bool widestSide, List<string> log)
        {
            const float G = 0.3f;
            float jambL = 1.95f, jambR = 1.95f, top = G + 5.4f;
            var arch = new List<(float y, float half)>();
            if (gatehouse != null)
            {
                var samples = new List<(float y, float l, float r)>();
                using (var probe = new MeshProbe(gatehouse))
                {
                    Vector3 At(float x, float y) => castle.TransformPoint(new Vector3(x, y, z));
                    top = G + 0.3f + (probe.Cast(At(0f, G + 0.3f), castle.up, 20f) ?? throw new InvalidOperationException($"{name}: the arch has no top"));
                    for (float y = G + 0.2f; y < top - 0.01f; y += 0.05f)
                        if (probe.Cast(At(0f, y), -castle.right, 8f) is { } l && probe.Cast(At(0f, y), castle.right, 8f) is { } r)
                            samples.Add((y, l, r));
                }
                float Median(IEnumerable<float> v) { float[] s = v.OrderBy(x => x).ToArray(); return s[s.Length / 2]; }
                var jamb = samples.Where(s => s.y > G + 0.5f && s.y < G + 4f).ToArray();
                jambL = Median(jamb.Select(s => s.l));
                jambR = Median(jamb.Select(s => s.r));
                float last = (jambL + jambR) * 0.5f;
                if (widestSide)
                {
                    // Clutter in the way of the rays here (the panel's frame): the arch is a half ellipse from the jambs'
                    // top (4 m) to the measured crown, which matched the clean samples within ~10 cm.
                    for (float y = G + 4.05f; y < top; y += 0.05f)
                    {
                        float k = (y - G - 4f) / (top - G - 4f);
                        arch.Add((y, last * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k))));
                    }
                }
                else
                    foreach ((float y, float l, float r) in samples.Where(s => s.y >= G + 4f))
                        arch.Add((y, last = Mathf.Min(last, (l + r) * 0.5f)));
            }
            float W = (jambL + jambR) * 0.5f - 0.03f, cx = (jambR - jambL) * 0.5f;
            arch.Insert(0, (G + 4f, W + 0.03f));
            arch.Add((top, 0f));
            log.Add($"{name} at z {z:0.00}: {jambL:0.00} m left, {jambR:0.00} m right of the middle, arch top {top - G:0.00} m; {2f * W:0.00} m wide");
            log.Add("  arch: " + string.Join(" ", arch.Where((_, i) => i % 4 == 0).Select(a => $"{a.y - G:0.0}:{a.half:0.00}")));

            // The top of the door at |x| from its middle (y from the floor), 3 cm under the stone.
            float TopAt(float x)
            {
                x = Mathf.Abs(x);
                for (int i = 1; i < arch.Count; i++)
                    if (arch[i].half < x)
                    {
                        (float y0, float h0) = arch[i - 1];
                        (float y1, float h1) = arch[i];
                        float k = h0 - h1 > 1e-4f ? (h0 - x) / (h0 - h1) : 0f;
                        return Mathf.Lerp(y0, y1, Mathf.Clamp01(k)) - G - 0.03f;
                    }
                return top - G - 0.03f;
            }

            var root = new GameObject(name).transform;
            root.SetParent(castle, false);
            root.localPosition = new Vector3(cx, G, z);
            const int WoodA = 0, WoodB = 1, Core = 2, Iron = 3;
            var m = new MeshBuild(4);
            const float bottom = -0.03f;
            foreach (int side in new[] { -1, 1 })
            {
                int planks = Mathf.Max(4, Mathf.RoundToInt(W / 0.27f));
                float pw = W / planks;
                for (int i = 0; i < planks; i++)
                {
                    float a = side * i * pw, b = side * (i + 1) * pw, xl = Mathf.Min(a, b), xr = Mathf.Max(a, b);
                    // A plank (a hair narrower: dark seams between) on the dark core.
                    m.Prism((i + (side > 0 ? 1 : 0)) % 2 == 0 ? WoodA : WoodB, xl + 0.007f, xr - 0.007f, bottom, bottom,
                        TopAt(xl + 0.007f), TopAt(xr - 0.007f), -half, half);
                    m.Prism(Core, xl, xr, bottom, bottom, TopAt(xl) - 0.02f, TopAt(xr) - 0.02f, -half + 0.02f, half - 0.02f);
                }
                // The leaf runs on 30 cm into the jamb (hidden in the stone): it closes the model's door pockets beside it,
                // where you looked past the gate to the outside.
                float edgeTop = TopAt(W) - 0.3f;
                m.Prism(Core, side > 0 ? W : -W - 0.3f, side > 0 ? W + 0.3f : -W, bottom, bottom, edgeTop, edgeTop, -half, half);
                // Iron straps across each leaf, studded, on both faces.
                foreach (float y in new[] { 0.85f, 2.35f, 3.85f })
                {
                    if (TopAt(W) < y + 0.25f) continue;
                    float x0 = side * 0.06f, x1 = side * (W - 0.05f);
                    foreach (int face in new[] { -1, 1 })
                    {
                        float zIn = face * half, zOut = face * (half + 0.025f);
                        m.Box(Iron, new Vector3(Mathf.Min(x0, x1), y - 0.07f, Mathf.Min(zIn, zOut)), new Vector3(Mathf.Max(x0, x1), y + 0.07f, Mathf.Max(zIn, zOut)));
                        for (float s = 0.2f; s < W - 0.1f; s += 0.33f)
                        {
                            float sx = side * s, zs = face * (half + 0.05f);
                            m.Box(Iron, new Vector3(sx - 0.03f, y - 0.03f, Mathf.Min(zOut, zs)), new Vector3(sx + 0.03f, y + 0.03f, Mathf.Max(zOut, zs)));
                        }
                    }
                }
            }
            foreach (int face in new[] { -1, 1 })
            {
                float zIn = face * half, zOut = face * (half + 0.022f);
                float z0 = Mathf.Min(zIn, zOut), z1 = Mathf.Max(zIn, zOut);
                // The meeting stile in the middle, the edges, and the rim along the arch.
                m.Prism(Iron, -0.045f, 0.045f, bottom, bottom, TopAt(0.045f) - 0.04f, TopAt(0.045f) - 0.04f, z0, z1);
                foreach (int side in new[] { -1, 1 })
                    m.Prism(Iron, side > 0 ? W - 0.08f : -W, side > 0 ? W : -W + 0.08f, bottom, bottom, TopAt(W - 0.08f), TopAt(W - 0.08f), z0, z1);
                for (float x = -W; x < W - 0.001f; x += 0.1f)
                {
                    float xa = x, xb = Mathf.Min(W, x + 0.1f);
                    m.Prism(Iron, xa, xb, TopAt(xa) - 0.13f, TopAt(xb) - 0.13f, TopAt(xa), TopAt(xb), z0, z1);
                }
            }
            Material woodA = WoodMaterial("GateDoorPlankA", new Color(0.5f, 0.33f, 0.2f));
            Material woodB = WoodMaterial("GateDoorPlankB", new Color(0.42f, 0.27f, 0.16f));
            Material core = GetMaterial("GateDoorCore", new Color(0.07f, 0.05f, 0.04f));
            Material iron = GetMaterial("GateIron", new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.35f);
            GameObject leaves = MeshObject("Leaves", root, m.Save("Castle" + name), new[] { woodA, woodB, core, iron });
            TagSurface(leaves, SurfaceKind.Wood);
            foreach (int face in new[] { -1, 1 })
                foreach (int side in new[] { -1, 1 })
                {
                    GameObject ring = Primitive(PrimitiveType.Cylinder, "RingHandle", root, new Vector3(side * 0.3f, 1.45f, face * (half + 0.06f)), new Vector3(0.3f, 0.012f, 0.3f), iron, keepCollider: false);
                    ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Primitive(PrimitiveType.Sphere, "RingBoss", root, new Vector3(side * 0.3f, 1.58f, face * (half + 0.04f)), Vector3.one * 0.09f, iron, keepCollider: false);
                }
            log.Add($"  {name}: {root.position}, {TopAt(0f):0.00} m tall in the middle, {TopAt(W):0.00} m at its edges");
            return (root, W);
        }

        // ================================================================== the castle's curtain walls

        /// <summary>Batch: the castle's walls as clean masonry joined into the towers and the gatehouse (the Tripo wall
        /// pieces had ragged ends, a fake doorway and dark smears).</summary>
        public static void TeamCCastleWallsBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform castle = FindByPath("Environment/Island3/PirateCastle") ?? throw new InvalidOperationException("No castle");
            var log = new List<string>();
            CurtainWalls(castle, log);
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SaveLog("teamc-walls.txt", log, "castle walls rebuilt");
        });

        private static void CurtainWalls(Transform castle, List<string> log)
        {
            const float X = 31f, Z = 20f, G = 0.3f; // BuildPirateCastle's frame
            // The old pieces' size, to keep the castle's proportions.
            Transform[] tripoWalls = castle.Cast<Transform>().Where(t => t.name == "Tripo_castle_wall").ToArray();
            float wallTop = G + 10f, thick = 2.6f;
            if (tripoWalls.Length > 0)
            {
                Bounds[] b = tripoWalls.Select(w => TripoBounds(w, castle)).ToArray();
                wallTop = b.Select(x => x.max.y).OrderBy(x => x).ElementAt(b.Length / 2);
                thick = Mathf.Clamp(b.Select(x => Mathf.Min(x.size.x, x.size.z)).OrderBy(x => x).ElementAt(b.Length / 2) * 0.7f, 2.2f, 3.2f);
            }
            float towerR = 4.5f, gateHalf = 12f;
            if (castle.Cast<Transform>().FirstOrDefault(t => t.name == "Tripo_castle_round_tower") is { } tower)
            {
                Bounds tb = TripoBounds(tower, tower.parent);
                towerR = Mathf.Min(tb.size.x, tb.size.z) * 0.5f;
            }
            if (castle.Find("Tripo_castle_gatehouse") is { } gate) gateHalf = TripoBounds(gate, castle).extents.x;
            log.Add($"old wall pieces: {tripoWalls.Length}, top {wallTop - G:0.0} m, thickness ~{thick:0.0} m; towers {towerR:0.0} m round; gatehouse ±{gateHalf:0.0} m");
            foreach (Transform t in tripoWalls) Object.DestroyImmediate(t.gameObject);
            foreach (string n in new[] { "WallSouth", "WallWest", "WallEast", "WallNorthWest", "WallNorthEast", "CurtainWalls" })
                foreach (Transform t in castle.Cast<Transform>().Where(c => c.name == n).ToArray())
                    Object.DestroyImmediate(t.gameObject);

            Material stone = Island3RockMaterial("CastleStone", new Color(0.62f, 0.6f, 0.57f), "CastleStone", 3.2f);
            Material stoneDark = Island3RockMaterial("CastleStoneDark", new Color(0.42f, 0.4f, 0.38f), "CastleStone", 3.2f);
            Material slit = GetMaterial("CastleSlit", new Color(0.03f, 0.03f, 0.035f));
            var walls = new GameObject("CurtainWalls").transform;
            walls.SetParent(castle, false);
            TagSurface(walls.gameObject, SurfaceKind.Rock);

            float walk = wallTop - 1.4f; // the wall walk; parapet and merlons up to wallTop
            const float sunk = -1.6f;     // the wall's foot, into the plinth and the ground
            void Line(string name, Vector3 a, Vector3 b, float trimA, float trimB)
            {
                Vector3 along = b - a;
                float len = along.magnitude;
                var w = new GameObject(name).transform;
                w.SetParent(walls, false);
                w.localPosition = a;
                w.localRotation = Quaternion.LookRotation(along.normalized); // local z along the wall
                Vector3 mid = (a + b) * 0.5f;
                float outward = Mathf.Sign(Vector3.Dot(w.localRotation * Vector3.right, new Vector3(mid.x, 0f, mid.z)));
                float o = outward * thick * 0.5f;
                void Box(string n, float x0, float x1, float y0, float y1, float z0, float z1, Material mat, bool solid) =>
                    ResortBox(w, n, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f),
                        new Vector3(Mathf.Abs(x1 - x0), y1 - y0, z1 - z0), mat, solid);
                float zA = trimA, zB = len - trimB; // where the wall shows (outside the towers)

                Box("Wall", -thick * 0.5f, thick * 0.5f, G + sunk, walk, 0f, len, stone, true);
                Box("Plinth", -thick * 0.5f - 0.6f, thick * 0.5f + 0.6f, G + sunk, G + 0.9f, zA - 1f, zB + 1f, stoneDark, true);
                Box("PlinthCap", -thick * 0.5f - 0.35f, thick * 0.5f + 0.35f, G + 0.9f, G + 1.15f, zA - 1f, zB + 1f, stoneDark, false);
                Box("StringCourse", -thick * 0.5f - 0.12f, thick * 0.5f + 0.12f, G + (walk - G) * 0.55f, G + (walk - G) * 0.55f + 0.18f, zA, zB, stoneDark, false);
                Box("Cornice", -thick * 0.5f - 0.3f, thick * 0.5f + 0.3f, walk - 0.42f, walk - 0.06f, zA, zB, stoneDark, false);
                // Parapets: a breastwork on the outside with merlons, a low wall on the yard side.
                Box("Parapet", o - outward * 0.55f, o, walk, walk + 0.55f, zA, zB, stone, true);
                Box("InnerParapet", -o, -o + outward * 0.4f, walk, walk + 0.45f, zA, zB, stone, true);
                int merlons = Mathf.Max(1, Mathf.FloorToInt((zB - zA + 1f) / 2.3f));
                float step = (zB - zA) / merlons;
                for (int i = 0; i < merlons; i++)
                {
                    float zc = zA + step * (i + 0.5f);
                    Box("Merlon", o - outward * 0.55f, o, walk + 0.55f, wallTop, zc - 0.65f, zc + 0.65f, stone, true);
                }
                // Buttresses on the outside every ~9 m, an arrow slit between each pair.
                int bays = Mathf.Max(1, Mathf.RoundToInt((zB - zA) / 9f));
                float bay = (zB - zA) / bays;
                for (int i = 0; i <= bays; i++)
                {
                    float zc = zA + bay * i;
                    if (i > 0 && i < bays)
                    {
                        Box("Buttress", o, o + outward * 0.75f, G + 0.9f, walk - 1.6f, zc - 0.8f, zc + 0.8f, stone, true);
                        Box("ButtressCap", o, o + outward * 0.55f, walk - 1.6f, walk - 1.35f, zc - 0.7f, zc + 0.7f, stoneDark, false);
                    }
                    if (i < bays)
                    {
                        float zs = zc + bay * 0.5f;
                        Box("Slit", o, o + outward * 0.03f, G + (walk - G) * 0.62f, G + (walk - G) * 0.62f + 1.3f, zs - 0.09f, zs + 0.09f, slit, false);
                        Box("SlitSill", o, o + outward * 0.12f, G + (walk - G) * 0.62f - 0.12f, G + (walk - G) * 0.62f, zs - 0.25f, zs + 0.25f, stoneDark, false);
                    }
                }
                log.Add($"{name}: {len:0.0} m, shows {zB - zA:0.0} m, {merlons} merlons, {bays - 1} buttresses");
            }
            float trim = towerR * 0.85f;
            Line("WallSouth", new Vector3(-X, 0f, -Z), new Vector3(X, 0f, -Z), trim, trim);
            Line("WallWest", new Vector3(-X, 0f, Z), new Vector3(-X, 0f, -Z), trim, trim);
            Line("WallEast", new Vector3(X, 0f, -Z), new Vector3(X, 0f, Z), trim, trim);
            float g = gateHalf - 1f;
            Line("WallNorthWest", new Vector3(-g, 0f, Z), new Vector3(-X, 0f, Z), 1f, trim);
            Line("WallNorthEast", new Vector3(X, 0f, Z), new Vector3(g, 0f, Z), trim, 1f);
        }

        // ================================================================== C4: the river out to the sea

        /// <summary>Batch: island 3's ground redrawn with the river's mouth cut through to the sea (Island3Height: the
        /// last stretch used to stop on the foot of the plateau's slope, a dry strip short of the coast); plants now in
        /// the water go, the rest are settled on the new banks.</summary>
        public static void TeamCRiverMouthBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            var log = new List<string>();
            var groundGo = island.Find("Island3Ground").gameObject;
            Mesh mesh = GetIsland3Mesh();
            groundGo.GetComponent<MeshFilter>().sharedMesh = mesh;
            var collider = groundGo.GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            if (GameObject.Find("Environment/BeachTerrain") is { } terrain) RefreshBeachTerrain(terrain);
            Physics.SyncTransforms();

            // The bed along the mouth, from the jungle to the sea.
            float worst = float.MinValue;
            for (int i = 0; i < Island3RiverMouth.Length - 1; i++)
                for (float k = 0f; k <= 1f; k += 0.02f)
                {
                    Vector2 p = Vector2.Lerp(Island3RiverMouth[i], Island3RiverMouth[i + 1], k);
                    worst = Mathf.Max(worst, Island3GroundAt(island, p.x, p.y));
                }
            log.Add($"river mouth bed: highest {worst:0.00} m (the sea is at {WaterLevel})");
            if (worst > WaterLevel - 0.5f) throw new InvalidOperationException($"The river's bed still rises to {worst:0.00} m");

            int drowned = 0;
            foreach (string group in new[] { "Palms", "Jungle" })
                if (island.Find(group) is { } plants)
                    foreach (Transform p in plants.Cast<Transform>().ToArray())
                        if (Island3GroundAt(island, p.position.x, p.position.z) < WaterLevel + 0.25f)
                        {
                            Object.DestroyImmediate(p.gameObject);
                            drowned++;
                        }
            log.Add($"{drowned} plants that stood where the water now is taken out");
            foreach (Transform t in island.Cast<Transform>())
                foreach (Transform c in t.Cast<Transform>())
                    if (DistanceToLine(new Vector2(c.position.x, c.position.z), Island3RiverMouth) < 8f && t.name is not ("Jungle" or "Palms"))
                        log.Add($"near the new mouth (check): {t.name}/{c.name} at {c.position}");
            SettleIsland3Plants(island, false);
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            SaveLog("teamc-river.txt", log, "C4 river reaches the sea");
        });

        // ================================================================== B4: palms off island 3, jungle where marked

        // Where the user marked jungle on island 3 (the screenshot, 2026-10-13): the meadow between the tavern and the
        // castle, and the grass between the village and the jungle from the crossroads north.
        private static readonly Vector2[][] Island3NewJungle =
        {
            new Vector2[] { new(34f, -926f), new(66f, -925f), new(73f, -938f), new(73f, -957f), new(60f, -958f), new(38f, -957f), new(32f, -944f) },
            new Vector2[] { new(30f, -862f), new(42f, -866f), new(56f, -884f), new(70f, -902f), new(72f, -917f), new(50f, -921f), new(30f, -918f), new(25f, -903f), new(26f, -886f) },
        };

        /// <summary>Batch: island 3's coconut palms go (B4: they belong on island 1; the jungle's palms become jungle
        /// trees), and jungle (trees, bushes, ferns) fills the two meadows the user marked.</summary>
        public static void TeamCJunglePalmsBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            var log = new List<string>();
            Transform palms = island.Find("Palms");
            int gone = 0;
            if (palms != null)
                foreach (Transform p in palms.Cast<Transform>().ToArray()) { Object.DestroyImmediate(p.gameObject); gone++; }
            log.Add($"{gone} coconut palms taken off island 3");

            Transform jungle = island.Find("Jungle") ?? throw new InvalidOperationException("No jungle");
            var rng = new System.Random(4413);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            GameObject Tree(Vector3 feet, float yaw)
            {
                GameObject tree = TripoModel("jungle_tree", jungle, feet, yaw, Vector3.one * R(0.85f, 1.3f));
                var trunk = tree.AddComponent<CapsuleCollider>();
                trunk.height = 6f;
                trunk.radius = 0.35f;
                trunk.center = Vector3.up * 3f;
                return tree;
            }
            int swapped = 0;
            foreach (Transform p in jungle.Cast<Transform>().Where(c => c.name == "palm_tall_Pivot").ToArray())
            {
                Tree(p.localPosition, R(0f, 360f));
                Object.DestroyImmediate(p.gameObject);
                swapped++;
            }
            log.Add($"{swapped} palms in the jungle became jungle trees");

            // What the new jungle keeps clear of.
            var trees = jungle.Cast<Transform>().Where(c => c.name == "Tripo_jungle_tree").Select(c => new Vector2(c.position.x, c.position.z)).ToList();
            var bushes = jungle.Cast<Transform>().Where(c => c.name == "Tripo_jungle_bush").Select(c => new Vector2(c.position.x, c.position.z)).ToList();
            var ferns = jungle.Cast<Transform>().Where(c => c.name == "Tripo_jungle_fern").Select(c => new Vector2(c.position.x, c.position.z)).ToList();
            var marks = new List<Vector2>();
            foreach (string group in new[] { "Story", "NightArrival" })
                if (island.Find(group) is { } g)
                    marks.AddRange(g.GetComponentsInChildren<Transform>().Where(t => t != g).Select(t => new Vector2(t.position.x, t.position.z)));
            Vector2 ring = island.Find("Octagon") is { } oct ? new Vector2(oct.position.x, oct.position.z) : OctagonOutsideAt;
            ShackRoom room = TripoShackRoom(TripoTavern);
            Vector3 doorWorld = island.Find("PirateTavern") is { } tav ? tav.TransformPoint(new Vector3(room.doorX, 0f, room.z1 + 1.5f)) : new Vector3(TavernAt.x, 0f, TavernAt.y + 12f);
            Vector2 tavernDoor = new(doorWorld.x, doorWorld.z);
            Vector2 NearestRoad(Vector2 p)
            {
                Vector2 best = p; float d = float.MaxValue;
                foreach (Vector2[] road in Island3Roads)
                    for (int i = 0; i < road.Length - 1; i++)
                        for (float k = 0f; k <= 1f; k += 0.05f)
                        {
                            Vector2 q = Vector2.Lerp(road[i], road[i + 1], k);
                            if ((q - p).sqrMagnitude < d) { d = (q - p).sqrMagnitude; best = q; }
                        }
                return best;
            }
            Vector2[] ringPath = { ring, NearestRoad(ring) };
            Vector2[] ringToTavern = { ring, tavernDoor };
            bool Near(List<Vector2> list, Vector2 p, float d) => list.Any(q => (q - p).sqrMagnitude < d * d);
            bool Blocked(Vector2 p, float h, float radius)
            {
                foreach (Collider c in Physics.OverlapSphere(new Vector3(p.x, h + 1.2f, p.y), radius, ~0, QueryTriggerInteraction.Ignore))
                    if (c.name != "Island3Ground" && !c.transform.IsChildOf(jungle)) return true;
                return false;
            }
            float minX = Island3NewJungle.SelectMany(q => q).Min(q => q.x), maxX = Island3NewJungle.SelectMany(q => q).Max(q => q.x);
            float minZ = Island3NewJungle.SelectMany(q => q).Min(q => q.y), maxZ = Island3NewJungle.SelectMany(q => q).Max(q => q.y);
            int newTrees = 0, newBushes = 0, newFerns = 0;
            for (int i = 0; i < 24000; i++)
            {
                int kind = i < 9000 ? 0 : i < 16000 ? 1 : 2;
                var p = new Vector2(R(minX, maxX), R(minZ, maxZ));
                if (!Island3NewJungle.Any(poly => InPolygon(p, poly))) continue;
                float h = Island3GroundAt(island, p.x, p.y);
                if (h < WaterLevel + 0.6f) continue;
                float road = DistanceToRoads(p);
                if (VillageHouses.Any(v => Vector2.Distance(v.at, p) < (kind == 0 ? 8.5f : 6.5f))) continue;
                if (Vector2.Distance(p, TavernAt) < TavernRadius + (kind == 0 ? 3f : 1f)) continue;
                if (Vector2.Distance(p, ring) < (kind == 0 ? 9f : 6.5f)) continue;
                if (DistanceToLine(p, ringPath) < 2.6f || DistanceToLine(p, ringToTavern) < 2.6f) continue;
                if (BoxDistanceOut(p.x, p.y, CastleCenter, CastleHalf, 0f) < 7f) continue;
                if (Island3Pools.Any(pool => Vector2.Distance(p, pool.centre) < pool.radius + 3f)) continue;
                if (Near(marks, p, kind == 0 ? 4f : 2.5f)) continue;
                var feet = new Vector3(p.x, h - 0.1f, p.y);
                float yaw = R(0f, 360f);
                if (kind == 0)
                {
                    if (road < 4.8f || Near(trees, p, 6f) || Blocked(p, h, 1.6f)) continue;
                    Tree(feet, yaw);
                    trees.Add(p);
                    newTrees++;
                }
                else if (kind == 1)
                {
                    if (road < 2.8f || Near(trees, p, 1.8f) || Near(bushes, p, 3f) || Blocked(p, h, 1f)) continue;
                    TripoModel("jungle_bush", jungle, feet, yaw, Vector3.one * R(0.8f, 1.5f));
                    bushes.Add(p);
                    newBushes++;
                }
                else
                {
                    if (road < 2.3f || Near(trees, p, 1.2f) || Near(bushes, p, 1.5f) || Near(ferns, p, 1.6f) || Blocked(p, h, 0.7f)) continue;
                    TripoModel("jungle_fern", jungle, feet, yaw, Vector3.one * R(0.8f, 1.4f));
                    ferns.Add(p);
                    newFerns++;
                }
            }
            log.Add($"new jungle in the marked meadows: {newTrees} trees, {newBushes} bushes, {newFerns} ferns");
            SettleIsland3Plants(island, false);
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SaveLog("teamc-jungle.txt", log, "B4 palms off island 3, jungle where marked");
        });

        // ================================================================== B4: coconut palms back on island 1

        /// <summary>Batch: eleven shakeable coconut palms back on island 1, in small groups round its edges (the busy
        /// front beach, the docks and everything standing on the sand kept clear).</summary>
        public static void TeamBIsland1PalmsBatch() => RunBatch(() =>
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            var log = new List<string>();
            if (env.Find("Island1Palms") is { } old) Object.DestroyImmediate(old.gameObject);
            var group = new GameObject("Island1Palms").transform;
            group.SetParent(env, false);

            // Everything drawn on the island (towels, umbrellas, kiosks...), to keep the palms off it.
            Bounds islandBox = new(new Vector3(IslandCenter.x, 0f, IslandCenter.y), new Vector3(IslandHalfSize.x * 2f + 20f, 60f, IslandHalfSize.y * 2f + 20f));
            Bounds[] things = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && islandBox.Intersects(r.bounds) && r.bounds.size.x < 40f && r.bounds.size.z < 40f && !r.transform.IsChildOf(group))
                .Select(r => r.bounds).ToArray();
            bool Free(Vector2 p, float clear) => !things.Any(b =>
                p.x > b.min.x - clear && p.x < b.max.x + clear && p.y > b.min.z - clear && p.y < b.max.z + clear && b.min.y < 4f);

            // Groups (island-relative: x -1 west .. 1 east, z -1 front .. 1 back) and the palms' offsets in each.
            (Vector2 anchor, Vector2[] offsets)[] groves =
            {
                (new(-0.78f, 0.62f), new Vector2[] { new(0f, 0f), new(2.6f, 1.4f), new(-0.6f, -2.8f) }),
                (new(0.8f, 0.66f), new Vector2[] { new(0f, 0f), new(-2.8f, 0.9f), new(0.8f, -2.7f) }),
                (new(-0.86f, -0.05f), new Vector2[] { new(0f, 0f), new(0.9f, 2.9f) }),
                (new(0.88f, 0.02f), new Vector2[] { new(0f, 0f), new(-1f, -2.8f) }),
                (new(0.08f, 0.86f), new Vector2[] { new(0f, 0f) }),
            };
            var placed = new List<Vector2>();
            int index = 0;
            foreach ((Vector2 anchor, Vector2[] offsets) in groves)
                foreach (Vector2 offset in offsets)
                {
                    Vector2 want = IslandCenter + Vector2.Scale(anchor, IslandHalfSize) + offset;
                    Vector2? spot = null;
                    // Inward until on dry sand and clear of things and the other palms (a little spiral search).
                    for (int k = 0; k < 60 && spot == null; k++)
                    {
                        Vector2 p = Vector2.Lerp(want, IslandCenter, k / 60f * 0.35f) + (k == 0 ? Vector2.zero : new Vector2(Mathf.Cos(k * 2.4f), Mathf.Sin(k * 2.4f)) * (0.6f + k * 0.05f));
                        // Dry sand (it lies at 0, the sea at WaterLevel) all round, 4 m from the wet edge.
                        if (BeachHeight(p.x, p.y) < WaterLevel + 0.3f) continue;
                        bool dry = true;
                        for (int d = 0; d < 8 && dry; d++)
                            dry = BeachHeight(p.x + Mathf.Cos(d * Mathf.PI / 4f) * 4f, p.y + Mathf.Sin(d * Mathf.PI / 4f) * 4f) > WaterLevel + 0.15f;
                        if (!dry || !Free(p, 1.8f)) continue;
                        if (placed.Any(q => Vector2.Distance(q, p) < 2.4f)) continue;
                        spot = p;
                    }
                    if (spot is not { } s) { log.Add($"no room near {want}"); continue; }
                    var feet = new Vector3(s.x, BeachHeight(s.x, s.y) - 0.15f, s.y);
                    GameObject palm = MeshyArt.Place("palm_tall", group, 6.5f + index % 3 * 0.65f, feet, index * 67f + 20f);
                    if (palm == null) throw new InvalidOperationException("palm_tall model missing");
                    var col = palm.AddComponent<CapsuleCollider>();
                    col.center = new Vector3(0f, 2.4f, 0f);
                    col.height = 4.8f;
                    col.radius = 0.3f;
                    MakeShakeable(palm);
                    placed.Add(s);
                    index++;
                    log.Add($"palm at ({s.x:0.0}, {s.y:0.0})");
                }
            log.Insert(0, $"{placed.Count} coconut palms on island 1");
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island1");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SaveLog("teamb-palms.txt", log, "B4 palms back on island 1");
        });
    }
}
