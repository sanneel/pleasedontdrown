using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Dev;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 3, Skull Cove: the pirates' island, ~220 m south of the hotel island (Docs/15-Island-3-Skull-Cove.md).
    /// North: a ridge of rock crags that meets the sea in cliffs. Middle: meadows, a river from a spring and the pirate
    /// village round dirt roads, with the dock on the east coast. South: a cliff plateau with the Pirate King's castle, two
    /// pools and a stream that falls off the south cliff as a waterfall; behind it, at sea level, the cave where the stolen
    /// casino money is hidden (swim in through the falls). Everything is greybox: Tripo models replace it later.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector2 Island3Center = new(20f, -900f);
        // The layout is drawn at its first size and shrunk round the centre: 30%, then 20% more (the user asked for a
        // smaller island without the empty meadows). Things keep their own size: castle, cave, pools, houses, roads,
        // beaches, cliffs.
        private const float Island3Scale = 0.56f;
        private static Vector2 I3(float x, float z) => Island3Center + (new Vector2(x, z) - Island3Center) * Island3Scale;
        private static Vector2[] I3Path(params Vector2[] points) => points.Select(p => I3(p.x, p.y)).ToArray();
        private static Vector3 I3At(float x, float y, float z) { Vector2 p = I3(x, z); return new Vector3(p.x, y, p.y); }
        private static readonly Vector2 Island3HalfSize = new Vector2(170f, 210f) * Island3Scale;
        private const float Island3CornerRadius = 70f * Island3Scale;
        private const float Island3CliffTop = 18f;
        // Island 3 has its own ground mesh (vertex coloured, the cave cut out of it) over this patch of the terrain grid;
        // the shared terrain leaves the patch out but still carries its heights for the seabed (waves).
        private const float Island3MinX = -210f, Island3MaxX = 250f, Island3MinZ = -1160f, Island3MaxZ = -640f;

        private static readonly Vector2 CastleCenter = I3(15f, -1060f) + new Vector2(-12f, 3f); // a little north (clear of the cliff) and west (clear of the pools)
        private static readonly Vector2 CastleHalf = new(34f, 23f);
        private static readonly float CaveX = I3(125f, 0f).x;
        private static readonly float DockZ = I3(0f, -905f).y;
        // The plateau is kept flat round the castle and round the pools, stream and cave.
        private static readonly Vector2 FallsArea = I3(108f, -1080f), FallsAreaHalf = new Vector2(42f, 36f) * Island3Scale;
        private static readonly Vector2 Island3Spring = I3(26f, -822f);
        private static readonly Vector2[] Island3River =
            I3Path(new(26f, -822f), new(18f, -848f), new(-4f, -893f), new(-38f, -930f), new(-85f, -955f), new(-140f, -968f), new(-215f, -978f));
        private static readonly (Vector2 centre, float radius)[] Island3Pools = { (I3(86f, -1050f), 7f), (I3(100f, -1058f), 5.5f) };
        private static readonly Vector2[] Island3Stream = I3Path(new(100f, -1058f), new(114f, -1072f), new(125f, -1088f));
        // The village (the user's island map, 2026-10-11): the main road from the dock crosses a village lane; the lane
        // runs north between the houses and south up the hill to the tavern on the plateau by the waterfall.
        private static readonly Vector2 TavernAt = new(86f, -967f);
        private static readonly Vector2 VillageSquare = I3(150f, -918f);
        private static readonly Vector2[] VillageLaneNorth = { VillageSquare, new(90f, -886f), new(88f, -862f), new(85f, -849f) };
        private static readonly Vector2[] VillageLaneSouth = { VillageSquare, new(89f, -932f), TavernAt + new Vector2(0f, 12f) };
        private static readonly Vector2[][] Island3Roads =
        {
            VillageLaneNorth,
            VillageLaneSouth,
            I3Path(new(200f, -905f), new(150f, -918f), new(110f, -938f), new(68f, -942f), new(40f, -968f), new(26f, -1000f), new(17f, -1028f), new(15f, -1040f)),
            I3Path(new(68f, -942f), new(40f, -925f), new(15f, -930f), new(5f, -952f)),
            I3Path(new(26f, -1000f), new(60f, -1030f), new(86f, -1043f)),
            I3Path(new(110f, -938f), new(85f, -900f), new(60f, -868f), new(34f, -834f)),
        };
        // The pirate houses come in little groups (the yellow spots on the island map).
        private static readonly Vector2[] PirateHouseGroups =
            I3Path(new(20f, -912f), new(77f, -905f), new(125f, -912f), new(5f, -965f), new(35f, -948f), new(88f, -963f), new(130f, -968f));
        // The northern crags: an ellipse of rock.
        private static readonly Vector2 Island3Crags = I3(15f, -752f), Island3CragsHalf = new Vector2(88f, 62f) * Island3Scale;

        // =====================================================================
        // The ground
        // =====================================================================

        private static float Smooth(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        /// <summary>1 inside the box, fading to 0 by <paramref name="fade"/> metres outside it.</summary>
        private static float Near(float x, float z, Vector2 centre, Vector2 half, float fade) =>
            1f - Smooth(0f, fade, BoxDistanceOut(x, z, centre, half, 0f));

        private static float DistanceToLine(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector2 a = line[i], ab = line[i + 1] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        private static float DistanceToRoads(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (Vector2[] road in Island3Roads) best = Mathf.Min(best, DistanceToLine(p, road));
            return best;
        }

        private static Vector2 ClosestOnRoads(Vector2 p)
        {
            Vector2 best = p;
            float bestSq = float.MaxValue;
            foreach (Vector2[] road in Island3Roads)
                for (int i = 0; i < road.Length - 1; i++)
                {
                    Vector2 a = road[i], ab = road[i + 1] - a;
                    Vector2 q = a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    if ((q - p).sqrMagnitude < bestSq) { bestSq = (q - p).sqrMagnitude; best = q; }
                }
            return best;
        }

        /// <summary>Island 3's shore coordinate (4 at the water's edge, growing inland), a lumpy rounded block.</summary>
        private static float Island3Shore(float x, float z, float boxOut)
        {
            float wobble = 6f * Mathf.Sin(x * 0.035f + 0.4f) + 5f * Mathf.Sin(z * 0.028f + 1.1f)
                + (Mathf.PerlinNoise(x * 0.02f + 31f, z * 0.02f + 17f) - 0.5f) * 18f;
            // A calmer line along the south cliffs, so the castle keeps its distance from the edge.
            wobble *= Island3Scale * Mathf.Lerp(1f, 0.35f, Smooth(Island3Center.y - 95f, Island3Center.y - 130f, z));
            return 4f - boxOut + wobble;
        }

        /// <summary>Height above the lowland: the southern plateau (castle, waterfall cliffs) and the northern crags.</summary>
        private static float Island3Highland(float x, float z)
        {
            float flat = 1f - Mathf.Max(Near(x, z, CastleCenter, CastleHalf, 10f), Near(x, z, FallsArea, FallsAreaHalf, 12f));
            // Where the slope up to the plateau starts: 40 m of slope that is done by the castle's north wall.
            float foot = CastleCenter.y + CastleHalf.y + 44f + 4f * Mathf.Sin(x * 0.043f + 2f);
            float plateau = Smooth(foot, foot - 40f, z) *
                (Island3CliffTop + (Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 50f) - 0.5f) * 2.4f * flat);
            float dx = (x - Island3Crags.x) / Island3CragsHalf.x, dz = (z - Island3Crags.y) / Island3CragsHalf.y;
            float m = 1f - (dx * dx + dz * dz);
            if (m <= 0f) return plateau;
            float crags = Mathf.PerlinNoise(x * 0.035f + 9f, z * 0.035f + 3f);
            float ridged = 1f - Mathf.Abs(Mathf.PerlinNoise(x * 0.07f + 2f, z * 0.07f + 8f) * 2f - 1f);
            float mountain = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m * 1.8f)) * (16f + 18f * crags + 9f * ridged);
            return Mathf.Max(plateau, mountain);
        }

        /// <summary>The ground of island 3 (the plateau over the cave too: the hole is cut from the mesh, not the heights).</summary>
        private static float Island3Height(float x, float z)
        {
            float boxOut = BoxDistanceOut(x, z, Island3Center, Island3HalfSize, Island3CornerRadius);
            if (boxOut > 215f) return -9f;
            float s = Island3Shore(x, z, boxOut);
            float h = ProfileHeight(s, x, z, false);

            h += Smooth(6f, 26f, s) * Mathf.PerlinNoise(x * 0.022f + 40f, z * 0.022f + 7f) * 1.6f // meadows roll a little
                 * (1f - Near(x, z, CastleCenter, CastleHalf, 10f));                                  // (not under the castle)
            float high = Island3Highland(x, z);
            h += high * Smooth(0.5f, 4.5f, s);                       // cliffs: full height 4 m in from the water's edge...
            h -= Mathf.Clamp01(high / 8f) * Smooth(4f, -8f, s) * 5f; // ...with deep water at their foot, not a beach
            // The river (lowland only, the sea fills it) and the spring pond it starts from.
            var p = new Vector2(x, z);
            float river = Mathf.Min(DistanceToLine(p, Island3River) - 3.5f, Vector2.Distance(p, Island3Spring) - 8f);
            h = Mathf.Lerp(h, Mathf.Min(h, -1.7f), Smooth(4f, 0f, river) * (1f - Smooth(1f, 3f, high)));
            // On the plateau: two pools and the stream that runs to the waterfall.
            float onTop = Smooth(Island3CliffTop - 6f, Island3CliffTop - 1f, high);
            if (onTop > 0f)
            {
                foreach ((Vector2 centre, float radius) in Island3Pools)
                    h -= 1.1f * onTop * Smooth(radius + 1.5f, radius - 1f, Vector2.Distance(p, centre));
                h -= 0.5f * onTop * Smooth(2.4f, 1.1f, DistanceToLine(p, Island3Stream));
            }
            if (_rawHeight) return h;
            // The waterfall's gorge in the crags, then the level pads under the tavern and the houses.
            h = CragFallsCarve(x, z, h);
            for (int i = 0; i < LevelPads.Length; i++)
            {
                float w = Smooth(LevelPads[i].radius + LevelPads[i].fade, LevelPads[i].radius, Vector2.Distance(p, LevelPads[i].at));
                if (w > 0f) h = Mathf.Lerp(h, PadGround(i), w);
            }
            return h;
        }

        // While true, Island3Height leaves out the carving and levelling below (they need the plain ground first).
        private static bool _rawHeight;

        // Level pads: the ground under the tavern and each house is flattened to the height in its middle, so on the
        // slopes the village sits on terraces. (x, z) centre, flat radius, fade.
        private static (Vector2 at, float radius, float fade)[] _levelPads;
        private static float[] _padGround;

        private static (Vector2 at, float radius, float fade)[] LevelPads
        {
            get
            {
                if (_levelPads != null) return _levelPads;
                var pads = new List<(Vector2, float, float)>();
                if (TripoPrefab(TripoTavern) != null) pads.Add((TavernAt, TavernRadius, 8f));
                foreach ((Vector2 at, float _) in VillageHouses) pads.Add((at, 5.5f, 5f));
                _padGround = Enumerable.Repeat(float.NaN, pads.Count).ToArray();
                return _levelPads = pads.ToArray();
            }
        }

        private static float PadGround(int i)
        {
            if (!float.IsNaN(_padGround[i])) return _padGround[i];
            _rawHeight = true;
            try { _padGround[i] = Mathf.Max(Island3Height(LevelPads[i].at.x, LevelPads[i].at.y), WaterLevel + 0.6f); }
            finally { _rawHeight = false; }
            return _padGround[i];
        }

        // The waterfall in the crags: a gorge cut into the mountain's south side, north of the spring the river starts
        // from. The water drops down its cliff face into the gorge's pool, which is the spring's pond.
        private const float CragFallsHalfWidth = 6f;
        private static Vector2? _cragFalls;
        private static float _cragFallsTop;

        /// <summary>x: the falls' x; y: the z of the gorge's back wall (the cliff face the water drops down).</summary>
        private static Vector2 CragFalls
        {
            get
            {
                if (_cragFalls != null) return _cragFalls.Value;
                float x = Island3Spring.x, z = Island3Spring.y, h = 0f;
                _rawHeight = true;
                try { while (z < Island3Spring.y + 45f && (h = Island3Height(x, z)) < 21f) z += 0.5f; }
                finally { _rawHeight = false; }
                _cragFallsTop = h;
                _cragFalls = new Vector2(x, z - 1f);
                return _cragFalls.Value;
            }
        }

        private static float CragFallsTop { get { _ = CragFalls; return _cragFallsTop; } }

        private static bool InCragFalls(Vector2 p, float margin) =>
            Mathf.Abs(p.x - CragFalls.x) < CragFallsHalfWidth + margin && p.y > Island3Spring.y - margin && p.y < CragFalls.y + margin;

        private static float CragFallsCarve(float x, float z, float h)
        {
            Vector2 f = CragFalls;
            float across = Mathf.Abs(x - f.x);
            if (across > CragFallsHalfWidth + 5f || z > f.y + 0.6f || z < Island3Spring.y - 3f) return h;
            float w = Smooth(CragFallsHalfWidth + 5f, CragFallsHalfWidth, across) * Smooth(f.y + 0.6f, f.y - 0.6f, z);
            return Mathf.Lerp(h, Mathf.Min(h, -1.7f), w);
        }

        private static void BuildCragFalls(Transform root, List<Vector3> taken)
        {
            var falls = new GameObject("CragFalls").transform;
            falls.SetParent(root, false);
            Material water = WaterfallMaterial();
            Vector2 f = CragFalls;
            float top = CragFallsTop + 0.4f, bottom = WaterLevel - 0.3f, drop = top - bottom, face = f.y - 1.4f;
            FlowingSheet(falls, "CragFallsSheet", new Vector3(f.x, bottom + drop * 0.5f, face), Quaternion.identity, new Vector2(7.5f, drop), water, 1.4f, new Vector2(2.5f, drop / 6f));
            FlowingSheet(falls, "CragFallsSheetFront", new Vector3(f.x, bottom + drop * 0.5f, face - 0.45f), Quaternion.identity, new Vector2(6.4f, drop), water, 2.1f, new Vector2(2f, drop / 8f));
            // The stream running down the crag to the lip.
            var up = new Vector3(f.x, BeachHeight(f.x, f.y + 8f) + 0.25f, f.y + 8f);
            var lip = new Vector3(f.x, top, face + 0.1f);
            float tilt = Mathf.Atan2(up.z - lip.z, up.y - lip.y) * Mathf.Rad2Deg;
            FlowingSheet(falls, "CragStream", (up + lip) * 0.5f, Quaternion.Euler(tilt, 0f, 0f), new Vector2(4f, (up - lip).magnitude + 0.6f), water, 0.9f, new Vector2(1.2f, 2f));
            // Big rocks frame the falls at the lip and in the pool.
            var rng = new System.Random(3331);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            foreach (float side in new[] { -1f, 1f })
            {
                SkullCoveRock(falls, new Vector3(f.x + side * (CragFallsHalfWidth + 1.5f), top - 3f, face + 1.5f), new Vector3(R(5f, 7f), R(8f, 11f), R(5f, 7f)), R(0f, 360f), R(-8f, 8f));
                SkullCoveRock(falls, new Vector3(f.x + side * (CragFallsHalfWidth - 0.5f), bottom + 0.4f, face - R(3f, 6f)), new Vector3(R(3f, 4.5f), R(2.5f, 3.5f), R(3f, 4.5f)), R(0f, 360f), R(-10f, 10f));
            }
            // Mist where it lands.
            var mist = new GameObject("CragFallsMist");
            mist.transform.SetParent(falls, false);
            mist.transform.SetPositionAndRotation(new Vector3(f.x, WaterLevel + 0.3f, face - 1.2f), Quaternion.Euler(-90f, 0f, 0f));
            var particles = mist.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
            main.startColor = new Color(1f, 1f, 1f, 0.3f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 30f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(7f, 1.2f, 0.4f);
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            mist.GetComponent<ParticleSystemRenderer>().sharedMaterial = GetSplashMaterial();
            taken.Add(new Vector3(f.x, (Island3Spring.y + f.y) * 0.5f, CragFallsHalfWidth + 4f));
        }

        private static float? _caveFaceZ;

        /// <summary>z of the foot of the south cliff under the waterfall: the cave's mouth (found by walking south at x = CaveX).</summary>
        private static float CaveFaceZ
        {
            get
            {
                if (_caveFaceZ.HasValue) return _caveFaceZ.Value;
                float z = CastleCenter.y;
                while (z > -1200f && Island3Height(CaveX, z) > Island3CliffTop - 1f) z -= 0.25f;
                _caveFaceZ = z - 4f;
                return _caveFaceZ.Value;
            }
        }

        /// <summary>Inside a box in the cave's frame: x from CaveX, z inland from the cliff foot.</summary>
        private static bool InCave(float x, float z, float halfX, float fromZ, float toZ)
        {
            float dz = z - CaveFaceZ;
            return Mathf.Abs(x - CaveX) <= halfX && dz >= fromZ && dz <= toZ;
        }

        /// <summary>The seabed the waves feel: the terrain, but shallow inside the cave so its water stays calm.</summary>
        private static float Island3Seabed(float x, float z) => InCave(x, z, 8f, -4f, 30f) ? -1f : BeachHeight(x, z);

        private static bool InIsland3Patch(float x0, float z0) =>
            x0 >= Island3MinX - 0.01f && x0 + TerrainStep <= Island3MaxX + 0.01f && z0 >= Island3MinZ - 0.01f && z0 + TerrainStep <= Island3MaxZ + 0.01f;

        /// <summary>Regenerates the shared terrain (render, collision and the seabed's height grid) in place.</summary>
        private static void RefreshBeachTerrain(GameObject terrain)
        {
            Mesh mesh = GetBeachMesh();
            terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
            terrain.GetComponent<MeshCollider>().sharedMesh = null;
            terrain.GetComponent<MeshCollider>().sharedMesh = mesh;
            var so = new SerializedObject(terrain.GetComponent<Seabed>());
            Require(so, "_grid").objectReferenceValue = mesh;
            Require(so, "_min").vector2Value = new Vector2(TerrainMinX, TerrainMinZ);
            Require(so, "_step").floatValue = TerrainStep;
            Require(so, "_countX").intValue = Mathf.RoundToInt((TerrainMaxX - TerrainMinX) / TerrainStep) + 1;
            Require(so, "_countZ").intValue = Mathf.RoundToInt((TerrainMaxZ - TerrainMinZ) / TerrainStep) + 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Island 3's ground: the same 2 m grid as the shared terrain (seamless), painted with vertex colours.</summary>
        private static Mesh GetIsland3Mesh()
        {
            const float step = TerrainStep;
            int nx = Mathf.RoundToInt((Island3MaxX - Island3MinX) / step) + 1;
            int nz = Mathf.RoundToInt((Island3MaxZ - Island3MinZ) / step) + 1;
            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = Island3MinX + ix * step, z = Island3MinZ + iz * step;
                    vertices[iz * nx + ix] = new Vector3(x, BeachHeight(x, z), z);
                    uvs[iz * nx + ix] = new Vector2(x, z) * 0.25f;
                }
            var triangles = new List<int>((nx - 1) * (nz - 1) * 6);
            for (int iz = 0; iz < nz - 1; iz++)
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    float x0 = Island3MinX + ix * step, z0 = Island3MinZ + iz * step;
                    // The cave is cut out: its rock shell (BuildMoneyCave) stands in for the cliff and plateau there.
                    if (InCave(x0, z0, 13f, -4f, 34f) && InCave(x0 + step, z0 + step, 13f, -4f, 34f)) continue;
                    int a = iz * nx + ix, b = a + nx;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }

            string dir = MeshDir + "/Island3";
            Directory.CreateDirectory(dir);
            string path = dir + "/Island3Ground.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "Island3Ground" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Vector3[] normals = mesh.normals;
            var colours = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) colours[i] = Island3GroundColour(vertices[i], normals[i]);
            mesh.colors = colours;
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Sand at the water, grass inland, dirt roads and yards, grey rock on steep ground and the crags.</summary>
        private static Color Island3GroundColour(Vector3 v, Vector3 normal)
        {
            Color sand = new(0.92f, 0.82f, 0.6f), wet = new(0.66f, 0.58f, 0.43f), grass = new(0.43f, 0.62f, 0.27f),
                lush = new(0.27f, 0.47f, 0.2f), dirt = new(0.6f, 0.46f, 0.31f), rock = new(0.53f, 0.51f, 0.48f), crag = new(0.38f, 0.36f, 0.35f);
            float x = v.x, z = v.z, h = v.y;
            var p = new Vector2(x, z);
            float s = Island3Shore(x, z, BoxDistanceOut(x, z, Island3Center, Island3HalfSize, Island3CornerRadius));
            Color c = Color.Lerp(sand, wet, Smooth(0.1f, -1.5f, h));
            float green = Smooth(8f, 15f, s) * Smooth(-0.2f, 0.5f, h);
            foreach ((Vector2 centre, float radius) in Island3Pools)
                green *= Smooth(radius - 0.5f, radius + 1f, Vector2.Distance(p, centre)); // pool beds
            c = Color.Lerp(c, Color.Lerp(lush, grass, Mathf.PerlinNoise(x * 0.05f + 3f, z * 0.05f + 9f)), green);
            float bare = Mathf.Max(Smooth(3.4f, 1.8f, DistanceToRoads(p)), Near(x, z, CastleCenter, CastleHalf, 7f));
            c = Color.Lerp(c, dirt, bare * green);
            float rocky = Mathf.Max(Smooth(0.8f, 0.6f, normal.y), Smooth(21f, 25f, h));
            if (InCave(x, z, 18f, -10f, 40f)) rocky = 1f;
            c = Color.Lerp(c, Color.Lerp(rock, crag, Mathf.PerlinNoise(x * 0.08f, z * 0.08f + 20f)), rocky);
            float shade = 0.92f + 0.16f * Mathf.PerlinNoise(x * 0.17f + 50f, z * 0.17f);
            return new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
        }

        /// <summary>The character shader (vertex colours, soft light, shadows) with the rim and backlight glow off.</summary>
        private static Material Island3GroundMaterial()
        {
            Material mat = LoadOrCreateMaterial("Island3Ground", Shader.Find("PleaseDontDrown/Avatar"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Softness", 1f);
            mat.SetFloat("_Rim", 0f);
            mat.SetFloat("_SSS", 0f);
            mat.SetFloat("_Ambient", 0.6f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // =====================================================================
        // The island
        // =====================================================================

        private static void BuildIsland3(Transform env)
        {
            var root = new GameObject("Island3").transform;
            root.SetParent(env, false);
            var ground = new GameObject("Island3Ground");
            ground.transform.SetParent(root, false);
            Mesh mesh = GetIsland3Mesh();
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = Island3GroundMaterial();
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;

            var taken = new List<Vector3>(); // (x, z, radius) already used: houses and the dock, then palms keep clear
            BuildSkullCoveRocks(root);
            BuildTavern(root, taken);
            BuildPirateVillage(root, taken);
            BuildPirateCastle(root);
            BuildMoneyCave(root);
            BuildPlateauWater(root);
            BuildCragFalls(root, taken);
            Transform arrival = BuildPirateDock(root, taken);
            BuildIsland3Jungle(root, taken);
            BuildIsland3Palms(root, taken);
            var dev = Object.FindFirstObjectByType<DevIsland>();
            if (dev != null) SetRef(dev, "_pirate", arrival);
        }

        /// <summary>A boulder (the coast rock model, 1 m across) stretched to <paramref name="size"/>, solid to its mesh.</summary>
        private static void SkullCoveRock(Transform parent, Vector3 centre, Vector3 size, float yaw, float tilt, bool localSpace = false)
        {
            var rock = new GameObject("Rock").transform;
            rock.SetParent(parent, false);
            if (localSpace)
                rock.SetLocalPositionAndRotation(centre, Quaternion.Euler(tilt, yaw, tilt * 0.5f));
            else
                rock.SetPositionAndRotation(centre, Quaternion.Euler(tilt, yaw, tilt * 0.5f));
            rock.localScale = size;
            TagSurface(rock.gameObject, SurfaceKind.Rock);
            GameObject model = PropModel("rock", rock);
            if (model == null)
            {
                Primitive(PrimitiveType.Sphere, "Boulder", rock, Vector3.zero, Vector3.one, GetMaterial("Rock", new Color(0.5f, 0.5f, 0.52f), smoothness: 0.1f));
                return;
            }
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        private static void BuildSkullCoveRocks(Transform root)
        {
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(root, false);
            var rng = new System.Random(3301);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Ridge(float x, float z)
            {
                float dx = (x - Island3Crags.x) / Island3CragsHalf.x, dz = (z - Island3Crags.y) / Island3CragsHalf.y;
                return 1f - (dx * dx + dz * dz);
            }

            // The northern crags: big boulders heaped over the ridge, like the rocks along the coasts.
            for (int i = 0, placed = 0; i < 800 && placed < 45; i++)
            {
                float x = Island3Crags.x + R(-1f, 1f) * Island3CragsHalf.x, z = Island3Crags.y + R(-1f, 1f) * Island3CragsHalf.y, m = Ridge(x, z);
                if (m < 0.08f || InCragFalls(new Vector2(x, z), 9f)) continue;
                Vector3 size = new(R(7f, 16f), R(5f, 12f) * (0.7f + m), R(7f, 16f));
                SkullCoveRock(rocks, new Vector3(x, BeachHeight(x, z) - size.y * 0.25f, z), size, R(0f, 360f), R(-12f, 12f));
                placed++;
            }
            for (int i = 0, placed = 0; i < 400 && placed < 6; i++)
            {
                float x = Island3Crags.x + R(-0.8f, 0.8f) * Island3CragsHalf.x, z = Island3Crags.y + R(-0.8f, 0.8f) * Island3CragsHalf.y;
                if (Ridge(x, z) < 0.35f || InCragFalls(new Vector2(x, z), 9f)) continue;
                Vector3 size = new(R(5f, 8f), R(16f, 26f), R(5f, 8f));
                if (!TripoPinnacle(rocks, new Vector3(x, BeachHeight(x, z) - 1.5f, z), size.x * 1.4f, size.y, R(0f, 360f)))
                    SkullCoveRock(rocks, new Vector3(x, BeachHeight(x, z) + size.y * 0.25f, z), size, R(0f, 360f), R(-6f, 6f));
                placed++;
            }

            BuildRiprapCoast(rocks);
            // Sea stacks off the corners, as on the map.
            foreach ((Vector2 p, float height) in new[] { (I3(-125f, -1140f), 22f), (I3(175f, -1125f), 17f), (I3(-175f, -712f), 15f) })
            {
                float bottom = BeachHeight(p.x, p.y) - 0.5f;
                if (!TripoPinnacle(rocks, new Vector3(p.x, bottom, p.y), 13f, height - bottom, R(0f, 360f)))
                    SkullCoveRock(rocks, new Vector3(p.x, (bottom + height) * 0.5f, p.y), new Vector3(9f, height - bottom, 8f), R(0f, 360f), 0f);
            }
        }

        // =====================================================================
        // The coast: a breakwater of big angular boulders all round
        // =====================================================================

        /// <summary>
        /// Rough angular boulder shapes (an icosahedron split once, corners pushed in and out, flattened a little): the
        /// broken granite blocks of a breakwater. Radius about 1; the bounds are returned so they can be fitted exactly.
        /// </summary>
        private static (Vector3[] vertices, int[] triangles, Bounds bounds)[] RiprapShapes(int count, int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            Vector3[] ico =
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var shapes = new (Vector3[], int[], Bounds)[count];
            for (int s = 0; s < count; s++)
            {
                var vertices = new List<Vector3>();
                foreach (Vector3 v in ico) vertices.Add(v.normalized * R(0.78f, 1.18f)); // big facets: corners in and out
                var midpoints = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = Math.Min(a, b) * 1000L + Math.Max(a, b);
                    if (midpoints.TryGetValue(key, out int i)) return i;
                    Vector3 m = (vertices[a] + vertices[b]) * 0.5f;
                    vertices.Add(m * R(0.97f, 1.07f));
                    return midpoints[key] = vertices.Count - 1;
                }
                var triangles = new List<int>();
                for (int f = 0; f < faces.Length; f += 3)
                {
                    int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    triangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                // Blocks, not balls: flatten a bit and stretch one way.
                var squash = new Vector3(R(0.85f, 1.25f), R(0.6f, 0.85f), R(0.85f, 1.25f));
                var bounds = new Bounds();
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 v = Vector3.Scale(vertices[i], squash);
                    if (v.y < -0.35f) v.y = Mathf.Lerp(v.y, -0.35f, 0.6f); // a flatter underside to sit on
                    vertices[i] = v;
                    if (i == 0) bounds = new Bounds(v, Vector3.zero); else bounds.Encapsulate(v);
                }
                shapes[s] = (vertices.ToArray(), triangles.ToArray(), bounds);
            }
            return shapes;
        }

        /// <summary>
        /// The whole coast lined with big boulders like a breakwater (grey granite, dark slate, rusty tan, darker where
        /// wet): one row half in the water, one on the edge, a looser row behind and some loose blocks out in the shallows.
        /// Under the cliffs only the water rows. Gaps for the dock, the river mouth and the cave behind the waterfall.
        /// Merged into one mesh (and collider) per ~90 m of coast.
        /// </summary>
        private static void BuildRiprapCoast(Transform parent)
        {
            // The coastline: from far out, walk in toward the middle until the ground is above the water.
            var ring = new List<Vector2>();
            for (int i = 0; i < 2400; i++)
            {
                float a = i * Mathf.PI * 2f / 2400f;
                var ray = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p = Island3Center + ray * 320f;
                for (int k = 0; k < 1200 && Island3Height(p.x, p.y) <= WaterLevel; k++) p -= ray * 0.25f;
                ring.Add(p);
            }
            // Even spacing along it.
            const float spacing = 2.3f;
            var coast = new List<(Vector2 p, Vector2 outward)>();
            float carried = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i], b = ring[(i + 1) % ring.Count];
                float length = Vector2.Distance(a, b);
                if (length > 25f) continue; // a jump across a bay or the river mouth
                for (float d = spacing - carried; d <= length; d += spacing)
                {
                    Vector2 p = Vector2.Lerp(a, b, d / length);
                    Vector2 along = (ring[(i + 2) % ring.Count] - ring[(i - 1 + ring.Count) % ring.Count]).normalized;
                    coast.Add((p, new Vector2(along.y, -along.x)));
                }
                carried = (carried + length) % spacing;
            }

            var shapes = RiprapShapes(12, 3321);
            var rng = new System.Random(3323);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Color[] stone =
            {
                new(0.52f, 0.52f, 0.53f), new(0.34f, 0.35f, 0.37f), new(0.74f, 0.62f, 0.47f),
                new(0.7f, 0.5f, 0.34f), new(0.5f, 0.53f, 0.47f), new(0.6f, 0.58f, 0.55f)
            };
            Color rust = new(0.72f, 0.48f, 0.3f);
            Material material = Island3GroundMaterial(); // vertex colours, like the ground
            string dir = MeshDir + "/Island3";
            Directory.CreateDirectory(dir);

            var vertices = new List<Vector3>();
            var colours = new List<Color>();
            var triangles = new List<int>();
            Vector3 origin = Vector3.zero;
            int chunk = 0;

            void Flush()
            {
                if (triangles.Count == 0) return;
                string path = $"{dir}/Riprap_{chunk:00}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool isNew = mesh == null;
                if (isNew) mesh = new Mesh { name = $"Riprap_{chunk:00}" };
                mesh.Clear();
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetColors(colours);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (isNew) AssetDatabase.CreateAsset(mesh, path);
                EditorUtility.SetDirty(mesh);
                var go = new GameObject($"Riprap_{chunk:00}");
                go.transform.SetParent(parent, false);
                go.transform.position = origin;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                TagSurface(go, SurfaceKind.Rock);
                vertices.Clear(); colours.Clear(); triangles.Clear();
                chunk++;
            }

            // One boulder from <paramref name="bottom"/> up to <paramref name="top"/>, about <paramref name="width"/> across.
            void Boulder(Vector2 at, float bottom, float top, float width, float depth)
            {
                var (shapeVerts, shapeTris, b) = shapes[rng.Next(shapes.Length)];
                float height = Mathf.Max(0.6f, top - bottom);
                var scale = new Vector3(width / b.size.x, height / b.size.y, depth / b.size.z);
                var rotation = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
                var centre = new Vector3(at.x, bottom - b.min.y * scale.y, at.y);
                Color body = stone[rng.Next(stone.Length)];
                float rusty = rng.NextDouble() < 0.45 ? R(0.3f, 0.8f) : 0f;
                float seed = R(0f, 100f);
                for (int i = 0; i < shapeTris.Length; i += 3)
                {
                    int k = vertices.Count;
                    Vector3 sum = Vector3.zero;
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 w = centre + rotation * Vector3.Scale(shapeVerts[shapeTris[i + j]], scale);
                        sum += w;
                        vertices.Add(w - origin);
                    }
                    Vector3 mid = sum / 3f;
                    // Rust patches, a little variation per facet, darker where the sea wets it.
                    float patch = Smooth(0.5f, 0.72f, Mathf.PerlinNoise(mid.x * 0.7f + seed, mid.z * 0.7f + mid.y * 0.7f)) * rusty;
                    Color c = Color.Lerp(body, rust, patch) * R(0.88f, 1.08f);
                    if (mid.y < WaterLevel + 0.35f) c *= 0.72f;
                    c.a = 1f;
                    colours.Add(c); colours.Add(c); colours.Add(c);
                    triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + 2);
                }
            }

            int inChunk = 0;
            foreach ((Vector2 p, Vector2 outward) in coast)
            {
                if (p.x > Island3Center.x + 50f && Mathf.Abs(p.y - DockZ) < 14f) continue;  // landing by the dock
                if (Mathf.Abs(p.x - CaveX) < 17f && p.y < CaveFaceZ + 20f) continue;         // the cave behind the falls
                if (DistanceToLine(p, Island3River) < 9f) continue;                        // the river mouth
                if (inChunk == 0) origin = new Vector3(p.x, 0f, p.y);
                Vector2 sideways = new(-outward.y, outward.x);
                bool cliff = Island3Height(p.x - outward.x * 6f, p.y - outward.y * 6f) > 4f;
                Vector2 Jitter(float outFrom, float outTo) => p + outward * R(outFrom, outTo) + sideways * R(-0.8f, 0.8f);
                float Ground(Vector2 q) => Island3Height(q.x, q.y);

                // Half in the water along the edge.
                Vector2 a = Jitter(0.4f, 2.2f);
                float width = cliff ? R(3f, 5f) : R(2.2f, 4f);
                Boulder(a, Ground(a) - 0.4f, WaterLevel + R(0.7f, 1.8f), width, width * R(0.8f, 1.2f));
                // Loose blocks out in the shallows (bigger under the cliffs).
                if (rng.NextDouble() < (cliff ? 0.7 : 0.4))
                {
                    Vector2 d = Jitter(2.6f, 5f);
                    width = cliff ? R(2.5f, 4.5f) : R(1.6f, 3f);
                    Boulder(d, Ground(d) - 0.4f, WaterLevel + R(-0.3f, 1.2f), width, width * R(0.8f, 1.2f));
                }
                if (!cliff)
                {
                    // On the edge, up on the beach.
                    Vector2 b = Jitter(-1.6f, 0f);
                    width = R(2f, 4.2f);
                    float ground = Ground(b);
                    Boulder(b, ground - 0.4f, ground + R(1.3f, 2.4f), width, width * R(0.8f, 1.2f));
                    // A looser row behind.
                    if (rng.NextDouble() < 0.6)
                    {
                        Vector2 c = Jitter(-4.5f, -2.2f);
                        width = R(1.4f, 3f);
                        ground = Ground(c);
                        Boulder(c, ground - 0.3f, ground + R(0.8f, 1.8f), width, width * R(0.8f, 1.2f));
                    }
                }
                if (++inChunk >= 40) { Flush(); inChunk = 0; }
            }
            Flush();
        }

        // =====================================================================
        // Tripo models (ArtSource/Tools/tripo_prepare_island3.py: front = +z, real size, feet at the origin)
        // =====================================================================

        private const string TripoIsland3Dir = "Assets/_Game/Art/Tripo/Island3";
        private static readonly string[] TripoShacks = { "pirate_shack_thatch", "pirate_shack_tarp", "pirate_shack_shingle" };

        private static GameObject TripoPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoIsland3Dir}/{name}.glb");

        /// <summary>A Tripo model placed by its feet (null if the model isn't in the project: the greybox stays).</summary>
        private static GameObject TripoModel(string name, Transform parent, Vector3 feet, float yaw, Vector3 scale)
        {
            GameObject prefab = TripoPrefab(name);
            if (prefab == null) return null;
            GameObject go = Object.Instantiate(prefab, parent, false);
            go.name = "Tripo_" + name;
            go.transform.localPosition = feet;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            return go;
        }

        /// <summary>Bounds of everything drawn under <paramref name="root"/>, in <paramref name="space"/>'s local frame.</summary>
        private static Bounds TripoBounds(Transform root, Transform space)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 m = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }

        /// <summary>The Tripo rock spire (20 m tall, ~20 across at the foot) stretched to size, with a capsule to bump into.</summary>
        private static bool TripoPinnacle(Transform parent, Vector3 foot, float width, float height, float yaw)
        {
            if (TripoPrefab("sea_rock_pinnacle") == null) return false;
            var spire = new GameObject("RockSpire").transform;
            spire.SetParent(parent, false);
            spire.position = foot;
            TagSurface(spire.gameObject, SurfaceKind.Rock);
            TripoModel("sea_rock_pinnacle", spire, Vector3.zero, yaw, new Vector3(width / 20f, height / 20f, width / 20f));
            var capsule = spire.gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = width * 0.3f;
            capsule.height = height;
            capsule.center = Vector3.up * height * 0.5f;
            return true;
        }

        /// <summary>
        /// A walk-in Tripo building (a shack, or the tavern): the model with its own stilts or base, porch and roof, a
        /// plank room inside it, a solid porch and a ramp over its steps. <paramref name="half"/>: half its footprint.
        /// </summary>
        private static Transform TripoHouse(Transform parent, string name, Vector2 at, float yaw, float scale, string shack, Vector2 half = default)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            if (half == default) half = new Vector2(3.2f, 3.6f);
            float top = float.MinValue, low = float.MaxValue;
            foreach (float cx in new[] { -half.x * scale, 0f, half.x * scale })
                foreach (float cz in new[] { -half.y * scale, 0f, half.y * scale })
                {
                    Vector3 p = new Vector3(at.x, 0f, at.y) + turn * new Vector3(cx, 0f, cz);
                    float h = BeachHeight(p.x, p.z);
                    top = Mathf.Max(top, h);
                    low = Mathf.Min(low, h);
                }
            var house = new GameObject(name).transform;
            house.SetParent(parent, false);
            // The stilts stand halfway between the high and the low ground under the house, a little sunk in.
            house.SetPositionAndRotation(new Vector3(at.x, Mathf.Max((top + low) * 0.5f, WaterLevel + 0.3f) - 0.15f, at.y), turn);
            GameObject model = TripoModel(shack, house, Vector3.zero, 0f, Vector3.one * scale);
            Bounds bounds = TripoBounds(model.transform, house);

            // Walk-in: the model's door and inside are cut away (tripo_prepare_island3.py); a plank room stands in there.
            // Its walls are solid and its outside is hidden inside the model's walls.
            ShackRoom r = TripoShackRoom(shack);
            float x0 = r.x0 * scale, x1 = r.x1 * scale, z0 = r.z0 * scale, z1 = r.z1 * scale, floor = r.floor * scale, ceiling = r.top * scale;
            float rcx = (x0 + x1) * 0.5f, rcz = (z0 + z1) * 0.5f, wx = x1 - x0, wz = z1 - z0, rh = ceiling - floor;
            const float t = 0.12f;
            float door = r.doorWidth * scale, dx = r.doorX * scale;
            float doorTop = floor + r.doorHeight * scale;
            bool tavern = shack == TripoTavern;
            Material planks = GetMaterial("ShackPlanks", new Color(0.55f, 0.39f, 0.25f));
            Material boards = GetMaterial("ShackFloorboards", new Color(0.42f, 0.29f, 0.18f));
            ResortBox(house, "RoomFloor", new Vector3(rcx, floor - 0.1f, rcz), new Vector3(wx, 0.2f, wz), boards);
            ResortBox(house, "RoomCeiling", new Vector3(rcx, ceiling + 0.05f, rcz), new Vector3(wx, 0.1f, wz), planks);
            ResortBox(house, "RoomBack", new Vector3(rcx, floor + rh * 0.5f, z0 + t * 0.5f), new Vector3(wx, rh, t), planks);
            ResortBox(house, "RoomSide", new Vector3(x0 + t * 0.5f, floor + rh * 0.5f, rcz), new Vector3(t, rh, wz), planks);
            ResortBox(house, "RoomSide", new Vector3(x1 - t * 0.5f, floor + rh * 0.5f, rcz), new Vector3(t, rh, wz), planks);
            float doorL = dx - door * 0.5f, doorR = dx + door * 0.5f;
            ResortBox(house, "RoomFront", new Vector3((x0 + doorL) * 0.5f, floor + rh * 0.5f, z1 - t * 0.5f), new Vector3(doorL - x0, rh, t), planks);
            ResortBox(house, "RoomFront", new Vector3((x1 + doorR) * 0.5f, floor + rh * 0.5f, z1 - t * 0.5f), new Vector3(x1 - doorR, rh, t), planks);
            if (ceiling - doorTop > 0.05f)
                ResortBox(house, "RoomLintel", new Vector3(dx, (doorTop + ceiling) * 0.5f, z1 - t * 0.5f), new Vector3(door, ceiling - doorTop, t), planks);
            // Inside: board-by-board plank walls and floor in mixed shades, ceiling beams, a rug, a table with a lantern,
            // and the pirates' loot (a money sack and coins) with a crate, all clear of the way in from the door.
            Material trim = GetMaterial("PirateTrim", new Color(0.16f, 0.11f, 0.08f));
            Material rug = GetMaterial("ShackRug", new Color(0.55f, 0.12f, 0.1f));
            Material furniture = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material lamp = GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f));
            Material[] boardShades =
            {
                GetMaterial("ShackBoardA", new Color(0.5f, 0.34f, 0.2f)),
                GetMaterial("ShackBoardB", new Color(0.44f, 0.29f, 0.17f)),
                GetMaterial("ShackBoardC", new Color(0.56f, 0.4f, 0.25f)),
                GetMaterial("ShackBoardD", new Color(0.38f, 0.25f, 0.15f))
            };
            const float board = 0.26f, gap = 0.025f;
            int shade = shack.Length;
            void Boards(Vector3 from, Vector3 along, float length, Vector3 inward, Vector3 up, float height, string part)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(length / board));
                float each = length / count;
                for (int i = 0; i < count; i++)
                {
                    Vector3 centre = from + along * (each * (i + 0.5f)) + up * (height * 0.5f) + inward * 0.012f;
                    Vector3 size = Abs(along * (each - gap)) + Abs(up * height) + Abs(inward * 0.024f);
                    ResortBox(house, part, centre, size, boardShades[(shade += 3) % boardShades.Length], false);
                }
            }
            static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            float wallH = rh - 0.02f, inX0 = x0 + t, inX1 = x1 - t, inZ0 = z0 + t, inZ1 = z1 - t;
            Boards(new Vector3(inX0, floor, inZ0), Vector3.right, inX1 - inX0, Vector3.forward, Vector3.up, wallH, "WallBoard");
            Boards(new Vector3(inX0, floor, inZ0), Vector3.forward, inZ1 - inZ0, Vector3.right, Vector3.up, wallH, "WallBoard");
            Boards(new Vector3(inX1, floor, inZ0), Vector3.forward, inZ1 - inZ0, Vector3.left, Vector3.up, wallH, "WallBoard");
            Boards(new Vector3(inX0, floor, inZ1), Vector3.right, doorL - inX0, Vector3.back, Vector3.up, wallH, "WallBoard");
            Boards(new Vector3(doorR, floor, inZ1), Vector3.right, inX1 - doorR, Vector3.back, Vector3.up, wallH, "WallBoard");
            Boards(new Vector3(inX0, floor, inZ0), Vector3.right, inX1 - inX0, Vector3.up, Vector3.forward, inZ1 - inZ0, "FloorBoard");
            // Dark corner posts, a skirting rail and ceiling beams frame the boards.
            foreach (float px in new[] { inX0 + 0.06f, inX1 - 0.06f })
                foreach (float pz in new[] { inZ0 + 0.06f, inZ1 - 0.06f })
                    ResortBox(house, "RoomPost", new Vector3(px, floor + rh * 0.5f, pz), new Vector3(0.14f, rh, 0.14f), trim, false);
            if (tavern) TavernBar(house, inX0, inX1, inZ0, inZ1, floor, dx, trim, furniture, lamp);
            else
            {
                ResortBox(house, "RoomRail", new Vector3(rcx, floor + 1.05f, inZ0 + 0.04f), new Vector3(inX1 - inX0, 0.08f, 0.05f), trim, false);
                for (int i = 1; i <= 3; i++)
                    ResortBox(house, "RoomBeam", new Vector3(rcx, ceiling - 0.09f, inZ0 + (inZ1 - inZ0) * i / 4f), new Vector3(inX1 - inX0, 0.16f, 0.16f), trim, false);
                ResortBox(house, "Rug", new Vector3(0f, floor + 0.035f, rcz), new Vector3(Mathf.Min(1.6f, wx - 1f), 0.02f, Mathf.Min(1.2f, wz - 0.8f)), rug, false);
                Vector3 tableAt = new(x1 - 0.85f, floor, z0 + 0.75f);
                ResortBox(house, "TableTop", tableAt + Vector3.up * 0.76f, new Vector3(1.1f, 0.07f, 0.75f), furniture);
                foreach (float lx in new[] { -0.45f, 0.45f })
                    foreach (float lz in new[] { -0.28f, 0.28f })
                        ResortBox(house, "TableLeg", tableAt + new Vector3(lx, 0.37f, lz), new Vector3(0.07f, 0.74f, 0.07f), furniture, false);
                ResortBox(house, "TableLantern", tableAt + new Vector3(0.25f, 0.92f, 0f), new Vector3(0.16f, 0.24f, 0.16f), lamp, false);
                ResortBox(house, "TableLanternTop", tableAt + new Vector3(0.25f, 1.06f, 0f), new Vector3(0.2f, 0.04f, 0.2f), trim, false);
                TripoModel("casino_chip_stacks", house, tableAt + new Vector3(-0.25f, 0.795f, 0f), 30f, Vector3.one * 0.6f);
                TripoModel("money_sack", house, new Vector3(x0 + 0.55f, floor, z0 + 0.6f), 140f, Vector3.one * 0.8f);
                TripoModel("gold_coin_pile", house, new Vector3(x0 + 0.65f, floor, z0 + 1.35f), 70f, Vector3.one * 0.3f);
                PropModel("crate", house, new Vector3(x0 + 0.55f, floor + 0.36f, Mathf.Min(rcz + 0.9f, inZ1 - 0.5f)), 25f, 1.2f);
            }
            if (!tavern) ShackDoor(house, name + "_Door", new Vector3(doorL, floor, z1 - t), door, doorTop - floor);
            foreach (float lx in tavern ? new[] { x0 + wx * 0.25f, x0 + wx * 0.75f } : new[] { rcx })
                foreach (float lz in tavern ? new[] { z0 + wz * 0.3f, z0 + wz * 0.72f } : new[] { rcz })
                {
                    var light = new GameObject("RoomLight").AddComponent<Light>();
                    light.transform.SetParent(house, false);
                    light.transform.localPosition = new Vector3(lx, ceiling - 0.4f, lz);
                    light.type = LightType.Point;
                    light.range = tavern ? 7f : 5f;
                    light.intensity = 1.5f;
                    light.color = new Color(1f, 0.75f, 0.45f);
                    light.shadows = LightShadows.None;
                }

            // The porch in front of the door is solid at floor height, and the model's steps get a ramp to walk up.
            float porchEnd = r.porchZ > 0f ? r.porchZ * scale : bounds.max.z - 0.15f;
            if (porchEnd - z1 > 0.2f)
                Collider(house, "Porch", new Vector3(rcx, floor - 0.1f, (z1 + porchEnd) * 0.5f), new Vector3(wx, 0.2f, porchEnd - z1));
            Vector3 foot = house.TransformPoint(new Vector3(dx, 0f, porchEnd + 1.2f));
            float ground = BeachHeight(foot.x, foot.z) - house.position.y;
            float rise = floor - ground;
            if (rise > 0.15f)
            {
                float run = Mathf.Max(1f, rise * 1.5f); // about 34 degrees
                float length = Mathf.Sqrt(rise * rise + run * run);
                Collider(house, "Steps", new Vector3(dx, ground + rise * 0.5f - 0.1f, porchEnd + run * 0.5f), new Vector3(tavern ? 3f : 1.6f, 0.2f, length),
                    Quaternion.Euler(Mathf.Atan2(rise, run) * Mathf.Rad2Deg, 0f, 0f));
            }
            return house;
        }

        /// <summary>
        /// A real door in a shack's doorway (Interact to open/close, networked like the island 1 doors): plank leaf with
        /// braces and a brass knob, hinged on its left at <paramref name="hinge"/> (the inside face of the front wall).
        /// </summary>
        private static void ShackDoor(Transform house, string name, Vector3 hinge, float width, float height)
        {
            Material leafMat = GetMaterial("PirateDoorLeaf", new Color(0.42f, 0.27f, 0.15f));
            Material boards = GetMaterial("PirateDoorBoards", new Color(0.3f, 0.19f, 0.1f));
            Material iron = GetMaterial("PirateDoorIron", new Color(0.12f, 0.11f, 0.1f), metallic: 0.6f, smoothness: 0.4f);
            Material brass = GetMaterial("Brass", new Color(0.85f, 0.65f, 0.25f), metallic: 0.8f, smoothness: 0.7f);
            float w = width - 0.04f, h = height - 0.03f;
            const float thick = 0.07f;
            var root = new GameObject(name);
            root.transform.SetParent(house, false);
            root.transform.localPosition = hinge + new Vector3(0.02f, 0.01f, thick * 0.5f + 0.02f);
            var pivot = new GameObject("Hinge").transform;
            pivot.SetParent(root.transform, false);
            GameObject leaf = Primitive(PrimitiveType.Cube, "Leaf", pivot, new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(w, h, thick), leafMat);
            TagSurface(leaf, SurfaceKind.Wood);
            var outline = new List<Renderer> { leaf.GetComponent<Renderer>() };
            for (int i = 1; i < 5; i++)
                foreach (float side in new[] { 1f, -1f })
                    outline.Add(Primitive(PrimitiveType.Cube, "Groove", pivot, new Vector3(w * i / 5f, h * 0.5f, side * (thick * 0.5f + 0.004f)),
                        new Vector3(0.02f, h - 0.04f, 0.01f), boards, keepCollider: false).GetComponent<Renderer>());
            foreach (float y in new[] { 0.18f, 0.82f })
                foreach (float side in new[] { 1f, -1f })
                {
                    outline.Add(Primitive(PrimitiveType.Cube, "Brace", pivot, new Vector3(w * 0.5f, h * y, side * (thick * 0.5f + 0.015f)),
                        new Vector3(w - 0.08f, 0.13f, 0.03f), boards, keepCollider: false).GetComponent<Renderer>());
                    outline.Add(Primitive(PrimitiveType.Cube, "Strap", pivot, new Vector3(0.18f, h * y, side * (thick * 0.5f + 0.035f)),
                        new Vector3(0.36f, 0.06f, 0.012f), iron, keepCollider: false).GetComponent<Renderer>());
                }
            GameObject diagonal = Primitive(PrimitiveType.Cube, "Diagonal", pivot, new Vector3(w * 0.5f, h * 0.5f, thick * 0.5f + 0.015f), new Vector3(0.11f, h * 0.68f, 0.03f), boards, keepCollider: false);
            diagonal.transform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(w - 0.2f, h * 0.64f) * Mathf.Rad2Deg);
            outline.Add(diagonal.GetComponent<Renderer>());
            foreach (float side in new[] { 1f, -1f })
                outline.Add(Primitive(PrimitiveType.Sphere, "Knob", pivot, new Vector3(w - 0.11f, h * 0.47f, side * (thick * 0.5f + 0.05f)),
                    Vector3.one * 0.07f, brass, keepCollider: false).GetComponent<Renderer>());
            root.AddComponent<FishNet.Object.NetworkObject>();
            var door = root.AddComponent<Door>();
            SetRef(door, "_hinge", pivot);
            SetRef(door, "_audio", SpatialAudio(root, 2f, 25f));
            ConfigureInteractable(root.AddComponent<PleaseDontDrown.Interaction.Interactable>(), new[] { leaf.GetComponent<Collider>() }, outline.ToArray(), 2.8f);
        }

        // The room inside each shack, measured by tripo_prepare_island3.py (shack_rooms.json, Unity frame: front is +z):
        // x and z extents, floor and ceiling height, on the 6 m wide model.
        [Serializable] private class ShackRoom { public string name; public float x0, x1, z0, z1, floor, top, doorX, doorWidth, doorHeight, porchZ; }
        [Serializable] private class ShackRoomList { public ShackRoom[] rooms; }

        private static ShackRoom TripoShackRoom(string shack) =>
            JsonUtility.FromJson<ShackRoomList>(File.ReadAllText(TripoIsland3Dir + "/shack_rooms.json")).rooms.First(r => r.name == shack);

        // =====================================================================
        // The tavern: the pirates' big bar, the size of four houses (2 x 2)
        // =====================================================================

        private const string TripoTavern = "pirate_tavern";
        private static readonly Vector2 TavernHalf = new(8.7f, 11f); // the model's footprint, porch and steps included
        private const float TavernRadius = 13f;
        private static (Vector2 at, float yaw)? _tavernSpot;

        /// <summary>The tavern stands on the plateau by the waterfall, its steps north down the lane into the village.</summary>
        private static (Vector2 at, float yaw) TavernSpot => (TavernAt, 0f);

        private static void BuildTavern(Transform root, List<Vector3> taken)
        {
            if (TripoPrefab(TripoTavern) == null) return;
            (Vector2 at, float yaw) = TavernSpot;
            Transform tavern = TripoHouse(root, "PirateTavern", at, yaw, 1f, TripoTavern, TavernHalf);
            TagSurface(tavern.gameObject, SurfaceKind.Wood);
            taken.Add(new Vector3(at.x, at.y, TavernRadius));
        }

        /// <summary>
        /// The bar room: a long counter across the back with stools, shelves of bottles and kegs behind it, round tables
        /// with stools in the front half, barrels in the front corners. The way in from the door to the middle stays clear.
        /// </summary>
        private static void TavernBar(Transform house, float x0, float x1, float z0, float z1, float floor, float doorX,
            Material trim, Material wood, Material lamp)
        {
            Material counterTop = GetMaterial("TavernCounterTop", new Color(0.24f, 0.14f, 0.08f), smoothness: 0.35f);
            Material barrel = GetMaterial("TavernBarrel", new Color(0.46f, 0.3f, 0.16f));
            Material hoop = GetMaterial("PirateDoorIron", new Color(0.12f, 0.11f, 0.1f), metallic: 0.6f, smoothness: 0.4f);
            Material seat = GetMaterial("TavernStool", new Color(0.36f, 0.22f, 0.12f));
            Material[] bottles =
            {
                GetMaterial("BottleGreen", new Color(0.15f, 0.45f, 0.2f), smoothness: 0.8f),
                GetMaterial("BottleAmber", new Color(0.65f, 0.35f, 0.08f), smoothness: 0.8f),
                GetMaterial("BottleRed", new Color(0.55f, 0.1f, 0.12f), smoothness: 0.8f),
                GetMaterial("BottleBlue", new Color(0.15f, 0.3f, 0.6f), smoothness: 0.8f)
            };
            float wx = x1 - x0, cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

            // The counter: 1.1 m high, across the back, with a gap at one end to walk behind it.
            float counterZ = z0 + 2.2f, counterL = x0 + 1.4f, counterR = x1 - 0.4f, counterX = (counterL + counterR) * 0.5f;
            ResortBox(house, "BarCounter", new Vector3(counterX, floor + 0.5f, counterZ), new Vector3(counterR - counterL, 1f, 0.6f), wood);
            ResortBox(house, "BarTop", new Vector3(counterX, floor + 1.05f, counterZ + 0.05f), new Vector3(counterR - counterL + 0.1f, 0.1f, 0.8f), counterTop, false);
            ResortBox(house, "BarFootRail", new Vector3(counterX, floor + 0.2f, counterZ + 0.42f), new Vector3(counterR - counterL, 0.06f, 0.06f), hoop, false);
            for (float x = counterL + 0.5f; x < counterR - 0.3f; x += 1.1f)
            {
                Primitive(PrimitiveType.Cylinder, "BarStoolLeg", house, new Vector3(x, floor + 0.38f, counterZ + 0.85f), new Vector3(0.12f, 0.38f, 0.12f), seat, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "BarStool", house, new Vector3(x, floor + 0.78f, counterZ + 0.85f), new Vector3(0.42f, 0.04f, 0.42f), seat);
            }
            // Behind it: shelves of bottles on the back wall and a row of kegs lying under them.
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float y = floor + 1.35f + shelf * 0.55f;
                ResortBox(house, "BarShelf", new Vector3(cx, y, z0 + 0.18f), new Vector3(wx - 1f, 0.05f, 0.32f), trim, false);
                int i = shelf * 7;
                for (float x = x0 + 0.7f; x < x1 - 0.6f; x += 0.24f, i++)
                {
                    float tall = 0.22f + 0.08f * ((i * 7) % 3);
                    Primitive(PrimitiveType.Cylinder, "Bottle", house, new Vector3(x, y + 0.025f + tall * 0.5f, z0 + 0.18f), new Vector3(0.09f, tall * 0.5f, 0.09f),
                        bottles[(i * 5) % bottles.Length], keepCollider: false);
                }
            }
            for (float x = counterL + 0.6f; x < counterR - 0.5f; x += 1.3f)
            {
                GameObject keg = Primitive(PrimitiveType.Cylinder, "Keg", house, new Vector3(x, floor + 0.42f, z0 + 0.95f), new Vector3(0.8f, 0.45f, 0.8f), barrel, keepCollider: false);
                keg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                foreach (float o in new[] { -0.3f, 0.3f })
                {
                    GameObject band = Primitive(PrimitiveType.Cylinder, "KegHoop", house, new Vector3(x, floor + 0.42f, z0 + 0.95f + o), new Vector3(0.84f, 0.03f, 0.84f), hoop, keepCollider: false);
                    band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
            // Mugs and a lantern on the counter.
            for (float x = counterL + 0.8f; x < counterR - 0.5f; x += 1.7f)
                Primitive(PrimitiveType.Cylinder, "Mug", house, new Vector3(x, floor + 1.18f, counterZ + 0.1f), new Vector3(0.12f, 0.08f, 0.12f), barrel, keepCollider: false);
            ResortBox(house, "BarLantern", new Vector3(counterR - 0.5f, floor + 1.25f, counterZ), new Vector3(0.18f, 0.28f, 0.18f), lamp, false);

            // Round tables with stools, two each side of the way in from the door.
            foreach (float side in new[] { -1f, 1f })
                foreach (float along in new[] { 0.3f, 0.75f })
                {
                    float tx = side < 0 ? (x0 + doorX - 1.9f) * 0.5f + 0.4f : (doorX + 1.9f + x1) * 0.5f - 0.4f;
                    float tz = Mathf.Lerp(counterZ + 2f, z1 - 1.4f, along);
                    if (Mathf.Abs(tx - doorX) < 2f || tx - 1.3f < x0 || tx + 1.3f > x1) continue;
                    Primitive(PrimitiveType.Cylinder, "TableFoot", house, new Vector3(tx, floor + 0.37f, tz), new Vector3(0.14f, 0.37f, 0.14f), wood, keepCollider: false);
                    Primitive(PrimitiveType.Cylinder, "RoundTable", house, new Vector3(tx, floor + 0.76f, tz), new Vector3(1.2f, 0.04f, 1.2f), counterTop);
                    Primitive(PrimitiveType.Cylinder, "Candle", house, new Vector3(tx, floor + 0.86f, tz), new Vector3(0.07f, 0.07f, 0.07f), lamp, keepCollider: false);
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI * 0.5f + 0.4f;
                        Primitive(PrimitiveType.Cylinder, "Stool", house, new Vector3(tx + Mathf.Cos(a) * 0.95f, floor + 0.25f, tz + Mathf.Sin(a) * 0.95f),
                            new Vector3(0.38f, 0.25f, 0.38f), seat);
                    }
                }
            // Barrels standing in the front corners, a ship's wheel and crossed oars on the side walls.
            foreach (float x in new[] { x0 + 0.5f, x1 - 0.5f })
            {
                Primitive(PrimitiveType.Cylinder, "Barrel", house, new Vector3(x, floor + 0.5f, z1 - 0.5f), new Vector3(0.75f, 0.5f, 0.75f), barrel);
                foreach (float y in new[] { 0.25f, 0.75f })
                    Primitive(PrimitiveType.Cylinder, "BarrelHoop", house, new Vector3(x, floor + y, z1 - 0.5f), new Vector3(0.78f, 0.03f, 0.78f), hoop, keepCollider: false);
            }
            GameObject wheel = Primitive(PrimitiveType.Cylinder, "ShipWheel", house, new Vector3(x0 + 0.05f, floor + 2.2f, cz + 1f), new Vector3(1.2f, 0.03f, 1.2f), wood, keepCollider: false);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            for (int k = 0; k < 4; k++)
            {
                GameObject spoke = Primitive(PrimitiveType.Cube, "WheelSpoke", house, new Vector3(x0 + 0.1f, floor + 2.2f, cz + 1f), new Vector3(0.05f, 1.6f, 0.07f), trim, keepCollider: false);
                spoke.transform.localRotation = Quaternion.Euler(k * 45f, 0f, 0f);
            }
            foreach (float tilt in new[] { 35f, -35f })
            {
                GameObject oar = Primitive(PrimitiveType.Cube, "Oar", house, new Vector3(x1 - 0.05f, floor + 2.2f, cz + 1f), new Vector3(0.05f, 2.2f, 0.12f), wood, keepCollider: false);
                oar.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            }
        }

        // =====================================================================
        // The pirate village
        // =====================================================================

        private static void BuildPirateVillage(Transform root, List<Vector3> taken)
        {
            var village = new GameObject("PirateVillage").transform;
            village.SetParent(root, false);
            TagSurface(village.gameObject, SurfaceKind.Wood);
            Material[] walls =
            {
                GetMaterial("PirateWoodLight", new Color(0.6f, 0.44f, 0.28f)),
                GetMaterial("PirateWoodGrey", new Color(0.55f, 0.52f, 0.46f)),
                GetMaterial("PirateWoodTeal", new Color(0.32f, 0.5f, 0.5f)),
                GetMaterial("PirateWoodRed", new Color(0.58f, 0.27f, 0.21f))
            };
            Material[] roofs =
            {
                GetMaterial("PirateThatch", new Color(0.74f, 0.61f, 0.36f)),
                GetMaterial("PirateTarpRed", new Color(0.62f, 0.17f, 0.14f)),
                GetMaterial("PirateTarpBlue", new Color(0.2f, 0.32f, 0.5f)),
                GetMaterial("PirateShingles", new Color(0.27f, 0.21f, 0.18f))
            };
            Material post = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material trim = GetMaterial("PirateTrim", new Color(0.16f, 0.11f, 0.08f));
            Material lamp = GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f));
            Material flag = JollyRogerMaterial();

            for (int n = 0; n < VillageHouses.Length; n++)
            {
                (Vector2 at, float yaw) = VillageHouses[n];
                string shack = TripoShacks[n % TripoShacks.Length];
                float shackScale = 1f + 0.07f * (n * 7 % 3);
                bool tripo = TripoPrefab(shack) != null;
                float width = 4.4f + 2.2f * (n * 5 % 7) / 6f, depth = 4f + 1.6f * (n * 3 % 5) / 4f;
                float radius = tripo ? 3.8f * shackScale + 1.4f : Mathf.Max(width, depth + 1.6f) * 0.5f + 0.8f;
                if (tripo) TripoHouse(village, $"PirateHouse_{n:00}", at, yaw, shackScale, shack);
                else PirateHouse(village, $"PirateHouse_{n:00}", at, yaw, width, depth, n, walls, roofs, post, trim, lamp, flag);
                taken.Add(new Vector3(at.x, at.y, radius));
            }
            DressVillage(village, taken, post, trim, lamp, flag);
            // Greybox shacks: planks, trims and lanterns become a few meshes, not thousands (Tripo shacks keep their rooms as they are).
            if (TripoPrefab(TripoShacks[0]) == null) CombineResortDetails(village, "Island3");
        }

        // The houses (purple on the user's map): both sides of the village lane north of the main road, two pairs on the
        // lane south of it, two by the castle road. Each is pushed clear of the roads and faces the nearest one.
        private static (Vector2 at, float yaw)[] _villageHouses;
        private static (Vector2 at, float yaw)[] VillageHouses => _villageHouses ??= DesignVillage();

        private static (Vector2 at, float yaw)[] DesignVillage()
        {
            Vector2[] wanted =
            {
                new(79f, -897f), new(101f, -897f), new(79f, -885f), new(101f, -885f), new(78f, -873f), new(100f, -873f),
                new(77f, -861f), new(98f, -861f), new(77f, -934f), new(100f, -929f), new(76f, -946f), new(99f, -942f),
                new(27f, -927f), new(9f, -908f)
            };
            var houses = new List<(Vector2, float)>();
            foreach (Vector2 w in wanted)
            {
                Vector2 at = w;
                for (int k = 0; k < 8; k++)
                {
                    float d = DistanceToRoads(at);
                    if (d >= 7.4f) break;
                    Vector2 away = at - ClosestOnRoads(at);
                    at += (away.sqrMagnitude > 1e-4f ? away.normalized : Vector2.right) * (7.5f - d);
                }
                if (Vector2.Distance(at, TavernAt) < TavernRadius + 6f) continue;
                if (houses.Any(h => Vector2.Distance(h.Item1, at) < 10.5f)) continue;
                Vector2 toRoad = ClosestOnRoads(at) - at;
                houses.Add((at, Mathf.Atan2(toRoad.x, toRoad.y) * Mathf.Rad2Deg)); // the porch faces the road
            }
            return houses.ToArray();
        }

        /// <summary>
        /// Makes the houses a pirate village: a tall Jolly Roger over the square where the lane meets the main road,
        /// lantern posts down the lanes, barrels and crates by the houses.
        /// </summary>
        private static void DressVillage(Transform village, List<Vector3> taken, Material post, Material trim, Material lamp, Material flag)
        {
            Material barrel = GetMaterial("TavernBarrel", new Color(0.46f, 0.3f, 0.16f));
            Material hoop = GetMaterial("PirateDoorIron", new Color(0.12f, 0.11f, 0.1f), metallic: 0.6f, smoothness: 0.4f);
            var dress = new GameObject("VillageDressing").transform;
            dress.SetParent(village, false);

            Vector2 pole = VillageSquare + new Vector2(-7f, 3f);
            PirateFlag(dress, new Vector3(pole.x, BeachHeight(pole.x, pole.y) - 0.2f, pole.y), 10f, new Vector2(3.2f, 2.1f), post, flag);
            Collider(dress, "FlagPoleBase", new Vector3(pole.x, BeachHeight(pole.x, pole.y) + 1f, pole.y), new Vector3(0.3f, 2f, 0.3f));
            taken.Add(new Vector3(pole.x, pole.y, 1.5f));

            // Lantern posts every 11 m down each lane, alternating sides.
            int lit = 0;
            foreach (Vector2[] lane in new[] { VillageLaneNorth, VillageLaneSouth })
            {
                float run = 6f;
                int side = 1;
                for (int i = 0; i < lane.Length - 1; i++)
                {
                    Vector2 a = lane[i], b = lane[i + 1], along = (b - a).normalized;
                    for (; run < (b - a).magnitude; run += 11f, side = -side)
                    {
                        Vector2 at = a + along * run + new Vector2(along.y, -along.x) * side * 2.8f;
                        if (taken.Any(t => Vector2.Distance(at, new Vector2(t.x, t.y)) < t.z + 0.6f)) continue;
                        float g = BeachHeight(at.x, at.y);
                        var lantern = new GameObject("LanternPost").transform;
                        lantern.SetParent(dress, false);
                        lantern.SetPositionAndRotation(new Vector3(at.x, g, at.y), Quaternion.Euler(0f, Mathf.Atan2(-side * along.y, side * along.x) * Mathf.Rad2Deg, 0f));
                        Primitive(PrimitiveType.Cylinder, "Post", lantern, new Vector3(0f, 1.6f, 0f), new Vector3(0.16f, 1.6f, 0.16f), post);
                        ResortBox(lantern, "Arm", new Vector3(0f, 3.05f, -0.3f), new Vector3(0.08f, 0.08f, 0.7f), trim, false);
                        ResortBox(lantern, "Lantern", new Vector3(0f, 2.75f, -0.6f), new Vector3(0.22f, 0.32f, 0.22f), lamp, false);
                        ResortBox(lantern, "LanternCap", new Vector3(0f, 2.95f, -0.6f), new Vector3(0.28f, 0.06f, 0.28f), trim, false);
                        if (lit++ % 2 == 0)
                        {
                            var light = new GameObject("LanternLight").AddComponent<Light>();
                            light.transform.SetParent(lantern, false);
                            light.transform.localPosition = new Vector3(0f, 2.6f, -0.6f);
                            light.type = LightType.Point;
                            light.range = 8f;
                            light.intensity = 1f;
                            light.color = new Color(1f, 0.72f, 0.4f);
                            light.shadows = LightShadows.None;
                        }
                        taken.Add(new Vector3(at.x, at.y, 0.8f));
                    }
                    run -= (b - a).magnitude;
                }
            }

            // Barrels and crates beside every other house.
            for (int n = 0; n < VillageHouses.Length; n += 2)
            {
                (Vector2 at, float yaw) = VillageHouses[n];
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                foreach ((Vector3 local, bool crate) in new[] { (new Vector3(-4.6f, 0f, 1.6f), false), (new Vector3(-4.7f, 0f, 0.5f), false), (new Vector3(-4.4f, 0f, -0.7f), true) })
                {
                    Vector3 w = new Vector3(at.x, 0f, at.y) + turn * local;
                    float g = BeachHeight(w.x, w.z);
                    if (crate)
                    {
                        if (PropModel("crate", dress, new Vector3(w.x, g + 0.36f, w.z), yaw + 20f, 1.2f) == null) continue;
                    }
                    else
                    {
                        Primitive(PrimitiveType.Cylinder, "Barrel", dress, new Vector3(w.x, g + 0.5f, w.z), new Vector3(0.75f, 0.5f, 0.75f), barrel);
                        foreach (float y in new[] { 0.25f, 0.75f })
                            Primitive(PrimitiveType.Cylinder, "BarrelHoop", dress, new Vector3(w.x, g + y, w.z), new Vector3(0.78f, 0.03f, 0.78f), hoop, keepCollider: false);
                    }
                }
            }
        }

        /// <summary>
        /// A pirate shack on stilts: plank walls (solid), a porch out front (+z) toward the road, a steep gable roof,
        /// a door, shuttered windows and a lantern. Some fly the Jolly Roger, some have crates on the porch.
        /// </summary>
        private static void PirateHouse(Transform parent, string name, Vector2 at, float yaw, float w, float d, int look,
            Material[] walls, Material[] roofs, Material post, Material trim, Material lamp, Material flag)
        {
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            // The floor stands on stilts above the highest ground under the house and its porch.
            float top = float.MinValue, low = float.MaxValue;
            foreach (float cx in new[] { -w * 0.5f - 0.3f, 0f, w * 0.5f + 0.3f })
                foreach (float cz in new[] { -d * 0.5f, 0f, d * 0.5f + 1.6f })
                {
                    Vector3 p = new Vector3(at.x, 0f, at.y) + turn * new Vector3(cx, 0f, cz);
                    float h = BeachHeight(p.x, p.z);
                    top = Mathf.Max(top, h);
                    low = Mathf.Min(low, h);
                }
            float floorY = Mathf.Max(top, WaterLevel + 0.3f) + 0.55f;
            var house = new GameObject(name).transform;
            house.SetParent(parent, false);
            house.SetPositionAndRotation(new Vector3(at.x, floorY, at.y), turn);
            Material wall = walls[look % walls.Length], roof = roofs[(look + look / walls.Length) % roofs.Length];
            float hw = 2.7f + look % 3 * 0.25f;

            float stilt = floorY - low + 0.4f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -d * 0.5f + 0.15f, d * 0.5f, d * 0.5f + 1.45f })
                    ResortBox(house, "Stilt", new Vector3(sx * w * 0.5f, -stilt * 0.5f, sz), new Vector3(0.25f, stilt, 0.25f), post);
            ResortBox(house, "Floor", new Vector3(0f, -0.1f, 0.8f), new Vector3(w + 0.4f, 0.2f, d + 1.6f), post);
            if (floorY - low > 0.5f) // steps down from the porch
                for (int i = 1; i <= 2; i++)
                    ResortBox(house, "Step", new Vector3(0f, -0.1f - i * 0.25f, d * 0.5f + 1.6f + i * 0.35f), new Vector3(1.4f, 0.2f, 0.35f), post);
            ResortBox(house, "Walls", new Vector3(0f, hw * 0.5f, 0f), new Vector3(w, hw, d), wall);
            for (float y = 0.45f; y < hw - 0.1f; y += 0.45f)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    ResortBox(house, "Plank", new Vector3(0f, y, side * (d * 0.5f + 0.015f)), new Vector3(w + 0.02f, 0.05f, 0.03f), trim, false);
                    ResortBox(house, "Plank", new Vector3(side * (w * 0.5f + 0.015f), y, 0f), new Vector3(0.03f, 0.05f, d + 0.02f), trim, false);
                }
            }
            foreach (int sx in new[] { -1, 1 }) // corner boards
                foreach (int sz in new[] { -1, 1 })
                    ResortBox(house, "Corner", new Vector3(sx * (w * 0.5f + 0.03f), hw * 0.5f, sz * (d * 0.5f + 0.03f)), new Vector3(0.16f, hw, 0.16f), post, false);
            float doorX = (look % 2 == 0 ? -1f : 1f) * w * 0.2f;
            ResortBox(house, "Door", new Vector3(doorX, 1.05f, d * 0.5f + 0.04f), new Vector3(1f, 2.1f, 0.06f), trim, false);
            ResortBox(house, "DoorFrame", new Vector3(doorX, 2.15f, d * 0.5f + 0.06f), new Vector3(1.25f, 0.14f, 0.08f), post, false);
            float windowX = Mathf.Clamp(-doorX * 1.7f, -w * 0.5f + 0.75f, w * 0.5f - 0.75f);
            ResortBox(house, "Window", new Vector3(windowX, 1.6f, d * 0.5f + 0.04f), new Vector3(0.85f, 0.75f, 0.06f), trim, false);
            ResortBox(house, "Shutter", new Vector3(windowX - 0.72f, 1.6f, d * 0.5f + 0.05f), new Vector3(0.42f, 0.8f, 0.05f), post, false);
            foreach (int side in new[] { -1, 1 })
                ResortBox(house, "Window", new Vector3(side * (w * 0.5f + 0.04f), 1.6f, 0f), new Vector3(0.06f, 0.75f, 0.9f), trim, false);
            foreach (int side in new[] { -1, 1 }) // porch rails
                ResortBox(house, "Rail", new Vector3(side * (w * 0.5f + 0.1f), 0.55f, d * 0.5f + 0.8f), new Vector3(0.08f, 0.08f, 1.6f), post, false);
            ResortBox(house, "Lantern", new Vector3(doorX + (doorX < 0f ? 0.85f : -0.85f), 2.2f, d * 0.5f + 0.25f), new Vector3(0.22f, 0.3f, 0.22f), lamp, false);

            // Steep (45°) gable roof, ridge along the width; a diamond block fills the gable ends under it.
            const float pitch = 45f;
            float run = d * 0.5f + 0.45f, eave = hw - 0.45f;
            foreach (int side in new[] { -1, 1 })
            {
                GameObject slab = ResortBox(house, "Roof", new Vector3(0f, eave + run * 0.5f + 0.09f, side * run * 0.5f),
                    new Vector3(w + 0.8f, 0.18f, run / Mathf.Cos(pitch * Mathf.Deg2Rad)), roof);
                slab.transform.localRotation = Quaternion.Euler(side * pitch, 0f, 0f);
            }
            float diamond = d / Mathf.Sqrt(2f);
            ResortBox(house, "Gable", new Vector3(0f, hw, 0f), new Vector3(w - 0.06f, diamond, diamond), wall, false)
                .transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            if (look % 3 == 0)
                PirateFlag(house, new Vector3(-w * 0.5f - 0.35f, -0.2f, -d * 0.5f + 0.3f), hw + 4f, new Vector2(1.6f, 1.05f), post, flag);
            if (look % 2 == 1)
            {
                PropModel("crate", house, new Vector3(w * 0.5f - 0.55f, 0.3f, d * 0.5f + 1.05f), look * 23f);
                PropModel("crate", house, new Vector3(w * 0.5f - 0.6f, 0.9f, d * 0.5f + 1.0f), look * 41f);
            }
        }

        /// <summary>A pole with a Jolly Roger at the top (the cloth is drawn from both sides).</summary>
        private static void PirateFlag(Transform parent, Vector3 foot, float poleHeight, Vector2 size, Material pole, Material flag)
        {
            Primitive(PrimitiveType.Cylinder, "FlagPole", parent, foot + Vector3.up * poleHeight * 0.5f, new Vector3(0.12f, poleHeight * 0.5f, 0.12f), pole, keepCollider: false);
            Primitive(PrimitiveType.Quad, "Flag", parent, foot + new Vector3(size.x * 0.5f + 0.06f, poleHeight - size.y * 0.5f - 0.1f, 0f),
                new Vector3(size.x, size.y, 1f), flag, keepCollider: false);
        }

        /// <summary>Black flag, white skull and crossbones (drawn once into a texture asset).</summary>
        private static Material JollyRogerMaterial()
        {
            string texPath = $"{MaterialDir}/JollyRoger.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                const int w = 256, h = 160;
                var px = new Color32[w * h];
                var cloth = new Color32(18, 17, 20, 255);
                var bone = new Color32(236, 231, 216, 255);
                for (int i = 0; i < px.Length; i++) px[i] = cloth;
                void Disc(float cx, float cy, float r, Color32 c)
                {
                    for (int y = Mathf.Max(0, (int)(cy - r)); y <= Mathf.Min(h - 1, (int)(cy + r)); y++)
                        for (int x = Mathf.Max(0, (int)(cx - r)); x <= Mathf.Min(w - 1, (int)(cx + r)); x++)
                            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * w + x] = c;
                }
                void Bone(Vector2 a, Vector2 b)
                {
                    for (float t = 0f; t <= 1f; t += 0.004f) Disc(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), 6f, bone);
                    Vector2 across = new Vector2(-(b - a).y, (b - a).x).normalized * 6f, along = (b - a).normalized * 4f;
                    foreach (Vector2 end in new[] { a - along, b + along })
                    {
                        Disc(end.x + across.x, end.y + across.y, 8f, bone);
                        Disc(end.x - across.x, end.y - across.y, 8f, bone);
                    }
                }
                Bone(new Vector2(66f, 30f), new Vector2(190f, 128f));
                Bone(new Vector2(66f, 128f), new Vector2(190f, 30f));
                Disc(128f, 92f, 36f, bone);                                     // cranium
                for (int y = 50; y < 74; y++) for (int x = 106; x < 151; x++) px[y * w + x] = bone; // jaw
                Disc(113f, 94f, 10f, cloth);                                    // eyes
                Disc(143f, 94f, 10f, cloth);
                Disc(128f, 76f, 5f, cloth);                                     // nose
                foreach (int x in new[] { 115, 122, 129, 136, 143 })            // teeth
                    for (int y = 51; y < 63; y++) { px[y * w + x] = cloth; px[y * w + x + 1] = cloth; }
                tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "JollyRoger", wrapMode = TextureWrapMode.Clamp };
                tex.SetPixels32(px);
                tex.Apply(true);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            Material mat = GetMaterial("JollyRoger", Color.white, smoothness: 0.05f);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Cull", 0f); // both sides
            mat.doubleSidedGI = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // =====================================================================
        // The Pirate King's castle
        // =====================================================================

        /// <summary>A faceted cone (base radius 0.5 at y 0, tip at y 1): tower roofs and stalactites.</summary>
        private static Mesh ConeMesh()
        {
            string dir = MeshDir + "/Island3";
            string path = dir + "/Cone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            Directory.CreateDirectory(dir);
            const int sides = 16;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                int k = vertices.Count;
                vertices.Add(Vector3.up);
                vertices.Add(new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.5f);
                vertices.Add(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.5f);
                triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + 2);
            }
            mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject Cone(Transform parent, string name, Vector3 foot, float radius, float height, Material material, bool upsideDown = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = foot;
            if (upsideDown) go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, height, radius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = ConeMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Merlons along a wall top from <paramref name="from"/> to <paramref name="to"/> (local, at the wall's top).</summary>
        private static void Battlements(Transform parent, Vector3 from, Vector3 to, float thickness, Material stone)
        {
            Vector3 along = to - from;
            int count = Mathf.Max(1, Mathf.FloorToInt(along.magnitude / 2.4f));
            Quaternion turn = Quaternion.LookRotation(along.normalized);
            for (int i = 0; i <= count; i++)
                ResortBox(parent, "Merlon", Vector3.Lerp(from, to, i / (float)count) + Vector3.up * 0.65f, new Vector3(thickness + 0.2f, 1.3f, 1.3f), stone, false)
                    .transform.localRotation = turn;
        }

        private static void Torch(Transform parent, Vector3 foot, Material wood, Material flame, bool lit = true)
        {
            var torch = new GameObject("Torch").transform;
            torch.SetParent(parent, false);
            torch.localPosition = foot;
            ResortBox(torch, "Post", new Vector3(0f, 1.2f, 0f), new Vector3(0.18f, 2.4f, 0.18f), wood);
            ResortBox(torch, "Bowl", new Vector3(0f, 2.4f, 0f), new Vector3(0.4f, 0.15f, 0.4f), wood, false);
            ResortBox(torch, "Flame", new Vector3(0f, 2.65f, 0f), new Vector3(0.28f, 0.42f, 0.28f), flame, false);
            if (!lit) return;
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(torch, false);
            light.transform.localPosition = new Vector3(0f, 3f, 0f);
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.62f, 0.3f);
            light.shadows = LightShadows.None;
        }

        private static void CastleTower(Transform castle, Vector3 foot, float radius, float height, Material stone, Material trim, Material roof,
            Material dark, Material pole, Material flag)
        {
            Primitive(PrimitiveType.Cylinder, "Tower", castle, foot + Vector3.up * height * 0.5f, new Vector3(radius * 2f, height * 0.5f, radius * 2f), stone);
            Primitive(PrimitiveType.Cylinder, "TowerBand", castle, foot + Vector3.up * (height - 0.3f), new Vector3(radius * 2f + 0.8f, 0.3f, radius * 2f + 0.8f), trim, keepCollider: false);
            for (int i = 0; i < 4; i++) // arrow slits
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                foreach (float y in new[] { height * 0.4f, height * 0.72f })
                    ResortBox(castle, "Slit", foot + new Vector3(Mathf.Cos(a) * (radius + 0.02f), y, Mathf.Sin(a) * (radius + 0.02f)), new Vector3(0.5f, 1.6f, 0.5f), dark, false)
                        .transform.localRotation = Quaternion.Euler(0f, -i * 90f - 45f, 0f);
            }
            float roofHeight = radius * 1.6f;
            Cone(castle, "TowerRoof", foot + Vector3.up * height, radius + 0.7f, roofHeight, roof);
            PirateFlag(castle, foot + Vector3.up * (height + roofHeight - 0.3f), 3f, new Vector2(2.2f, 1.4f), pole, flag);
        }

        /// <summary>
        /// The castle on the southern plateau, gate to the north (the road up from the village): curtain walls with
        /// battlements, four round corner towers with red cone roofs, a gatehouse with open doors, and the keep. The keep
        /// is a real hall: walk in through its door to the Pirate King's throne. Greybox for a Tripo castle.
        /// </summary>
        private static void BuildPirateCastle(Transform root)
        {
            Material stone = GetMaterial("CastleStone", new Color(0.5f, 0.47f, 0.43f), smoothness: 0.12f);
            Material stoneDark = GetMaterial("CastleStoneDark", new Color(0.34f, 0.32f, 0.3f), smoothness: 0.1f);
            Material roof = GetMaterial("CastleRoof", new Color(0.46f, 0.12f, 0.1f), smoothness: 0.2f);
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material dark = GetMaterial("PirateTrim", new Color(0.16f, 0.11f, 0.08f));
            Material gold = GetMaterial("PirateGold", new Color(0.95f, 0.72f, 0.2f), metallic: 0.7f, smoothness: 0.6f, emission: new Color(0.25f, 0.16f, 0.02f));
            Material red = GetMaterial("KingVelvet", new Color(0.55f, 0.06f, 0.08f));
            Material flame = GetMaterial("TorchFlame", new Color(1f, 0.55f, 0.15f), emission: new Color(3f, 1.3f, 0.3f));
            Material flag = JollyRogerMaterial();

            var castle = new GameObject("PirateCastle").transform;
            castle.SetParent(root, false);
            castle.position = new Vector3(CastleCenter.x, Island3CliffTop, CastleCenter.y);
            TagSurface(castle.gameObject, SurfaceKind.Rock);
            const float X = 31f, Z = 20f, H = 10f, t = 2.6f, G = 0.3f; // wall lines, wall height and thickness, yard level

            ResortBox(castle, "Foundation", new Vector3(0f, G - 2.5f, 0f), new Vector3(2f * X + 8f, 5f, 2f * Z + 8f), stoneDark);
            ResortBox(castle, "WallSouth", new Vector3(0f, G + H * 0.5f, -Z), new Vector3(2f * X, H, t), stone);
            ResortBox(castle, "WallWest", new Vector3(-X, G + H * 0.5f, 0f), new Vector3(t, H, 2f * Z), stone);
            ResortBox(castle, "WallEast", new Vector3(X, G + H * 0.5f, 0f), new Vector3(t, H, 2f * Z), stone);
            ResortBox(castle, "WallNorthWest", new Vector3(-(X + 3f) * 0.5f, G + H * 0.5f, Z), new Vector3(X - 3f, H, t), stone);
            ResortBox(castle, "WallNorthEast", new Vector3((X + 3f) * 0.5f, G + H * 0.5f, Z), new Vector3(X - 3f, H, t), stone);
            ResortBox(castle, "GateArch", new Vector3(0f, G + 7f + (H - 7f) * 0.5f, Z), new Vector3(6f, H - 7f, t), stone);
            Vector3 top = Vector3.up * (G + H);
            Battlements(castle, new Vector3(-X, 0f, -Z) + top, new Vector3(X, 0f, -Z) + top, t, stone);
            Battlements(castle, new Vector3(-X, 0f, -Z) + top, new Vector3(-X, 0f, Z) + top, t, stone);
            Battlements(castle, new Vector3(X, 0f, -Z) + top, new Vector3(X, 0f, Z) + top, t, stone);
            Battlements(castle, new Vector3(-X, 0f, Z) + top, new Vector3(-3f, 0f, Z) + top, t, stone);
            Battlements(castle, new Vector3(3f, 0f, Z) + top, new Vector3(X, 0f, Z) + top, t, stone);
            foreach ((Vector3 c, Vector3 size) in new[] {
                         (new Vector3(0f, 0f, -Z), new Vector3(2f * X, 0.4f, t + 0.6f)), (new Vector3(-X, 0f, 0f), new Vector3(t + 0.6f, 0.4f, 2f * Z)),
                         (new Vector3(X, 0f, 0f), new Vector3(t + 0.6f, 0.4f, 2f * Z)), (new Vector3(0f, 0f, Z), new Vector3(2f * X, 0.4f, t + 0.6f)) })
                ResortBox(castle, "Cornice", c + Vector3.up * (G + H - 0.25f), size, stoneDark, false);

            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    CastleTower(castle, new Vector3(sx * X, G, sz * Z), 5f, 17f, stone, stoneDark, roof, dark, wood, flag);

            // Gatehouse: two square towers, the doors standing open into the yard, the flag hung between the towers.
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(castle, "GateTower", new Vector3(side * 5.8f, G + 7f, Z + 0.6f), new Vector3(5.2f, 14f, 6.2f), stone);
                Vector3 towerTop = new(side * 5.8f, G + 14f, Z + 0.6f);
                Battlements(castle, towerTop + new Vector3(-2.6f, 0f, 3.1f), towerTop + new Vector3(2.6f, 0f, 3.1f), 0.6f, stone);
                Battlements(castle, towerTop + new Vector3(-2.6f, 0f, -3.1f), towerTop + new Vector3(2.6f, 0f, -3.1f), 0.6f, stone);
                var hinge = new GameObject("GateHinge").transform;
                hinge.SetParent(castle, false);
                hinge.localPosition = new Vector3(side * 3f, G, Z - 1f);
                hinge.localRotation = Quaternion.Euler(0f, -side * 75f, 0f); // swung in, toward the yard
                ResortBox(hinge, "GateDoor", new Vector3(-side * 1.5f, 3.4f, 0f), new Vector3(3f, 6.8f, 0.3f), wood);
                foreach (float y in new[] { 1.2f, 3.4f, 5.6f })
                    ResortBox(hinge, "IronBand", new Vector3(-side * 1.5f, y, 0f), new Vector3(3.02f, 0.18f, 0.34f), dark, false);
                Torch(castle, new Vector3(side * 4.6f, G, Z + 4.2f), wood, flame);
            }
            ResortBox(castle, "FlagBeam", new Vector3(0f, G + 13.8f, Z + 1.4f), new Vector3(6.4f, 0.3f, 0.3f), wood, false);
            Primitive(PrimitiveType.Quad, "GateFlag", castle, new Vector3(0f, G + 12.1f, Z + 1.45f), new Vector3(4.4f, 3f, 1f), flag, keepCollider: false);

            // The keep: a hollow hall (26 x 18 m) with its door to the north and the throne at the back.
            // With the Tripo keep the hall is smaller, to fit inside the model's walls (they stand well in from its corners).
            bool tripoKeep = TripoPrefab("castle_keep") != null;
            const float kz = -6f, kt = 1.2f;
            float KH = tripoKeep ? 12f : 13f, kw = tripoKeep ? 16f : 26f, kd = tripoKeep ? 14f : 18f;
            float front = kz + kd * 0.5f, back = kz - kd * 0.5f;
            ResortBox(castle, "KeepBack", new Vector3(0f, G + KH * 0.5f, back + kt * 0.5f), new Vector3(kw, KH, kt), stone);
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(castle, "KeepSide", new Vector3(side * (kw * 0.5f - kt * 0.5f), G + KH * 0.5f, kz), new Vector3(kt, KH, kd), stone);
                ResortBox(castle, "KeepFront", new Vector3(side * (kw * 0.25f + 1f), G + KH * 0.5f, front - kt * 0.5f), new Vector3(kw * 0.5f - 2f, KH, kt), stone);
            }
            ResortBox(castle, "KeepLintel", new Vector3(0f, G + 5.5f + (KH - 5.5f) * 0.5f, front - kt * 0.5f), new Vector3(4f, KH - 5.5f, kt), stone);
            ResortBox(castle, "KeepRoof", new Vector3(0f, G + KH + 0.4f, kz), new Vector3(kw, 0.8f, kd), stoneDark);
            Vector3 keepTop = Vector3.up * (G + KH + 0.8f);
            Battlements(castle, new Vector3(-kw * 0.5f, 0f, front) + keepTop, new Vector3(kw * 0.5f, 0f, front) + keepTop, 0.8f, stone);
            Battlements(castle, new Vector3(-kw * 0.5f, 0f, back) + keepTop, new Vector3(kw * 0.5f, 0f, back) + keepTop, 0.8f, stone);
            Battlements(castle, new Vector3(-kw * 0.5f, 0f, back) + keepTop, new Vector3(-kw * 0.5f, 0f, front) + keepTop, 0.8f, stone);
            Battlements(castle, new Vector3(kw * 0.5f, 0f, back) + keepTop, new Vector3(kw * 0.5f, 0f, front) + keepTop, 0.8f, stone);
            foreach (float x in new[] { -9f, -4.5f, 4.5f, 9f }) // tall windows, dark
                ResortBox(castle, "KeepWindow", new Vector3(x, G + 8f, front + 0.02f), new Vector3(1f, 2.6f, 0.06f), dark, false);
            ResortBox(castle, "KeepTower", new Vector3(0f, G + KH + 0.8f + 4f, kz - 2f), new Vector3(9f, 8f, 9f), stone);
            Cone(castle, "KeepTowerRoof", new Vector3(0f, G + KH + 0.8f + 8f, kz - 2f), 6.6f, 9f, roof);
            PirateFlag(castle, new Vector3(0f, G + KH + 0.8f + 16.6f, kz - 2f), 6f, new Vector2(4.2f, 2.7f), wood, flag);

            // Inside: red carpet from the door, a dais and the throne, heaps of gold, Jolly Roger banners, torchlight.
            ResortBox(castle, "Carpet", new Vector3(0f, G + 0.02f, kz + 1f), new Vector3(2.2f, 0.04f, kd - 4f), red, false);
            ResortBox(castle, "Dais", new Vector3(0f, G + 0.2f, back + 3f), new Vector3(7f, 0.4f, 3.6f), stoneDark);
            ResortBox(castle, "ThroneSeat", new Vector3(0f, G + 0.85f, back + 2.8f), new Vector3(2f, 0.9f, 1.4f), red);
            ResortBox(castle, "ThroneBack", new Vector3(0f, G + 2.4f, back + 2f), new Vector3(2.2f, 3.2f, 0.35f), gold);
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(castle, "ThroneArm", new Vector3(side * 1.05f, G + 1.5f, back + 2.8f), new Vector3(0.25f, 0.5f, 1.4f), gold, false);
                Primitive(PrimitiveType.Sphere, "GoldHeap", castle, new Vector3(side * 3.6f, G + 0.4f, back + 2.6f), new Vector3(2.4f, 1.2f, 2f), gold, keepCollider: false);
                Primitive(PrimitiveType.Quad, "Banner", castle, new Vector3(side * kw * 0.23f, G + 7f, back + kt + 0.05f), new Vector3(3.4f, 2.2f, 1f), flag, keepCollider: false);
                Torch(castle, new Vector3(side * (kw * 0.5f - 3f), G, back + 3f), wood, flame);
                Torch(castle, new Vector3(side * 3.2f, G, front + 1.6f), wood, flame, lit: false);
            }

            DressCastleWithTripo(castle, X, Z, G, kz, back);

            // The yard: crates and barrels by the walls.
            var rng = new System.Random(3307);
            foreach (Vector3 spot in new[] { new Vector3(-24f, G, 14f), new Vector3(24f, G, 13f), new Vector3(-25f, G, -14f), new Vector3(25f, G, -15f) })
            {
                for (int i = 0; i < 4; i++)
                {
                    var offset = new Vector3((float)rng.NextDouble() * 3f - 1.5f, 0f, (float)rng.NextDouble() * 3f - 1.5f);
                    if (i % 2 == 0) PropModel("crate", castle, spot + offset + Vector3.up * 0.48f, (float)rng.NextDouble() * 90f, 1.6f);
                    else Primitive(PrimitiveType.Cylinder, "Barrel", castle, spot + offset + Vector3.up * 0.55f, new Vector3(0.9f, 0.55f, 0.9f), wood);
                }
            }
        }

        /// <summary>
        /// The Tripo castle over the greybox: the greybox outside is hidden but keeps its collision; the keep model is a
        /// shell with its front door cut open (drawn one-sided), so the greybox hall inside shows through and stays walk-in.
        /// </summary>
        private static void DressCastleWithTripo(Transform castle, float X, float Z, float G, float keepZ, float hallBack)
        {
            if (TripoPrefab("castle_keep") == null) return;
            var hidden = new HashSet<string>
            {
                "WallSouth", "WallWest", "WallEast", "WallNorthWest", "WallNorthEast", "GateArch", "Merlon", "Cornice", "Tower",
                "TowerBand", "Slit", "TowerRoof", "FlagPole", "Flag", "GateTower", "FlagBeam", "GateFlag", "KeepWindow",
                "KeepTower", "KeepTowerRoof", "ThroneSeat", "ThroneBack", "ThroneArm", "GoldHeap"
            };
            foreach (MeshRenderer r in castle.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!hidden.Contains(r.gameObject.name)) continue;
                Object.DestroyImmediate(r.GetComponent<MeshFilter>());
                Object.DestroyImmediate(r);
            }
            foreach (Transform t in castle.Cast<Transform>().ToArray()) // the model has its own gate doors
                if (t.name == "GateHinge") Object.DestroyImmediate(t.gameObject);

            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    TripoModel("castle_round_tower", castle, new Vector3(sx * X, G, sz * Z), sx * 70f + sz * 25f, Vector3.one);
            void WallLine(Vector3 a, Vector3 b)
            {
                Vector3 along = b - a;
                int count = Mathf.Max(1, Mathf.RoundToInt(along.magnitude / 30f));
                float segment = along.magnitude / count;
                float yaw = Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg; // the model is long along its x
                for (int i = 0; i < count; i++)
                    TripoModel("castle_wall", castle, a + along.normalized * segment * (i + 0.5f), yaw, new Vector3(segment / 30f, 1f, 1f));
            }
            WallLine(new Vector3(-X, G, -Z), new Vector3(X, G, -Z));
            WallLine(new Vector3(-X, G, -Z), new Vector3(-X, G, Z));
            WallLine(new Vector3(X, G, -Z), new Vector3(X, G, Z));
            WallLine(new Vector3(-X, G, Z), new Vector3(-11f, G, Z));
            WallLine(new Vector3(11f, G, Z), new Vector3(X, G, Z));
            TripoModel("castle_gatehouse", castle, new Vector3(0f, G, Z), 0f, Vector3.one);
            foreach (int side in new[] { -1, 1 }) // its arch is ~3.7 m wide: narrow the greybox passage to match
                Collider(castle, "GateJamb", new Vector3(side * 2.7f, G + 3.5f, Z), new Vector3(1.6f, 7f, 9f));
            TripoModel("castle_keep", castle, new Vector3(0f, G, keepZ), 0f, Vector3.one);
            TripoModel("pirate_throne", castle, new Vector3(0f, G + 0.4f, hallBack + 2.5f), 0f, Vector3.one);
            foreach (int side in new[] { -1, 1 })
                TripoModel("gold_coin_pile", castle, new Vector3(side * 3.9f, G, hallBack + 2.6f), side * 35f, Vector3.one * 0.8f);
        }

        // =====================================================================
        // The waterfall and the money cave
        // =====================================================================

        /// <summary>White-blue vertical streaks, alpha blended, both sides: scrolled by WaterfallFlow.</summary>
        private static Material WaterfallMaterial()
        {
            string texPath = $"{MaterialDir}/WaterfallStreaks.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                const int w = 64, h = 128;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "WaterfallStreaks", wrapMode = TextureWrapMode.Repeat };
                for (int x = 0; x < w; x++)
                {
                    // Periodic in x and y so the sheet tiles without seams.
                    float u = x / (float)w * Mathf.PI * 2f;
                    float phase1 = 3f * Mathf.Sin(u) + 2f * Mathf.Sin(3f * u + 1f), phase2 = 2f * Mathf.Sin(2f * u + 0.5f) + Mathf.Sin(5f * u);
                    float column = 0.55f + 0.25f * Mathf.Sin(7f * u + 0.3f) + 0.2f * Mathf.Sin(13f * u);
                    for (int y = 0; y < h; y++)
                    {
                        float v = y / (float)h * Mathf.PI * 2f;
                        float a = Mathf.Clamp01(column * (0.6f + 0.25f * Mathf.Sin(2f * v + phase1) + 0.2f * Mathf.Sin(5f * v + phase2)));
                        float foam = Mathf.Clamp01((a - 0.55f) * 2.5f);
                        tex.SetPixel(x, y, new Color(Mathf.Lerp(0.72f, 1f, foam), Mathf.Lerp(0.88f, 1f, foam), 1f, 0.35f + a * 0.6f));
                    }
                }
                tex.Apply(true);
                AssetDatabase.CreateAsset(tex, texPath);
            }
            Material mat = LoadOrCreateMaterial("Waterfall", Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(0.86f, 0.94f, 1f, 0.9f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void FlowingSheet(Transform parent, string name, Vector3 position, Quaternion rotation, Vector2 size, Material material, float speed, Vector2 tiling)
        {
            GameObject sheet = Primitive(PrimitiveType.Quad, name, parent, position, new Vector3(size.x, size.y, 1f), material, keepCollider: false);
            sheet.transform.localRotation = rotation;
            sheet.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var so = new SerializedObject(sheet.AddComponent<WaterfallFlow>());
            Require(so, "_speed").floatValue = speed;
            Require(so, "_tiling").vector2Value = tiling;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The stolen money's hiding place. The ground mesh has a hole here; this rock shell takes its place: walls down
        /// to the seabed, a roof that is the plateau above, a 12 m mouth at sea level behind the waterfall. Inside, the sea
        /// comes in (the ocean is everywhere at sea level), a ramp climbs out of the water to a dry ledge with the loot.
        /// Frame: x from CaveX, z inland from the cliff foot (CaveFaceZ), y world.
        /// </summary>
        private static void BuildMoneyCave(Transform root)
        {
            Material rock = GetMaterial("CaveRock", new Color(0.44f, 0.42f, 0.4f), smoothness: 0.1f);
            Material rockDark = GetMaterial("CaveRockDark", new Color(0.27f, 0.26f, 0.26f), smoothness: 0.15f);
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material gold = GetMaterial("PirateGold", new Color(0.95f, 0.72f, 0.2f), metallic: 0.7f, smoothness: 0.6f, emission: new Color(0.25f, 0.16f, 0.02f));
            Material cash = GetMaterial("Banknotes", new Color(0.45f, 0.64f, 0.42f));
            Material band = GetMaterial("White", new Color(0.95f, 0.95f, 0.95f));
            Material burlap = GetMaterial("MoneySack", new Color(0.7f, 0.6f, 0.42f));
            Material flame = GetMaterial("TorchFlame", new Color(1f, 0.55f, 0.15f), emission: new Color(3f, 1.3f, 0.3f));
            Material[] chips =
            {
                GetMaterial("ChipRed", new Color(0.75f, 0.1f, 0.1f)), GetMaterial("ChipBlack", new Color(0.08f, 0.08f, 0.09f)),
                GetMaterial("ChipBlue", new Color(0.12f, 0.25f, 0.65f))
            };
            float T = Island3CliffTop - 0.05f; // a hair under the ground round it, so the edges never flicker
            const float bottom = -10f, ceiling = 7f, ledge = 0.6f;

            var cave = new GameObject("MoneyCave").transform;
            cave.SetParent(root, false);
            cave.position = new Vector3(CaveX, 0f, CaveFaceZ);
            TagSurface(cave.gameObject, SurfaceKind.Rock);
            float Mid(float a, float b) => (a + b) * 0.5f;

            // Shell: x [-13, 13], z [-4, 34]. Hollow inside: x [-8, 8], z [-4, 30], up to the ceiling.
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(cave, "CaveWall", new Vector3(side * 10.5f, Mid(bottom, T), 15f), new Vector3(5f, T - bottom, 38f), rock);
                ResortBox(cave, "MouthPillar", new Vector3(side * 7f, Mid(bottom, ceiling), -3f), new Vector3(2f, ceiling - bottom, 2f), rock);
            }
            ResortBox(cave, "CaveBack", new Vector3(0f, Mid(bottom, T), 32f), new Vector3(16f, T - bottom, 4f), rock);
            ResortBox(cave, "CaveRoof", new Vector3(0f, Mid(ceiling, T), 13f), new Vector3(16f, T - ceiling, 34f), rock);
            ResortBox(cave, "CavePoolFloor", new Vector3(0f, Mid(bottom, -4f), 5f), new Vector3(16f, -4f - bottom, 18f), rockDark);
            ResortBox(cave, "CaveLedge", new Vector3(0f, Mid(bottom, ledge), 22f), new Vector3(16f, ledge - bottom, 16f), rockDark);
            // A ramp out of the water up to the ledge (swimmers walk out; there is nothing to climb).
            float rise = ledge + 2.6f, run = 9f, slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            ResortBox(cave, "CaveRamp", new Vector3(0f, Mid(-2.6f, ledge) - 0.42f, 9.5f), new Vector3(9f, 0.8f, Mathf.Sqrt(rise * rise + run * run) + 0.4f), rockDark)
                .transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);

            // The Tripo rock facade over the mouth (its arch is cut through: 9.2 m wide, up to the ceiling), facing the sea.
            // It has no collision of its own: three boxes keep swimmers to the arch.
            bool facade = TripoModel("sea_cave_entrance", cave, new Vector3(0f, -3f, -4.5f), 180f, Vector3.one) != null;
            if (facade)
            {
                foreach (int side in new[] { -1, 1 })
                    Collider(cave, "FacadeSide", new Vector3(side * 9.3f, 7.5f, -4.5f), new Vector3(9.4f, 21f, 7f));
                Collider(cave, "FacadeTop", new Vector3(0f, 12.5f, -4.5f), new Vector3(9.2f, 11f, 7f));
            }
            // Boulders soften the box shapes: round the mouth, on the shell's top edges, along the walls inside.
            var rng = new System.Random(3311);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            foreach (int side in new[] { -1, 1 })
            {
                if (!facade)
                {
                    SkullCoveRock(cave, new Vector3(side * 11.5f, 1f, -4.5f), new Vector3(6f, 9f, 5f), R(0f, 360f), R(-8f, 8f), localSpace: true);
                    SkullCoveRock(cave, new Vector3(side * 12f, T - 0.5f, -2f), new Vector3(5f, 3f, 5f), R(0f, 360f), R(-8f, 8f), localSpace: true);
                    SkullCoveRock(cave, new Vector3(side * 8.5f, 9f, -4.5f), new Vector3(4f, 5f, 3f), R(0f, 360f), R(-8f, 8f), localSpace: true);
                }
                for (float z = 2f; z < 29f; z += R(4f, 6.5f))
                    SkullCoveRock(cave, new Vector3(side * 7.6f, R(0.5f, 5f), z), new Vector3(R(2f, 3.5f), R(2f, 4f), R(2.5f, 4f)), R(0f, 360f), R(-10f, 10f), localSpace: true);
            }
            for (int i = 0; i < 16; i++) // stalactites
                Cone(cave, "Stalactite", new Vector3(R(-6.5f, 6.5f), ceiling + 0.2f, R(0f, 28f)), R(0.3f, 0.75f), R(1.4f, 3.4f), rockDark, upsideDown: true);

            // The falls: two sheets over the mouth, the stream running over the roof to the lip, mist where it lands.
            Material falls = WaterfallMaterial();
            float drop = T + 0.1f - WaterLevel;
            float face = facade ? -8.4f : -4.35f; // the falls hang in front of the facade's arch
            FlowingSheet(cave, "WaterfallSheet", new Vector3(0f, WaterLevel + drop * 0.5f, face), Quaternion.identity, new Vector2(13.4f, drop), falls, 1.4f, new Vector2(4f, 3f));
            FlowingSheet(cave, "WaterfallSheetFront", new Vector3(0f, WaterLevel + drop * 0.5f, face - 0.45f), Quaternion.identity, new Vector2(12.2f, drop), falls, 2.1f, new Vector2(3f, 2.2f));
            FlowingSheet(cave, "RoofStream", new Vector3(0f, T + 0.03f, 15f), Quaternion.Euler(90f, 0f, 0f), new Vector2(3.6f, 30f), falls, 0.45f, new Vector2(1f, 8f));
            float lipFrom = 1.6f, lipTo = face + 0.1f;
            FlowingSheet(cave, "FallsLip", new Vector3(0f, T + 0.06f, (lipFrom + lipTo) * 0.5f), Quaternion.Euler(90f, 0f, 0f), new Vector2(13.4f, lipFrom - lipTo), falls, 0.8f, new Vector2(4f, 1f));
            var mist = new GameObject("WaterfallMist");
            mist.transform.SetParent(cave, false);
            mist.transform.localPosition = new Vector3(0f, WaterLevel + 0.3f, face - 1.25f);
            mist.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // emit up
            var particles = mist.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.6f);
            main.startColor = new Color(1f, 1f, 1f, 0.32f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 45f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(13f, 1.2f, 0.4f);
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            mist.GetComponent<ParticleSystemRenderer>().sharedMaterial = GetSplashMaterial();

            // The loot on the ledge: chests full of gold, heaps of coins, cash bricks, money sacks and casino chips.
            var loot = new GameObject("StolenMoney").transform;
            loot.SetParent(cave, false);
            bool tripoLoot = TripoPrefab("treasure_chest_open") != null;
            if (tripoLoot)
            {
                foreach ((Vector3 at, float yaw) in new[] { (new Vector3(-4f, ledge, 24f), 200f), (new Vector3(3.5f, ledge, 26f), 165f), (new Vector3(0f, ledge, 28.2f), 180f) })
                {
                    GameObject chest = TripoModel("treasure_chest_open", loot, at, yaw, Vector3.one);
                    var box = chest.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.45f, 0f);
                    box.size = new Vector3(1.5f, 0.9f, 1.1f);
                }
                foreach ((Vector3 at, float scale) in new[] { (new Vector3(-1.5f, ledge, 19.5f), 1f), (new Vector3(2.5f, ledge, 21f), 0.8f),
                             (new Vector3(0f, ledge, 24.3f), 1.15f), (new Vector3(-5.5f, ledge, 28f), 0.85f) })
                    TripoModel("gold_coin_pile", loot, at, R(0f, 360f), Vector3.one * scale);
                foreach (Vector3 at in new[] { new Vector3(-6f, ledge, 18f), new Vector3(6.1f, ledge, 20.2f), new Vector3(-6.2f, ledge, 25.5f),
                             new Vector3(6f, ledge, 27.5f), new Vector3(2.8f, ledge, 29f), new Vector3(-2.6f, ledge, 29.2f) })
                    TripoModel("money_sack", loot, at, R(120f, 240f), Vector3.one * R(0.9f, 1.15f));
                foreach (Vector3 at in new[] { new Vector3(-3.2f, ledge, 17.5f), new Vector3(1f, ledge, 18f), new Vector3(4f, ledge, 22.8f),
                             new Vector3(-3.6f, ledge, 20.2f), new Vector3(-0.8f, ledge, 21.6f) })
                    TripoModel("casino_chip_stacks", loot, at, R(0f, 360f), Vector3.one * R(0.9f, 1.3f));
            }
            else foreach ((Vector3 at, float yaw) in new[] { (new Vector3(-4f, ledge, 24f), 20f), (new Vector3(3.5f, ledge, 26f), -15f), (new Vector3(0f, ledge, 28.3f), 0f) })
            {
                var chest = new GameObject("TreasureChest").transform;
                chest.SetParent(loot, false);
                chest.localPosition = at;
                chest.localRotation = Quaternion.Euler(0f, yaw, 0f);
                ResortBox(chest, "Body", new Vector3(0f, 0.4f, 0f), new Vector3(1.6f, 0.8f, 1f), wood);
                foreach (float x in new[] { -0.55f, 0.55f })
                    ResortBox(chest, "Band", new Vector3(x, 0.4f, 0f), new Vector3(0.12f, 0.82f, 1.02f), gold, false);
                Primitive(PrimitiveType.Sphere, "Gold", chest, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 0.5f, 0.85f), gold, keepCollider: false);
                var hinge = new GameObject("LidHinge").transform;
                hinge.SetParent(chest, false);
                hinge.localPosition = new Vector3(0f, 0.8f, -0.5f);
                hinge.localRotation = Quaternion.Euler(-70f, 0f, 0f);
                ResortBox(hinge, "Lid", new Vector3(0f, 0.06f, 0.5f), new Vector3(1.6f, 0.12f, 1f), wood, false);
            }
            if (!tripoLoot)
                foreach ((Vector3 at, Vector3 size) in new[] { (new Vector3(-1.5f, ledge, 19.5f), new Vector3(3f, 1f, 2.4f)),
                         (new Vector3(2.5f, ledge, 21f), new Vector3(2.2f, 0.8f, 2f)), (new Vector3(0f, ledge, 24.5f), new Vector3(3.6f, 1.4f, 3f)),
                         (new Vector3(-5.5f, ledge, 28f), new Vector3(2.4f, 1.1f, 2.4f)) })
                Primitive(PrimitiveType.Sphere, "CoinHeap", loot, at, size, gold, keepCollider: false);
            foreach (Vector3 pile in new[] { new Vector3(5f, ledge, 18.5f), new Vector3(-5.5f, ledge, 21.5f), new Vector3(5.5f, ledge, 22.5f) })
                for (int layer = 0; layer < 4; layer++)
                    for (int i = 0; i < 4 - layer; i++)
                    {
                        Vector3 brick = pile + new Vector3((i - (3 - layer) * 0.5f) * 0.36f, 0.09f + layer * 0.18f, R(-0.05f, 0.05f));
                        ResortBox(loot, "CashBrick", brick, new Vector3(0.34f, 0.17f, 0.7f), cash, false);
                        ResortBox(loot, "CashBand", brick, new Vector3(0.35f, 0.175f, 0.12f), band, false);
                    }
            for (int i = 0; i < (tripoLoot ? 0 : 9); i++)
            {
                Vector3 at = new(R(-6.5f, 6.5f), ledge, R(16.5f, 29f));
                if (Mathf.Abs(at.x) < 2.2f && at.z > 23f) continue; // keep clear of the middle chest
                Primitive(PrimitiveType.Sphere, "MoneySack", loot, at + Vector3.up * 0.45f, new Vector3(0.85f, 0.95f, 0.85f), burlap, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "SackNeck", loot, at + Vector3.up * 0.98f, new Vector3(0.28f, 0.12f, 0.28f), burlap, keepCollider: false);
            }
            for (int i = 0; i < (tripoLoot ? 0 : 10); i++) // the casino's chips, in stacks
            {
                Vector3 at = new(R(-6f, 6f), ledge, R(17f, 23f));
                int height = rng.Next(3, 10);
                Material chip = chips[i % chips.Length];
                for (int k = 0; k < height; k++)
                    Primitive(PrimitiveType.Cylinder, "Chip", loot, at + Vector3.up * (0.025f + k * 0.052f), new Vector3(0.34f, 0.025f, 0.34f), chip, keepCollider: false);
            }
            foreach (int side in new[] { -1, 1 })
                Torch(cave, new Vector3(side * 6.3f, ledge, 17f), wood, flame);
            var glow = new GameObject("GoldGlow").AddComponent<Light>();
            glow.transform.SetParent(cave, false);
            glow.transform.localPosition = new Vector3(0f, 3f, 24f);
            glow.type = LightType.Point;
            glow.range = 10f;
            glow.intensity = 1.6f;
            glow.color = new Color(1f, 0.8f, 0.4f);
            glow.shadows = LightShadows.None;
        }

        /// <summary>The two pools on the plateau and the stream from them to the cave roof (shallow, for looks).</summary>
        private static void BuildPlateauWater(Transform root)
        {
            Material fresh = GetMaterial("Island3FreshWater", new Color(0.17f, 0.5f, 0.56f), smoothness: 0.92f);
            Material falls = WaterfallMaterial();
            var water = new GameObject("PlateauWater").transform;
            water.SetParent(root, false);
            float level = Island3CliffTop - 0.35f;
            foreach ((Vector2 centre, float radius) in Island3Pools)
                Primitive(PrimitiveType.Cylinder, "Pool", water, new Vector3(centre.x, level, centre.y), new Vector3((radius + 0.8f) * 2f, 0.02f, (radius + 0.8f) * 2f), fresh, keepCollider: false);
            for (int i = 0; i < Island3Stream.Length - 1; i++)
            {
                Vector2 a = Island3Stream[i], b = Island3Stream[i + 1], along = b - a;
                float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg;
                FlowingSheet(water, "Stream", new Vector3(Mid2(a, b).x, level + 0.03f, Mid2(a, b).y), Quaternion.Euler(90f, yaw + 180f, 0f),
                    new Vector2(3.4f, along.magnitude + 1.5f), falls, 0.45f, new Vector2(1f, 3f));
            }
        }

        private static Vector2 Mid2(Vector2 a, Vector2 b) => (a + b) * 0.5f;

        // =====================================================================
        // Getting there, palms
        // =====================================================================

        /// <summary>The pirates' dock on the east coast (the boat from the hotel lands here), the arrival spot and a travel pad.</summary>
        private static Transform BuildPirateDock(Transform root, List<Vector3> taken)
        {
            Material wood = GetMaterial("Wood", new Color(0.55f, 0.36f, 0.22f));
            float shoreX = Island3Center.x + 60f;
            while (shoreX < 260f && BeachHeight(shoreX, DockZ) > WaterLevel) shoreX += 0.5f;
            var dock = new GameObject("PirateDock").transform;
            dock.SetParent(root, false);
            dock.SetPositionAndRotation(new Vector3(shoreX, 0f, DockZ), Quaternion.Euler(0f, -90f, 0f)); // runs out east
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3(0f, 0.175f, -6.5f), new Vector3(2.4f, 0.25f, 25f), wood);
            for (float z = 2f; z >= -18f; z -= 4f)
                foreach (float x in new[] { -1.1f, 1.1f })
                    Primitive(PrimitiveType.Cube, "Post", dock, new Vector3(x, -2.2f, z), new Vector3(0.22f, 4.8f, 0.22f), wood);
            DressProp(dock, "dock");
            taken.Add(new Vector3(shoreX - 8f, DockZ, 6f));

            Transform arrival = Point(root, "Island3Arrival", OnGround(new Vector3(shoreX - 12f, 0f, DockZ)) + Vector3.up * 0.3f, -90f);
            Material padGlow = GetMaterial("PadGlow", new Color(0.55f, 0.35f, 1f), emission: new Color(0.9f, 0.5f, 2.2f));
            TeleportPadAt(root, "TravelPad_Island3", OnGround(new Vector3(shoreX - 14f, 0f, DockZ + 6f)), -90f, Destination.PirateIsland, padGlow);
            taken.Add(new Vector3(shoreX - 14f, DockZ + 6f, 3f));
            return arrival;
        }

        // The jungle (orange on the user's map): the west half of the island, both banks of the river from the crags to
        // the castle's west side, and the strip between the crags and the west coast.
        private static readonly Vector2[][] JunglePolygons =
        {
            new Vector2[] { new(-59f, -990f), new(-86f, -960f), new(-68f, -870f), new(-30f, -855f), new(10f, -850f), new(37f, -847f), new(38f, -864f), new(24f, -890f), new(27f, -922f), new(5f, -955f) },
            new Vector2[] { new(-50f, -852f), new(-80f, -810f), new(-31f, -792f), new(-34f, -846f) },
        };

        private static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        private static bool InJungle(Vector2 p) => JunglePolygons.Any(poly => InPolygon(p, poly));

        /// <summary>
        /// Dense jungle: big broadleaf trees (and some palms) about 5.5 m apart, leafy bushes between them and ferns
        /// filling the ground, clear of the paths, the river, the falls and the castle. Tripo models
        /// (jungle_tree / jungle_bush / jungle_fern); palms and green blobs if they're missing.
        /// </summary>
        private static void BuildIsland3Jungle(Transform root, List<Vector3> taken)
        {
            var jungle = new GameObject("Jungle").transform;
            jungle.SetParent(root, false);
            TagSurface(jungle.gameObject, SurfaceKind.Wood);
            Material leaf = GetMaterial("JungleLeaf", new Color(0.18f, 0.5f, 0.16f));
            Material leafLight = GetMaterial("JungleLeafLight", new Color(0.3f, 0.62f, 0.2f));
            var rng = new System.Random(3321);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var trees = new List<Vector2>();
            var bushes = new List<Vector2>();
            var ferns = new List<Vector2>();
            float minX = JunglePolygons.SelectMany(q => q).Min(q => q.x), maxX = JunglePolygons.SelectMany(q => q).Max(q => q.x);
            float minZ = JunglePolygons.SelectMany(q => q).Min(q => q.y), maxZ = JunglePolygons.SelectMany(q => q).Max(q => q.y);
            bool Near(List<Vector2> list, Vector2 p, float d) => list.Any(q => (q - p).sqrMagnitude < d * d);

            for (int i = 0; i < 36000; i++)
            {
                int kind = i < 14000 ? 0 : i < 25000 ? 1 : 2; // trees first, then bushes, then ferns
                if ((kind == 0 && trees.Count >= 140) || (kind == 1 && bushes.Count >= 260) || (kind == 2 && ferns.Count >= 340)) continue;
                var p = new Vector2(R(minX, maxX), R(minZ, maxZ));
                if (!InJungle(p)) continue;
                float h = BeachHeight(p.x, p.y);
                if (h < 0.4f || h > 26f) continue;
                float boxOut = BoxDistanceOut(p.x, p.y, Island3Center, Island3HalfSize, Island3CornerRadius);
                if (Island3Shore(p.x, p.y, boxOut) < 8f) continue;
                if (DistanceToLine(p, Island3River) < 5f || Vector2.Distance(p, Island3Spring) < 10f || InCragFalls(p, 4f)) continue;
                if (BoxDistanceOut(p.x, p.y, CastleCenter, CastleHalf, 0f) < 6f) continue;
                if (taken.Any(t => Vector2.Distance(p, new Vector2(t.x, t.y)) < t.z + 1f)) continue;
                float road = DistanceToRoads(p);
                var feet = new Vector3(p.x, h - 0.1f, p.y);
                float yaw = R(0f, 360f);
                if (kind == 0)
                {
                    if (road < 4.5f || Near(trees, p, 5.5f)) continue;
                    GameObject tree = trees.Count % 6 == 5 ? MeshyArt.Place("palm_tall", jungle, R(8f, 11f), feet, yaw)
                        : TripoModel("jungle_tree", jungle, feet, yaw, Vector3.one * R(0.8f, 1.3f));
                    if (tree == null) tree = MeshyArt.Place("palm_tall", jungle, R(8f, 11f), feet, yaw);
                    if (tree == null) return;
                    var trunk = tree.AddComponent<CapsuleCollider>();
                    trunk.height = 6f;
                    trunk.radius = 0.35f;
                    trunk.center = Vector3.up * 3f;
                    trees.Add(p);
                }
                else if (kind == 1)
                {
                    if (road < 2.6f || Near(trees, p, 1.8f) || Near(bushes, p, 2.8f)) continue;
                    if (TripoModel("jungle_bush", jungle, feet, yaw, Vector3.one * R(0.8f, 1.6f)) == null)
                        Primitive(PrimitiveType.Sphere, "Bush", jungle, feet + Vector3.up * 0.6f, new Vector3(R(1.6f, 2.6f), R(1.1f, 1.6f), R(1.6f, 2.6f)), leaf, keepCollider: false);
                    bushes.Add(p);
                }
                else
                {
                    if (road < 2.2f || Near(trees, p, 1.2f) || Near(bushes, p, 1.5f) || Near(ferns, p, 1.5f)) continue;
                    if (TripoModel("jungle_fern", jungle, feet, yaw, Vector3.one * R(0.8f, 1.5f)) == null)
                        Primitive(PrimitiveType.Sphere, "Fern", jungle, feet + Vector3.up * 0.3f, new Vector3(R(1f, 1.6f), R(0.5f, 0.8f), R(1f, 1.6f)), leafLight, keepCollider: false);
                    ferns.Add(p);
                }
            }
            foreach (Vector2 t in trees) taken.Add(new Vector3(t.x, t.y, 1.2f));
        }

        private static void BuildIsland3Palms(Transform root, List<Vector3> taken)
        {
            var palms = new GameObject("Palms").transform;
            palms.SetParent(root, false);
            var rng = new System.Random(3313);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0, placed = 0; i < 4000 && placed < 90; i++)
            {
                float x = Island3Center.x + R(-1f, 1f) * Island3HalfSize.x, z = Island3Center.y + R(-1f, 1f) * Island3HalfSize.y;
                var p = new Vector2(x, z);
                float h = BeachHeight(x, z), high = Island3Highland(x, z);
                float boxOut = BoxDistanceOut(x, z, Island3Center, Island3HalfSize, Island3CornerRadius);
                if (Island3Shore(x, z, boxOut) < 9f || h < 0.1f) continue;
                if (high > 1.5f && high < Island3CliffTop - 0.6f) continue; // no trees on the slopes
                if (h > Island3CliffTop + 1.5f) continue;                  // nor on the crags
                if (BoxDistanceOut(x, z, CastleCenter, CastleHalf, 0f) < 6f || InCave(x, z, 18f, -8f, 40f)) continue;
                if (DistanceToRoads(p) < 3.5f || DistanceToLine(p, Island3River) < 6.5f || Vector2.Distance(p, Island3Spring) < 11f) continue;
                if (DistanceToLine(p, Island3Stream) < 4f || InJungle(p) || InCragFalls(p, 6f)) continue;
                bool clear = true;
                foreach ((Vector2 centre, float radius) in Island3Pools)
                    if (Vector2.Distance(p, centre) < radius + 3f) clear = false;
                foreach (Vector3 t in taken)
                    if (Vector2.Distance(p, new Vector2(t.x, t.y)) < t.z + 2.5f) clear = false;
                if (!clear) continue;
                GameObject palm = MeshyArt.Place("palm_tall", palms, R(7f, 10f), new Vector3(x, h, z), R(0f, 360f));
                if (palm == null) return;
                var trunk = palm.AddComponent<CapsuleCollider>();
                trunk.height = 6f;
                trunk.radius = 0.24f;
                trunk.center = Vector3.up * 3f;
                taken.Add(new Vector3(x, z, 1.5f));
                placed++;
            }
        }

        // =====================================================================
        // Edit-mode update, checks and review pictures
        // =====================================================================

        [MenuItem("PLEASE DON'T DROWN/Build island three (Skull Cove)")]
        public static void BuildIsland3InScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open the Game scene in edit mode before building island three.");
            Transform env = GameObject.Find("Environment")?.transform;
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            if (env == null || terrain == null) throw new InvalidOperationException("Environment/terrain missing; no scene edits applied.");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-island3-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            _caveFaceZ = null;
            _levelPads = null;
            _cragFalls = null;
            _villageHouses = null;
            RefreshBeachTerrain(terrain);
            Transform old = env.Find("Island3");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            BuildIsland3(env);
            // Island 1's two "rocks to swim to" out in the sea are gone (the builder no longer makes them).
            bool island1Rocks = false;
            foreach (Transform child in env.Cast<Transform>().ToArray())
                if (child.name == "Rock" && child.position.z > -100f && Mathf.Abs(child.position.x) < 100f)
                {
                    Object.DestroyImmediate(child.gameObject);
                    island1Rocks = true;
                }
            if (island1Rocks) BakeNavMeshes(only: "Island1");
            BakeNavMeshes(only: "Island3");
            var loader = Object.FindFirstObjectByType<NavMeshLoader>();
            var baked = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{NavMeshDir}/Island3.asset");
            if (loader == null || baked == null) throw new InvalidOperationException("Navigation loader or island three navmesh missing");
            var loaderSo = new SerializedObject(loader);
            SerializedProperty data = Require(loaderSo, "_data");
            var all = new List<Object>();
            for (int i = 0; i < data.arraySize; i++) all.Add(data.GetArrayElementAtIndex(i).objectReferenceValue);
            if (!all.Contains(baked)) { all.Add(baked); SetRefs(loader, "_data", all.ToArray()); }
            AssignSceneIds(scene);
            VerifyIsland3(env.Find("Island3"));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureIsland3();
            Selection.activeGameObject = env.Find("Island3").gameObject;
            Debug.Log("[Island3] PASS: Skull Cove built (terrain, crags, village, castle, waterfall cave); navigation and previews saved.");
        }

        private static void VerifyIsland3(Transform island)
        {
            Physics.SyncTransforms();
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            var seabed = new SerializedObject(terrain.GetComponent<Seabed>());
            if (terrain.GetComponent<MeshFilter>().sharedMesh.vertexCount != Require(seabed, "_countX").intValue * Require(seabed, "_countZ").intValue)
                throw new InvalidOperationException("Seabed grid does not match the terrain mesh");
            if (terrain.GetComponent<MeshCollider>().sharedMesh != terrain.GetComponent<MeshFilter>().sharedMesh)
                throw new InvalidOperationException("Terrain collision/render mismatch");

            // The castle stands on the plateau, clear of the cliff edge.
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    float h = BeachHeight(CastleCenter.x + sx * CastleHalf.x, CastleCenter.y + sz * CastleHalf.y);
                    if (Mathf.Abs(h - Island3CliffTop) > 1.2f) throw new InvalidOperationException($"Castle corner off the plateau ({h:0.0} m)");
                }
            for (float x = CastleCenter.x - CastleHalf.x; x <= CastleCenter.x + CastleHalf.x; x += 2f)
                if (BeachHeight(x, CastleCenter.y - CastleHalf.y - 4f) < Island3CliffTop - 1.5f)
                    throw new InvalidOperationException($"Castle too close to the cliff at x {x:0}");

            // The cave mouth is open behind the waterfall, all the way to the back.
            Vector3 from = new(CaveX, 1.6f, CaveFaceZ - 12f);
            if (!Physics.Raycast(from, Vector3.forward, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) || hit.point.z < CaveFaceZ + 25f)
                throw new InvalidOperationException("Cave mouth blocked by " + (hit.collider != null ? hit.collider.name : "nothing (no back wall)"));
            // Gate and keep door line up: straight in from the road to the throne.
            from = new Vector3(CastleCenter.x, Island3CliffTop + 1.5f, CastleCenter.y + 32f);
            if (!Physics.Raycast(from, Vector3.back, out hit, 80f, ~0, QueryTriggerInteraction.Ignore) || hit.point.z > CastleCenter.y - 8f)
                throw new InvalidOperationException("Castle gate or keep door blocked by " + (hit.collider != null ? hit.collider.name : "nothing"));

            // Walkable: from the dock to the village, the castle yard and the throne room.
            var baked = AssetDatabase.LoadAssetAtPath<NavMeshData>($"{NavMeshDir}/Island3.asset");
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(baked);
            try
            {
                Vector3 start = island.Find("Island3Arrival").position;
                (string, Vector3)[] goals = new[]
                {
                    ("village", OnGround(I3At(68f, 0f, -942f))),
                    ("castle yard", new Vector3(CastleCenter.x, Island3CliffTop + 0.3f, CastleCenter.y + 13f)),
                    ("throne room", new Vector3(CastleCenter.x, Island3CliffTop + 0.3f, CastleCenter.y - 6f)),
                    ("pools", OnGround(new Vector3(Island3Pools[0].centre.x - 9f, 0f, Island3Pools[0].centre.y + 4f)))
                };
                if (!NavMesh.SamplePosition(start, out NavMeshHit a, 3f, NavMesh.AllAreas)) throw new InvalidOperationException("No navmesh at the island three arrival");
                // Into the houses: up the steps, over the porch, through the door to the middle of the room.
                var rooms = new List<(string, Vector3)>();
                foreach (Transform house in island.Find("PirateVillage"))
                {
                    Transform floor = house.Find("RoomFloor");
                    if (floor != null && rooms.Count < 4) rooms.Add((house.name + " room", floor.position + Vector3.up * 0.15f));
                }
                if (TripoPrefab(TripoShacks[0]) != null && rooms.Count == 0) throw new InvalidOperationException("No walk-in house rooms");
                Transform barFloor = island.Find("PirateTavern/RoomFloor");
                if (TripoPrefab(TripoTavern) != null && barFloor == null) throw new InvalidOperationException("No tavern");
                if (barFloor != null)
                    rooms.Add(("PirateTavern bar", barFloor.position + Vector3.up * 0.15f));
                goals = goals.Concat(rooms).ToArray();
                foreach ((string name, Vector3 goal) in goals)
                {
                    if (!NavMesh.SamplePosition(goal, out NavMeshHit b, 3f, NavMesh.AllAreas)) throw new InvalidOperationException($"No navmesh at the {name}");
                    if (name.StartsWith("Pirate") && Mathf.Abs(b.position.y - goal.y) > 0.5f)
                        throw new InvalidOperationException($"No walkable floor in {name} (nearest walkable ground is {goal.y - b.position.y:0.0} m lower)");
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidOperationException($"No walking route from the dock to the {name}");
                }
            }
            finally { instance.Remove(); }
        }

        private static void CaptureIsland3()
        {
            float west = Island3Center.x - 60f;
            while (west > -260f && BeachHeight(west, -880f) > WaterLevel) west -= 0.5f;
            float north = Island3Center.y + 80f;
            float northX = Island3Center.x + 60f;
            while (north < -600f && BeachHeight(northX, north) > WaterLevel) north += 0.5f;
            Vector2 c = CastleCenter, g = VillageHouses[0].at;
            Transform shack = GameObject.Find("Environment/Island3/PirateVillage")?.transform.GetChild(0);
            Transform room = shack != null ? shack.Find("RoomFloor") : null;
            Vector3 roomAt = room != null ? room.position : new Vector3(g.x, 2f, g.y);
            Vector3 frontAt = shack != null ? shack.TransformPoint(new Vector3(0f, room != null ? room.localPosition.y + 1.6f : 2f, 9f)) : roomAt;
            Vector3 insideAt = shack != null ? shack.TransformPoint(new Vector3(0f, room != null ? room.localPosition.y + 1.7f : 2f, 1.2f)) : roomAt;
            Vector3 backAt = shack != null ? shack.TransformPoint(new Vector3(0f, room != null ? room.localPosition.y + 1.2f : 2f, -3f)) : roomAt;
            Transform bar = GameObject.Find("Environment/Island3/PirateTavern")?.transform;
            Vector3 barFront = bar != null ? bar.TransformPoint(new Vector3(-3f, 5f, 24f)) : roomAt, barAt = bar != null ? bar.TransformPoint(new Vector3(0f, 3f, 0f)) : roomAt;
            Vector3 barIn = bar != null ? bar.TransformPoint(new Vector3(0.3f, 3.4f, 3.6f)) : roomAt, barBack = bar != null ? bar.TransformPoint(new Vector3(0f, 2.4f, -6f)) : roomAt;
            CaptureViews("Screenshots/Review/Island3", new[]
        {
            ("coast-west", new Vector3(west + 7f, 2.4f, -868f), new Vector3(west - 4f, 0.2f, -888f)),
            ("coast-north", new Vector3(northX - 12f, 3f, north - 6f), new Vector3(northX + 10f, 0.3f, north + 2f)),
            ("overview", I3At(330f, 230f, -690f), I3At(20f, 0f, -930f)),
            ("overview-south", I3At(-120f, 170f, -1290f), I3At(40f, 0f, -960f)),
            ("arrival", I3At(178f, 4f, -905f), I3At(60f, 5f, -940f)),
            ("village", new Vector3(VillageSquare.x + 30f, 16f, VillageSquare.y + 40f), new Vector3(VillageSquare.x - 4f, 3f, VillageSquare.y - 14f)),
            ("village-lane", new Vector3(VillageSquare.x - 2f, 4.5f, VillageSquare.y + 36f), new Vector3(VillageSquare.x, 4f, VillageSquare.y - 20f)),
            ("jungle", new Vector3(-30f, 30f, -900f), new Vector3(-20f, 4f, -930f)),
            ("jungle-inside", OnGround(new Vector3(-12f, 0f, -905f)) + Vector3.up * 1.7f, OnGround(new Vector3(-40f, 0f, -925f)) + Vector3.up * 2f),
            ("crag-falls", new Vector3(CragFalls.x + 14f, 12f, Island3Spring.y - 32f), new Vector3(CragFalls.x, 9f, CragFalls.y)),
            ("crags", I3At(130f, 35f, -700f), I3At(15f, 18f, -760f)),
            ("castle", new Vector3(c.x + 3f, 32f, c.y + 75f), new Vector3(c.x, 22f, c.y)),
            ("gate", new Vector3(c.x + 9f, Island3CliffTop + 3f, c.y + 50f), new Vector3(c.x, Island3CliffTop + 6f, c.y + 20f)),
            ("house", new Vector3(g.x + 16f, 4f, g.y - 18f), new Vector3(g.x, 2f, g.y)),
            ("house-door", frontAt, roomAt + Vector3.up * 1.2f),
            ("house-inside", insideAt, backAt),
            ("tavern", barFront, barAt),
            ("tavern-inside", barIn, barBack),
            ("throne", new Vector3(c.x, Island3CliffTop + 2f, c.y + 8f), new Vector3(c.x, Island3CliffTop + 1.5f, c.y - 12f)),
            ("waterfall", new Vector3(CaveX + 25f, 6f, CaveFaceZ - 45f), new Vector3(CaveX, 8f, CaveFaceZ)),
            ("cave", new Vector3(CaveX, 2.5f, CaveFaceZ + 3f), new Vector3(CaveX, 0.5f, CaveFaceZ + 26f)),
        });
        }

        public static void ReviewIsland3Batch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CaptureIsland3();
        }

        /// <summary>Batch: open the Game scene, build island three, then (with -pdd-player) the Windows player.</summary>
        public static void BuildIsland3Batch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildIsland3InScene();
            if (!Environment.GetCommandLineArgs().Contains("-pdd-player")) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = BuildExe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.totalErrors + " errors");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[Island3] Windows build succeeded.");
        }

        /// <summary>Batch: the Windows player from the saved Game scene as it is (no scene rebuild). -pdd-out &lt;exe&gt; to build elsewhere.</summary>
        public static void BuildSavedScenePlayerBatch()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int outAt = Array.IndexOf(cl, "-pdd-out");
            string exe = outAt >= 0 && outAt + 1 < cl.Length ? cl[outAt + 1] : BuildExe;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = exe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.totalErrors + " errors");
            File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(exe)!, "steam_appid.txt"), true);
            Debug.Log("[Build] Player from the saved scene: " + exe);
        }

        public static void ReviewIsland3()
        {
            VerifyIsland3(GameObject.Find("Environment/Island3").transform);
            CaptureIsland3();
            Debug.Log("[Island3] Checks passed; review pictures saved.");
        }
    }

    // One-shot request: the already-open editor builds island three without losing unsaved work elsewhere.
    [InitializeOnLoad]
    internal static class Island3EditorRequest
    {
        static Island3EditorRequest() { EditorApplication.update += Tick; }

        private static void Tick()
        {
            const string request = "Logs/island3-request.txt", result = "Logs/island3-result.txt";
            // Only an open editor takes the request (batch runs of other tools must not consume it).
            if (Application.isBatchMode || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (action == "review") GameSceneBuilder.ReviewIsland3();
                else GameSceneBuilder.BuildIsland3InScene();
                File.WriteAllText(result, "PASS: " + action);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                File.WriteAllText(result, "FAIL: " + e);
            }
        }
    }
}
