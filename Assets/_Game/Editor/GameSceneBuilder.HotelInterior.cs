using System;
using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The hotel's ground floor, opened up across the whole building (2026-10-09). Hotel-local metres: floor top y .3,
    /// ceiling y 3.5, front wall z -1, back wall z -18.4. The reception lobby keeps x -11.85..11.85 and grows a back
    /// hall (fountain, piano, elevators behind the reception wall); the doctor's room is the east wing (x 12..23.4,
    /// the story bed moves in there) and the casino the west wing behind a black door, with a blackjack table.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const float InteriorBack = -18.4f, InteriorFront = -1f, PartitionX = 12f, WingOuterX = 23.5f;
        private const float WingDoorZ = -5.2f, WingDoorWidth = 1.6f, WingDoorHeight = 2.4f;
        private static float WingDoorNear => WingDoorZ + WingDoorWidth * .5f;   // -4.4
        private static float WingDoorFar => WingDoorZ - WingDoorWidth * .5f;    // -6.0

        /// <summary>The old box hotel's floor, ceiling and walls, stretched over the whole ground floor with two doorways.</summary>
        private static void ExpandHotelShell(Transform hotel)
        {
            float depth = InteriorFront - InteriorBack, midZ = (InteriorFront + InteriorBack) * .5f;
            var ivory = GetMaterial("HotelFinishedIvory", new Color(.96f, .93f, .85f), smoothness: .12f);
            void Place(string name, Vector3 p, Vector3 s)
            {
                Transform t = hotel.Find(name);
                if (t == null)
                {
                    t = Primitive(PrimitiveType.Cube, name, hotel, p, s, ivory).transform;
                    TagSurface(t.gameObject, SurfaceKind.Wood);
                }
                t.localPosition = p; t.localScale = s; t.localRotation = Quaternion.identity;
            }
            Place("Floor", new Vector3(0, .15f, midZ), new Vector3(WingOuterX * 2 + .3f, .3f, depth + .3f));
            Place("Ceiling", new Vector3(0, 3.65f, midZ), new Vector3(WingOuterX * 2 + .6f, .3f, depth + .6f));
            Place("WallBack", new Vector3(0, 1.9f, InteriorBack - .15f), new Vector3(WingOuterX * 2 + .3f, 3.2f, .3f));
            foreach (int side in new[] { -1, 1 })
            {
                string s = side < 0 ? "Left" : "Right";
                float nearLen = InteriorFront - WingDoorNear + .15f, farLen = WingDoorFar - InteriorBack;
                Place("Wall" + s, new Vector3(side * PartitionX, 1.9f, (WingDoorFar + InteriorBack) * .5f), new Vector3(.3f, 3.2f, farLen));
                Place("Wall" + s + "Front", new Vector3(side * PartitionX, 1.9f, WingDoorNear + nearLen * .5f - .15f), new Vector3(.3f, 3.2f, nearLen));
                Place("Wall" + s + "Lintel", new Vector3(side * PartitionX, (.3f + WingDoorHeight + 3.5f) * .5f, WingDoorZ),
                    new Vector3(.3f, 3.5f - .3f - WingDoorHeight, WingDoorWidth + .02f));
                Place("WingWallOuter" + s, new Vector3(side * (WingOuterX + .15f), 1.9f, midZ), new Vector3(.3f, 3.2f, depth + .3f));
                float frontLen = WingOuterX - PartitionX + .3f;
                Place("WingWallFront" + s, new Vector3(side * (PartitionX + frontLen * .5f - .15f), 1.9f, InteriorFront), new Vector3(frontLen, 3.2f, .3f));
            }
        }

        /// <summary>Logs where players arrive on island 2 against the ground and sea under them (Logs/island2-spawns.txt).</summary>
        public static void DumpIsland2SpawnsBatch()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ScenePath);
            Physics.SyncTransforms();
            var log = new System.Text.StringBuilder();
            foreach (string name in new[] { "Island2Spawn", "HotelArrival", "TravelPad_Island2", "Npc_Receptionist", "HotelDoor" })
            {
                var t = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == name);
                if (t == null) { log.AppendLine(name + ": MISSING"); continue; }
                Vector3 p = t.position;
                var hits = Physics.RaycastAll(p + Vector3.up * 40, Vector3.down, 80, ~0, QueryTriggerInteraction.Ignore)
                    .OrderBy(h => h.distance).Select(h => $"{h.collider.name}@{h.point.y:F2}");
                log.AppendLine($"{name} ({t.parent?.name}): {p.x:F2} {p.y:F2} {p.z:F2}  ground hits: {string.Join(", ", hits)}");
            }
            System.IO.File.WriteAllText("Logs/island2-spawns.txt", log.ToString());
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>
        /// A flat-shaded ring sector prism round the local y axis: radius r0..r1 (r0 0 = a pie/disc), height y0..y1,
        /// angle a0..a1 degrees measured from +x towards +z. Saved under Meshes/HotelInterior.
        /// </summary>
        private static Mesh RingMesh(string name, float r0, float r1, float y0, float y1, float a0, float a1, int segments)
        {
            string path = MeshDir + "/HotelInterior/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var v = new List<Vector3>(); var tris = new List<int>();
            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0) (p1, p3) = (p3, p1);
                int b = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            Vector3 P(float r, float a, float y) => new Vector3(r * Mathf.Cos(a * Mathf.Deg2Rad), y, r * Mathf.Sin(a * Mathf.Deg2Rad));
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segments), b = Mathf.Lerp(a0, a1, (i + 1) / (float)segments);
                Vector3 mid = P(1, (a + b) * .5f, 0);
                Quad(P(r0, a, y1), P(r1, a, y1), P(r1, b, y1), P(r0, b, y1), Vector3.up);
                Quad(P(r0, a, y0), P(r1, a, y0), P(r1, b, y0), P(r0, b, y0), Vector3.down);
                Quad(P(r1, a, y0), P(r1, b, y0), P(r1, b, y1), P(r1, a, y1), mid);
                if (r0 > 0) Quad(P(r0, a, y0), P(r0, b, y0), P(r0, b, y1), P(r0, a, y1), -mid);
            }
            if (a1 - a0 < 359.9f)
                foreach (float a in new[] { a0, a1 })
                {
                    Vector3 side = a == a0 ? -P(1, a + 90, 0) : P(1, a + 90, 0);
                    Quad(P(r0, a, y0), P(r1, a, y0), P(r1, a, y1), P(r0, a, y1), side);
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            System.IO.Directory.CreateDirectory(MeshDir + "/HotelInterior");
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static GameObject MeshPiece(Transform parent, string name, Mesh mesh, Vector3 p, float yaw, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.localPosition = p; go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        private static void PointLight(Transform parent, string name, Vector3 p, Color colour, float intensity, float range)
        {
            var light = new GameObject(name).AddComponent<Light>(); light.transform.SetParent(parent, false);
            light.transform.localPosition = p; light.type = LightType.Point; light.color = colour;
            light.intensity = intensity; light.range = range; light.shadows = LightShadows.None;
        }

        /// <summary>Three tiered gold rings of candle bulbs and crystal drops, a rose on the ceiling and a warm light.</summary>
        private static void Chandelier(Transform root, Vector3 c, float scale = 1f)
        {
            var gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            var bulb = GetMaterial("LobbyBulb", new Color(1f, .9f, .7f), emission: new Color(1f, .74f, .4f) * 2.2f);
            var crystal = GetMaterial("LobbyCrystal", new Color(.95f, .97f, 1f), .2f, .97f);
            Primitive(PrimitiveType.Cylinder, "CeilingRose", root, c + Vector3.up * 3.49f, new Vector3(.7f, .01f, .7f) * scale, gold, false);
            Primitive(PrimitiveType.Cylinder, "ChandelierRod", root, c + Vector3.up * 3.2f, new Vector3(.04f, .3f, .04f), gold, false);
            (float r, float y, int n)[] tiers = { (.62f, 2.88f, 12), (.42f, 2.66f, 9), (.22f, 2.46f, 6) };
            foreach (var (r0, y, n) in tiers)
            {
                float r = r0 * scale;
                Primitive(PrimitiveType.Cylinder, "ChandelierTier", root, c + Vector3.up * y, new Vector3(r * 2, .0175f, r * 2), gold, false);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    Vector3 rim = c + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    Primitive(PrimitiveType.Sphere, "ChandelierCandle", root, rim + Vector3.up * .07f, Vector3.one * .07f, bulb, false);
                    Primitive(PrimitiveType.Sphere, "ChandelierCrystal", root, rim - Vector3.up * .1f, new Vector3(.045f, .1f, .045f), crystal, false);
                }
            }
            Primitive(PrimitiveType.Sphere, "ChandelierFinial", root, c + Vector3.up * 2.3f, new Vector3(.12f, .2f, .12f), crystal, false);
            PointLight(root, "ChandelierLight", c + Vector3.up * 2.6f, new Color(1f, .82f, .6f), 1.6f, 8);
        }

        // ------------------------------------------------------------------ lobby back hall

        /// <summary>
        /// Behind the reception: a wall carries the reception backdrop (z -13); right of it marble columns open onto a
        /// back hall with a gold fountain and a grand piano; behind the wall, an elevator alcove.
        /// </summary>
        private static void BuildLobbyBackHall(Transform root)
        {
            var gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            var cream = GetMaterial("HotelFinishedIvory", new Color(.96f, .93f, .85f), smoothness: .12f);
            var marbleWhite = GetMaterial("LobbyMarbleWhite", new Color(.95f, .94f, .91f), 0f, .7f);
            var water = GetMaterial("LobbyFountainWater", new Color(.20f, .55f, .62f), 0f, .95f);
            var black = GetMaterial("LobbyPianoBlack", new Color(.015f, .015f, .02f), 0f, .55f);
            var ivoryKeys = GetMaterial("LobbyPianoKeys", new Color(.97f, .96f, .92f), 0f, .5f);
            var brushed = GetMaterial("LobbyBrushedGold", new Color(.86f, .68f, .38f), .9f, .45f);
            var glow = GetMaterial("LobbyButtonGlow", new Color(1f, .85f, .5f), emission: new Color(1f, .7f, .3f) * 1.6f);

            // The reception wall (solid) from the left wall to x -0.75; the backdrop already hangs on its front face.
            Primitive(PrimitiveType.Cube, "ReceptionWall", root, new Vector3(-6.3f, 1.9f, -13.1f), new Vector3(11.1f, 3.2f, .3f), cream);
            Primitive(PrimitiveType.Cube, "ReceptionWallEnd", root, new Vector3(-.68f, 1.9f, -13.1f), new Vector3(.2f, 3.2f, .5f), cream, false);
            Primitive(PrimitiveType.Cube, "ReceptionWallEndCap", root, new Vector3(-.68f, 3.3f, -13.1f), new Vector3(.26f, .14f, .56f), gold, false);
            Primitive(PrimitiveType.Cube, "ReceptionWallEndBase", root, new Vector3(-.68f, .42f, -13.1f), new Vector3(.26f, .2f, .56f), gold, false);

            // Marble columns between the lobby and the back hall.
            foreach (float x in new[] { 2.6f, 6.2f, 9.8f })
            {
                Vector3 c = new Vector3(x, 0, -13.1f);
                Primitive(PrimitiveType.Cylinder, "HallColumn", root, c + Vector3.up * 1.9f, new Vector3(.56f, 1.6f, .56f), marbleWhite);
                Primitive(PrimitiveType.Cylinder, "HallColumnBase", root, c + Vector3.up * .42f, new Vector3(.74f, .12f, .74f), gold, false);
                Primitive(PrimitiveType.Cube, "HallColumnPlinth", root, c + Vector3.up * .36f, new Vector3(.8f, .12f, .8f), marbleWhite, false);
                Primitive(PrimitiveType.Cylinder, "HallColumnCap", root, c + Vector3.up * 3.33f, new Vector3(.74f, .1f, .74f), gold, false);
            }

            // Fountain: a marble basin with a gold rim, water, and two gold bowls on a stem.
            Vector3 f = new Vector3(6.2f, .3f, -15.8f);
            var basin = MeshPiece(root, "FountainBasin", RingMesh("FountainBasin", 1.35f, 1.6f, 0, .55f, 0, 360, 40), f, 0, marbleWhite);
            var col = basin.AddComponent<CapsuleCollider>(); col.radius = 1.6f; col.height = 3.2f; col.center = new Vector3(0, 1.6f, 0);
            MeshPiece(root, "FountainRim", RingMesh("FountainRim", 1.3f, 1.66f, .55f, .62f, 0, 360, 40), f, 0, gold);
            MeshPiece(root, "FountainWater", RingMesh("FountainWater", 0, 1.36f, .38f, .42f, 0, 360, 40), f, 0, water);
            Primitive(PrimitiveType.Cylinder, "FountainStem", root, f + Vector3.up * .9f, new Vector3(.22f, .5f, .22f), marbleWhite, false);
            MeshPiece(root, "FountainBowl", RingMesh("FountainBowl", 0, .75f, 1.2f, 1.32f, 0, 360, 32), f, 0, gold);
            MeshPiece(root, "FountainBowlWater", RingMesh("FountainBowlWater", 0, .66f, 1.32f, 1.33f, 0, 360, 32), f, 0, water);
            Primitive(PrimitiveType.Cylinder, "FountainStemTop", root, f + Vector3.up * 1.6f, new Vector3(.12f, .28f, .12f), gold, false);
            MeshPiece(root, "FountainBowlTop", RingMesh("FountainBowlTop", 0, .38f, 1.86f, 1.94f, 0, 360, 24), f, 0, gold);
            Primitive(PrimitiveType.Sphere, "FountainFinial", root, f + Vector3.up * 2.05f, Vector3.one * .16f, gold, false);
            Chandelier(root, new Vector3(f.x, 0, f.z));

            // Grand piano (lid open) and its bench, angled toward the lobby.
            var piano = new GameObject("GrandPiano").transform; piano.SetParent(root, false);
            piano.localPosition = new Vector3(1.9f, .3f, -16.2f); piano.localRotation = Quaternion.Euler(0, 25, 0);
            Primitive(PrimitiveType.Cube, "PianoBody", piano, new Vector3(0, .82f, 0), new Vector3(1.5f, .3f, 1.9f), black);
            MeshPiece(piano, "PianoTail", RingMesh("PianoTail", 0, .75f, .67f, .97f, 180, 360, 20), new Vector3(0, 0, -.95f), 0, black);
            Primitive(PrimitiveType.Cube, "PianoKeys", piano, new Vector3(0, .9f, 1.05f), new Vector3(1.42f, .05f, .22f), ivoryKeys, false);
            Primitive(PrimitiveType.Cube, "PianoKeyBlock", piano, new Vector3(0, .98f, 1.1f), new Vector3(1.5f, .06f, .12f), black, false);
            var lid = Primitive(PrimitiveType.Cube, "PianoLid", piano, new Vector3(-.35f, 1.35f, -.3f), new Vector3(.03f, 1.0f, 1.9f), black, false);
            lid.transform.localRotation = Quaternion.Euler(0, 0, 35);
            foreach (var p in new[] { new Vector3(-.65f, .34f, .8f), new Vector3(.65f, .34f, .8f), new Vector3(0, .34f, -1.5f) })
            {
                Primitive(PrimitiveType.Cylinder, "PianoLeg", piano, p, new Vector3(.12f, .34f, .12f), black, false);
                Primitive(PrimitiveType.Cylinder, "PianoCaster", piano, new Vector3(p.x, .03f, p.z), new Vector3(.14f, .03f, .14f), gold, false);
            }
            Primitive(PrimitiveType.Cube, "PianoBench", piano, new Vector3(0, .48f, 1.75f), new Vector3(.9f, .08f, .38f), black);
            foreach (float x in new[] { -.4f, .4f })
                Primitive(PrimitiveType.Cube, "PianoBenchLeg", piano, new Vector3(x, .22f, 1.75f), new Vector3(.05f, .44f, .3f), black, false);

            // Elevator alcove behind the reception wall: two brushed-gold lifts with floor dials and call buttons.
            foreach (float x in new[] { -8.6f, -3.9f })
            {
                float z = InteriorBack + .02f;
                Primitive(PrimitiveType.Cube, "ElevatorFrame", root, new Vector3(x, 1.65f, z + .06f), new Vector3(2.0f, 2.7f, .1f), gold, false);
                Primitive(PrimitiveType.Cube, "ElevatorDoor", root, new Vector3(x - .41f, 1.55f, z + .12f), new Vector3(.8f, 2.45f, .04f), brushed, false);
                Primitive(PrimitiveType.Cube, "ElevatorDoor", root, new Vector3(x + .41f, 1.55f, z + .12f), new Vector3(.8f, 2.45f, .04f), brushed, false);
                Primitive(PrimitiveType.Cube, "ElevatorDial", root, new Vector3(x, 3.18f, z + .1f), new Vector3(.9f, .3f, .04f), gold, false);
                Primitive(PrimitiveType.Cube, "ElevatorDialFace", root, new Vector3(x, 3.18f, z + .125f), new Vector3(.8f, .22f, .01f), black, false);
                Primitive(PrimitiveType.Cube, "ElevatorPanel", root, new Vector3(x + 1.35f, 1.35f, z + .07f), new Vector3(.18f, .4f, .04f), gold, false);
                foreach (float y in new[] { 1.43f, 1.27f })
                    Primitive(PrimitiveType.Sphere, "ElevatorButton", root, new Vector3(x + 1.35f, y, z + .095f), Vector3.one * .06f, glow, false);
            }
            Chandelier(root, new Vector3(-6.2f, 0, -15.8f), .85f);
        }

        // ------------------------------------------------------------------ doctor

        /// <summary>The east wing: a clinic. The story bed moves in (by the window wall), two more beds behind curtains,
        /// the doctor's desk, a medicine cabinet, a sink and an eye chart.</summary>
        private static void BuildDoctorRoom(Transform hotel, Transform root)
        {
            var white = GetMaterial("White", new Color(.95f, .95f, .95f));
            var red = GetMaterial("RescueRed", new Color(.86f, .16f, .13f));
            var sheet = GetMaterial("BedSheet", new Color(.8f, .9f, 1f));
            var gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            var tile = GetMaterial("ClinicTile", new Color(.86f, .91f, .90f), 0f, .35f);
            var mint = GetMaterial("ClinicMint", new Color(.55f, .78f, .74f), 0f, .2f);
            var curtain = GetMaterial("ClinicCurtain", new Color(.62f, .80f, .90f), 0f, .1f);
            var steel = GetMaterial("ClinicSteel", new Color(.75f, .77f, .80f), .8f, .6f);
            var wood = GetMaterial("LobbyWalnut", new Color(.30f, .17f, .09f), 0f, .55f);
            var dark = GetMaterial("ClinicScreen", new Color(.05f, .06f, .07f), 0f, .6f);
            var screen = GetMaterial("ClinicScreenGlow", new Color(.3f, .7f, .9f), emission: new Color(.2f, .55f, .8f) * 1.3f);
            var panel = GetMaterial("ClinicLightPanel", new Color(.95f, .98f, 1f), emission: new Color(.9f, .95f, 1f) * 1.6f);
            var glass = GetMaterial("ClinicGlass", new Color(.75f, .88f, .92f), .1f, .95f);
            Color[] bottleColours = { new(.8f, .25f, .2f), new(.25f, .5f, .85f), new(.95f, .8f, .3f), new(.3f, .7f, .4f), new(.95f, .95f, .95f) };

            GameObject Box(string name, Vector3 p, Vector3 s, Material m, bool solid = false) => Primitive(PrimitiveType.Cube, name, root, p, s, m, solid);
            GameObject Cyl(string name, Vector3 p, float r, float h, Material m, bool solid = false) =>
                Primitive(PrimitiveType.Cylinder, name, root, p, new Vector3(r * 2, h * .5f, r * 2), m, solid);
            const float x0 = PartitionX + .15f, x1 = WingOuterX;
            float midX = (x0 + x1) * .5f, midZ = (InteriorFront + InteriorBack) * .5f, lenZ = InteriorFront - InteriorBack - .3f;

            Box("ClinicFloor", new Vector3(midX, .306f, midZ), new Vector3(x1 - x0, .012f, lenZ), tile);
            for (float x = x0 + 1.2f; x < x1; x += 1.2f) Box("ClinicTileJoint", new Vector3(x, .313f, midZ), new Vector3(.015f, .002f, lenZ), steel);
            // Mint dado and a white rail round the room.
            foreach (var (c, s) in new[] {
                (new Vector3(x1 - .02f, .75f, midZ), new Vector3(.03f, .9f, lenZ)),
                (new Vector3(midX, .75f, InteriorBack + .02f), new Vector3(x1 - x0, .9f, .03f)),
                (new Vector3(midX, .75f, InteriorFront - .17f), new Vector3(x1 - x0, .9f, .03f)),
                (new Vector3(x0 + .02f, .75f, (WingDoorFar + InteriorBack) * .5f), new Vector3(.03f, .9f, WingDoorFar - InteriorBack)),
                (new Vector3(x0 + .02f, .75f, (InteriorFront - .15f + WingDoorNear) * .5f), new Vector3(.03f, .9f, InteriorFront - .15f - WingDoorNear)) })
            {
                Box("ClinicDado", c, s, mint);
                Box("ClinicRail", c + Vector3.up * .47f, new Vector3(Mathf.Max(s.x, .05f), .05f, Mathf.Max(s.z, .05f)), white);
            }

            // Doorway from the lobby: white frame, red cross and DOCTOR over it on the lobby side.
            foreach (float z in new[] { WingDoorNear + .05f, WingDoorFar - .05f })
                Box("DoctorDoorJamb", new Vector3(PartitionX, 1.5f, z), new Vector3(.4f, 2.42f, .1f), white);
            Box("DoctorDoorHead", new Vector3(PartitionX, 2.75f, WingDoorZ), new Vector3(.4f, .1f, WingDoorWidth + .2f), white);
            Box("DoctorSignPlate", new Vector3(PartitionX - .17f, 3.1f, WingDoorZ), new Vector3(.03f, .5f, 2.4f), white);
            Box("DoctorSignCrossV", new Vector3(PartitionX - .19f, 3.1f, WingDoorZ + .85f), new Vector3(.02f, .36f, .12f), red);
            Box("DoctorSignCrossH", new Vector3(PartitionX - .19f, 3.1f, WingDoorZ + .85f), new Vector3(.02f, .12f, .36f), red);
            WorldText(root, "DoctorSign", new Vector3(PartitionX - .19f, 3.1f, WingDoorZ - .25f), "DOCTOR", 80, .035f, red.color)
                .transform.localRotation = Quaternion.Euler(0, 90, 0);

            // The story bed (and its patient point, sheet, pillow, cross and sign) by the outer wall, head to the wall.
            Vector3 bed = new Vector3(x1 - 1.45f, .65f, -9.6f);
            void Move(string name, Vector3 p, Vector3? scale = null, float yaw = 0)
            {
                Transform t = hotel.Find(name); if (t == null) throw new InvalidOperationException("Infirmary piece missing: " + name);
                t.localPosition = p; t.localRotation = Quaternion.Euler(0, yaw, 0); if (scale.HasValue) t.localScale = scale.Value;
            }
            Move("HospitalBed", bed);
            Move("Sheet", bed + Vector3.up * .27f);
            Move("Pillow", bed + new Vector3(.8f, .35f, 0));
            Move("PatientPoint", bed + Vector3.up * .35f, yaw: 90);
            Move("CrossV", new Vector3(x1 - .04f, 2.55f, bed.z), new Vector3(.02f, .9f, .3f));
            Move("CrossH", new Vector3(x1 - .04f, 2.55f, bed.z), new Vector3(.02f, .3f, .9f));
            Move("InfirmarySign", new Vector3(x1 - .04f, 1.85f, bed.z), yaw: 90);
            // Two more beds behind curtains, same make.
            foreach (float z in new[] { bed.z, -13.2f, -16.6f })
            {
                Vector3 b = new Vector3(bed.x, .65f, z);
                if (z != bed.z)
                {
                    Box("WardBed", b, new Vector3(2.1f, .5f, 1f), white, true);
                    Box("WardSheet", b + Vector3.up * .27f, new Vector3(2.05f, .06f, .95f), sheet);
                    Box("WardPillow", b + new Vector3(.8f, .35f, 0), new Vector3(.4f, .12f, .7f), white);
                }
                Box("Headboard", new Vector3(x1 - .3f, .95f, z), new Vector3(.06f, 1.1f, 1.1f), steel);
                foreach (float dz in new[] { -.52f, .52f })
                    Box("BedRail", b + new Vector3(-.1f, .45f, dz), new Vector3(1.4f, .04f, .03f), steel);
                Box("BedsideCabinet", new Vector3(x1 - .45f, .65f, z + .95f), new Vector3(.5f, .7f, .5f), white, true);
                Cyl("IVStand", new Vector3(b.x - .6f, 1.25f, z - .75f), .02f, 1.9f, steel);
                Box("IVBag", new Vector3(b.x - .6f, 1.95f, z - .75f), new Vector3(.16f, .26f, .05f), curtain);
                Cyl("IVFoot", new Vector3(b.x - .6f, .33f, z - .75f), .22f, .04f, steel);
                Box("CurtainRail", new Vector3(b.x - .6f, 3.3f, z - 1.6f), new Vector3(2.9f, .04f, .04f), steel);
                if (z != -16.6f)
                    for (int i = 0; i < 6; i++)
                        Box("Curtain", new Vector3(b.x - 1.8f + i * .48f, 1.95f, z - 1.6f + (i % 2) * .05f), new Vector3(.5f, 2.6f, .03f), curtain);
            }

            // Doctor's desk by the window wall at the front: monitor, keyboard, files, a chair and a visitor chair.
            Vector3 desk = new Vector3(midX + .6f, .3f, -3.0f);
            Box("DoctorDesk", desk + new Vector3(0, .38f, 0), new Vector3(2.2f, .06f, .9f), wood, true);
            foreach (float dx in new[] { -1.0f, 1.0f }) Box("DoctorDeskSide", desk + new Vector3(dx, .19f, 0), new Vector3(.08f, .38f, .85f), wood);
            Box("DoctorMonitor", desk + new Vector3(.3f, .7f, .25f), new Vector3(.6f, .38f, .04f), dark);
            Box("DoctorMonitorScreen", desk + new Vector3(.3f, .7f, .225f), new Vector3(.54f, .32f, .01f), screen);
            Box("DoctorMonitorStand", desk + new Vector3(.3f, .48f, .27f), new Vector3(.06f, .14f, .06f), dark);
            Box("DoctorKeyboard", desk + new Vector3(.3f, .42f, -.05f), new Vector3(.45f, .02f, .15f), dark);
            Box("DoctorFiles", desk + new Vector3(-.6f, .44f, 0), new Vector3(.35f, .08f, .26f), sheet);
            Box("DoctorChairSeat", desk + new Vector3(0, .47f, -.8f), new Vector3(.55f, .08f, .55f), dark);
            Box("DoctorChairBack", desk + new Vector3(0, .8f, -1.05f), new Vector3(.55f, .6f, .06f), dark);
            Cyl("DoctorChairPost", desk + new Vector3(0, .22f, -.8f), .04f, .44f, steel);
            Box("VisitorChairSeat", desk + new Vector3(-.2f, .47f, .9f), new Vector3(.5f, .06f, .5f), mint);
            Box("VisitorChairBack", desk + new Vector3(-.2f, .75f, 1.13f), new Vector3(.5f, .5f, .05f), mint);
            foreach (var d in new[] { new Vector2(-.42f, .68f), new Vector2(.02f, .68f), new Vector2(-.42f, 1.12f), new Vector2(.02f, 1.12f) })
                Box("VisitorChairLeg", desk + new Vector3(d.x, .22f, d.y), new Vector3(.03f, .44f, .03f), steel);
            // Certificates on the front wall behind the desk.
            for (int i = 0; i < 3; i++)
            {
                Box("DiplomaFrame", new Vector3(midX - .4f + i * .8f, 2.2f, InteriorFront - .17f), new Vector3(.6f, .45f, .02f), gold);
                Box("Diploma", new Vector3(midX - .4f + i * .8f, 2.2f, InteriorFront - .185f), new Vector3(.52f, .37f, .01f), sheet);
            }

            // Medicine cabinet on the lobby wall: white carcass, glass doors, coloured bottles on three shelves.
            Vector3 cab = new Vector3(x0 + .3f, .3f, -12.5f);
            Box("MedicineCabinet", cab + new Vector3(-.1f, 1.1f, 0), new Vector3(.3f, 2.2f, 1.6f), white, true);
            foreach (float dz in new[] { -.78f, .78f }) Box("CabinetSide", cab + new Vector3(.2f, 1.1f, dz), new Vector3(.3f, 2.2f, .04f), white);
            Box("CabinetTop", cab + new Vector3(.2f, 2.18f, 0), new Vector3(.3f, .04f, 1.6f), white);
            for (int s = 0; s < 3; s++)
            {
                float y = .7f + s * .55f;
                Box("CabinetShelf", cab + new Vector3(.2f, y, 0), new Vector3(.3f, .03f, 1.5f), white);
                for (int i = 0; i < 7; i++)
                {
                    var m = GetMaterial("ClinicBottle" + (i + s) % bottleColours.Length, bottleColours[(i + s) % bottleColours.Length], 0f, .7f);
                    float h = .18f + (i % 3) * .04f;
                    Cyl("Bottle", cab + new Vector3(.2f, y + .015f + h * .5f, -.6f + i * .2f), .05f, h, m);
                }
            }
            Box("CabinetGlass", cab + new Vector3(.36f, 1.25f, 0), new Vector3(.02f, 1.7f, 1.56f), glass);
            Box("CabinetCross", cab + new Vector3(.36f, 2.05f, 0), new Vector3(.02f, .12f, .4f), red);
            Box("CabinetCross", cab + new Vector3(.36f, 2.05f, 0), new Vector3(.02f, .4f, .12f), red);

            // Sink, scale and eye chart on the back wall.
            Box("Sink", new Vector3(midX - 2.0f, 1.0f, InteriorBack + .3f), new Vector3(.7f, .2f, .5f), white, true);
            Box("SinkPedestal", new Vector3(midX - 2.0f, .6f, InteriorBack + .2f), new Vector3(.25f, .6f, .25f), white);
            Box("Mirror", new Vector3(midX - 2.0f, 1.8f, InteriorBack + .03f), new Vector3(.6f, .8f, .02f), glass);
            Box("Tap", new Vector3(midX - 2.0f, 1.18f, InteriorBack + .12f), new Vector3(.04f, .14f, .12f), steel);
            Box("EyeChart", new Vector3(midX - 4.5f, 1.9f, InteriorBack + .03f), new Vector3(.7f, 1.1f, .02f), white);
            string[] rows = { "E", "F P", "T O Z", "L P E D", "P E C F D" };
            for (int i = 0; i < rows.Length; i++)
                WorldText(root, "EyeChartRow", new Vector3(midX - 4.5f, 2.3f - i * .19f, InteriorBack + .05f), rows[i], 60, .03f - i * .004f, Color.black)
                    .transform.localRotation = Quaternion.Euler(0, 180, 0);
            Box("ScalePlate", new Vector3(midX - 3.4f, .34f, InteriorBack + .6f), new Vector3(.45f, .05f, .35f), steel);
            Cyl("ScalePost", new Vector3(midX - 3.4f, .95f, InteriorBack + .45f), .03f, 1.2f, steel);

            // Bright clinic panels and two cool lights.
            foreach (float z in new[] { -4f, -9.6f, -15f })
                Box("ClinicCeilingPanel", new Vector3(midX, 3.48f, z), new Vector3(2.4f, .02f, 1.2f), panel);
            foreach (float z in new[] { -5f, -13f })
                PointLight(root, "ClinicLight", new Vector3(midX, 3.0f, z), new Color(.92f, .97f, 1f), 1.5f, 10);
        }

        // ------------------------------------------------------------------ casino

        /// <summary>The west wing: a casino behind a black lacquered door. A blackjack table in the middle, slot machines
        /// along the outer wall, a cashier's cage with stacks of money at the back (the pirates' target in chapter 3).</summary>
        private static void BuildCasino(Transform hotel, Transform root)
        {
            var gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            var black = GetMaterial("CasinoBlack", new Color(.025f, .025f, .03f), 0f, .85f);
            var carpet = GetMaterial("CasinoCarpet", new Color(.32f, .03f, .06f), 0f, .08f);
            var panelling = GetMaterial("CasinoPanelling", new Color(.13f, .05f, .05f), 0f, .55f);
            var felt = GetMaterial("CasinoFelt", new Color(.04f, .36f, .20f), 0f, .05f);
            var leather = GetMaterial("CasinoLeather", new Color(.07f, .035f, .025f), 0f, .25f);
            var wood = GetMaterial("LobbyWalnut", new Color(.30f, .17f, .09f), 0f, .55f);
            var card = GetMaterial("CasinoCard", new Color(.98f, .98f, .96f), 0f, .3f);
            var cardBack = GetMaterial("CasinoCardBack", new Color(.7f, .08f, .1f), 0f, .3f);
            var cash = GetMaterial("CasinoCash", new Color(.42f, .62f, .40f), 0f, .2f);
            var lampGlow = GetMaterial("CasinoLampGlow", new Color(1f, .9f, .7f), emission: new Color(1f, .78f, .45f) * 1.8f);
            Color[] chipColours = { new(.85f, .12f, .12f), new(.12f, .25f, .8f), new(.1f, .55f, .2f), new(.05f, .05f, .05f), new(.95f, .95f, .95f), new(.55f, .15f, .6f) };
            Material Chip(int i) => GetMaterial("CasinoChip" + i, chipColours[i % chipColours.Length], 0f, .5f);

            GameObject Box(string name, Vector3 p, Vector3 s, Material m, bool solid = false) => Primitive(PrimitiveType.Cube, name, root, p, s, m, solid);
            GameObject Cyl(string name, Vector3 p, float r, float h, Material m, bool solid = false) =>
                Primitive(PrimitiveType.Cylinder, name, root, p, new Vector3(r * 2, h * .5f, r * 2), m, solid);
            const float x0 = -WingOuterX, x1 = -PartitionX - .15f;
            float midX = (x0 + x1) * .5f, midZ = (InteriorFront + InteriorBack) * .5f, lenZ = InteriorFront - InteriorBack - .3f;

            // Black lacquered door with a gold frame; CASINO in gold over it on the lobby side, gold pilasters, a rope line.
            var old = hotel.Find("CasinoEntrance"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var entrance = new GameObject("CasinoEntrance").transform; entrance.SetParent(hotel, false);
            entrance.localPosition = new Vector3(-PartitionX, .3f, 0); entrance.localRotation = Quaternion.Euler(0, 90, 0);
            BuildDoor(entrance, "CasinoDoor", new MeshyArt.DoorSpec { Hinge = new Vector3(-WingDoorNear, 0, 0), Width = WingDoorWidth,
                Height = WingDoorHeight, LeafDirection = 1, WallFacing = 1 }, new Color(.02f, .02f, .025f), new Color(.85f, .65f, .28f), false);
            Box("CasinoPortal", new Vector3(-PartitionX + .17f, 2.9f, WingDoorZ), new Vector3(.03f, .55f, 2.6f), black);
            WorldText(root, "CasinoSign", new Vector3(-PartitionX + .19f, 2.92f, WingDoorZ), "CASINO", 90, .04f, gold.color)
                .transform.localRotation = Quaternion.Euler(0, -90, 0);
            foreach (float z in new[] { WingDoorNear + .35f, WingDoorFar - .35f })
            {
                Box("CasinoPilaster", new Vector3(-PartitionX + .2f, 1.6f, z), new Vector3(.1f, 2.6f, .25f), black);
                Box("CasinoPilasterCap", new Vector3(-PartitionX + .22f, 2.95f, z), new Vector3(.14f, .1f, .32f), gold);
                Cyl("RopePost", new Vector3(-PartitionX + 1.1f, .75f, z + (z > WingDoorZ ? .5f : -.5f)), .05f, .9f, gold);
                Primitive(PrimitiveType.Sphere, "RopePostTop", root, new Vector3(-PartitionX + 1.1f, 1.23f, z + (z > WingDoorZ ? .5f : -.5f)), Vector3.one * .12f, gold, false);
            }
            Box("CasinoMat", new Vector3(-PartitionX + .9f, .315f, WingDoorZ), new Vector3(1.4f, .01f, 1.8f), carpet);

            // Deep red carpet with a gold grid; dark mahogany panelling with gold strips; a black ceiling with gold coffers.
            Box("CasinoFloor", new Vector3(midX, .306f, midZ), new Vector3(x1 - x0, .012f, lenZ), carpet);
            for (float x = x0 + 1.6f; x < x1 - .5f; x += 1.6f) Box("CasinoCarpetGold", new Vector3(x, .313f, midZ), new Vector3(.03f, .002f, lenZ), gold);
            for (float z = InteriorFront - 1.6f; z > InteriorBack + .5f; z -= 1.6f) Box("CasinoCarpetGold", new Vector3(midX, .313f, z), new Vector3(x1 - x0, .002f, .03f), gold);
            foreach (var (c, s) in new[] {
                (new Vector3(x0 + .02f, 1.9f, midZ), new Vector3(.03f, 3.2f, lenZ)),
                (new Vector3(midX, 1.9f, InteriorBack + .02f), new Vector3(x1 - x0, 3.2f, .03f)),
                (new Vector3(midX, 1.9f, InteriorFront - .17f), new Vector3(x1 - x0, 3.2f, .03f)),
                (new Vector3(x1 - .02f, 1.9f, (WingDoorFar + InteriorBack) * .5f), new Vector3(.03f, 3.2f, WingDoorFar - InteriorBack)),
                (new Vector3(x1 - .02f, 1.9f, (InteriorFront - .15f + WingDoorNear) * .5f), new Vector3(.03f, 3.2f, InteriorFront - .15f - WingDoorNear)),
                (new Vector3(x1 - .02f, 3.1f, WingDoorZ), new Vector3(.03f, .8f, WingDoorWidth)) })
            {
                Box("CasinoPanelling", c, s, panelling);
                foreach (float y in new[] { 1.1f, 3.35f })
                    if (Mathf.Abs(y - c.y) < s.y * .5f)
                        Box("CasinoWallGold", new Vector3(c.x, y, c.z), new Vector3(Mathf.Max(s.x, .05f), .05f, Mathf.Max(s.z, .05f)), gold);
            }
            Box("CasinoCeiling", new Vector3(midX, 3.47f, midZ), new Vector3(x1 - x0, .02f, lenZ), black);
            foreach (float z in new[] { -5f, -9.5f, -14f })
            {
                Box("CasinoCofferGold", new Vector3(midX, 3.455f, z), new Vector3(6.2f, .02f, 3.4f), gold);
                Box("CasinoCoffer", new Vector3(midX, 3.45f, z), new Vector3(6.0f, .02f, 3.2f), black);
            }

            // Blackjack table: half-moon, dealer on the straight side (toward the outer wall), five players round the arc.
            Vector3 t = new Vector3(midX - 1.0f, .3f, -9.5f);
            const float yaw = 0;   // arc toward +x (the door)
            MeshPiece(root, "BlackjackBase", RingMesh("BlackjackBase", 0, 1.35f, .1f, .82f, -90, 90, 28), t, yaw, wood);
            MeshPiece(root, "BlackjackTop", RingMesh("BlackjackTop", 0, 1.45f, .82f, .9f, -90, 90, 28), t, yaw, wood);
            MeshPiece(root, "BlackjackFelt", RingMesh("BlackjackFelt", 0, 1.32f, .9f, .905f, -90, 90, 28), t, yaw, felt);
            MeshPiece(root, "BlackjackRail", RingMesh("BlackjackRail", 1.32f, 1.6f, .87f, 1.0f, -90, 90, 28), t, yaw, leather);
            MeshPiece(root, "BlackjackRailGold", RingMesh("BlackjackRailGold", 1.29f, 1.33f, .9f, .915f, -90, 90, 28), t, yaw, gold);
            Box("BlackjackDealerEdge", t + new Vector3(-.03f, .87f, 0), new Vector3(.08f, .1f, 3.2f), gold);
            Box("BlackjackPlinth", t + new Vector3(.3f, .05f, 0), new Vector3(1.0f, .1f, 2.2f), black);
            Collider(root, "BlackjackCollider", t + new Vector3(.75f, .5f, 0), new Vector3(1.6f, 1.0f, 3.1f));
            // Betting circles, chips and the dealt cards (two each, the dealer one up one down), the shoe and the rack.
            for (int i = 0; i < 5; i++)
            {
                float a = (-60 + i * 30) * Mathf.Deg2Rad;
                Vector3 spot = t + new Vector3(Mathf.Cos(a) * 1.08f, .906f, Mathf.Sin(a) * 1.08f);
                MeshPiece(root, "BetCircle", RingMesh("BetCircle", .1f, .12f, 0, .002f, 0, 360, 24), spot, 0, gold);
                for (int k = 0; k < 2 + i % 3; k++)
                    Cyl("BetChip", spot + Vector3.up * (.006f + k * .008f), .045f, .008f, Chip(i + k));
                Vector3 hand = t + new Vector3(Mathf.Cos(a) * .8f, .907f, Mathf.Sin(a) * .8f);
                for (int c = 0; c < 2; c++)
                {
                    var cardGo = Box("Card", hand + new Vector3(0, c * .002f, c * .04f), new Vector3(.088f, .002f, .063f), card);
                    cardGo.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + c * 12, 0);
                    Box("CardPip", hand + new Vector3(0, c * .002f + .0015f, c * .04f), new Vector3(.02f, .001f, .02f), (i + c) % 2 == 0 ? cardBack : black);
                }
            }
            Box("DealerCardUp", t + new Vector3(.2f, .907f, .06f), new Vector3(.088f, .002f, .063f), card);
            Box("DealerCardDown", t + new Vector3(.2f, .907f, -.06f), new Vector3(.088f, .002f, .063f), cardBack);
            Box("CardShoe", t + new Vector3(.25f, .97f, .9f), new Vector3(.2f, .12f, .35f), black);
            Box("ChipRack", t + new Vector3(.12f, .93f, 0), new Vector3(.14f, .05f, .9f), black);
            for (int i = 0; i < 6; i++)
                Cyl("RackChips", t + new Vector3(.12f, .985f, -.38f + i * .15f), .045f, .06f, Chip(i));
            WorldText(root, "BlackjackPays", t + new Vector3(.52f, .907f, 0), "BLACKJACK PAYS 3 TO 2", 60, .014f, gold.color)
                .transform.localRotation = Quaternion.Euler(90, -90, 0);
            WorldText(root, "BlackjackRule", t + new Vector3(.38f, .907f, 0), "DEALER STANDS ON ALL 17s", 50, .009f, new Color(.95f, .92f, .8f))
                .transform.localRotation = Quaternion.Euler(90, -90, 0);
            // Five stools round the arc, the dealer's stand behind, and a hanging lamp over the felt.
            for (int i = 0; i < 5; i++)
            {
                float a = (-60 + i * 30) * Mathf.Deg2Rad;
                Vector3 s = t + new Vector3(Mathf.Cos(a) * 2.05f, 0, Mathf.Sin(a) * 2.05f);
                Cyl("StoolSeat", s + Vector3.up * .78f, .22f, .1f, leather, true);
                Cyl("StoolRing", s + Vector3.up * .45f, .18f, .02f, gold);
                Cyl("StoolPost", s + Vector3.up * .42f, .035f, .72f, gold);
                Cyl("StoolFoot", s + Vector3.up * .02f, .2f, .04f, gold);
            }
            Box("DealerStand", t + new Vector3(-.55f, .05f, 0), new Vector3(.7f, .1f, 1.2f), black);
            Box("BlackjackLamp", t + new Vector3(.6f, 2.55f, 0), new Vector3(1.0f, .18f, 2.4f), black);
            Box("BlackjackLampGlow", t + new Vector3(.6f, 2.455f, 0), new Vector3(.9f, .01f, 2.3f), lampGlow);
            Box("BlackjackLampTrim", t + new Vector3(.6f, 2.65f, 0), new Vector3(1.04f, .03f, 2.44f), gold);
            foreach (float dz in new[] { -1f, 1f }) Box("BlackjackLampRod", t + new Vector3(.6f, 3.05f, dz), new Vector3(.02f, .82f, .02f), gold);
            PointLight(root, "BlackjackLight", t + new Vector3(.6f, 2.2f, 0), new Color(1f, .86f, .62f), 2.2f, 6);

            // Slot machines along the outer wall, facing the room.
            for (int i = 0; i < 6; i++)
            {
                Vector3 m = new Vector3(x0 + .45f, .3f, -2.4f - i * 1.15f);
                Box("SlotBase", m + new Vector3(0, .45f, 0), new Vector3(.6f, .9f, .8f), black, true);
                Box("SlotCabinet", m + new Vector3(0, 1.4f, 0), new Vector3(.55f, 1.0f, .75f), GetMaterial("CasinoSlot" + i % 3,
                    new[] { new Color(.65f, .05f, .08f), new Color(.08f, .12f, .45f), new Color(.45f, .3f, .05f) }[i % 3], .3f, .7f));
                Box("SlotTrim", m + new Vector3(.01f, 1.92f, 0), new Vector3(.57f, .04f, .77f), gold);
                Box("SlotScreen", m + new Vector3(.28f, 1.45f, 0), new Vector3(.01f, .4f, .55f),
                    GetMaterial("CasinoSlotScreen" + i % 3, new Color(.3f, .2f, .5f), emission: new[] { new Color(.9f, .3f, .2f), new Color(.2f, .6f, 1f), new Color(1f, .8f, .2f) }[i % 3] * 1.2f));
                for (int r = 0; r < 3; r++)
                    Box("SlotReel", m + new Vector3(.29f, 1.45f, -.17f + r * .17f), new Vector3(.005f, .26f, .13f), card);
                Box("SlotTopper", m + new Vector3(.05f, 2.05f, 0), new Vector3(.4f, .22f, .7f), lampGlow);
                Cyl("SlotLever", m + new Vector3(.05f, 1.5f, .45f), .02f, .4f, gold);
                Primitive(PrimitiveType.Sphere, "SlotLeverKnob", root, m + new Vector3(.05f, 1.72f, .45f), Vector3.one * .08f, Chip(0), false);
                Cyl("SlotStool", m + new Vector3(.9f, .62f, 0), .19f, .08f, leather, true);
                Cyl("SlotStoolPost", m + new Vector3(.9f, .32f, 0), .03f, .6f, gold);
            }

            // The cashier's cage at the back: a counter, gold bars, CASHIER over it, stacks of cash and chips behind.
            float cz = InteriorBack + 1.0f;
            Box("CashierCounter", new Vector3(midX, .85f, cz), new Vector3(5.0f, 1.1f, .6f), panelling, true);
            Box("CashierCounterTop", new Vector3(midX, 1.42f, cz), new Vector3(5.1f, .05f, .7f), black);
            for (float x = midX - 2.45f; x <= midX + 2.46f; x += .12f)
                Cyl("CashierBar", new Vector3(x, 2.4f, cz), .012f, 1.9f, gold);
            Box("CashierTopRail", new Vector3(midX, 3.35f, cz), new Vector3(5.0f, .06f, .06f), gold);
            Box("CashierWindow", new Vector3(midX, 1.75f, cz), new Vector3(.8f, .6f, .07f), black);
            WorldText(root, "CashierSign", new Vector3(midX, 3.15f, cz + .05f), "CASHIER", 70, .03f, gold.color)
                .transform.localRotation = Quaternion.Euler(0, 180, 0);
            var vault = new GameObject("CasinoMoney").transform; vault.SetParent(root, false);   // story anchor: the pirates' haul
            vault.localPosition = new Vector3(midX, 1.67f, InteriorBack + .3f);
            for (int i = 0; i < 9; i++)
                for (int k = 0; k < 3 + i % 3; k++)
                    Box("CashBrick", new Vector3(midX - 2.0f + i * .5f, 1.71f + k * .09f, InteriorBack + .3f), new Vector3(.42f, .08f, .2f), cash);
            Box("MoneyShelf", new Vector3(midX, 1.65f, InteriorBack + .3f), new Vector3(4.8f, .04f, .4f), wood);
            foreach (float dx in new[] { -2.3f, 2.3f }) Box("MoneyShelfBracket", new Vector3(midX + dx, 1.5f, InteriorBack + .2f), new Vector3(.05f, .3f, .2f), gold);
            WorldText(root, "CasinoBrand", new Vector3(x0 + .06f, 2.65f, -12.8f), "GRAND CORAL CASINO", 90, .035f, gold.color)
                .transform.localRotation = Quaternion.Euler(0, -90, 0);

            Chandelier(root, new Vector3(midX, 0, -4.6f), .9f);
            Chandelier(root, new Vector3(midX, 0, -14.4f), .9f);
        }
    }
}
