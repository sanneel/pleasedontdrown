using System.Collections.Generic;
using PleaseDontDrown.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Bullet wounds that stay on people where the bullets went in, the way Unreal's mesh decals work: the patch of
    /// the body mesh round the hit (as it is posed at that moment) is cut out, copied a couple of millimetres proud of
    /// the skin and fixed to the bone under it, so the wound wraps round an arm or a chest and moves with the body.
    /// (Drawn first as an extra skinned mesh on the body's bones, it crashed the game on quitting.) Each wound is a dark
    /// hole with torn red edges and a run of blood that trickles down over a few seconds (down at the moment of the
    /// hit). A spatter lands on whatever is just behind the body (sand, a wall, a deck).
    /// Every machine makes its own from its own copy of the bullet (ProjectileSystem.OnHit); nothing is synced.
    /// The local player's own body (not drawn in first person) never gets them.
    /// </summary>
    public sealed class BulletWounds : MonoBehaviour
    {
        private const float WoundLife = 300f;
        private const int MaxPerBody = 18;
        private const int MaxLayers = 48;
        private const float RunSeconds = 4.5f;
        private const float Proud = 0.0025f; // metres off the skin (no z-fighting)
        private const int MaxSplats = 160;
        private const float SplatLife = 90f;

        /// <summary>One wound: its own little mesh on a bone, and where each vertex sits in the wound's plane.</summary>
        private sealed class Wound
        {
            public float Born, Width, Run;
            public Vector3 Normal; // world, at the moment of the hit (for 'wounds cam')
            public readonly List<Vector3> Vertices = new(); // world, as cut; then the bone's own space
            public readonly List<Vector3> Normals = new(); // outward, the same way
            public readonly List<Vector2> Plane = new(); // metres: x across, y up (world up at the moment of the hit)
            public readonly List<int> Triangles = new();
            public int Bone; // the body's bone most of the patch hangs on
            public GameObject Go;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        /// <summary>The wounds on one body mesh.</summary>
        private sealed class Layer
        {
            public SkinnedMeshRenderer Body;
            public Mesh BodyMesh;
            public readonly List<Wound> Wounds = new();
            public bool Growing;
        }

        /// <summary>A body mesh's data, read once.</summary>
        private sealed class MeshData
        {
            public Vector3[] Vertices, Normals;
            public int[] Triangles;
            public BoneWeight[] Weights;
            public Matrix4x4[] Bindposes;
        }

        private static BulletWounds _instance;
        private static bool _off;
        private static float _lastMs, _worstMs;
        private static bool _log;
        private static bool _noBleed, _noSpatter, _noDecal; // (tests: switch parts off one by one)
        private static Vector3 _lastPoint, _lastNormal = Vector3.up;

        private readonly List<Layer> _layers = new();
        private readonly Dictionary<Mesh, MeshData> _meshes = new();
        private readonly List<SkinnedMeshRenderer> _found = new();
        private Vector3[] _skinned = new Vector3[0];
        private bool[] _near = new bool[0];
        private Matrix4x4[] _boneMatrices = new Matrix4x4[0];
        private readonly Dictionary<int, int> _remap = new();
        private readonly List<Vector2> _uv = new();
        private readonly Dictionary<int, float> _boneVotes = new();
        private readonly RaycastHit[] _behind = new RaycastHit[12];

        private Material _woundMaterial, _splatMaterial;
        private Mesh _splatMesh;
        // Spatters are a ring of small quad objects, reused oldest first. (Drawn with Graphics.RenderMeshInstanced and
        // this lit material, the game crashed on quitting.)
        private readonly MeshRenderer[] _splats = new MeshRenderer[MaxSplats];
        private readonly float[] _splatTimes = new float[MaxSplats];
        private int _splatNext;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _off = false;
            _lastMs = _worstMs = 0f;
            _log = false;
        }

        // (After DevCommands has cleared its list, which it does at the same early step as ResetStatics.)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCommands()
        {
            Core.DevCommands.Register("wounds", "[0|1|clear|log|cam [dist fov]]", "Bullet wounds on bodies: on, off, wipe them, log each hit, or film the newest one.", args =>
            {
                if (args.Length > 0 && args[0] == "clear") { if (_instance != null) _instance.ClearAll(); return; }
                if (args.Length > 0 && args[0] == "cam")
                {
                    // The movie camera a little way off the newest wound, looking straight at it.
                    float distance = args.Length > 1 ? Core.DevCommands.ParseFloat(args, 1) : 0.35f;
                    float fov = args.Length > 2 ? Core.DevCommands.ParseFloat(args, 2) : 35f;
                    Vector3 eye = _lastPoint + (_lastNormal + Vector3.up * 0.25f).normalized * distance;
                    string F(float v) => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    Core.DevCommands.Execute($"cam fixed {F(eye.x)} {F(eye.y)} {F(eye.z)} {F(_lastPoint.x)} {F(_lastPoint.y)} {F(_lastPoint.z)} {F(fov)}");
                    return;
                }
                if (args.Length > 1 && args[0] == "skip")
                {
                    _noBleed = args[1].Contains("bleed");
                    _noSpatter = args[1].Contains("spatter");
                    _noDecal = args[1].Contains("decal");
                    Core.DevCommands.Print($"skipping bleed {_noBleed}, spatter {_noSpatter}, decal {_noDecal}");
                    return;
                }
                if (args.Length > 1 && args[0] == "mat" && _instance != null)
                {
                    // Tests: look at the wounds with other material settings ("sprite": unlit, the picture's own colours;
                    // "smooth 0.2": the lit one with that shine; "tint r g b").
                    Material m = _instance._woundMaterial;
                    if (args[1] == "sprite") m = new Material(Shader.Find("Sprites/Default")) { mainTexture = _instance._woundMaterial.mainTexture };
                    else if (args[1] == "smooth" && args.Length > 2) m.SetFloat("_Smoothness", Core.DevCommands.ParseFloat(args, 2));
                    else if (args[1] == "tint" && args.Length > 4) m.SetColor("_BaseColor", new Color(Core.DevCommands.ParseFloat(args, 2), Core.DevCommands.ParseFloat(args, 3), Core.DevCommands.ParseFloat(args, 4)));
                    foreach (Layer l in _instance._layers)
                        foreach (Wound w in l.Wounds)
                            if (w.Renderer != null) w.Renderer.sharedMaterial = m;
                    Core.DevCommands.Print($"wound material: {m.shader.name}, smoothness {(m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f):F2}, " +
                                           $"colour {(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.clear)}, keywords {string.Join(" ", m.shaderKeywords)}");
                    return;
                }
                if (args.Length > 0 && args[0] == "png")
                {
                    // The wound and spatter pictures as PNGs next to the game (to check them by eye).
                    string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Screenshots"));
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "wound_texture.png"), WoundTexture().EncodeToPNG());
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "spatter_texture.png"), SplatTexture().EncodeToPNG());
                    Core.DevCommands.Print($"saved to {dir}");
                    return;
                }
                if (args.Length > 0 && args[0] == "log") { _log = !_log; Core.DevCommands.Print($"wound log {(_log ? "on" : "off")}"); return; }
                _off = args.Length > 0 && Core.DevCommands.ParseFloat(args, 0) < 0.5f;
                if (_off && _instance != null) _instance.ClearAll();
                Core.DevCommands.Print($"bullet wounds {(_off ? "off" : "on")}, {(_instance != null ? _instance.Count : 0)} now, " +
                                       $"the last took {_lastMs:F2} ms, the slowest {_worstMs:F2} ms");
            });
        }

        private int Count
        {
            get
            {
                int n = 0;
                foreach (Layer l in _layers) n += l.Wounds.Count;
                return n;
            }
        }

        private static BulletWounds Instance
        {
            get
            {
                if (_instance == null) _instance = new GameObject("BulletWounds").AddComponent<BulletWounds>();
                return _instance;
            }
        }

        private void Awake()
        {
            _instance = this;
            Material source = Resources.Load<Material>("BulletWound");
            _woundMaterial = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default"));
            _woundMaterial.mainTexture = WoundTexture();
            if (_woundMaterial.HasProperty("_BaseMap")) _woundMaterial.SetTexture("_BaseMap", _woundMaterial.mainTexture);
            _splatMaterial = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default"));
            _splatMaterial.mainTexture = SplatTexture();
            if (_splatMaterial.HasProperty("_BaseMap")) _splatMaterial.SetTexture("_BaseMap", _splatMaterial.mainTexture);
            if (_splatMaterial.HasProperty("_Smoothness")) _splatMaterial.SetFloat("_Smoothness", 0.55f);
            _splatMesh = SplatQuad();
        }

        // ------------------------------------------------------------------ adding

        /// <summary>
        /// A bullet went into a character at <paramref name="point"/> flying along <paramref name="direction"/>:
        /// a wound on the drawn body where the line of the bullet meets it, and a spatter behind.
        /// </summary>
        public static void Add(Collider hit, Vector3 point, Vector3 direction, ProjectileKind kind)
        {
            if (_off || hit == null || direction.sqrMagnitude < 1e-6f) return;
            Transform root = CharacterRoot(hit);
            if (_log) Debug.Log($"[Wound] hit {hit.name} at {point:F2}: {(root != null ? root.name : "not a character")}");
            if (root == null) return;
            // (Bigger than a real entry wound, so it reads as a hole from a few metres: the user, 2026-10-22, "add HOLEs".)
            float width = kind == ProjectileKind.Heavy ? 0.08f : kind == ProjectileKind.Pellet ? 0.035f : 0.06f;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Instance.AddWound(root, point, direction.normalized, width * Random.Range(0.9f, 1.15f), kind);
            _lastMs = (float)watch.Elapsed.TotalMilliseconds;
            _worstMs = Mathf.Max(_worstMs, _lastMs);
        }

        /// <summary>The character a collider belongs to (a lifeguard other than us, a tourist, a story character).</summary>
        private static Transform CharacterRoot(Collider c)
        {
            if (c.GetComponentInParent<PlayerHub>() is { } player) return player == PlayerHub.Local ? null : player.transform;
            if (c.GetComponentInParent<Rescue.VictimBody>() is { } victim) return victim.transform;
            // (A tourist's arms and legs hang outside its hierarchy; the item knows them.)
            if (Items.Item.FromCollider(c) is { } item && item.TryGetComponent(out Rescue.VictimBody limbOwner)) return limbOwner.transform;
            if (c.GetComponentInParent<Story.StoryNpc>() is { } npc) return npc.transform;
            return null;
        }

        private void AddWound(Transform root, Vector3 point, Vector3 dir, float width, ProjectileKind kind)
        {
            // Where the bullet's line meets the drawn skin (the collider it hit is only roughly the body's shape).
            root.GetComponentsInChildren(false, _found);
            Vector3 origin = point - dir * 0.5f;
            const float reach = 1.0f;
            SkinnedMeshRenderer bestRenderer = null;
            Wound best = null;
            float bestT = reach;
            Vector3 at = point;
            foreach (SkinnedMeshRenderer r in _found)
            {
                if (_log) Debug.Log($"[Wound]   {r.name}: usable {Usable(r)}, mesh {(r.sharedMesh != null ? r.sharedMesh.name : "none")} " +
                                    $"readable {(r.sharedMesh != null && r.sharedMesh.isReadable)}, shadows {r.shadowCastingMode}, off {r.forceRenderingOff}");
                if (!Usable(r)) continue;
                Bounds b = r.bounds;
                b.Expand(0.1f);
                if (!b.IntersectRay(new Ray(origin, dir), out float enter) || enter > bestT)
                {
                    if (_log) Debug.Log($"[Wound]   {r.name}: the bullet's line misses its bounds {b}");
                    continue;
                }
                MeshData data = DataOf(r.sharedMesh);
                if (data == null || !Skin(r, data, origin, dir, reach))
                {
                    if (_log) Debug.Log($"[Wound]   {r.name}: no skinning data (weights {r.sharedMesh.boneWeights.Length}, vertices {r.sharedMesh.vertexCount})");
                    continue;
                }
                int[] tris = data.Triangles;
                Vector3 rayDir = dir;
                int tri = FirstHit(tris, origin, rayDir, bestT, out float near);
                if (tri < 0 && NearestSkin(data.Vertices.Length, point, 0.15f, out Vector3 skin))
                {
                    // The bullet clipped the edge of the collider beside the drawn body (by the neck, under an arm):
                    // the wound goes on the nearest skin instead, along a line from where the bullet came.
                    rayDir = (skin - origin).normalized;
                    tri = FirstHit(tris, origin, rayDir, bestT, out near);
                }
                if (_log) Debug.Log($"[Wound]   {r.name}: {tris.Length / 3} triangles, hit {(tri >= 0 ? $"at {near - 0.5f:F3} m from the collider's point" : "none")}");
                if (tri < 0) continue;
                // Nearer than any part so far (skin under a shirt loses to the shirt): cut it now, while this mesh's
                // posed vertices are still in _skinned.
                Wound cut = CutWound(data, tri, origin + rayDir * near, rayDir, width);
                if (_log) Debug.Log($"[Wound]   {r.name}: cut {(cut != null ? cut.Triangles.Count / 3 : 0)} triangles, run {(cut != null ? cut.Run : 0f):F3} m");
                if (cut == null) continue;
                bestT = near;
                at = origin + rayDir * near;
                best = cut;
                bestRenderer = r;
            }
            if (best == null) return;
            if (!_noDecal && Place(bestRenderer, best))
            {
                Layer layer = LayerFor(bestRenderer);
                layer.Wounds.Add(best);
                while (layer.Wounds.Count > MaxPerBody) DropWound(layer, 0);
                layer.Growing = true;
            }

            // Blood thrown back out of it, drops (a stain on the sand under them), and a spatter behind.
            _lastPoint = at;
            _lastNormal = best.Normal;
            if (!_noBleed) Rescue.BloodFx.Bleed(at);
            if (!_noSpatter && (kind != ProjectileKind.Pellet || Random.value < 0.35f)) Spatter(root, at, dir, kind == ProjectileKind.Heavy ? 1.5f : kind == ProjectileKind.Pellet ? 0.6f : 1f);
        }

        /// <summary>The patch of skin round the hit, copied off the body mesh (null if none of it faces the bullet).</summary>
        private Wound CutWound(MeshData data, int tri, Vector3 hitPoint, Vector3 dir, float width)
        {
            Vector3 a = _skinned[data.Triangles[tri]], b = _skinned[data.Triangles[tri + 1]], c = _skinned[data.Triangles[tri + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, dir) > 0f) normal = -normal;
            bool flipped = Vector3.Dot(Vector3.Cross(b - a, c - a), dir) > 0f; // (never, now that only front faces count)

            // The wound's plane: up is world up laid on the skin. On skin facing up or down (someone lying there) the
            // blood has nowhere to run, so there's no run.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, normal);
            float runShare = Mathf.Clamp01((up.magnitude - 0.35f) / 0.4f);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
            up.Normalize();
            Vector3 right = Vector3.Cross(normal, up);
            float run = Random.Range(0.06f, 0.13f) * (width / 0.06f) * runShare;

            var wound = new Wound { Born = Time.time, Width = width, Run = run, Normal = normal };
            float below = width * 0.5f + run;
            float side = width * 0.55f;
            _remap.Clear();
            _boneVotes.Clear();
            int[] tris = data.Triangles;
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (!_near[tris[i]] || !_near[tris[i + 1]] || !_near[tris[i + 2]]) continue;
                Vector3 p0 = _skinned[tris[i]], p1 = _skinned[tris[i + 1]], p2 = _skinned[tris[i + 2]];
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.sqrMagnitude < 1e-12f) continue;
                // Facing the same way as the skin at the hit, or across it (a crease the blood runs over), not the back of
                // the arm through it.
                if (Vector3.Dot(cross.normalized, flipped ? -normal : normal) < -0.1f) continue;
                if (!InBox(p0, p1, p2, hitPoint, right, up, normal, side, below, width * 0.6f)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[i + k];
                    if (!_remap.TryGetValue(vi, out int ni))
                    {
                        ni = wound.Vertices.Count;
                        _remap[vi] = ni;
                        wound.Vertices.Add(_skinned[vi]);
                        BoneWeight bw = data.Weights[vi];
                        _boneVotes[bw.boneIndex0] = (_boneVotes.TryGetValue(bw.boneIndex0, out float v0) ? v0 : 0f) + bw.weight0;
                        Vector3 d = _skinned[vi] - hitPoint;
                        wound.Plane.Add(new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, up)));
                    }
                    wound.Triangles.Add(ni);
                }
            }
            if (wound.Triangles.Count == 0) return null;
            float most = -1f;
            foreach (KeyValuePair<int, float> vote in _boneVotes)
                if (vote.Value > most) { most = vote.Value; wound.Bone = vote.Key; }
            // Raised a little off the skin, along the patch's own normals (so it doesn't flicker into it).
            var lift = new Vector3[wound.Vertices.Count];
            for (int i = 0; i < wound.Triangles.Count; i += 3)
            {
                int i0 = wound.Triangles[i], i1 = wound.Triangles[i + 1], i2 = wound.Triangles[i + 2];
                Vector3 n = Vector3.Cross(wound.Vertices[i1] - wound.Vertices[i0], wound.Vertices[i2] - wound.Vertices[i0]);
                lift[i0] += n;
                lift[i1] += n;
                lift[i2] += n;
            }
            for (int i = 0; i < lift.Length; i++)
            {
                Vector3 n = lift[i].sqrMagnitude > 1e-14f ? lift[i].normalized : normal;
                if (Vector3.Dot(n, normal) < 0f) n = -n; // (outward, whatever the mesh's winding)
                wound.Vertices[i] += n * Proud;
                wound.Normals.Add(n);
            }
            return wound;
        }

        /// <summary>The wound's own mesh, fixed to its bone (in that bone's space, so it moves with it).</summary>
        private bool Place(SkinnedMeshRenderer body, Wound wound)
        {
            Transform[] bones = body.bones;
            Transform bone = wound.Bone >= 0 && wound.Bone < bones.Length && bones[wound.Bone] != null ? bones[wound.Bone]
                : body.rootBone != null ? body.rootBone : body.transform;
            var go = new GameObject("BulletWound");
            go.transform.SetParent(bone, false);
            // Into the bone's space. Normals go by the transpose (what the renderer undoes): recalculated from the
            // triangles they pointed into the body on a mirrored bone, lit from inside by the sand.
            Matrix4x4 toWorldT = bone.localToWorldMatrix.transpose;
            for (int i = 0; i < wound.Vertices.Count; i++)
            {
                wound.Vertices[i] = bone.InverseTransformPoint(wound.Vertices[i]);
                wound.Normals[i] = toWorldT.MultiplyVector(wound.Normals[i]).normalized;
            }
            var mesh = new Mesh { name = "BulletWound" };
            mesh.SetVertices(wound.Vertices);
            mesh.SetTriangles(wound.Triangles, 0);
            _uv.Clear();
            AppendUvs(wound);
            mesh.SetUVs(0, _uv);
            mesh.SetNormals(wound.Normals);
            mesh.RecalculateBounds();
            go.layer = body.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _woundMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = body.lightProbeUsage;
            renderer.renderingLayerMask = body.renderingLayerMask;
            renderer.forceRenderingOff = body.forceRenderingOff;
            wound.Go = go;
            wound.Renderer = renderer;
            wound.Mesh = mesh;
            return true;
        }

        private static bool InBox(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 centre, Vector3 right, Vector3 up, Vector3 normal,
            float side, float below, float above)
        {
            // Any corner (or the middle) inside the wound's box, close to the skin's surface at the hit.
            Vector3 mid = (p0 + p1 + p2) / 3f;
            return Inside(p0) || Inside(p1) || Inside(p2) || Inside(mid);

            bool Inside(Vector3 p)
            {
                Vector3 d = p - centre;
                float x = Vector3.Dot(d, right), y = Vector3.Dot(d, up), z = Vector3.Dot(d, normal);
                return Mathf.Abs(x) <= side && y >= -below && y <= above && Mathf.Abs(z) <= 0.045f;
            }
        }

        private static bool Usable(SkinnedMeshRenderer r) =>
            r != null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff &&
            r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && r.sharedMesh != null && r.sharedMesh.isReadable &&
            r.bones != null && r.bones.Length > 0;

        private MeshData DataOf(Mesh mesh)
        {
            if (_meshes.TryGetValue(mesh, out MeshData data)) return data;
            if (_meshes.Count > 96) _meshes.Clear();
            BoneWeight[] weights = mesh.boneWeights;
            if (weights == null || weights.Length != mesh.vertexCount) { _meshes[mesh] = null; return null; }
            data = new MeshData
            {
                Vertices = mesh.vertices, Normals = mesh.normals, Triangles = mesh.triangles, Weights = weights, Bindposes = mesh.bindposes
            };
            _meshes[mesh] = data;
            return data;
        }

        /// <summary>
        /// Where the body's vertices near the bullet's line are right now (bones only; blend shapes don't move a wound
        /// far). Each vertex is first placed roughly by its main bone alone; only those within a hand's breadth of the
        /// line are skinned properly, the rest are marked far (<see cref="_near"/>) and skipped.
        /// </summary>
        private bool Skin(SkinnedMeshRenderer r, MeshData data, Vector3 origin, Vector3 dir, float reach)
        {
            Transform[] bones = r.bones;
            if (data.Bindposes.Length == 0) return false;
            if (_boneMatrices.Length < bones.Length) _boneMatrices = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || i >= data.Bindposes.Length) _boneMatrices[i] = r.transform.localToWorldMatrix;
                else _boneMatrices[i] = bones[i].localToWorldMatrix * data.Bindposes[i];
            }
            int count = data.Vertices.Length;
            if (_skinned.Length < count)
            {
                _skinned = new Vector3[Mathf.NextPowerOfTwo(count)];
                _near = new bool[_skinned.Length];
            }
            int bonesLength = bones.Length;
            const float NearSq = 0.3f * 0.3f;
            for (int i = 0; i < count; i++)
            {
                BoneWeight w = data.Weights[i];
                Vector3 v = data.Vertices[i];
                // A vertex on one bone only (most of a limb or the trunk) is placed by that bone alone, and skipped if
                // it is far from the line. One shared between bones (round a joint) is always skinned properly: placed
                // by its main bone alone it can be a long way off, and good skin got skipped (holes in the wound, or
                // the wound on the far side of a leg).
                Vector3 rough = w.boneIndex0 < bonesLength ? _boneMatrices[w.boneIndex0].MultiplyPoint3x4(v) : r.transform.TransformPoint(v);
                if (w.weight0 > 0.98f)
                {
                    Vector3 rel = rough - origin;
                    float along = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, reach);
                    _near[i] = (rel - dir * along).sqrMagnitude <= NearSq;
                    _skinned[i] = rough;
                    continue;
                }
                Vector3 p = default;
                if (w.weight0 > 0f && w.boneIndex0 < bonesLength) p += _boneMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0;
                if (w.weight1 > 0f && w.boneIndex1 < bonesLength) p += _boneMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1;
                if (w.weight2 > 0f && w.boneIndex2 < bonesLength) p += _boneMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2;
                if (w.weight3 > 0f && w.boneIndex3 < bonesLength) p += _boneMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
                float total = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                _skinned[i] = total > 1e-4f ? p / total : rough;
                Vector3 relFull = _skinned[i] - origin;
                float alongFull = Mathf.Clamp(Vector3.Dot(relFull, dir), 0f, reach);
                _near[i] = (relFull - dir * alongFull).sqrMagnitude <= NearSq;
            }
            return true;
        }

        /// <summary>The first triangle (of those near the line) the ray meets before <paramref name="limit"/>; -1 if none.</summary>
        private int FirstHit(int[] tris, Vector3 origin, Vector3 dir, float limit, out float near)
        {
            int tri = -1;
            near = limit;
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (!_near[tris[i]] || !_near[tris[i + 1]] || !_near[tris[i + 2]]) continue;
                if (!RayTriangle(origin, dir, _skinned[tris[i]], _skinned[tris[i + 1]], _skinned[tris[i + 2]], out float t) || t >= near) continue;
                near = t;
                tri = i;
            }
            return tri;
        }

        /// <summary>The posed vertex nearest <paramref name="point"/>, if one is within <paramref name="within"/> metres.</summary>
        private bool NearestSkin(int count, Vector3 point, float within, out Vector3 nearest)
        {
            float best = within * within;
            nearest = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (!_near[i]) continue;
                float d = (_skinned[i] - point).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = _skinned[i];
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Möller–Trumbore, the outside only: a bullet goes in through skin facing it (Unity's front faces, whose
        /// cross(b - a, c - a) points out), never through the inside of the far side.
        /// </summary>
        private static bool RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            if (Vector3.Dot(Vector3.Cross(e1, e2), dir) >= 0f) return false;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // ------------------------------------------------------------------ the decal meshes

        private Layer LayerFor(SkinnedMeshRenderer body)
        {
            foreach (Layer l in _layers)
                if (l.Body == body) return l;
            if (_layers.Count >= MaxLayers) DropLayer(0);
            var layer = new Layer { Body = body, BodyMesh = body.sharedMesh };
            _layers.Add(layer);
            return layer;
        }

        /// <summary>
        /// The wound texture is two squares stacked: the hole on top (its middle at v = 0.75), the run of blood below.
        /// The run is squeezed into however far the blood has got by now, its rounded end leading.
        /// </summary>
        private void AppendUvs(Wound w)
        {
            float age = Time.time - w.Born;
            float u = Mathf.Clamp01(age / RunSeconds);
            float run = Mathf.Max(0.0005f, w.Run * (1f - (1f - u) * (1f - u)) * Mathf.Clamp01((age - 0.25f) * 4f));
            float half = w.Width * 0.5f;
            foreach (Vector2 p in w.Plane)
            {
                float uu = 0.5f + p.x / w.Width;
                float vv = p.y >= -half ? 0.75f + 0.5f * p.y / w.Width : 0.5f - 0.5f * (-p.y - half) / run;
                _uv.Add(new Vector2(uu, vv));
            }
        }

        private void LateUpdate()
        {
            float now = Time.time;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                Layer l = _layers[i];
                // The body went (or was re-dressed): its wounds go with it.
                if (l.Body == null || l.Body.sharedMesh != l.BodyMesh) { DropLayer(i); continue; }
                for (int k = l.Wounds.Count - 1; k >= 0; k--)
                    if (l.Wounds[k].Go == null || now - l.Wounds[k].Born > WoundLife) DropWound(l, k);
                if (l.Wounds.Count == 0) { DropLayer(i); continue; }
                // Shown only when the body is.
                bool hidden = l.Body.forceRenderingOff || !l.Body.enabled || l.Body.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
                bool growing = false;
                foreach (Wound w in l.Wounds)
                {
                    if (w.Renderer.forceRenderingOff != hidden) w.Renderer.forceRenderingOff = hidden;
                    if (!l.Growing || now - w.Born > RunSeconds + 0.3f) continue;
                    growing = true;
                    _uv.Clear();
                    AppendUvs(w);
                    w.Mesh.SetUVs(0, _uv);
                }
                l.Growing = growing;
            }
            ExpireSplats(now);
        }

        private void DropWound(Layer layer, int index)
        {
            Wound w = layer.Wounds[index];
            if (w.Go != null) Destroy(w.Go);
            if (w.Mesh != null) Destroy(w.Mesh);
            layer.Wounds.RemoveAt(index);
        }

        private void DropLayer(int index)
        {
            Layer l = _layers[index];
            for (int k = l.Wounds.Count - 1; k >= 0; k--) DropWound(l, k);
            _layers.RemoveAt(index);
        }

        private void ClearAll()
        {
            for (int i = _layers.Count - 1; i >= 0; i--) DropLayer(i);
            foreach (MeshRenderer splat in _splats)
                if (splat != null) splat.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ spatter behind

        private void Spatter(Transform root, Vector3 at, Vector3 dir, float size)
        {
            // Out of the far side, a little spread, onto the first thing that isn't a body within a couple of metres.
            Vector3 d = (dir + Random.insideUnitSphere * 0.12f + Vector3.down * 0.15f).normalized;
            int n = Physics.RaycastNonAlloc(at + d * 0.05f, d, _behind, 2.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit hit = default;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _behind[i];
                if (h.distance >= best || h.collider.attachedRigidbody != null || h.collider.transform.IsChildOf(root)) continue;
                if (h.collider.GetComponentInParent<PlayerHub>() != null) continue;
                best = h.distance;
                hit = h;
            }
            if (best == float.MaxValue) return;
            // Further away, bigger and thinner; stretched along the way the blood was going.
            float scale = Mathf.Lerp(0.16f, 0.4f, best / 2.6f) * size * Random.Range(0.8f, 1.2f);
            Vector3 along = Vector3.ProjectOnPlane(d, hit.normal);
            float stretch = 1f + Mathf.Clamp01(along.magnitude) * 0.9f;
            Quaternion rot = along.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(-hit.normal, along.normalized)
                : Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            MeshRenderer quad = _splats[_splatNext];
            if (quad == null)
            {
                var go = new GameObject("BloodSpatter");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _splatMesh;
                quad = go.AddComponent<MeshRenderer>();
                quad.sharedMaterial = _splatMaterial;
                quad.shadowCastingMode = ShadowCastingMode.Off;
                _splats[_splatNext] = quad;
            }
            quad.transform.SetPositionAndRotation(hit.point + hit.normal * 0.006f, rot);
            quad.transform.localScale = new Vector3(scale, scale * stretch, 1f);
            quad.gameObject.SetActive(true);
            _splatTimes[_splatNext] = Time.time;
            _splatNext = (_splatNext + 1) % MaxSplats;
        }

        private void ExpireSplats(float now)
        {
            for (int i = 0; i < MaxSplats; i++)
                if (_splats[i] != null && _splats[i].gameObject.activeSelf && now - _splatTimes[i] > SplatLife) _splats[i].gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ pictures

        private static readonly Color HoleCore = new(0.012f, 0.003f, 0.003f);
        private static readonly Color Torn = new(0.55f, 0.07f, 0.06f);
        private static readonly Color Wall = new(0.2f, 0.015f, 0.015f);
        private static readonly Color Bruise = new(0.3f, 0.05f, 0.07f);
        private static readonly Color Blood = new(0.5f, 0.02f, 0.025f);
        private static readonly Color Dried = new(0.22f, 0.02f, 0.02f);

        /// <summary>128 x 256: the hole (dark core, torn ring, a smear round it) over the run of blood.</summary>
        private static Texture2D WoundTexture()
        {
            const int w = 128, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "BulletWound" };
            var px = new Color[w * h];
            const float cx = 63.5f, cy = 191.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = new(0f, 0f, 0f, 0f);
                    if (y >= 1 && y < h - 1 && x >= 1 && x < w - 1)
                    {
                        float dx = x - cx, dy = y - cy;
                        float angle = Mathf.Atan2(dy, dx);
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        // A ragged edge: the radii wobble with angle.
                        float wob = Mathf.PerlinNoise(Mathf.Cos(angle) * 2.2f + 5f, Mathf.Sin(angle) * 2.2f + 5f);
                        float speck = Mathf.PerlinNoise(x * 0.23f, y * 0.23f);
                        // The hole: black in the middle, its inner wall going from black to a deep dark red (it reads as
                        // going into the body), a raised torn rim, a dark bruise round it, flecks thrown off.
                        float coreR = 12f + wob * 3f, wallR = coreR + 6f, rimR = wallR + 6f + wob * 4f, bruiseR = rimR + 12f;
                        if (r < coreR) c = HoleCore;
                        else if (r < wallR)
                        {
                            float t = (r - coreR) / (wallR - coreR);
                            c = Color.Lerp(HoleCore, Wall, t * t);
                        }
                        else if (r < rimR)
                        {
                            float t = (r - wallR) / (rimR - wallR);
                            c = Color.Lerp(Torn, Blood, t) * (0.85f + speck * 0.3f);
                        }
                        else if (r < bruiseR && speck > 0.3f + (r - rimR) / bruiseR) c = Color.Lerp(Bruise, Dried, speck);
                        else if (r < bruiseR + 14f && speck > 0.62f + (r - bruiseR) / 40f) c = Color.Lerp(Blood, Dried, speck); // flecks
                        if (c.r > 0f || c.g > 0f) c.a = 1f;
                        bool hole = r < rimR;

                        // The run: from the hole straight down, wobbling, narrowing, ending in a drop.
                        if (y < cy)
                        {
                            float k = 1f - y / cy; // 0 at the hole, 1 at the bottom
                            float mid = cx + (Mathf.PerlinNoise(y * 0.035f, 2.7f) - 0.5f) * 14f * k;
                            float half = Mathf.Lerp(8f, 4.2f, k);
                            float tip = y < 18 ? Mathf.Sqrt(Mathf.Max(0f, 1f - (y - 10f) * (y - 10f) / 64f)) * 7.5f : 0f; // the drop at the end
                            float edge = Mathf.Max(half, tip);
                            if (Mathf.Abs(x - mid) < edge && y > 2 && !hole) // (it runs from the rim, not across the hole)
                            {
                                float sheen = 1f - Mathf.Abs(x - mid) / edge;
                                c = Color.Lerp(Dried, Blood, 0.35f + sheen * 0.65f);
                                c.a = 1f;
                            }
                        }
                    }
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>64 x 64 spatter: a blot with drops thrown off it, more of them along +y (the way it was flying).</summary>
        private static Texture2D SplatTexture()
        {
            const int s = 96;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "BloodSpatter" };
            var px = new Color[s * s];
            var rng = new System.Random(4242);
            var drops = new List<Vector3>();
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = (float)rng.NextDouble() * 36f;
                Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f + 0.45f) * dist;
                drops.Add(new Vector3(48f + p.x, 40f + p.y, Mathf.Lerp(6f, 1.4f, dist / 36f)));
            }
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    Color c = new(0f, 0f, 0f, 0f);
                    if (x > 0 && y > 0 && x < s - 1 && y < s - 1)
                    {
                        float dx = x - 48f, dy = (y - 40f) * 1.2f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float n = Mathf.PerlinNoise(x * 0.12f + 3f, y * 0.12f + 7f);
                        bool hit = r < 10f + n * 8f;
                        foreach (Vector3 d in drops)
                            if ((x - d.x) * (x - d.x) + (y - d.y) * (y - d.y) < d.z * d.z) { hit = true; break; }
                        if (hit)
                        {
                            c = Color.Lerp(Dried, Blood, Mathf.PerlinNoise(x * 0.3f, y * 0.3f));
                            c.a = 1f;
                        }
                    }
                    px[y * s + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static Mesh SplatQuad()
        {
            // Faces -Z (turned to look into the surface it shows on the outside), +Y is the way the blood flew.
            var mesh = new Mesh { name = "BloodSpatter" };
            mesh.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) });
            mesh.SetUVs(0, new List<Vector2> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
