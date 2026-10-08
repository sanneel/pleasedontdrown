// Weapon surface details are baked into meshes; no extra runtime scripts or colliders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static class WeaponArtPolish
    {
        private const string MeshDir = "Assets/_Game/Art/Weapons/Details";
        private const string Prefix = "FinishDetails";
        private static readonly string[] Kinds = { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper" };

        [MenuItem("PLEASE DON'T DROWN/Polish gun prefabs")]
        public static void ApplyBatch()
        {
            try
            {
                foreach (string kind in Kinds)
                {
                    string path = $"Assets/_Game/Items/Prefabs/{kind}.prefab";
                    GameObject gun = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        string handling = EditorJsonUtility.ToJson(gun.GetComponent<Weapon>());
                        string item = EditorJsonUtility.ToJson(gun.GetComponent<Item>());
                        int colliders = gun.GetComponentsInChildren<Collider>(true).Length;
                        Apply(gun.transform, kind);
                        RefreshOutline(gun);
                        if (handling != EditorJsonUtility.ToJson(gun.GetComponent<Weapon>()) ||
                            item != EditorJsonUtility.ToJson(gun.GetComponent<Item>()) ||
                            colliders != gun.GetComponentsInChildren<Collider>(true).Length)
                            throw new InvalidOperationException($"{kind}: art pass changed handling or physics.");
                        PrefabUtility.SaveAsPrefabAsset(gun, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(gun); }
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[GunPolish] All five prefabs saved; handling, grips and colliders preserved.");
                if (Application.isBatchMode) CaptureBatch();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void RefreshOutline(GameObject gun)
        {
            var target = gun.GetComponent<PleaseDontDrown.Interaction.Interactable>();
            var so = new SerializedObject(target);
            var renderers = gun.GetComponentsInChildren<Renderer>(true);
            var property = so.FindProperty("_outlineRenderers");
            property.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void Apply(Transform gun, string kind)
        {
            Directory.CreateDirectory(MeshDir);
            foreach (Transform t in gun.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name.StartsWith(Prefix, StringComparison.Ordinal)) Object.DestroyImmediate(t.gameObject);
            WeaponSurfaceFinish.Apply(gun, kind);
            var art = new DetailBuilder(gun, kind);
            switch (kind)
            {
                case "Pistol": Pistol(art); break;
                case "SMG": Smg(art); break;
                case "Shotgun": Shotgun(art); break;
                case "Rifle": Rifle(art); break;
                case "Sniper": Sniper(art); break;
            }
            Attachments(art);
            art.Save();
        }

        private static void Pistol(DetailBuilder a)
        {
            // Narrow front serrations complement the model's existing rear serrations.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 5; i++) a.Patch(side, .030f, .067f + i * .009f, .029f, .0024f, 0);
                a.Patch(side, .005f, -.057f, .006f, .026f, 2);
                a.Screw(side, -.007f, -.096f, .0031f);
                a.Screw(side, -.014f, -.043f, .0028f);
                // Small diamond checks follow the slanted grip, on both sides.
                for (int row = 0; row < 9; row++)
                for (int col = 0; col < 4; col++)
                {
                    float y = -.049f - row * .0073f;
                    float z = -.071f - row * .0023f - col * .006f;
                    a.Patch(side, y, z, .0032f, .0032f, 0, diamond: true);
                }
                a.Patch(side, .043f, .025f, .0012f, .028f, 1);
                // A recessed ejection port with a narrow steel edge and two frame pins.
                if (side > 0)
                {
                    a.Patch(side, .033f, -.012f, .016f, .032f, 0);
                    a.Patch(side, .042f, -.012f, .0015f, .032f, 1);
                }
                a.Screw(side, -.026f, -.085f, .0022f);
                a.Patch(side, -.031f, -.096f, .002f, .02f, 0);
            }
        }

        private static void Smg(DetailBuilder a)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                a.Patch(side, .031f, -.018f, .021f, .083f, 2);
                a.Patch(side, .047f, .052f, .0012f, .22f, 1);
                for (int i = 0; i < 5; i++) a.Patch(side, .032f, .115f + i * .012f, .018f, .005f, 0);
                foreach (float z in new[] { -.052f, .01f, .152f }) a.Screw(side, .004f, z, .0032f);
                for (int i = 0; i < 7; i++) a.Patch(side, -.03f - i * .009f, -.039f - i * .0018f, .0025f, .026f, 2);
                a.Patch(side, .005f, -.024f, .003f, .01f, 3);
                if (side > 0) a.Patch(side, .02f, .025f, .012f, .039f, 0);
                for (int i = 0; i < 3; i++) a.Patch(side, .043f, -.04f + i * .006f, .003f, .001f, 1);
            }
            var handle = a.Gun.Find("ChargingHandle");
            for (int i = 0; i < 4; i++) a.Box(handle, new Vector3(.0074f, 0, -.01f + i * .006f), new Vector3(.0012f, .009f, .002f), 0);
            Magazine(a, "Magazine"); Magazine(a, "MagazineExtended");
        }

        private static void Shotgun(DetailBuilder a)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                a.Patch(side, .007f, -.043f, .033f, .10f, 2);
                a.Patch(side, .043f, -.018f, .0013f, .16f, 1);
                a.Screw(side, .008f, -.081f, .0035f);
                a.Screw(side, -.008f, .082f, .0025f);
                a.Patch(side, .015f, -.079f, .004f, .017f, 0);
                a.Patch(side, .015f, -.085f, .002f, .003f, 3);
                for (int i = 0; i < 3; i++) a.Patch(side, .034f, -.084f + i * .005f, .004f, .001f, 1);
                a.Screw(side, .005f, .074f, .0035f);
                // Restrained grain lines and a diamond patch in the wooden stock.
                for (int i = 0; i < 5; i++)
                {
                    int line = i;
                    a.SurfaceLine(side, t => new Vector2(-.02f - line * .012f + Mathf.Sin(t * 7 + line) * .0018f,
                        -.394f + t * (.16f - line * .011f)), .0008f, 4, acrossY: true);
                }
                for (int row = 0; row < 5; row++)
                for (int col = 0; col < 8; col++) a.Patch(side, -.022f - row * .006f, -.18f - col * .006f, .0028f, .0028f, 4, diamond: true);
            }
            Transform pump = a.Gun.Find("Pump");
            // These are under the pump transform, so every rib travels with the action.
            for (int i = 0; i < 9; i++)
            {
                float z = -.065f + i * .016f;
                a.Box(pump, new Vector3(-.0267f, 0, z), new Vector3(.002f, .036f, .0028f), 4);
                a.Box(pump, new Vector3(.0267f, 0, z), new Vector3(.002f, .036f, .0028f), 4);
                a.Box(pump, new Vector3(0, -.0236f, z), new Vector3(.05f, .0015f, .0028f), 4);
            }
            a.Ring(pump, new Vector3(0, 0, -.074f), .019f, .029f, .009f, 2);
            a.Ring(pump, new Vector3(0, 0, .074f), .019f, .029f, .009f, 2);
            foreach (string name in new[] { "Magazine", "MagazineExtended" })
            {
                Transform tube = a.Gun.Find(name);
                // Tube primitives are rotated/scaled: use their unit mesh coordinates.
                a.Ring(tube, new Vector3(0, .97f, 0), .30f, .53f, .035f, 1, Quaternion.Euler(90, 0, 0));
            }
        }

        private static void Rifle(DetailBuilder a)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                a.Patch(side, .017f, .064f, .027f, .089f, 2);
                a.Patch(side, .07f, .053f, .0015f, .15f, 1);
                a.Screw(side, -.009f, -.035f, .004f);
                a.Screw(side, .011f, .13f, .0037f);
                a.Screw(side, .038f, -.295f, .0038f);
                for (int row = 0; row < 7; row++)
                for (int col = 0; col < 4; col++) a.Patch(side, -.055f - row * .008f, -.073f - row * .003f - col * .0055f, .003f, .003f, 0, diamond: true);
                // Magazine flutes track the curved silhouette of the imported body.
                for (int col = 0; col < 3; col++)
                {
                    int flute = col;
                    a.SurfaceLine(side, t => new Vector2(-.047f - t * .108f, .047f + flute * .014f + t * t * .028f), .0024f, 0);
                }
                a.Patch(side, .018f, -.071f, .003f, .011f, 3);
                if (side > 0)
                {
                    a.Patch(side, .044f, .055f, .016f, .067f, 0);
                    a.Patch(side, .053f, .055f, .0018f, .067f, 1);
                }
                for (int i = 0; i < 5; i++) a.Patch(side, .015f, -.25f + i * .014f, .015f, .003f, 0);
                for (int i = 0; i < 4; i++) a.Screw(side, .033f, .186f + i * .041f, .0028f);
            }
            Magazine(a, "MagazineExtended");
        }

        private static void Sniper(DetailBuilder a)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                a.Patch(side, -.007f, .14f, .024f, .13f, 2);
                a.Screw(side, -.01f, .078f, .004f);
                a.Screw(side, -.01f, .204f, .004f);
                for (int i = 0; i < 6; i++) a.Patch(side, .011f, .34f + i * .038f, .007f, .014f, 0);
                for (int row = 0; row < 7; row++) a.Patch(side, -.065f - row * .009f, -.072f - row * .0015f, .003f, .028f, 2);
                a.Screw(side, -.012f, -.299f, .004f);
                a.Screw(side, -.063f, -.295f, .004f);
                a.Patch(side, .041f, .055f, .015f, .065f, 0);
                a.Patch(side, .05f, .055f, .0015f, .065f, 1);
                for (int i = 0; i < 5; i++) a.Patch(side, -.035f, -.27f + i * .013f, .025f, .0028f, 0);
            }
            // A clean optic with circular ends, distinct mounts, knurled adjustment rings and lenses.
            foreach (float z in new[] { .004f, .148f })
            {
                a.Box(a.Gun, new Vector3(0,.069f,z), new Vector3(.043f,.048f,.026f), 0);
                a.Box(a.Gun, new Vector3(0,.105f,z), new Vector3(.028f,.025f,.014f), 2);
                a.Ring(a.Gun, new Vector3(0,.132f,z), .0175f,.022f,.012f,0);
            }
            a.ScopeTube(new Vector3(0,.132f,0), new[] {
                new Vector2(-.083f,.028f), new Vector2(-.076f,.03f), new Vector2(-.034f,.03f),
                new Vector2(-.026f,.019f), new Vector2(.157f,.019f), new Vector2(.211f,.032f),
                new Vector2(.254f,.032f) }, 2);
            a.Ring(a.Gun, new Vector3(0,.132f,-.083f), .023f,.03f,.005f,0);
            a.Ring(a.Gun, new Vector3(0,.132f,-.081f), 0,.023f,.001f,5);
            a.Ring(a.Gun, new Vector3(0,.132f,.252f), 0,.028f,.001f,5);
            a.Ring(a.Gun, new Vector3(0,.132f,.254f), .028f,.033f,.005f,0);
            foreach (float z in new[] { -.07f, -.045f, .225f, .244f })
                a.Ring(a.Gun, new Vector3(0,.132f,z), .0295f,.0315f,.003f,0);
            a.Ring(a.Gun, new Vector3(0,.162f,.074f), 0,.015f,.022f,0,Quaternion.Euler(90,0,0));
            a.Ring(a.Gun, new Vector3(.029f,.132f,.074f), 0,.013f,.019f,0,Quaternion.Euler(0,90,0));
            for (int i = 0; i < 16; i++)
            {
                float t = i * Mathf.PI / 8;
                a.Box(a.Gun, new Vector3(Mathf.Cos(t)*.015f,.174f,.074f+Mathf.Sin(t)*.015f),new Vector3(.002f,.001f,.002f),1);
                a.Box(a.Gun, new Vector3(Mathf.Cos(t)*.031f,.132f+Mathf.Sin(t)*.031f,-.056f),new Vector3(.002f,.002f,.016f),0);
            }
            Magazine(a, "MagazineExtended");
        }

        private static void Magazine(DetailBuilder a, string name)
        {
            Transform mag = a.Gun.Find(name);
            if (mag == null) return;
            // Existing magazines are scaled unit cubes; details use the same unit space.
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 3; i++) a.Box(mag, new Vector3(side * .507f, -.07f, -.28f + i * .28f), new Vector3(.025f, .63f, .045f), 0);
                for (int i = 0; i < 4; i++) a.Box(mag, new Vector3(side * .522f, -.28f + i * .15f, .37f), new Vector3(.018f, .025f, .072f), 1);
            }
            a.Box(mag, new Vector3(0, -.49f, 0), new Vector3(1.09f, .055f, 1.08f), 0);
            a.Box(mag, new Vector3(0, -.454f, 0), new Vector3(1.04f, .012f, 1.035f), 1);
        }

        private static void Attachments(DetailBuilder a)
        {
            Transform can = a.Gun.Find("BarrelSuppressor");
            if (can != null)
            {
                Transform body = can.Find("Can");
                float length = body.localScale.y * 2f, r = body.localScale.x * .5f;
                // Open annuli at the muzzle: a dark inset plus a fine steel crown.
                a.Ring(can, new Vector3(0, 0, length + .0005f), r * .22f, r, .0015f, 0);
                a.Ring(can, new Vector3(0, 0, length + .0014f), r * .72f, r * .98f, .001f, 1);
                foreach (float z in new[] { .013f, length * .79f, length - .009f }) a.Ring(can, new Vector3(0, 0, z), r * .98f, r * 1.065f, .004f, 2);
                for (int i = 0; i < 10; i++)
                {
                    float t = i * Mathf.PI / 5f;
                    a.Box(can, new Vector3(Mathf.Cos(t) * r, Mathf.Sin(t) * r, length * .18f), new Vector3(.0022f, .0022f, .021f), 0);
                }
            }
            Transform brake = a.Gun.Find("BarrelCompensator");
            if (brake != null)
            {
                float size = brake.Find("Block").localScale.x;
                for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++) a.Box(brake, new Vector3(side * (size * .5f + .0004f), -.004f, size * (.2f + i * .3f)), new Vector3(.001f, size * .36f, size * .12f), 0);
                a.Ring(brake, new Vector3(0, 0, size * 1.105f), size * .12f, size * .39f, .0015f, 0);
            }
            Transform scope = a.Gun.Find("SightScope");
            // Hidden greybox scope on the sniper remains hidden; the integrated optic is detailed above.
            if (scope != null && scope.Find("Tube").GetComponent<Renderer>().enabled)
            {
                float r = scope.Find("Tube").localScale.x * .5f;
                float length = scope.Find("Tube").localScale.y * 2f;
                foreach (float z in new[] { -length * .23f, length * .23f }) a.Ring(scope, new Vector3(0, 0, z), r * .96f, r * 1.17f, .008f, 0);
                a.Ring(scope, new Vector3(0, 0, -length * .42f), r * 1.17f, r * 1.3f, .008f, 2);
                a.Ring(scope, new Vector3(0, 0, length * .49f), r * 1.26f, r * 1.4f, .006f, 1);
                for (int i = 0; i < 12; i++)
                {
                    float t = i * Mathf.PI / 6f;
                    a.Box(scope, new Vector3(Mathf.Cos(t) * r * .55f, r * 1.71f, Mathf.Sin(t) * r * .55f), new Vector3(.0018f, .001f, .0025f), 1);
                }
            }
            Transform dot = a.Gun.Find("SightRedDot");
            if (dot != null)
            {
                float s = dot.Find("Base").localScale.x / .028f;
                for (int side = -1; side <= 1; side += 2)
                {
                    a.Box(dot, new Vector3(side * .0173f, .006f, .008f) * s, new Vector3(.001f, .007f, .01f) * s, 0);
                    a.Box(dot, new Vector3(side * .0179f, .006f, .008f) * s, new Vector3(.0005f, .0012f, .005f) * s, 1);
                }
            }
        }

        // Surface patches sample the real imported mesh in gun space, so checks and screws hug curved surfaces.
        private sealed class DetailBuilder
        {
            public readonly Transform Gun;
            private readonly string kind;
            private readonly List<(Vector3 a, Vector3 b, Vector3 c)> faces = new();
            private readonly Dictionary<(Transform parent, int material), Geometry> groups = new();
            private readonly Material[] materials;

            public DetailBuilder(Transform gun, string kind)
            {
                Gun = gun; this.kind = kind;
                materials = new[]
                {
                    Material("Recess", new Color(.075f, .085f, .10f), .12f, .22f),
                    Material("Steel", new Color(.51f, .56f, .61f), .65f, .4f),
                    Material("Slate", new Color(.24f, .29f, .33f), .32f, .3f),
                    Material("Safety", new Color(.78f, .19f, .10f), .05f, .25f),
                    Material("Walnut", new Color(.25f, .12f, .055f), 0f, .18f),
                    Material("Lens", new Color(.035f, .10f, .14f), .45f, .78f)
                };
                Transform model = gun.Find("Meshy_" + kind.ToLowerInvariant()) ?? gun.Find("Model_" + kind.ToLowerInvariant());
                if (model == null) throw new InvalidOperationException($"{kind}: expected fitted gun model.");
                foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    Matrix4x4 matrix = gun.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    Vector3[] v = mesh.vertices; int[] indices = mesh.triangles;
                    for (int i = 0; i < indices.Length; i += 3)
                        faces.Add((matrix.MultiplyPoint3x4(v[indices[i]]), matrix.MultiplyPoint3x4(v[indices[i + 1]]), matrix.MultiplyPoint3x4(v[indices[i + 2]])));
                }
            }

            private static Material Material(string name, Color color, float metal, float smooth)
            {
                string path = $"{MeshDir}/Detail{name}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("PleaseDontDrown/Weapon"));
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = Shader.Find("PleaseDontDrown/Weapon");
                mat.shaderKeywords = Array.Empty<string>();
                mat.SetColor("_BaseColor", color); mat.SetFloat("_Metallic", metal); mat.SetFloat("_Smoothness", smooth);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                return mat;
            }

            private Geometry Group(Transform parent, int mat)
            {
                var key = (parent, mat);
                if (!groups.TryGetValue(key, out Geometry g)) groups.Add(key, g = new Geometry());
                return g;
            }

            private bool Surface(int side, float y, float z, out Vector3 point)
            {
                float best = float.NegativeInfinity;
                foreach (var f in faces)
                {
                    float dy0 = f.b.y - f.a.y, dz0 = f.b.z - f.a.z;
                    float dy1 = f.c.y - f.a.y, dz1 = f.c.z - f.a.z;
                    float det = dy0 * dz1 - dy1 * dz0;
                    if (Mathf.Abs(det) < 1e-10f) continue;
                    float dy = y - f.a.y, dz = z - f.a.z;
                    float u = (dy * dz1 - dy1 * dz) / det, v = (dy0 * dz - dy * dz0) / det;
                    if (u < 0 || v < 0 || u + v > 1) continue;
                    float x = f.a.x + u * (f.b.x - f.a.x) + v * (f.c.x - f.a.x);
                    best = Mathf.Max(best, x * side);
                }
                point = new Vector3((best + .00065f) * side, y, z);
                return !float.IsNegativeInfinity(best);
            }

            public void Patch(int side, float y, float z, float height, float width, int mat, bool diamond = false)
            {
                // Subdivide large patches to follow the actual surface without bridging bevels.
                int ny = diamond ? 1 : Mathf.Max(1, Mathf.CeilToInt(height / .006f));
                int nz = diamond ? 1 : Mathf.Max(1, Mathf.CeilToInt(width / .006f));
                for (int iy = 0; iy < ny; iy++) for (int iz = 0; iz < nz; iz++)
                {
                    float y0 = y - height / 2 + height * iy / ny, y1 = y0 + height / ny;
                    float z0 = z - width / 2 + width * iz / nz, z1 = z0 + width / nz;
                    Vector3 p0, p1, p2, p3;
                    bool valid = diamond
                        ? Surface(side, y, z0, out p0) & Surface(side, y1, z, out p1) & Surface(side, y, z1, out p2) & Surface(side, y0, z, out p3)
                        : Surface(side, y0, z0, out p0) & Surface(side, y1, z0, out p1) & Surface(side, y1, z1, out p2) & Surface(side, y0, z1, out p3);
                    if (!valid || Mathf.Max(p0.x, p1.x, p2.x, p3.x) - Mathf.Min(p0.x, p1.x, p2.x, p3.x) > .006f) continue;
                    Group(Gun, mat).Quad(p0, p1, p2, p3, Vector3.right * side);
                }
            }

            public void Screw(int side, float y, float z, float r)
            {
                if (!Surface(side, y, z, out Vector3 p)) return;
                Ring(Gun, p, 0, r, .0013f, 1, Quaternion.Euler(0, 90, 0));
                Box(Gun, p + Vector3.right * side * .00085f, new Vector3(.0005f, .0009f, r * 1.15f), 0);
            }

            public void SurfaceLine(int side, Func<float, Vector2> curve, float width, int mat, bool acrossY = false)
            {
                const int count = 40;
                Vector2 offset = (acrossY ? Vector2.right : Vector2.up) * width / 2;
                for (int i = 0; i < count; i++)
                {
                    Vector2 a = curve((float)i / count), b = curve((float)(i + 1) / count);
                    Vector2 av = a - offset, bv = b - offset, cv = b + offset, dv = a + offset;
                    if (!(Surface(side, av.x, av.y, out Vector3 p0) & Surface(side, bv.x, bv.y, out Vector3 p1) &
                        Surface(side, cv.x, cv.y, out Vector3 p2) & Surface(side, dv.x, dv.y, out Vector3 p3))) continue;
                    if (Mathf.Max(p0.x, p1.x, p2.x, p3.x) - Mathf.Min(p0.x, p1.x, p2.x, p3.x) > .006f) continue;
                    Group(Gun, mat).Quad(p0, p1, p2, p3, Vector3.right * side);
                }
            }

            public void FittedScopeRing(float z, float length, int mat)
            {
                float top = 0, bottom = 0;
                for (float y = .23f; y >= .06f; y -= .0004f)
                {
                    if (Surface(1, y, z, out _)) { if (top == 0) top = y; bottom = y; }
                    else if (top > 0) break;
                }
                if (top - bottom < .009f || top - bottom > .075f) return;
                float middle = (top + bottom) * .5f;
                if (!Surface(1, middle, z, out Vector3 right) || !Surface(-1, middle, z, out Vector3 left)) return;
                float radius = Mathf.Max((top - bottom) * .5f, (right.x - left.x) * .5f);
                Ring(Gun, new Vector3((right.x + left.x) * .5f, middle, z), radius * .98f, radius + .0015f, length, mat);
            }

            public void Box(Transform parent, Vector3 centre, Vector3 size, int mat)
            {
                if (parent == null) return;
                Geometry g = Group(parent, mat);
                Vector3 h = size / 2;
                Vector3 V(float x, float y, float z) => centre + Vector3.Scale(h, new Vector3(x, y, z));
                g.Quad(V(1,-1,-1), V(1,1,-1), V(1,1,1), V(1,-1,1), Vector3.right);
                g.Quad(V(-1,-1,1), V(-1,1,1), V(-1,1,-1), V(-1,-1,-1), Vector3.left);
                g.Quad(V(-1,1,-1), V(-1,1,1), V(1,1,1), V(1,1,-1), Vector3.up);
                g.Quad(V(-1,-1,1), V(-1,-1,-1), V(1,-1,-1), V(1,-1,1), Vector3.down);
                g.Quad(V(-1,-1,1), V(1,-1,1), V(1,1,1), V(-1,1,1), Vector3.forward);
                g.Quad(V(1,-1,-1), V(-1,-1,-1), V(-1,1,-1), V(1,1,-1), Vector3.back);
            }

            public void Ring(Transform parent, Vector3 centre, float inner, float outer, float length, int mat, Quaternion? rotation = null)
            {
                if (parent == null) return;
                Geometry g = Group(parent, mat); Quaternion q = rotation ?? Quaternion.identity;
                Vector3 V(float angle, float r, float z) => centre + q * new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, z);
                const int n = 32;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n, b = (i + 1) * Mathf.PI * 2 / n, h = length / 2;
                    Vector3 normal = q * new Vector3(Mathf.Cos((a+b)/2), Mathf.Sin((a+b)/2), 0);
                    g.Quad(V(a,outer,-h), V(b,outer,-h), V(b,outer,h), V(a,outer,h), normal);
                    g.Quad(V(a,inner,h), V(b,inner,h), V(b,outer,h), V(a,outer,h), q * Vector3.forward);
                    g.Quad(V(a,outer,-h), V(b,outer,-h), V(b,inner,-h), V(a,inner,-h), q * Vector3.back);
                    if (inner > 0) g.Quad(V(a,inner,h), V(b,inner,h), V(b,inner,-h), V(a,inner,-h), -normal);
                }
            }

            public void ScopeTube(Vector3 centre, Vector2[] profile, int mat)
            {
                Geometry g = Group(Gun, mat);
                const int count = 48;
                for (int j = 0; j + 1 < profile.Length; j++)
                for (int i = 0; i < count; i++)
                {
                    float a = i * Mathf.PI * 2 / count, b = (i + 1) * Mathf.PI * 2 / count;
                    Vector3 V(float t, Vector2 p) => centre + new Vector3(Mathf.Cos(t)*p.y,Mathf.Sin(t)*p.y,p.x);
                    g.Quad(V(a,profile[j]),V(b,profile[j]),V(b,profile[j+1]),V(a,profile[j+1]),new Vector3(Mathf.Cos(a),Mathf.Sin(a),0));
                }
            }

            public void Save()
            {
                int triangles = 0;
                foreach (var pair in groups)
                {
                    var (parent, material) = pair.Key; Geometry g = pair.Value;
                    if (g.vertices.Count == 0) continue;
                    string stem = kind + "_" + (parent == Gun ? "Body" : parent.name) + "_" + material;
                    string path = $"{MeshDir}/{stem}.asset";
                    Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    bool create = mesh == null;
                    if (create) mesh = new Mesh { name = stem };
                    mesh.Clear(); mesh.SetVertices(g.vertices); mesh.SetTriangles(g.indices, 0); mesh.SetUVs(0, g.uv);
                    WeaponSurfaceFinish.SmoothNormals(mesh); mesh.RecalculateBounds();
                    if (create) AssetDatabase.CreateAsset(mesh, path);
                    EditorUtility.SetDirty(mesh);
                    var go = new GameObject(Prefix + "_" + material);
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = materials[material];
                    triangles += g.indices.Count / 3;
                }
                Debug.Log($"[GunPolish] {kind}: {triangles} detail triangles in {groups.Count} material/animation groups.");
            }
        }

        private sealed class Geometry
        {
            public readonly List<Vector3> vertices = new();
            public readonly List<Vector2> uv = new();
            public readonly List<int> indices = new();
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                if (Vector3.Dot(Vector3.Cross(b-a, c-a), normal) >= 0) indices.AddRange(new[] { start, start+1, start+2, start, start+2, start+3 });
                else indices.AddRange(new[] { start, start+2, start+1, start, start+3, start+2 });
            }
        }

        public static void CaptureBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run captures in batch mode to preserve the open editor scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .6f, .68f);
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.2f;
            light.transform.rotation = Quaternion.Euler(38, -35, 0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .8f;
            fill.transform.rotation = Quaternion.Euler(20, 145, 0);
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .075f, .105f);
            camera.nearClipPlane = .005f; camera.farClipPlane = 20;
            string[] args = Environment.GetCommandLineArgs();
            string folder = args.Contains("-gunBaseline") ? "Screenshots/GunPolish/Before" : "Screenshots/GunPolish/After";
            Directory.CreateDirectory(folder);
            foreach (string kind in Kinds)
            {
                var gun = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Items/Prefabs/{kind}.prefab"));
                Bounds bounds = new Bounds(); bool first = true;
                foreach (Renderer r in gun.GetComponentsInChildren<Renderer>())
                    if (r.enabled) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.size.y * .8f, bounds.size.z * .38f);
                camera.transform.position = bounds.center + new Vector3(2, .32f, -.48f);
                camera.transform.LookAt(bounds.center);
                Capture(camera, $"{folder}/{kind}.png");
                camera.transform.position = bounds.center + new Vector3(-2, .32f, -.48f);
                camera.transform.LookAt(bounds.center);
                Capture(camera, $"{folder}/{kind}_left.png");
                camera.transform.position = bounds.center + new Vector3(2, .4f, -.8f);
                camera.transform.LookAt(bounds.center);
                camera.orthographicSize *= 1.3f;
                var fitted = new List<(GameObject part, bool active)>();
                string optic = kind == "Sniper" ? "SightScope" : kind == "Rifle" || kind == "SMG" ? "SightScope" : "SightRedDot";
                foreach (string part in new[] { optic, "BarrelSuppressor", "Laser", "MagazineExtended", "Magazine" })
                {
                    GameObject target = gun.transform.Find(part)?.gameObject;
                    if (target == null) continue;
                    fitted.Add((target, target.activeSelf));
                    target.SetActive(part != "Magazine");
                }
                Capture(camera, $"{folder}/{kind}_attachments.png");
                gun.transform.Find("BarrelSuppressor").gameObject.SetActive(false);
                var brake = gun.transform.Find("BarrelCompensator").gameObject;
                bool brakeActive = brake.activeSelf;
                brake.SetActive(true);
                Capture(camera, $"{folder}/{kind}_brake.png");
                brake.SetActive(brakeActive);
                foreach (var entry in fitted) entry.part.SetActive(entry.active);
                var so = new SerializedObject(gun.GetComponent<Item>());
                gun.transform.position = so.FindProperty("_holdOffset").vector3Value;
                gun.transform.rotation = Quaternion.Euler(so.FindProperty("_holdEuler").vector3Value);
                camera.orthographic = false; camera.fieldOfView = 74;
                camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Capture(camera, $"{folder}/{kind}_firstperson.png");
                Object.DestroyImmediate(gun);
            }
            Debug.Log("[GunPolish] Captures: " + folder);
        }

        private static void Capture(Camera camera, string path)
        {
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
