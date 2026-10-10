using System;
using System.IO;
using System.Linq;
using System.Text;
using PleaseDontDrown.World.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 2 rework (2026-10-09): the user's Tripo hotel, polished in Blender (ArtSource/Tools/build_realistic_hotel.py),
    /// replaces the old facade; resort decor is cleared (hotel + story only) and the island stands higher out of the sea.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // Island 2 footprint with its beach, used to find what sits on the island.
        private static bool OnIsland2(Vector3 p) => p.x > -175f && p.x < 215f && p.z > -495f && p.z < -205f;

        // Island 2 rises from the waterline to a 1.5 m plateau over ~10 m of beach (it used to sit 0.35 m above the sea).
        private const float Island2Plateau = 1.5f;
        private static bool _island2Flat; // true: the old, flat profile (to measure how far things must move)

        private static float Island2Rise(float shore) =>
            _island2Flat ? 0f : Island2Plateau * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 16f, shore));

        private static float Island2Ground(float x, float z)
        {
            float shore = Island2Shore(x, z);
            return ProfileHeight(shore, x, z, false) + Island2Rise(shore);
        }

        private static float Island2Lift(Vector3 p)
        {
            _island2Flat = true; float before = BeachHeight(p.x, p.z);
            _island2Flat = false; return BeachHeight(p.x, p.z) - before;
        }

        private static readonly (string name, Color colour, float metallic, float smoothness)[] RealHotelPaint =
        {
            ("wall", new Color(.93f, .85f, .71f), 0f, .12f),   // warm cream render (plaster texture on top)
            ("trim", new Color(.80f, .69f, .49f), 0f, .20f),   // sandstone cornices
            ("white", new Color(.95f, .93f, .87f), 0f, .25f),  // balusters, mouldings, frames
            ("teal", new Color(.17f, .56f, .64f), 0f, .42f),   // painted window surrounds, columns
            ("glass", new Color(.11f, .20f, .27f), .5f, .95f), // tinted reflective glazing
            ("glass_b", new Color(.20f, .32f, .38f), .45f, .93f), // lighter tint
            ("curtain", new Color(.46f, .38f, .29f), .1f, .80f), // drawn curtains behind the glass
            ("roof", new Color(.67f, .65f, .62f), 0f, .10f),   // roofs
            ("metal", new Color(.55f, .58f, .60f), .8f, .5f),  // roof railings
            ("gold", new Color(1f, .78f, .38f), 1f, .78f),     // polished brass
            ("stone", new Color(.93f, .86f, .74f), 0f, .30f),  // plinths, podium paving
        };

        // Hotel-local: the lobby sits 7 m back from where the story builder puts it, inside the polished facade
        // (front wall at z -1, just behind the facade at z -0.3; the door opens under the porte-cochere).
        private const float LobbyShift = -7f;

        private static Material RealHotelMaterial(Material imported)
        {
            string key = imported.name.Replace("tripohotel_", "");
            var paint = RealHotelPaint.FirstOrDefault(p => p.name == key);
            if (paint.name == null) throw new InvalidOperationException("Unknown hotel material " + imported.name);
            var material = GetMaterial("HotelReal_" + key, paint.colour, paint.metallic, paint.smoothness);
            if (key == "stone")
            {
                // Forecourt and plinths: warm limestone paving, 1 m slabs (UVs are metres).
                var tile = FinishedLobbyFloorMaterial().GetTexture("_BaseMap");
                material.SetTexture("_BaseMap", tile); material.SetTexture("_MainTex", tile);
                material.SetTextureScale("_BaseMap", Vector2.one); material.SetTextureScale("_MainTex", Vector2.one);
            }
            else if (key is "wall" or "trim" or "white" or "roof")
            {
                // A subtle plaster/stone grain (UVs are metres) so walls don't read as flat 3D-model colour.
                material.SetTexture("_BaseMap", PlasterTexture()); material.SetTexture("_MainTex", PlasterTexture());
                var tiling = new Vector2(.33f, .33f);
                material.SetTextureScale("_BaseMap", tiling); material.SetTextureScale("_MainTex", tiling);
            }
            // The AI mesh has inward-facing patches (wing corners, balcony bays); one-sided they showed the sky through
            // the building. Render both faces.
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off); material.doubleSidedGI = true;
            material.enableInstancing = true; EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D PlasterTexture()
        {
            const string path = "Assets/_Game/Art/Props/hotel_plaster.png";
            if (!File.Exists(path))
            {
                const int size = 512; var image = new Texture2D(size, size, TextureFormat.RGB24, true);
                var pixels = new Color32[size * size];
                float Tile(float x, float y, float f) // tileable noise: blend four offsets of Perlin
                {
                    float u = x / size, v = y / size;
                    float a = Mathf.PerlinNoise(x * f, y * f), b = Mathf.PerlinNoise((x - size) * f, y * f);
                    float c = Mathf.PerlinNoise(x * f, (y - size) * f), d = Mathf.PerlinNoise((x - size) * f, (y - size) * f);
                    return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
                }
                var rng = new System.Random(7);
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float n = Tile(x, y, .012f) * .55f + Tile(x, y, .05f) * .3f + Tile(x, y, .2f) * .15f;
                    float grain = (float)rng.NextDouble() * .025f;
                    byte value = (byte)Mathf.Clamp(Mathf.RoundToInt(255 * (.9f + (n - .5f) * .14f + grain)), 0, 255);
                    pixels[y * size + x] = new Color32(value, value, value, 255);
                }
                image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); Object.DestroyImmediate(image);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Moves the story lobby (walls, desk, bed, shelf, anchors, receptionist) inside the polished hotel, once.</summary>
        private static void MoveLobbyInside(Transform hotel)
        {
            var floor = hotel.Find("Floor");
            if (floor == null || floor.localPosition.z < -3f) return;              // already inside
            var offset = hotel.TransformVector(new Vector3(0, 0, LobbyShift));
            foreach (Transform child in hotel.Cast<Transform>().ToArray())
                if (child.name is not ("ResortExterior" or "HotelEntranceFinish" or "LobbyLuxury")) child.position += offset;
            var receptionist = GameObject.Find("Story/Npc_Receptionist");
            if (receptionist != null) receptionist.transform.position += offset;
        }

        /// <summary>Hotel + story only: removes the resort decor and puts the polished Tripo hotel in.</summary>
        private static void ApplyRealisticHotel(Transform hotel)
        {
            for (int i = 0; i < 3; i++)
                if (LoadProp("resort_hotel_real_lod" + i) == null) throw new InvalidOperationException("Polished hotel LOD not imported: " + i);
            var exterior = hotel.Find("ResortExterior") ?? new GameObject("ResortExterior").transform;
            exterior.SetParent(hotel, false);
            var tower = exterior.Find("CentralTower")?.GetComponent<LODGroup>();
            if (tower == null)
            {
                tower = new GameObject("CentralTower").AddComponent<LODGroup>();
                tower.transform.SetParent(exterior, false);
            }
            // Pools, gardens, pavilions, cabanas, loungers, parasols, planters, the beach bar and the resort palms go.
            foreach (Transform child in exterior.Cast<Transform>().ToArray())
                if (child != tower.transform) Object.DestroyImmediate(child.gameObject);
            var beach = hotel.Find("ResortBeachLife"); if (beach != null) Object.DestroyImmediate(beach.gameObject);

            // The polished model is already in metres with its pivot on the ground.
            tower.transform.localPosition = new Vector3(0, 0, -4); tower.transform.localRotation = Quaternion.identity;
            tower.transform.localScale = Vector3.one;
            foreach (Transform child in tower.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var levels = new LOD[3]; float[] transitions = { .30f, .08f, .012f };
            var collision = new System.Collections.Generic.List<Mesh>();
            for (int i = 0; i < 3; i++)
            {
                var copy = Object.Instantiate(LoadProp("resort_hotel_real_lod" + i), tower.transform);
                copy.name = "HotelLOD" + i;
                var renderers = copy.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers) r.sharedMaterials = r.sharedMaterials.Select(RealHotelMaterial).ToArray();
                foreach (var c in copy.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                // Facade from LOD1; the clean details (columns, podium, rails) at full detail from LOD0.
                var filters = copy.GetComponentsInChildren<MeshFilter>();
                if (i == 1) collision.Add(filters.First(f => f.name.Contains("HotelLOD")).sharedMesh);
                if (i == 0) collision.Add(filters.First(f => f.name.Contains("HotelWindows")).sharedMesh);
                levels[i] = new LOD(transitions[i], renderers);
            }
            tower.SetLODs(levels); tower.RecalculateBounds();
            tower.fadeMode = LODFadeMode.None; tower.animateCrossFading = false;

            // Collision follows the real facade (canopy, columns, balconies); solid cores stop anyone slipping
            // through a gap into the closed guest floors. The lobby (24 x 12 m, z -13 .. -1) stays open.
            foreach (var mesh in collision)
            {
                var shell = new GameObject("HotelCollision").transform; shell.SetParent(tower.transform, false);
                shell.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            // The ground floor is open from wall to wall (lobby, doctor, casino; GameSceneBuilder.HotelInterior.cs).
            // The back base storey's wall (hotel_fill.py, outer face z -18.9); the cores stay inside the facade, which
            // steps back to z -16.8 at the wings above it (they once stood ~3 m out of the back as an invisible wall).
            Collider(exterior, "HotelCoreBack", new Vector3(0, 2.2f, -18.65f), new Vector3(47.8f, 4.4f, .5f));
            Collider(exterior, "HotelCoreUpper", new Vector3(0, 21.45f, -8.45f), new Vector3(45, 35.1f, 14.3f));
            NavKeepOut(exterior, new Vector3(0, 10, -9), new Vector3(52, 20, 26));
            // Two steps up onto the stone podium in front of the porte-cochere (its front edge is z 11.9).
            var stone = GetMaterial("HotelReal_stone", RealHotelPaint.First(p => p.name == "stone").colour, 0, .32f);
            ResortBox(exterior, "PodiumStep", new Vector3(0, .1f, 12.25f), new Vector3(13, .2f, .7f), stone);
            ResortBox(exterior, "PodiumStep", new Vector3(0, .05f, 12.85f), new Vector3(13.4f, .1f, .6f), stone);

            // Reception: the story lobby moves inside the building; its old entrance pieces (built for earlier facades)
            // go, the door now opens under the porte-cochere.
            MoveLobbyInside(hotel);
            ExpandHotelShell(hotel);
            BuildFinishedHotelEntrance(hotel);
            var oldTrim = hotel.Find("ReceptionPolish"); if (oldTrim != null) Object.DestroyImmediate(oldTrim.gameObject);
            var entrance = hotel.Find("HotelEntranceFinish");
            entrance.localPosition = new Vector3(0, 0, LobbyShift);
            foreach (Transform t in entrance.Cast<Transform>().ToArray())
                if (t.name is "VestibuleBase" or "VestibuleTopRail" or "EntrancePortalJamb" or "EntrancePortalLintel" or
                    "ArrivalCanopyRoof" or "ArrivalCanopyColumn" or "EntranceDeck" or "EntranceStep" or "ReceptionLounge" or
                    "LobbyPlanter" or "LobbyCeilingLamp" or "WarmEntranceLight")
                    Object.DestroyImmediate(t.gameObject);
            foreach (Transform t in hotel.Cast<Transform>().ToArray())
                if (t.name is "Step" or "Window") Object.DestroyImmediate(t.gameObject);
            BuildLuxuryLobby(hotel);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Gold-and-marble luxury for the reception (hotel-local; the lobby spans x -12..12, z -13..-1, floor 0.3,
        /// ceiling 3.5). Story pieces keep their places: desk and receptionist at x -6, bed at x 8.5 by the back wall,
        /// first-aid shelf by the front wall, door at x 0.
        /// </summary>
        private static void BuildLuxuryLobby(Transform hotel)
        {
            var old = hotel.Find("LobbyLuxury"); if (old != null) Object.DestroyImmediate(old.gameObject);
            AssetDatabase.DeleteAsset(MeshDir + "/Lobby");        // combined meshes are rebuilt, never patched in place
            AssetDatabase.DeleteAsset(MeshDir + "/HotelInterior");
            var root = new GameObject("LobbyLuxury").transform; root.SetParent(hotel, false);
            var gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            var cream = RealHotelMaterial(new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "tripohotel_white" });
            var marble = FinishedLobbyFloorMaterial();
            var polished = GetMaterial("LobbyMarble", new Color(.97f, .95f, .90f), 0f, .88f);
            polished.SetTexture("_BaseMap", marble.GetTexture("_BaseMap")); polished.SetTextureScale("_BaseMap", new Vector2(12, 8.7f));
            var wood = GetMaterial("LobbyWalnut", new Color(.30f, .17f, .09f), 0f, .55f);
            var darkMarble = GetMaterial("LobbyDarkMarble", new Color(.07f, .11f, .12f), 0f, .7f);
            var carpet = GetMaterial("LobbyCarpet", new Color(.46f, .07f, .10f), 0f, .08f);
            var bulb = GetMaterial("LobbyBulb", new Color(1f, .9f, .7f), emission: new Color(1f, .74f, .4f) * 2.2f);
            var crystal = GetMaterial("LobbyCrystal", new Color(.95f, .97f, 1f), .2f, .97f);
            var painting = GetMaterial("LobbyPainting", new Color(.12f, .38f, .48f), 0f, .3f);

            GameObject Box(string name, Vector3 p, Vector3 size, Material m, bool solid = false) =>
                Primitive(PrimitiveType.Cube, name, root, p, size, m, solid);
            GameObject Cyl(string name, Vector3 p, float r, float h, Material m, bool solid = false) =>
                Primitive(PrimitiveType.Cylinder, name, root, p, new Vector3(r * 2, h * .5f, r * 2), m, solid);

            // Polished marble floor and a burgundy runner with gold borders from the door to the reception.
            Box("MarbleFloor", new Vector3(0, .306f, -9.7f), new Vector3(23.6f, .012f, 17.3f), polished);
            Box("Runner", new Vector3(0, .316f, -4.6f), new Vector3(2.2f, .01f, 6.6f), carpet);
            foreach (float x in new[] { -1.12f, 1.12f }) Box("RunnerGold", new Vector3(x, .318f, -4.6f), new Vector3(.07f, .012f, 6.6f), gold);
            Box("DeskRug", new Vector3(-6, .316f, -3.1f), new Vector3(4.6f, .01f, 2.2f), carpet);
            foreach (float z in new[] { -2.02f, -4.18f }) Box("DeskRugGold", new Vector3(-6, .318f, z), new Vector3(4.6f, .012f, .07f), gold);
            foreach (float x in new[] { -8.27f, -3.73f }) Box("DeskRugGold", new Vector3(x, .318f, -3.1f), new Vector3(.07f, .012f, 2.2f), gold);

            // Walnut wainscot with a gold rail, gold crown moulding, cream pilasters with gold caps.
            void Wall(string side, Vector3 centre, Vector3 along, float length, Vector3 inward)
            {
                Vector3 thin = new Vector3(Mathf.Abs(along.x) * length + Mathf.Abs(inward.x) * .05f, 0, Mathf.Abs(along.z) * length + Mathf.Abs(inward.z) * .05f);
                Box("Wainscot" + side, centre + inward * .025f + Vector3.up * .78f, thin + Vector3.up * .95f, wood);
                Box("DadoRail" + side, centre + inward * .05f + Vector3.up * 1.28f, thin + Vector3.up * .06f + new Vector3(Mathf.Abs(inward.x), 0, Mathf.Abs(inward.z)) * .05f, gold);
                Box("Crown" + side, centre + inward * .06f + Vector3.up * 3.42f, thin + Vector3.up * .14f + new Vector3(Mathf.Abs(inward.x), 0, Mathf.Abs(inward.z)) * .08f, gold);
            }
            foreach (int side in new[] { -1, 1 })                   // side walls, broken by the casino / doctor doorways
            {
                string s = side < 0 ? "Left" : "Right";
                Vector3 inward = side < 0 ? Vector3.right : Vector3.left;
                Wall(s, new Vector3(side * 11.85f, 0, (-1.05f + WingDoorNear) * .5f), Vector3.forward, WingDoorNear + 1.05f, inward);
                Wall(s, new Vector3(side * 11.85f, 0, (WingDoorFar + InteriorBack + .05f) * .5f), Vector3.forward, WingDoorFar - InteriorBack - .05f, inward);
            }
            Wall("FrontL", new Vector3(-6.75f, 0, -1.15f), Vector3.right, 10.2f, Vector3.back);
            Wall("FrontR", new Vector3(6.75f, 0, -1.15f), Vector3.right, 10.2f, Vector3.back);
            Wall("Back", new Vector3(0, 0, InteriorBack + .15f), Vector3.right, 23.6f, Vector3.forward);
            Wall("Reception", new Vector3(-6.3f, 0, -12.95f), Vector3.right, 11.1f, Vector3.forward);
            Wall("ReceptionRear", new Vector3(-6.3f, 0, -13.25f), Vector3.right, 11.1f, Vector3.back);
            foreach (float z in new[] { -3.2f, -7.4f, -11f, -15.6f })
                foreach (int side in new[] { -1, 1 })
                {
                    Box("Pilaster", new Vector3(side * 11.78f, 1.9f, z), new Vector3(.14f, 3.2f, .45f), cream);
                    Box("PilasterCap", new Vector3(side * 11.74f, 3.3f, z), new Vector3(.2f, .14f, .55f), gold);
                    Box("PilasterBase", new Vector3(side * 11.74f, .42f, z), new Vector3(.2f, .2f, .55f), gold);
                }

            // Door: flanking columns and a gold portal on the inside.
            foreach (int side in new[] { -1, 1 })
            {
                Cyl("DoorColumn", new Vector3(side * 2.3f, 1.9f, -1.75f), .24f, 3.2f, cream, true);
                Cyl("DoorColumnBase", new Vector3(side * 2.3f, .42f, -1.75f), .3f, .2f, gold);
                Cyl("DoorColumnCap", new Vector3(side * 2.3f, 3.38f, -1.75f), .3f, .2f, gold);
            }
            Box("DoorPortal", new Vector3(0, 3.22f, -1.2f), new Vector3(3.0f, .12f, .08f), gold);

            // Reception desk in dark marble with gold fluting; the brand in gold on a gold-framed backdrop.
            if (hotel.Find("ReceptionDesk")?.GetComponent<Renderer>() is Renderer desk) desk.sharedMaterial = darkMarble;
            if (hotel.Find("DeskTop")?.GetComponent<Renderer>() is Renderer top) top.sharedMaterial = gold;
            if (hotel.Find("DeskSign")?.GetComponent<TextMesh>() is TextMesh sign) sign.color = new Color(1f, .82f, .45f);
            for (int i = 0; i < 5; i++)
                Box("DeskFlute", new Vector3(-7.4f + i * .7f, .85f, -4.385f), new Vector3(.05f, .95f, .02f), gold);
            var entrance = hotel.Find("HotelEntranceFinish");
            foreach (string name in new[] { "ReceptionBrand", "ReceptionWelcome" })
                if (entrance?.Find(name)?.GetComponent<TextMesh>() is TextMesh brand) brand.color = new Color(1f, .82f, .45f);
            foreach (var (p, s) in new[] { (new Vector3(-6, 3.13f, -12.79f), new Vector3(8.2f, .1f, .05f)), (new Vector3(-6, .87f, -12.79f), new Vector3(8.2f, .1f, .05f)),
                                           (new Vector3(-10.05f, 2f, -12.79f), new Vector3(.1f, 2.36f, .05f)), (new Vector3(-1.95f, 2f, -12.79f), new Vector3(.1f, 2.36f, .05f)) })
                Box("BackdropFrame", p, s, gold);

            // Coffered ceiling: walnut beams with gold edges; three tiered gold chandeliers hung in the bays.
            foreach (float x in new[] { -9f, -3f, 3f, 9f })
            {
                Box("CeilingBeam", new Vector3(x, 3.42f, -9.7f), new Vector3(.35f, .16f, 17.3f), wood);
                Box("CeilingBeamGold", new Vector3(x, 3.335f, -9.7f), new Vector3(.39f, .02f, 17.3f), gold);
            }
            foreach (float z in new[] { -7f, -13.1f })
            {
                Box("CeilingBeamLong", new Vector3(0, 3.42f, z), new Vector3(23.6f, .16f, .35f), wood);
                Box("CeilingBeamLongGold", new Vector3(0, 3.335f, z), new Vector3(23.6f, .02f, .39f), gold);
            }
            foreach (float x in new[] { -6f, 0f, 6f })
                foreach (float z in new[] { -4f, -10f })
                {
                    if (x == 0 && z == -4f) continue;
                    Vector3 c = new Vector3(x, 0, z);
                    Primitive(PrimitiveType.Cylinder, "CeilingRose", root, c + Vector3.up * 3.49f, new Vector3(.7f, .01f, .7f), gold, false);
                    Cyl("ChandelierRod", c + Vector3.up * 3.2f, .02f, .6f, gold);
                    (float r, float y, int n)[] tiers = { (.62f, 2.88f, 12), (.42f, 2.66f, 9), (.22f, 2.46f, 6) };
                    foreach (var (r, y, n) in tiers)
                    {
                        Cyl("ChandelierTier", c + Vector3.up * y, r, .035f, gold);
                        for (int i = 0; i < n; i++)
                        {
                            float a = i * Mathf.PI * 2 / n;
                            Vector3 rim = c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                            Primitive(PrimitiveType.Sphere, "ChandelierCandle", root, rim + Vector3.up * .07f, Vector3.one * .07f, bulb, false);
                            Primitive(PrimitiveType.Sphere, "ChandelierCrystal", root, rim - Vector3.up * .1f, new Vector3(.045f, .1f, .045f), crystal, false);
                        }
                    }
                    Primitive(PrimitiveType.Sphere, "ChandelierFinial", root, c + Vector3.up * 2.3f, new Vector3(.12f, .2f, .12f), crystal, false);
                    var light = new GameObject("ChandelierLight").AddComponent<Light>(); light.transform.SetParent(root, false);
                    light.transform.localPosition = c + Vector3.up * 2.6f; light.type = LightType.Point;
                    light.color = new Color(1f, .82f, .6f); light.intensity = 1.6f; light.range = 8; light.shadows = LightShadows.None;
                }
            var lobbyLight = hotel.Find("LobbyLight")?.GetComponent<Light>();
            if (lobbyLight != null) { lobbyLight.intensity = .6f; lobbyLight.color = new Color(1, .88f, .73f); }

            // Lounge: sofas round a gold table; gold-framed paintings; palms in gold-rimmed planters.
            var lounge = BeachProp(root, "LobbyLounge", "resort_lounge_set", new Vector3(3.2f, .32f, -7.6f), 90);
            lounge.transform.localScale = Vector3.one * .75f;
            foreach (float x in new[] { -1.1f, 1.1f }) Collider(lounge.transform, "LobbySeat", new Vector3(x, .4f, 0), new Vector3(.9f, .8f, 2.3f));
            Cyl("CoffeeTable", new Vector3(3.2f, .55f, -7.6f), .45f, .06f, darkMarble, true);
            Cyl("CoffeeTableBase", new Vector3(3.2f, .43f, -7.6f), .12f, .24f, gold);
            foreach (float z in new[] { -8.6f, -11.4f, -15.8f })
            {
                Box("PaintingFrame", new Vector3(-11.8f, 2.25f, z), new Vector3(.04f, 1.26f, 1.76f), gold);
                Box("Painting", new Vector3(-11.76f, 2.25f, z), new Vector3(.03f, 1.1f, 1.6f), painting);
            }
            foreach (var p in new[] { new Vector3(-10.4f, .3f, -2.2f), new Vector3(10.4f, .3f, -2.2f), new Vector3(-10.4f, .3f, -11.6f),
                                      new Vector3(10.9f, .3f, -17.2f) })
            {
                var planter = BeachProp(root, "LobbyPlanter", "resort_planter", p, 90);
                Box("PlanterGoldRim", p + Vector3.up * .76f, new Vector3(1.2f, .05f, 2.05f), gold);
                planter.transform.localScale = Vector3.one * .9f;
            }
            BuildLobbyBackHall(root);
            BuildDoctorRoom(hotel, root);
            BuildCasino(hotel, root);
            CombineResortDetails(root, "Lobby");

            // Gold and polished marble reflect the warm lobby, not the sky outside: a baked box-projected probe.
            var probe = new GameObject("LobbyReflections").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(root, false); probe.transform.localPosition = new Vector3(0, 1.9f, -9.7f);
            probe.size = new Vector3(46.8f, 3.4f, 17.3f); probe.boxProjection = true; probe.importance = 2;
            probe.resolution = 256; probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            Physics.SyncTransforms();
            const string cube = "Assets/_Game/Art/Props/hotel_lobby_reflections.exr";
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
            if (!Lightmapping.BakeReflectionProbe(probe, cube)) throw new InvalidOperationException("Lobby reflection bake failed");
            AssetDatabase.ImportAsset(cube, ImportAssetOptions.ForceSynchronousImport);
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(cube);
        }

        /// <summary>Lifts what stands on island 2 by how much the ground under it rose (nothing moves at sea).</summary>
        private static int LiftIsland2Objects(UnityEngine.SceneManagement.Scene scene)
        {
            int moved = 0;
            void Visit(Transform t)
            {
                if (t.name == "BeachTerrain" || t.GetComponent<WaterSurface>() != null) return;
                Vector3 p = t.position;
                bool container = new Vector2(p.x, p.z).sqrMagnitude < .01f;
                if (!container && OnIsland2(p))
                {
                    float lift = Island2Lift(p);
                    if (lift > .005f) { t.position = p + Vector3.up * lift; moved++; }
                    return;
                }
                if (container) foreach (Transform c in t.Cast<Transform>().ToArray()) Visit(c);
            }
            foreach (var root in scene.GetRootGameObjects()) Visit(root.transform);
            return moved;
        }

        [MenuItem("PLEASE DON'T DROWN/Island two: polished hotel, raised island, no decor")]
        public static void RealisticHotelIsland()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var hotel = GameObject.Find("Environment/Hotel")?.transform;
            var terrain = GameObject.Find("Environment/BeachTerrain");
            if (scene.path != ScenePath || hotel == null || terrain == null || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Game in edit mode first; no scene edits made.");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-hotel-island-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            var report = new StringBuilder();

            // Raise the ground once: if the live terrain is already high at the hotel, only refresh the hotel.
            Physics.SyncTransforms();
            var probe = new Ray(new Vector3(20, 60, -232), Vector3.down);
            if (!terrain.GetComponent<MeshCollider>().Raycast(probe, out var hit, 120)) throw new InvalidOperationException("No terrain under the hotel");
            float groundBefore = hit.point.y;
            bool raise = groundBefore < Island2Plateau * .5f;
            if (raise)
            {
                RefreshBeachTerrain(terrain);
                report.AppendLine($"raised island 2 by up to {Island2Plateau} m; moved {LiftIsland2Objects(scene)} objects");
            }
            else report.AppendLine($"island 2 already raised (ground {groundBefore:F2} m); objects not moved");

            // Some towel models had drifted ~20 m from their towel spot (to the waterline); put each back on its spot.
            var towels = GameObject.Find("Environment/BeachTowels_Island2")?.transform;
            if (towels != null)
                foreach (Transform towel in towels)
                foreach (Transform model in towel)
                {
                    if (!model.name.StartsWith("Model_towel")) continue;
                    Vector3 spot = towel.position;
                    if (new Vector2(model.position.x - spot.x, model.position.z - spot.z).magnitude > 1.5f)
                    { model.position = new Vector3(spot.x, model.position.y, spot.z); report.AppendLine("towel back on its spot: " + towel.name); }
                    model.position = new Vector3(model.position.x, BeachHeight(model.position.x, model.position.z) + .02f, model.position.z);
                }

            ApplyRealisticHotel(hotel);
            VerifyRealisticHotel(hotel, terrain);
            CheckHotelEntranceGeometry(hotel, hotel.Find("ResortExterior/CentralTower").GetComponent<LODGroup>());
            BakeNavMeshes(true); AssignSceneIds(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureViews("Screenshots/Review/HotelIsland", new[] {
                ("overview", new Vector3(170, 120, -150), new Vector3(20, 0, -300)),
                ("arrival", new Vector3(-6, 6, -190), new Vector3(20, 14, -250)),
                ("approach", new Vector3(32, 4.2f, -206), new Vector3(20, 8, -240)),
                ("canopy", new Vector3(31, 3.4f, -224), new Vector3(20, 4.5f, -241)),
                ("entrance", new Vector3(21.5f, 3.4f, -229), new Vector3(20, 3.3f, -245)),
                ("lobby", new Vector3(24.5f, 3.5f, -246.2f), new Vector3(13, 2.6f, -255)),
                ("lobby_door", new Vector3(14, 3.5f, -255.5f), new Vector3(25, 2.6f, -246)),
                ("lobby_desk", new Vector3(18.5f, 3.1f, -247.6f), new Vector3(13.5f, 2.6f, -252)),
                ("side", new Vector3(75, 14, -230), new Vector3(20, 18, -250)),
                ("facade", new Vector3(5, 12, -205), new Vector3(8, 16, -244)),
                ("podium", new Vector3(10, 3, -228), new Vector3(14, 1.6f, -238)),
                ("lobby_wide", new Vector3(28, 4.1f, -246), new Vector3(16, 2.7f, -258)),
                ("back_hall", new Vector3(24, 3.7f, -252), new Vector3(25, 2.7f, -261)),
                ("casino_door", new Vector3(16, 3.2f, -247), new Vector3(8, 3.1f, -249.2f)),
                ("casino", new Vector3(6.8f, 3.7f, -247), new Vector3(1.2f, 2.5f, -253.5f)),
                ("casino_table", new Vector3(4.5f, 3.4f, -252), new Vector3(1.2f, 2.7f, -253.5f)),
                ("casino_back", new Vector3(5, 3.7f, -255), new Vector3(2.2f, 3, -262)),
                ("doctor", new Vector3(33.2f, 3.7f, -247), new Vector3(42, 2.7f, -256)),
                ("doctor_back", new Vector3(34, 3.7f, -256), new Vector3(40, 2.7f, -262)) });
            report.AppendLine("PASS: decor cleared (hotel + story kept), polished Tripo hotel with mesh collision, reception open, island-two navigation rebaked, scene saved.");
            File.WriteAllText("Logs/hotel-island-result.txt", report.ToString());
            Debug.Log("[HotelIsland] " + report);
        }

        private static void VerifyRealisticHotel(Transform hotel, GameObject terrain)
        {
            foreach (string anchor in new[] { "ReceptionDesk", "HotelDoor", "PatientPoint", "HospitalBed", "FirstAidShelf", "ShopSpawn" })
                if (hotel.Find(anchor) == null) throw new InvalidOperationException("Reception story anchor missing: " + anchor);
            if (hotel.Find("ResortBeachLife") != null || hotel.Find("ResortExterior").childCount > 8)
                throw new InvalidOperationException("Resort decor still present");
            float floor = hotel.position.y, ground = BeachHeight(hotel.position.x, hotel.position.z);
            if (Mathf.Abs(floor - ground) > .05f) throw new InvalidOperationException($"Hotel floor {floor:F2} not on the ground {ground:F2}");
            if (ground < Island2Plateau - .05f) throw new InvalidOperationException("Island 2 not raised under the hotel");
            if (terrain.GetComponent<MeshCollider>().sharedMesh != terrain.GetComponent<MeshFilter>().sharedMesh)
                throw new InvalidOperationException("Terrain collision/render mismatch");
            // Walk in from the beach: nothing solid between the steps and the reception door.
            Physics.SyncTransforms();
            foreach (float x in new[] { -.6f, 0, .6f })
            {
                // Up the steps, under the porte-cochere, through the door and into the lobby (front wall at z -1).
                Vector3 from = hotel.TransformPoint(new Vector3(x, 1.4f, 14.5f));
                if (Physics.SphereCast(from, .3f, -hotel.forward, out var hit, 16.5f, ~0, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException($"Entrance blocked at x={x} by {hit.collider.name}");
            }
        }

        /// <summary>Writes the scene hierarchy (3 levels, positions, components) to Logs/island2-hierarchy.txt.</summary>
        public static void DumpIsland2Batch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var text = new StringBuilder();
            void Visit(Transform t, int depth)
            {
                if (depth > 3) return;
                text.AppendLine($"{new string(' ', depth * 2)}{t.name} [{t.childCount}] {(OnIsland2(t.position) ? "ISLAND2 " : "")}{t.position:F1} " +
                                string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name).Where(n => n != "Transform")));
                foreach (Transform c in t) Visit(c, depth + 1);
            }
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) Visit(root.transform, 0);
            File.WriteAllText("Logs/island2-hierarchy.txt", text.ToString());
        }

        public static void RealisticHotelIslandBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry point only");
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                RealisticHotelIsland();
                if (Environment.GetCommandLineArgs().Contains("-pddBuild")) { BuildResortPlayer(); File.AppendAllText("Logs/hotel-island-result.txt", "Windows build succeeded.\n"); }
            }
            catch (Exception e) { File.WriteAllText("Logs/hotel-island-result.txt", "FAIL " + e); throw; }
        }
    }
}
