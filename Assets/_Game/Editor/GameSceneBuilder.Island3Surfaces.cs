using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 3's surfaces with real grain: the ground (sand, grass, jungle floor, dirt roads, rock), the boulders, the
    /// breakwater, the cave and the castle stone. Poly Haven scans (CC0) made into detail maps by
    /// ArtSource/Tools/make_island3_ground_textures.py, drawn by Assets/_Game/Data/Shaders/Ground.shader.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string Island3TextureDir = "Assets/_Game/Art/Island3Ground";
        private const string GroundShaderPath = "Assets/_Game/Data/Shaders/Ground.shader";

        // The ground's palette (sRGB); each vertex shades its layers toward their second colour.
        private static readonly Color I3Sand = new(0.92f, 0.82f, 0.6f), I3WetSand = new(0.66f, 0.58f, 0.43f);
        private static readonly Color I3Grass = new(0.43f, 0.62f, 0.27f), I3LushGrass = new(0.27f, 0.47f, 0.2f), I3DryGrass = new(0.58f, 0.62f, 0.3f);
        private static readonly Color I3JungleFloor = new(0.36f, 0.43f, 0.22f), I3Dirt = new(0.6f, 0.46f, 0.31f);
        private static readonly Color I3Rock = new(0.55f, 0.53f, 0.5f), I3Crag = new(0.4f, 0.38f, 0.37f);

        /// <summary>A scan's texture, imported linear, repeating, mip-mapped and sharp at grazing angles.</summary>
        private static Texture2D Island3Scan(string name)
        {
            string path = $"{Island3TextureDir}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.IO.FileNotFoundException("Island 3 scan missing (run make_island3_ground_textures.py)", path);
            if (importer.sRGBTexture || importer.anisoLevel != 8 || importer.textureCompression != TextureImporterCompression.CompressedHQ
                || importer.wrapMode != TextureWrapMode.Repeat || importer.maxTextureSize != 1024)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Island3SurfaceMaterial(string name, string mode)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GroundShaderPath);
            if (shader == null) throw new System.IO.FileNotFoundException("Ground shader missing", GroundShaderPath);
            Material mat = LoadOrCreateMaterial(name, shader);
            mat.SetFloat("_Mode", mode == "Ground" ? 0f : mode == "Rock" ? 1f : 2f);
            foreach (string m in new[] { "GROUND", "ROCK", "TRIPO" })
                if (m == mode.ToUpperInvariant()) mat.EnableKeyword("_MODE_" + m); else mat.DisableKeyword("_MODE_" + m);
            mat.enableInstancing = true;
            return mat;
        }

        private static void SetRockScans(Material mat, string sides, string top, float metres, float contrast)
        {
            mat.SetTexture("_RockD", Island3Scan(sides + "_D"));
            mat.SetTexture("_RockN", Island3Scan(sides + "_N"));
            mat.SetTexture("_RockTopD", Island3Scan(top + "_D"));
            mat.SetTexture("_RockTopN", Island3Scan(top + "_N"));
            mat.SetFloat("_RockScale", metres);
            mat.SetFloat("_RockContrast", contrast);
        }

        /// <summary>The ground: vertex colours are a shade (x2), uv1 the layer weights (grass, jungle, dirt, rock).</summary>
        private static Material Island3GroundMaterial()
        {
            Material mat = Island3SurfaceMaterial("Island3Ground", "Ground");
            foreach (string layer in new[] { "Sand", "Grass", "Jungle", "Dirt" })
            {
                mat.SetTexture($"_{layer}D", Island3Scan(layer + "_D"));
                mat.SetTexture($"_{layer}N", Island3Scan(layer + "_N"));
            }
            SetRockScans(mat, "Cliff", "Rock", 11f, 0.9f);
            mat.SetColor("_SandColor", I3Sand);
            mat.SetColor("_GrassColor", I3Grass);
            mat.SetColor("_JungleColor", I3JungleFloor);
            mat.SetColor("_DirtColor", I3Dirt);
            mat.SetColor("_RockColor", I3Rock);
            mat.SetVector("_Scales", new Vector4(6f, 3.2f, 4.5f, 4.5f));
            mat.SetVector("_Contrast", new Vector4(0.7f, 1.3f, 1f, 1f));
            mat.SetFloat("_NormalStrength", 1f);
            mat.SetFloat("_HeightBlend", 0.6f);
            mat.SetFloat("_BlendDepth", 0.18f);
            mat.SetFloat("_VertexColor", 1f);
            mat.SetFloat("_VertexScale", 2f);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_Occlusion", 0.7f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Plain rock, triplanar (no UVs needed). <paramref name="vertexColours"/>: the mesh's colours (linear) tint it.</summary>
        private static Material Island3RockMaterial(string name, Color colour, string scan = "Rock", float metres = 4f, float contrast = 1f, bool vertexColours = false)
        {
            Material mat = Island3SurfaceMaterial(name, "Rock");
            SetRockScans(mat, scan, scan, metres, contrast);
            mat.SetColor("_RockColor", colour);
            mat.SetFloat("_VertexColor", vertexColours ? 1f : 0f);
            mat.SetFloat("_VertexScale", 1f);
            mat.SetFloat("_NormalStrength", 1f);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Occlusion", 0.7f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Tripo models that get the stone grain over their own colours: scan, metres per tile, grain, desaturate, tint,
        // how much of the model's own map detail stays (the rest is its broad colour only), and whether the grain keeps
        // off coloured parts (towers and gatehouse: red roofs, flags). All-stone models take it everywhere: their maps'
        // yellow and orange specks would otherwise leave smooth bald patches.
        private static readonly Dictionary<string, (string scan, float metres, float contrast, float desaturate, Color tint, float detail, bool stoneOnly)> Island3TripoStone = new()
        {
            // The wall model's map is big smeared slabs: real masonry over its broad colour instead.
            ["castle_wall"] = ("CastleStone", 3.2f, 1.1f, 0.6f, new Color(1.45f, 1.42f, 1.38f), 0.2f, false),
            ["castle_round_tower"] = ("Rock", 2.4f, 0.7f, 0f, Color.white, 1f, true),
            ["castle_gatehouse"] = ("Rock", 2.4f, 0.7f, 0f, Color.white, 1f, true),
            ["castle_keep"] = ("Rock", 2.4f, 0.7f, 0f, Color.white, 1f, true),
            ["sea_cave_entrance"] = ("Cliff", 7f, 0.9f, 0.5f, new Color(1.05f, 1.02f, 0.98f), 1f, false),
            // The spires came out near black with orange specks: grey them to match the crags.
            ["sea_rock_pinnacle"] = ("Cliff", 6f, 1.1f, 1f, new Color(1.7f, 1.66f, 1.62f), 0.3f, false),
        };

        /// <summary>Swaps a Tripo model's materials for the stone-grain version (one material per model).</summary>
        private static void DressTripoStone(GameObject model, string name)
        {
            if (!Island3TripoStone.TryGetValue(name, out var look)) return;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material imported = materials[i];
                    if (imported == null || !imported.HasProperty("baseColorTexture") || imported.GetTexture("baseColorTexture") == null) continue;
                    Material mat = Island3SurfaceMaterial($"I3Stone_{name}_{i}", "Tripo");
                    SetRockScans(mat, look.scan, look.scan == "Cliff" ? "Rock" : look.scan, look.metres, look.contrast);
                    var texture = (Texture2D)imported.GetTexture("baseColorTexture");
                    texture.anisoLevel = Mathf.Max(texture.anisoLevel, 4);
                    mat.SetTexture("_BaseMap", texture);
                    mat.SetTextureScale("_BaseMap", imported.GetTextureScale("baseColorTexture"));
                    mat.SetTextureOffset("_BaseMap", imported.GetTextureOffset("baseColorTexture"));
                    Color factor = imported.HasProperty("baseColorFactor") ? imported.GetColor("baseColorFactor") : Color.white;
                    mat.SetColor("_BaseTint", factor * look.tint);
                    mat.SetFloat("_Desaturate", look.desaturate);
                    mat.SetFloat("_BaseDetail", look.detail);
                    mat.SetFloat("_BaseBlurMip", 6f);
                    mat.SetFloat("_StoneOnly", look.stoneOnly ? 1f : 0f);
                    mat.SetFloat("_VertexColor", 0f);
                    mat.SetFloat("_NormalStrength", 1f);
                    mat.SetFloat("_Smoothness", 0.1f);
                    mat.SetFloat("_Occlusion", 0.6f);
                    mat.SetFloat("_Cull", imported.HasProperty("_CullMode") ? imported.GetFloat("_CullMode") : 2f);
                    EditorUtility.SetDirty(mat);
                    materials[i] = mat;
                }
                renderer.sharedMaterials = materials;
            }
        }

        /// <summary>Batch: close-ups of island 3's surfaces at a player's distance (Screenshots/Review/Island3Surfaces).</summary>
        public static void ReviewIsland3SurfacesBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Vector2 c = CastleCenter, g = VillageHouses[0].at;
            Vector3 Eye(float x, float z, float up = 1.7f) => OnGround(new Vector3(x, 0f, z)) + Vector3.up * up;
            float coastX = Island3Center.x - 60f;
            while (coastX > -260f && BeachHeight(coastX, -880f) > WaterLevel) coastX -= 0.5f;
            CaptureViews("Screenshots/Review/Island3Surfaces", new[]
            {
                ("castle-wall", new Vector3(c.x - 14f, Island3CliffTop + 1.8f, c.y + 30f), new Vector3(c.x - 22f, Island3CliffTop + 5f, c.y + 20f)),
                ("castle-tower", new Vector3(c.x + 22f, Island3CliffTop + 2f, c.y + 34f), new Vector3(c.x + 31f, Island3CliffTop + 6f, c.y + 20f)),
                ("gatehouse", new Vector3(c.x + 4f, Island3CliffTop + 1.8f, c.y + 34f), new Vector3(c.x, Island3CliffTop + 5f, c.y + 20f)),
                ("grass-road", Eye(g.x + 10f, g.y - 12f), Eye(g.x + 2f, g.y - 2f, 0f)),
                ("cliff-close", Eye(Island3Spring.x + 6f, Island3Spring.y - 14f), new Vector3(Island3Spring.x, 14f, Island3Spring.y + 20f)),
                ("breakwater", new Vector3(coastX + 6f, 2.2f, -872f), new Vector3(coastX - 2f, 0.5f, -884f)),
                ("crag-top", new Vector3(Island3Crags.x + 20f, 34f, Island3Crags.y - 25f), new Vector3(Island3Crags.x, 26f, Island3Crags.y)),
            });
        }

        // ---------------------------------------------------------------------
        // The ground's layers
        // ---------------------------------------------------------------------

        /// <summary>0 outside the jungle, rising to 1 by 6 m inside its edge.</summary>
        private static float JungleFloor(Vector2 p)
        {
            if (!InJungle(p)) return 0f;
            float edge = JunglePolygons.Where(poly => InPolygon(p, poly)).Min(poly => DistanceToLine(p, poly.Append(poly[0]).ToArray()));
            return Smooth(0f, 6f, edge);
        }

        private static Vector3 Linear(Color c) => new(c.linear.r, c.linear.g, c.linear.b);

        /// <summary>
        /// Sand at the water, grass inland (jungle floor under the jungle), dirt roads and yards, rock on steep ground and
        /// the crags: the layer weights (grass, jungle, dirt, rock; sand is the rest) and a per-vertex shade (linear, /2).
        /// </summary>
        private static (Vector4 weights, Color shade) Island3GroundLayers(Vector3 v, Vector3 normal)
        {
            float x = v.x, z = v.z, h = v.y;
            var p = new Vector2(x, z);
            float s = Island3Shore(x, z, BoxDistanceOut(x, z, Island3Center, Island3HalfSize, Island3CornerRadius));
            // Beach sand only down by the water: a shore that rises steeply (the slopes up to the plateau, the cliff tops)
            // is grass, or rock where it's steep.
            float green = Mathf.Max(Smooth(8f, 15f, s), Smooth(2.2f, 4f, h)) * Smooth(-0.2f, 0.5f, h);
            foreach ((Vector2 centre, float radius) in Island3Pools)
                green *= Smooth(radius - 0.5f, radius + 1f, Vector2.Distance(p, centre)); // pool beds
            float bare = Mathf.Max(Smooth(3.4f, 1.8f, DistanceToRoads(p)), Near(x, z, CastleCenter, CastleHalf, 7f)) * green;
            float rocky = Mathf.Max(Smooth(0.8f, 0.6f, normal.y), Smooth(21f, 25f, h));
            if (InCave(x, z, 18f, -10f, 40f)) rocky = 1f;
            // Mud and pebbles along the river and round the spring pond.
            float toWater = Mathf.Min(DistanceToLine(p, Island3River) - 3.5f, Vector2.Distance(p, Island3Spring) - 8f);
            bare = Mathf.Max(bare, Smooth(3.5f, 1f, toWater) * green * 0.8f);

            float sand = (1f - green) * (1f - bare) * (1f - rocky);
            float grassAll = green * (1f - bare) * (1f - rocky);
            float jungle = grassAll * JungleFloor(p);
            var weights = new Vector4(grassAll - jungle, jungle, bare * (1f - rocky), rocky);

            // Shade: wet sand, lush and light grass, pale and dark rock, a little noise over everything.
            Vector3 Ratio(Color a, Color b) { Vector3 la = Linear(a), lb = Linear(b); return new Vector3(lb.x / la.x, lb.y / la.y, lb.z / la.z); }
            Vector3 sandShade = Vector3.Lerp(Vector3.one, Ratio(I3Sand, I3WetSand), Smooth(0.1f, -1.5f, h));
            Vector3 grassShade = Vector3.Lerp(Ratio(I3Grass, I3LushGrass), Vector3.one, Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 9f));
            grassShade = Vector3.Lerp(grassShade, Ratio(I3Grass, I3DryGrass), 0.8f * Smooth(0.55f, 0.75f, Mathf.PerlinNoise(x * 0.031f + 70f, z * 0.031f + 11f)));
            Vector3 rockShade = Vector3.Lerp(Vector3.one, Ratio(I3Rock, I3Crag), Mathf.PerlinNoise(x * 0.08f, z * 0.08f + 20f));
            Vector3 shade = sandShade * sand + grassShade * weights.x + Vector3.one * (weights.y + weights.z) + rockShade * rocky;
            shade *= 0.9f + 0.2f * Mathf.PerlinNoise(x * 0.17f + 50f, z * 0.17f);
            return (weights, new Color(shade.x * 0.5f, shade.y * 0.5f, shade.z * 0.5f, 1f));
        }
    }
}
