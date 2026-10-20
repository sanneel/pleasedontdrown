using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using PleaseDontDrown.World;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 2's resort, the rest of Docs/19 (2026-10-19, Tripo batch `prompts/island2-resort.md`), as one batch that can
    /// run again (it removes Environment/Island2Resort and builds it afresh):
    ///  * four over-water bungalows on stilts off the south shore, on a boardwalk out from the beach (walk out, stand on
    ///    their decks);
    ///  * cabana daybeds on the south beach by the boardwalk;
    ///  * the golf cart: the drivable cart (BarDining.AddGolfCart, a greybox) wears the Tripo cart, and a second one
    ///    stands parked in the pool's bay;
    ///  * a helipad with the resort's helicopter on the lawn inside the cart road's loop;
    ///  * a second yacht moored on the west side of the marina pier (look only; the hotel's yacht stays the one you sail).
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Island2ResortBatch</c>
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string Island2ResortName = "Island2Resort";
        private const float BoardwalkX = 0f, BoardwalkWidth = 2.4f, BoardwalkLength = 44f;

        public static void Island2ResortBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            if (env.Find(Island2ResortName) is { } old) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            var root = new GameObject(Island2ResortName).transform;
            root.SetParent(env, false);
            var log = new List<string>();

            BuildBungalows(root, log);
            Physics.SyncTransforms();
            BuildCabanas(root, log);
            DressGolfCarts(root, log);
            BuildHelipad(root, log);
            BuildSecondYacht(root, log);
            BuildTennisCourt(root, log);
            BuildSwimUpBar(root, log);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            foreach (string line in log) Debug.Log("[Island2Resort] " + line);
            Debug.Log($"[Island2Resort] PASS: {root.GetComponentsInChildren<Transform>().Length} objects");
        }

        /// <summary>A Tripo island 2 model placed by its feet (null without the model).</summary>
        private static GameObject ResortModel(string name, Transform parent, Vector3 feet, float yaw, float scale = 1f)
        {
            GameObject prefab = TripoIsland2(name);
            if (prefab == null) return null;
            GameObject go = Object.Instantiate(prefab, parent, false);
            go.name = "Tripo_" + name;
            go.transform.position = feet;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            return go;
        }

        /// <summary>A box collider round everything drawn under <paramref name="model"/>, on its parent (shrunk a little).</summary>
        private static BoxCollider BoxAround(GameObject model, float shrink = 0.9f)
        {
            Transform t = model.transform;
            Bounds b = TripoBounds(t, t);
            var box = model.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = Vector3.Scale(b.size, new Vector3(shrink, 1f, shrink));
            return box;
        }

        private static void MeshCollidersOn(GameObject model)
        {
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>())
                if (f.GetComponent<Collider>() == null) f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
        }

        /// <summary>Walking south from inland at <paramref name="x"/>: the z where the sand goes under the sea.</summary>
        private static float SouthWaterlineZ(float x)
        {
            for (float z = -290f; z > -400f; z -= 0.5f)
                if (GroundUnder(new Vector3(x, 0f, z)) < WaterLevel) return z;
            return -335f;
        }

        // ------------------------------------------------------------------ the over-water bungalows

        private static void BuildBungalows(Transform parent, List<string> log)
        {
            var root = new GameObject("Bungalows").transform;
            root.SetParent(parent, false);
            TagSurface(root.gameObject, SurfaceKind.Wood);
            float shore = SouthWaterlineZ(BoardwalkX);
            float start = shore + 6f; // on the sand, a few metres up the beach
            Material planks = GetMaterial("MarinaDeck", new Color(0.62f, 0.47f, 0.32f), smoothness: 0.2f);
            Material posts = GetMaterial("MarinaPost", new Color(0.36f, 0.26f, 0.18f), smoothness: 0.15f);

            // The bungalow's deck height above its feet, measured on the model (a ray up into its floor at the middle).
            GameObject probe = ResortModel("overwater_bungalow", root, new Vector3(0f, -200f, 0f), 0f);
            if (probe == null) { log.Add("no bungalow model: skipped"); return; }
            MeshCollidersOn(probe);
            Physics.SyncTransforms();
            float floor = 2.2f;
            if (Physics.Raycast(new Vector3(0f, -200f + 0.05f, -1.5f), Vector3.up, out RaycastHit under, 6f, ~0, QueryTriggerInteraction.Ignore))
                floor = under.point.y + 200f + 0.12f;
            Object.DestroyImmediate(probe);

            float deckTop = 1.25f; // above the sea, and the walk's top
            float feetY = deckTop - floor;
            float end = start - BoardwalkLength;
            float mid = (start + end) * 0.5f;
            Primitive(PrimitiveType.Cube, "Boardwalk", root, new Vector3(BoardwalkX, deckTop - 0.15f, mid), new Vector3(BoardwalkWidth, 0.3f, BoardwalkLength), planks);
            for (float z = start - 2f; z >= end; z -= 4f)
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = BoardwalkX + side * (BoardwalkWidth * 0.5f - 0.12f);
                    float bed = GroundUnder(new Vector3(x, 0f, z));
                    float top = deckTop + 0.9f; // the posts carry a rail
                    Primitive(PrimitiveType.Cylinder, "Post", root, new Vector3(x, (top + bed) * 0.5f, z), new Vector3(0.18f, (top - bed) * 0.5f, 0.18f), posts, keepCollider: false);
                }
            // Rails either side, broken where the bungalows' bridges join.
            var bridges = new[] { start - 16f, start - 32f };
            foreach (float side in new[] { -1f, 1f })
            {
                float x = BoardwalkX + side * (BoardwalkWidth * 0.5f - 0.12f);
                float from = start;
                foreach (float b in bridges.Append(end - 99f))
                {
                    float to = Mathf.Max(b + 1.6f, end);
                    if (from - to > 0.5f)
                        Primitive(PrimitiveType.Cube, "Rail", root, new Vector3(x, deckTop + 0.9f, (from + to) * 0.5f), new Vector3(0.1f, 0.08f, from - to), posts);
                    from = b - 1.6f;
                }
            }

            // Two bungalows each side, their fronts (decks, ladders) looking out to sea east and west, backs to the walk.
            int made = 0;
            foreach (float z in bridges)
                foreach (float side in new[] { -1f, 1f })
                {
                    float yaw = side > 0f ? 90f : -90f;
                    var at = new Vector3(BoardwalkX + side * (BoardwalkWidth * 0.5f + 1.2f + 4.9f), feetY, z);
                    GameObject b = ResortModel("overwater_bungalow", root, at, yaw);
                    MeshCollidersOn(b);
                    // Its stilts carried on down to the seabed (the model's stop just under its floor).
                    foreach (float px in new[] { -2.6f, 0f, 2.6f })
                        foreach (float pz in new[] { -3.2f, 0.4f, 3.2f })
                        {
                            Vector3 p = b.transform.TransformPoint(new Vector3(px, 0f, pz));
                            float bed = GroundUnder(p) - 0.3f;
                            float top = feetY + 0.6f;
                            Primitive(PrimitiveType.Cylinder, "Pile", root, new Vector3(p.x, (top + bed) * 0.5f, p.z), new Vector3(0.3f, (top - bed) * 0.5f, 0.3f), posts, keepCollider: false);
                        }
                    // A short bridge from the walk to its back deck.
                    Primitive(PrimitiveType.Cube, "Bridge", root, new Vector3(BoardwalkX + side * (BoardwalkWidth * 0.5f + 0.8f), deckTop - 0.15f, z),
                        new Vector3(1.8f, 0.3f, 2.4f), planks);
                    made++;
                }
            // A ladder down into the sea at the end (a lifeguard can always get out).
            Primitive(PrimitiveType.Cube, "EndLadder", root, new Vector3(BoardwalkX, (deckTop + WaterLevel - 1.2f) * 0.5f, end - 0.25f),
                new Vector3(0.9f, deckTop - WaterLevel + 1.2f, 0.1f), posts, keepCollider: false);
            log.Add($"bungalows: shore z {shore:F1}, walk z {start:F1}..{end:F1}, deck {deckTop:F2} (model floor {floor:F2} above its feet), {made} bungalows, seabed at the far end {GroundUnder(new Vector3(BoardwalkX, 0f, end)):F2}");
        }

        // ------------------------------------------------------------------ cabanas

        private static void BuildCabanas(Transform parent, List<string> log)
        {
            var root = new GameObject("Cabanas").transform;
            root.SetParent(parent, false);
            float shore = SouthWaterlineZ(BoardwalkX);
            int made = 0;
            foreach (float x in new[] { -28f, -20f, -12f, 12f, 20f, 28f })
            {
                float z = SouthWaterlineZ(x) + 7f;
                var feet = new Vector3(x, GroundUnder(new Vector3(x, 0f, z)), z);
                if (feet.y < WaterLevel + 0.3f || Blocked(feet, 1.6f)) { log.Add($"cabana at x {x}: no room"); continue; }
                GameObject c = ResortModel("beach_cabana", root, feet, 180f); // looking out to sea (south)
                if (c == null) break;
                BoxAround(c, 0.85f).isTrigger = false;
                made++;
            }
            log.Add($"cabanas: {made} on the south beach (shore z {shore:F1})");
        }

        // ------------------------------------------------------------------ golf carts

        private static void DressGolfCarts(Transform parent, List<string> log)
        {
            GameObject prefab = TripoIsland2("golf_cart");
            if (prefab == null) { log.Add("no golf cart model"); return; }
            // The drivable one: its greybox parts hidden, the model on it (the hull, seats and grips stay as they are).
            Transform cart = GameObject.Find("Environment/Hotel/GolfCart")?.transform;
            if (cart != null)
            {
                if (cart.Find("Tripo_golf_cart") is { } oldModel) Object.DestroyImmediate(oldModel.gameObject);
                foreach (Renderer r in cart.GetComponentsInChildren<Renderer>())
                    if (r.name is "Hull" or "Bench" or "Backrest" or "Dash" or "Roof" or "Post" or "Wheel" or "SteeringWheel") r.enabled = false;
                GameObject model = Object.Instantiate(prefab, cart, false);
                model.name = "Tripo_golf_cart";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
                log.Add($"the drivable cart wears the Tripo model ({TripoBounds(model.transform, cart).size})");
            }
            else log.Add("no drivable golf cart found (Environment/Hotel/GolfCart)");
            // A parked one in the pool's bay.
            if (GameObject.Find("CartParkingPool") is { } bay)
            {
                GameObject parked = ResortModel("golf_cart", parent, bay.transform.TransformPoint(new Vector3(-1.7f, 0f, 0f)), bay.transform.eulerAngles.y);
                BoxAround(parked, 0.95f);
                log.Add("a parked cart in the pool's bay");
            }
        }

        // ------------------------------------------------------------------ the helipad

        /// <summary>The first thing drawn under a point (colliders with no mesh drawn on them, like a sign's trigger box, are passed).</summary>
        private static bool RoofHit(Vector3 from, float length, out RaycastHit hit)
        {
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, length, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider is MeshCollider || h.collider.GetComponent<MeshRenderer>() != null) { hit = h; return true; }
            }
            hit = default;
            return false;
        }

        private static void BuildHelipad(Transform parent, List<string> log)
        {
            // On the open lawn inside the cart road's loop, south of the hotel (the hotel's roofs are terraces with rails
            // and signs: no flat 12 m anywhere up there but the porch over the front door).
            var spot = new Vector3(8f, 0f, -288f);
            spot.y = GroundUnder(spot);
            if (Blocked(spot, 6f)) log.Add("helipad: something on the lawn at " + spot);
            var best = (score: 0f, at: spot);
            var pad = new GameObject("Helipad").transform;
            pad.SetParent(parent, false);
            pad.position = best.at;
            Material dark = GetMaterial("HelipadDark", new Color(0.18f, 0.2f, 0.22f), smoothness: 0.2f);
            Material paint = GetMaterial("HelipadPaint", new Color(0.97f, 0.96f, 0.9f), smoothness: 0.3f);
            Material gold = GetMaterial("HelipadGold", new Color(0.86f, 0.68f, 0.28f), metallic: 0.6f, smoothness: 0.5f);
            Primitive(PrimitiveType.Cylinder, "Pad", pad, new Vector3(0f, 0.04f, 0f), new Vector3(11f, 0.04f, 11f), dark);
            Primitive(PrimitiveType.Cylinder, "Ring", pad, new Vector3(0f, 0.05f, 0f), new Vector3(10.2f, 0.04f, 10.2f), paint, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "RingInner", pad, new Vector3(0f, 0.055f, 0f), new Vector3(9.4f, 0.04f, 9.4f), dark, keepCollider: false);
            // The H, in gold (the resort's colours).
            Primitive(PrimitiveType.Cube, "H1", pad, new Vector3(-1.3f, 0.09f, 0f), new Vector3(0.6f, 0.02f, 4f), gold, keepCollider: false);
            Primitive(PrimitiveType.Cube, "H2", pad, new Vector3(1.3f, 0.09f, 0f), new Vector3(0.6f, 0.02f, 4f), gold, keepCollider: false);
            Primitive(PrimitiveType.Cube, "H3", pad, new Vector3(0f, 0.09f, 0f), new Vector3(2f, 0.02f, 0.6f), gold, keepCollider: false);
            GameObject heli = ResortModel("resort_helicopter", pad, best.at + Vector3.up * 0.08f, 35f, 0.9f);
            if (heli != null) BoxAround(heli, 0.5f);
            log.Add($"helipad on the lawn at {best.at}");
        }

        // ------------------------------------------------------------------ the swim-up bar

        /// <summary>
        /// A swim-up bar at the pool's east end (TeamB's pool: water x 54..70, z -256..-246): a tiled counter standing in
        /// the water along the east wall, three stools under the water in front of it, a ledge to stand on, and behind
        /// it on the deck a back bar with bottles under a thatched canopy. Look and stand only (no orders).
        /// </summary>
        private static void BuildSwimUpBar(Transform parent, List<string> log)
        {
            Rect w = PoolWater;
            // The pool's own water (TeamB): the deck is its edge, 0.15 m above it.
            GameObject poolWater = GameObject.Find("Environment/Island2Pool/PoolWater");
            if (poolWater == null) { log.Add("swim-up bar: no Island2Pool (run TeamBPoolBatch first)"); return; }
            float water = poolWater.transform.position.y, deck = water + PoolWaterBelowEdge;
            var root = new GameObject("SwimUpBar").transform;
            root.SetParent(parent, false);
            Material tile = GetMaterial("SwimBarTile", new Color(0.93f, 0.9f, 0.82f), smoothness: 0.5f);
            Material top = GetMaterial("SwimBarTop", new Color(0.12f, 0.45f, 0.52f), smoothness: 0.6f);
            Material gold = GetMaterial("HelipadGold", new Color(0.86f, 0.68f, 0.28f), metallic: 0.6f, smoothness: 0.5f);
            float floorY = PoolFloor;
            float zc = w.center.y, len = 5f;
            // The counter in the water along the east wall: from the pool floor up to 1.05 m over the water.
            float cx = w.xMax - 0.55f, topY = water + 0.55f;
            Primitive(PrimitiveType.Cube, "Counter", root, new Vector3(cx, (topY + floorY) * 0.5f, zc), new Vector3(1.1f, topY - floorY, len), tile);
            Primitive(PrimitiveType.Cube, "CounterTop", root, new Vector3(cx - 0.1f, topY + 0.04f, zc), new Vector3(1.4f, 0.08f, len + 0.3f), top, keepCollider: false);
            Primitive(PrimitiveType.Cube, "GoldTrim", root, new Vector3(cx - 0.81f, topY - 0.03f, zc), new Vector3(0.03f, 0.06f, len + 0.3f), gold, keepCollider: false);
            // A ledge to stand on (the water is 1.6 m deep), and three stools whose seats are under the water.
            float ledge = water - 1.15f;
            Primitive(PrimitiveType.Cube, "Ledge", root, new Vector3(cx - 1.6f, (ledge + floorY) * 0.5f, zc), new Vector3(1.0f, ledge - floorY, len), tile);
            int stools = 0;
            for (float z = zc - 2f; z <= zc + 2.01f; z += 2f)
            {
                float seat = water - 0.45f;
                Primitive(PrimitiveType.Cylinder, "StoolLeg", root, new Vector3(cx - 1.25f, (seat + ledge) * 0.5f, z), new Vector3(0.12f, (seat - ledge) * 0.5f, 0.12f), tile, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "StoolSeat", root, new Vector3(cx - 1.25f, seat, z), new Vector3(0.5f, 0.05f, 0.5f), top);
                stools++;
            }
            // Behind it on the deck: Tripo's tiki back bar facing the pool, under a Tripo thatched palapa whose four
            // posts stand on the pool's east wall and the deck (the model's posts are found on its mesh and squared up).
            float postX = w.xMax + 1f, rows = 0f;
            GameObject palapa = ResortModel("swimup_palapa", root, new Vector3(postX + 2.3f, deck + 0.06f, zc), 0f);
            if (palapa != null)
            {
                List<Vector3> feet = PostFeet(palapa, 0.3f);
                if (feet.Count == 4)
                {
                    // Turn it so its posts' square lines up with the pool, then move it so the west posts are on the wall.
                    Vector3 edge = feet[1] - feet[0];
                    float angle = Mathf.Atan2(edge.z, edge.x) * Mathf.Rad2Deg;
                    palapa.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                    feet = PostFeet(palapa, 0.3f);
                    float west = float.MaxValue;
                    foreach (Vector3 p in feet) west = Mathf.Min(west, p.x);
                    palapa.transform.position += new Vector3(postX - west, 0f, 0f);
                    foreach (Vector3 p in PostFeet(palapa, 0.3f))
                    {
                        var post = palapa.AddComponent<BoxCollider>();
                        post.center = palapa.transform.InverseTransformPoint(p + Vector3.up * 1.4f);
                        post.size = new Vector3(0.3f, 2.8f, 0.3f);
                    }
                    rows = palapa.transform.position.x;
                }
                else log.Add($"swim-up bar: palapa posts not found ({feet.Count} feet)");
            }
            GameObject backBar = ResortModel("tiki_back_bar", root, new Vector3(w.xMax + 4.0f, deck + 0.06f, zc), -90f);
            if (backBar != null)
            {
                BoxAround(backBar, 0.85f);
                TagSurface(backBar, SurfaceKind.Wood);
            }
            log.Add($"swim-up bar: water {water:F2}, deck {deck:F2}, {stools} stools, counter x {cx:F1}, palapa at x {rows:F2}");
        }

        /// <summary>Where a model's posts meet the ground: its mesh's vertices within <paramref name="band"/> m of its feet, in clumps.</summary>
        private static List<Vector3> PostFeet(GameObject model, float band)
        {
            var points = new List<Vector3>();
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 v in f.sharedMesh.vertices)
                    points.Add(f.transform.TransformPoint(v));
            float lowest = float.MaxValue;
            foreach (Vector3 p in points) lowest = Mathf.Min(lowest, p.y);
            var clumps = new List<(Vector3 sum, int n)>();
            foreach (Vector3 p in points)
            {
                if (p.y > lowest + band) continue;
                int i = clumps.FindIndex(c => Vector2.Distance(new Vector2(c.sum.x / c.n, c.sum.z / c.n), new Vector2(p.x, p.z)) < 0.5f);
                if (i < 0) clumps.Add((p, 1));
                else clumps[i] = (clumps[i].sum + p, clumps[i].n + 1);
            }
            var feet = clumps.ConvertAll(c => c.sum / c.n);
            // In order round the square (by angle about the middle), so feet[0] and feet[1] are neighbours.
            Vector3 mid = Vector3.zero;
            foreach (Vector3 p in feet) mid += p;
            if (feet.Count > 0) mid /= feet.Count;
            feet.Sort((a, b) => Mathf.Atan2(a.z - mid.z, a.x - mid.x).CompareTo(Mathf.Atan2(b.z - mid.z, b.x - mid.x)));
            return feet;
        }

        // ------------------------------------------------------------------ a tennis court

        /// <summary>A hard tennis court on the lawn east of the helipad: teal court in a green surround, white lines, the net.</summary>
        private static void BuildTennisCourt(Transform parent, List<string> log)
        {
            var centre = new Vector3(36f, 0f, -287f);
            centre.y = GroundUnder(centre);
            var root = new GameObject("TennisCourt").transform;
            root.SetParent(parent, false);
            root.position = centre;
            Material surround = GetMaterial("TennisSurround", new Color(0.22f, 0.5f, 0.36f), smoothness: 0.15f);
            Material court = GetMaterial("TennisCourt", new Color(0.12f, 0.45f, 0.55f), smoothness: 0.2f);
            Material line = GetMaterial("TennisLine", new Color(0.97f, 0.97f, 0.95f), smoothness: 0.25f);
            Material netMat = GetMaterial("TennisNet", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.1f);
            Material post = GetMaterial("TennisPost", new Color(0.2f, 0.32f, 0.26f), metallic: 0.4f, smoothness: 0.4f);
            const float L = 23.77f, W = 10.97f, singles = 8.23f, service = 6.4f;
            Primitive(PrimitiveType.Cube, "Surround", root, new Vector3(0f, 0.02f, 0f), new Vector3(L + 7f, 0.1f, W + 5f), surround);
            Primitive(PrimitiveType.Cube, "Court", root, new Vector3(0f, 0.075f, 0f), new Vector3(L, 0.01f, W), court, keepCollider: false);
            void Line(Vector3 at, Vector3 size) => Primitive(PrimitiveType.Cube, "Line", root, at + Vector3.up * 0.082f, size, line, keepCollider: false);
            const float t = 0.06f;
            foreach (float z in new[] { -W / 2f, W / 2f, -singles / 2f, singles / 2f }) Line(new Vector3(0f, 0f, z), new Vector3(L, 0.005f, t));
            foreach (float x in new[] { -L / 2f, L / 2f }) Line(new Vector3(x, 0f, 0f), new Vector3(t, 0.005f, W));
            foreach (float x in new[] { -service, service }) Line(new Vector3(x, 0f, 0f), new Vector3(t, 0.005f, singles));
            Line(Vector3.zero, new Vector3(service * 2f, 0.005f, t));
            // The net across the middle, on two posts, its white band along the top.
            Primitive(PrimitiveType.Cube, "Net", root, new Vector3(0f, 0.55f, 0f), new Vector3(0.03f, 0.9f, W + 0.9f), netMat);
            Primitive(PrimitiveType.Cube, "NetBand", root, new Vector3(0f, 1.0f, 0f), new Vector3(0.05f, 0.06f, W + 0.9f), line, keepCollider: false);
            foreach (float z in new[] { -(W / 2f + 0.45f), W / 2f + 0.45f })
                Primitive(PrimitiveType.Cylinder, "NetPost", root, new Vector3(0f, 0.55f, z), new Vector3(0.1f, 0.55f, 0.1f), post, keepCollider: false);
            log.Add($"tennis court at {centre}");
        }

        // ------------------------------------------------------------------ a second yacht at the marina

        private static void BuildSecondYacht(Transform parent, List<string> log)
        {
            Transform deck = GameObject.Find("Environment/Island2Finish/Marina/Deck")?.transform;
            if (deck == null) { log.Add("no marina pier"); return; }
            Bounds pier = deck.GetComponent<Renderer>().bounds;
            GameObject y = ResortModel("motor_yacht", parent, Vector3.zero, 0f);
            if (y == null) return;
            Bounds b = TripoBounds(y.transform, y.transform);
            // On the pier's west side, bow out to sea (north), keel 0.9 m under the water.
            y.transform.position = new Vector3(pier.min.x - b.extents.x - 0.35f, WaterLevel - 0.9f, pier.center.z - 2f);
            BoxAround(y, 0.85f);
            log.Add($"second yacht {b.size} at {y.transform.position}");
        }
    }
}
