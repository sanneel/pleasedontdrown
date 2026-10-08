using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Baked normals and a consistent finish for the imported gun bodies.</summary>
    public static class WeaponSurfaceFinish
    {
        private const string Folder = "Assets/_Game/Art/Weapons/Surfaces";

        public static void Apply(Transform gun, string kind)
        {
            Directory.CreateDirectory(Folder);
            Transform model = gun.Find("Meshy_" + kind.ToLowerInvariant()) ?? gun.Find("Model_" + kind.ToLowerInvariant());
            if (model == null) return;
            int index = 0;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh source = filter.sharedMesh;
                // Always start from the import so repeated polishing never smooths or clips a previous result.
                source = AssetDatabase.LoadAllAssetsAtPath($"Assets/_Game/Art/Weapons/{kind.ToLowerInvariant()}.glb")
                    .OfType<Mesh>().FirstOrDefault(m => m.name == source.name) ?? source;
                string path = $"{Folder}/{kind}_{index++}.asset";
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool create = mesh == null;
                if (create) mesh = Object.Instantiate(source);
                else if (mesh != source) EditorUtility.CopySerialized(source, mesh);
                if (kind == "Sniper") RemoveOldScope(mesh, gun.worldToLocalMatrix * filter.transform.localToWorldMatrix);
                // Blender authors the bevel and broad-surface normals. Keep them on import;
                // clipping the sniper optic clears normals, so that mesh still needs rebuilding.
                if (mesh.normals.Length != mesh.vertexCount) SmoothNormals(mesh);
                mesh.name = source.name;
                if (create) AssetDatabase.CreateAsset(mesh, path);
                EditorUtility.SetDirty(mesh);
                filter.sharedMesh = mesh;

                // The source palette painted shadows into neighboring triangles. A single metal finish
                // lets the lighting describe the surface; authored panels and hardware supply contrast.
                if (kind == "Pistol" || kind == "Rifle" || kind == "Sniper")
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    var materials = new Material[mesh.subMeshCount];
                    for (int i = 0; i < materials.Length; i++) materials[i] = BodyMaterial();
                    renderer.sharedMaterials = materials;
                }
            }
            // Keep this local to gun prefabs; shared world and character materials retain their lighting.
            foreach (MeshRenderer renderer in gun.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.renderQueue >= 3000) continue;
                    string colourProperty = source.HasProperty("baseColorFactor") ? "baseColorFactor" : "_BaseColor";
                    if (!source.HasProperty(colourProperty)) continue;
                    if (source.shader.name == "PleaseDontDrown/Weapon") continue;
                    Color colour = source.GetColor(colourProperty);
                    string textureProperty = source.HasProperty("baseColorTexture") ? "baseColorTexture" : "_BaseMap";
                    Texture texture = source.HasProperty(textureProperty) ? source.GetTexture(textureProperty) : null;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
                    string path = $"{Folder}/Finish_{guid}_{localId}.mat";
                    Material finish = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool create = finish == null;
                    if (create) finish = new Material(source);
                    else EditorUtility.CopySerialized(source, finish);
                    finish.name = "Weapon_" + source.name;
                    finish.shader = Shader.Find("PleaseDontDrown/Weapon");
                    finish.shaderKeywords = System.Array.Empty<string>();
                    finish.SetColor("_BaseColor", colour);
                    finish.SetTexture("_BaseMap", texture);
                    finish.SetFloat("_Metallic", .15f);
                    finish.SetFloat("_Smoothness", .25f);
                    if (create) AssetDatabase.CreateAsset(finish, path);
                    EditorUtility.SetDirty(finish);
                    materials[i] = finish;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static void RemoveOldScope(Mesh mesh, Matrix4x4 toGun)
        {
            // Replace the lumpy integral optic with a clean lathed one. Clip triangles at the mounting
            // plane instead of deleting whole faces, which would leave a jagged edge on the receiver.
            Vector3[] source = mesh.vertices;
            int[] indices = mesh.triangles;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var polygon = new List<Vector3> { source[indices[i]], source[indices[i + 1]], source[indices[i + 2]] };
                var clipped = new List<Vector3>();
                for (int j = 0; j < polygon.Count; j++)
                {
                    Vector3 a = polygon[j], b = polygon[(j + 1) % polygon.Count];
                    float da = toGun.MultiplyPoint3x4(a).y - .082f;
                    float db = toGun.MultiplyPoint3x4(b).y - .082f;
                    if (da <= 0) clipped.Add(a);
                    if ((da <= 0) != (db <= 0)) clipped.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
                }
                for (int j = 1; j + 1 < clipped.Count; j++)
                {
                    int start = vertices.Count;
                    vertices.Add(clipped[0]); vertices.Add(clipped[j]); vertices.Add(clipped[j + 1]);
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x, v.z)).ToList());
            mesh.RecalculateBounds();
        }

        private static Material BodyMaterial()
        {
            string path = Folder + "/SatinGunmetal.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("PleaseDontDrown/Weapon"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("PleaseDontDrown/Weapon");
            mat.shaderKeywords = System.Array.Empty<string>();
            mat.SetColor("_BaseColor", new Color(.30f, .33f, .38f));
            mat.SetFloat("_Metallic", .18f);
            mat.SetFloat("_Smoothness", .24f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Imported flat faces duplicate vertices, including at material seams. Average face normals
        // by position across those seams, while retaining sharp corners above 65 degrees.
        public static void SmoothNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var groups = new Dictionary<Vector3Int, List<(Vector3 normal, float weight)>>();
            var keys = new Vector3Int[vertices.Length];
            var original = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i] * 100000f;
                keys[i] = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
                if (!groups.ContainsKey(keys[i])) groups.Add(keys[i], new());
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                void Add(int p, int q, int r)
                {
                    float weight = Vector3.Angle(vertices[q] - vertices[p], vertices[r] - vertices[p]);
                    groups[keys[p]].Add((normal, weight));
                    original[p] += normal * weight;
                }
                Add(a, b, c); Add(b, c, a); Add(c, a, b);
            }
            var normals = new Vector3[vertices.Length];
            float crease = Mathf.Cos(65f * Mathf.Deg2Rad);
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 reference = original[i].normalized, sum = Vector3.zero;
                foreach (var face in groups[keys[i]])
                    if (Vector3.Dot(reference, face.normal) >= crease) sum += face.normal * face.weight;
                normals[i] = sum.sqrMagnitude > 1e-12f ? sum.normalized : reference;
            }
            mesh.normals = normals;
            if (mesh.uv.Length == vertices.Length) mesh.RecalculateTangents();
        }

        public static void ReviewBatch()
        {
            WeaponArtPolish.ApplyBatch();
            WeaponGripAuthoring.SaveBatch();
        }

        public static void BuildBatch()
        {
            ReviewBatch();
            Shader shader = Shader.Find("PleaseDontDrown/Weapon");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new System.InvalidOperationException("Weapon shader failed to compile.");
            foreach (string kind in new[] { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper" })
            {
                var gun = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Items/Prefabs/{kind}.prefab");
                int count = 0;
                foreach (MeshRenderer r in gun.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.renderQueue >= 3000) continue;
                    if (m.shader != shader) throw new System.InvalidOperationException($"{kind}/{r.name}: opaque material still uses {m.shader.name}");
                    count++;
                }
                Debug.Log($"[WeaponFinish] {kind}: validated {count} opaque material slots, including inactive attachments.");
            }
            WeaponGripReview.BuildSavedGameBatch();
        }
    }
}
