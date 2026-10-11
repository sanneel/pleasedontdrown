using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 3 at night, chapter 3's arrival (Docs/16-Plan-Islands-2-3.md): the team lands at a little dock on the
    /// north-east beach, under the crags; a pirates' stair tower against the crags' east cliff climbs to a lookout on a
    /// rock ledge 18 m up, with the whole village below; then down and in among the sleeping pirates (a campfire in the
    /// lane, sleepers on the porches) to the THIEF's shack at the far end of the village.
    /// Found by name by StoryDirector.Chapter3: "Island3/NightArrival/Dock" (where you land), ".../Lookout", ".../Sleeper_N",
    /// ".../Campfire"; the THIEF's point stays "Island3/Story/Thief".
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // Measured on the built island (Logs/island3-heights.csv): the NE beach is flat at ~0 m behind a low riprap row,
        // the meadow at the crags' east foot is flat at ~1 m, and the cliff has a ledge at ~18 m (x 58-60, z -826..-830).
        private static readonly Vector2 NightDockShore = new(78f, -786f);
        private const float NightDockSeaZ = -764f, NightDockDeck = 1.3f;
        private const float TowerLaneE = 72.9f, TowerLaneW = 70.9f, TowerLaneHalf = 0.9f;
        private const float TowerNorthZ = -815.5f, TowerSouthZ = -828.5f, TowerLanding = 2.5f;
        private const float TowerGround = 1.0f, TowerTop = 18.4f;
        private const float LookoutWestX = 61f, LookoutSouthZ = -833f;
        private static readonly Vector2 CampfireAt = new(88.5f, -867f);

        /// <summary>The house the THIEF hides in: the one furthest from the night landing, on the low ground.</summary>
        private static (Vector2 at, float yaw) ThiefHouse() =>
            VillageHouses.Where(h => h.at.x > 60f && BeachHeight(h.at.x, h.at.y) < 6f)
                .OrderByDescending(h => Vector2.Distance(h.at, NightDockShore)).First();

        private static Vector2 Porch((Vector2 at, float yaw) house, float out_ = 5f, float side = 0f)
        {
            Vector2 forward = new(Mathf.Sin(house.yaw * Mathf.Deg2Rad), Mathf.Cos(house.yaw * Mathf.Deg2Rad));
            return house.at + forward * out_ + new Vector2(forward.y, -forward.x) * side;
        }

        /// <summary>(x, z, radius) the night arrival uses, for the trees to keep clear of.</summary>
        private static System.Collections.Generic.IEnumerable<Vector3> NightTaken()
        {
            for (float z = NightDockSeaZ; z <= NightDockShore.y; z += 3f) yield return new Vector3(NightDockShore.x, z, 3f);
            yield return new Vector3((TowerLaneW + TowerLaneE) * 0.5f, (TowerNorthZ + TowerSouthZ) * 0.5f, 10f);
            yield return new Vector3((LookoutWestX + TowerLaneE) * 0.5f, (LookoutSouthZ + TowerSouthZ) * 0.5f, 7f);
            yield return new Vector3(CampfireAt.x, CampfireAt.y, 4.5f);
        }

        private static void BuildIsland3Night(Transform island)
        {
            Transform old = island.Find("NightArrival");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("NightArrival").transform;
            root.SetParent(island, false);
            ClearTreesFor(island);
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material plank = GetMaterial("PirateWoodPlank", new Color(0.46f, 0.33f, 0.2f));
            Material rope = GetMaterial("Rope", new Color(0.72f, 0.6f, 0.4f));
            Material lamp = GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f));
            Material fire = GetMaterial("CampfireFlame", new Color(1f, 0.55f, 0.15f), emission: new Color(4f, 1.6f, 0.3f));
            Material stone = GetMaterial("CampfireStone", new Color(0.35f, 0.34f, 0.33f));

            BuildNightDock(root, wood, plank, rope, lamp);
            BuildLookoutTower(root, wood, plank, rope, lamp);
            BuildCampfireAndSleepers(root, wood, fire, stone);

            // The THIEF waits at the far end of the village now (a proper sneak past the sleepers).
            Transform thief = island.Find("Story/Thief");
            if (thief != null)
            {
                (Vector2 at, float yaw) house = ThiefHouse();
                Vector2 porch = Porch(house);
                thief.SetPositionAndRotation(new Vector3(porch.x, BeachHeight(porch.x, porch.y) + 0.1f, porch.y), Quaternion.Euler(0f, house.yaw, 0f));
            }
        }

        /// <summary>
        /// Palms and bushes already standing where the tower, its stairs' foot or the dock go (an island built before the
        /// night arrival existed) are taken out: one in front of the first step is a trunk in your face.
        /// </summary>
        private static void ClearTreesFor(Transform island)
        {
            var clear = new System.Collections.Generic.List<Transform>();
            // Each tree is one child of the island's "Palms" or "Jungle" group.
            foreach (string group in new[] { "Palms", "Jungle" })
            {
                Transform trees = island.Find(group);
                if (trees == null) continue;
                foreach (Transform tree in trees)
                {
                    Vector3 p = tree.position;
                    bool tower = p.x > TowerLaneW - 4f && p.x < TowerLaneE + 4f && p.z > TowerSouthZ - TowerLanding - 3f && p.z < TowerNorthZ + TowerLanding + 6f;
                    bool dock = Mathf.Abs(p.x - NightDockShore.x) < 3.5f && p.z > NightDockShore.y - 6f && p.z < NightDockSeaZ + 8f;
                    if (tower || dock) clear.Add(tree);
                }
            }
            foreach (Transform t in clear)
            {
                Debug.Log($"[Island3] cleared {t.name} at {t.position:F1} for the night arrival");
                Object.DestroyImmediate(t.gameObject);
            }
        }

        // ------------------------------------------------------------------ the dock under the crags

        private static void BuildNightDock(Transform root, Material wood, Material plank, Material rope, Material lamp)
        {
            var dock = new GameObject("NightDock").transform;
            dock.SetParent(root, false);
            float x = NightDockShore.x, shoreZ = NightDockShore.y + 4f; // the deck reaches the dry sand here
            float length = shoreZ - NightDockSeaZ;
            // The walkway over the riprap and out into the water, a wider head at the end, a ramp down onto the beach.
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3(x, NightDockDeck - 0.12f, (shoreZ + NightDockSeaZ) * 0.5f), new Vector3(2.4f, 0.24f, length), plank);
            Primitive(PrimitiveType.Cube, "DeckHead", dock, new Vector3(x, NightDockDeck - 0.12f, NightDockSeaZ + 1.5f), new Vector3(6f, 0.24f, 3f), plank);
            float beachY = BeachHeight(x, NightDockShore.y - 1f);
            Vector3 rampFrom = new(x, NightDockDeck - 0.12f, shoreZ), rampTo = new(x, beachY - 0.05f, NightDockShore.y - 1f);
            Ramp(dock, "ShoreRamp", rampFrom, rampTo, 2.4f, plank);
            for (float z = NightDockSeaZ + 0.3f; z <= shoreZ; z += 3f)
                foreach (float side in new[] { -1.15f, 1.15f })
                {
                    float sx = z < NightDockSeaZ + 3f ? side * 2.5f : side;
                    Primitive(PrimitiveType.Cylinder, "Post", dock, new Vector3(x + sx, NightDockDeck - 2.4f, z), new Vector3(0.22f, 2.4f, 0.22f), wood, keepCollider: false);
                }
            // A rope rail along the walkway (posts with a rope between), a lantern at the head.
            foreach (float side in new[] { -1.1f, 1.1f })
            {
                float rz = (shoreZ + NightDockSeaZ + 3f) * 0.5f, rl = shoreZ - NightDockSeaZ - 3f;
                Primitive(PrimitiveType.Cube, "Rail", dock, new Vector3(x + side, NightDockDeck + 0.85f, rz), new Vector3(0.06f, 0.06f, rl), rope, keepCollider: false);
                Collider(dock, "RailBlock", new Vector3(x + side * 1.15f, NightDockDeck + 0.5f, rz), new Vector3(0.1f, 1f, rl));
            }
            LanternPost(dock, new Vector3(x + 2.6f, NightDockDeck, NightDockSeaZ + 2.6f), wood, lamp, "NightLight_Dock");
            // The rowboat they came in, tied alongside.
            var boat = new GameObject("Rowboat").transform;
            boat.SetParent(dock, false);
            boat.SetPositionAndRotation(new Vector3(x + 3.2f, WaterLevel + 0.05f, NightDockSeaZ + 6f), Quaternion.Euler(0f, 8f, 0f));
            Primitive(PrimitiveType.Cube, "Hull", boat, new Vector3(0f, 0.15f, 0f), new Vector3(1.5f, 0.45f, 4f), wood);
            Primitive(PrimitiveType.Cube, "Bow", boat, new Vector3(0f, 0.2f, 2.2f), new Vector3(0.9f, 0.4f, 0.6f), wood, keepCollider: false);
            foreach (float tz in new[] { -0.8f, 0.6f })
                Primitive(PrimitiveType.Cube, "Thwart", boat, new Vector3(0f, 0.42f, tz), new Vector3(1.4f, 0.08f, 0.3f), plank, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Oar", boat, new Vector3(0.55f, 0.48f, 0f), new Vector3(0.08f, 0.05f, 2.6f), plank, keepCollider: false);

            var arrival = new GameObject("Dock").transform;
            arrival.SetParent(root, false);
            arrival.SetPositionAndRotation(new Vector3(x, NightDockDeck + 0.15f, NightDockSeaZ + 4f), Quaternion.Euler(0f, 180f, 0f));
        }

        // ------------------------------------------------------------------ the stair tower and the lookout

        private static void BuildLookoutTower(Transform root, Material wood, Material plank, Material rope, Material lamp)
        {
            var tower = new GameObject("LookoutTower").transform;
            tower.SetParent(root, false);
            float rise = (TowerTop - TowerGround) / 3f;
            float l1 = TowerGround + rise, l2 = TowerGround + rise * 2f;
            float west = TowerLaneW - TowerLaneHalf - 0.1f, east = TowerLaneE + TowerLaneHalf + 0.1f, mid = (TowerLaneW + TowerLaneE) * 0.5f;
            // Three flights: up the east lane southward, back up the west lane northward, up the east lane to the top.
            Flight(tower, "Flight1", TowerLaneE, TowerNorthZ, TowerGround, TowerSouthZ, l1, plank);
            Flight(tower, "Flight2", TowerLaneW, TowerSouthZ, l1, TowerNorthZ, l2, plank);
            Flight(tower, "Flight3", TowerLaneE, TowerNorthZ, l2, TowerSouthZ, TowerTop, plank);
            // Landings: south at the first turn, north at the second.
            Platform(tower, "LandingSouth", west, east, TowerSouthZ - TowerLanding, TowerSouthZ, l1, plank);
            Platform(tower, "LandingNorth", west, east, TowerNorthZ, TowerNorthZ + TowerLanding, l2, plank);
            // Rails: outside the lanes along each flight, a divider between them, round the landings.
            Railing(tower, new Vector3(east, 0f, TowerNorthZ), new Vector3(east, 0f, TowerSouthZ), TowerGround, l1, rope);
            Railing(tower, new Vector3(east, 0f, TowerNorthZ), new Vector3(east, 0f, TowerSouthZ), l2, TowerTop, rope);
            Railing(tower, new Vector3(west, 0f, TowerSouthZ), new Vector3(west, 0f, TowerNorthZ), l1, l2, rope);
            Railing(tower, new Vector3(mid, 0f, TowerNorthZ), new Vector3(mid, 0f, TowerSouthZ), TowerGround + 0.6f, l1, rope);
            Railing(tower, new Vector3(mid, 0f, TowerSouthZ), new Vector3(mid, 0f, TowerNorthZ), l1, l2, rope);
            Railing(tower, new Vector3(mid, 0f, TowerNorthZ), new Vector3(mid, 0f, TowerSouthZ), l2, TowerTop, rope);
            Railing(tower, new Vector3(west, 0f, TowerSouthZ - TowerLanding), new Vector3(east, 0f, TowerSouthZ - TowerLanding), l1, l1, rope);
            Railing(tower, new Vector3(east, 0f, TowerSouthZ), new Vector3(east, 0f, TowerSouthZ - TowerLanding), l1, l1, rope);
            Railing(tower, new Vector3(west, 0f, TowerNorthZ + TowerLanding), new Vector3(east, 0f, TowerNorthZ + TowerLanding), l2, l2, rope);
            Railing(tower, new Vector3(west, 0f, TowerNorthZ), new Vector3(west, 0f, TowerNorthZ + TowerLanding), l2, l2, rope);
            Railing(tower, new Vector3(east, 0f, TowerNorthZ), new Vector3(east, 0f, TowerNorthZ + TowerLanding), l2, l2, rope);
            // The frame: posts from the ground to the top at the corners and halfway along, cross braces.
            foreach (float px in new[] { west, east })
                foreach (float pz in new[] { TowerNorthZ + TowerLanding, (TowerNorthZ + TowerSouthZ) * 0.5f, TowerSouthZ - TowerLanding })
                {
                    // (The south posts hold the lookout deck up: they stop under it, not through it.)
                    float top = pz < TowerSouthZ ? TowerTop - 0.24f : TowerTop + 1.1f;
                    Primitive(PrimitiveType.Cylinder, "Post", tower, new Vector3(px, (TowerGround - 0.3f + top) * 0.5f, pz), new Vector3(0.28f, (top - TowerGround + 0.3f) * 0.5f, 0.28f), wood);
                }
            for (float y = TowerGround + 3f; y < TowerTop; y += 5f)
                foreach (float px in new[] { west - 0.05f, east + 0.05f })
                    Primitive(PrimitiveType.Cube, "Brace", tower, new Vector3(px, y, (TowerNorthZ + TowerSouthZ) * 0.5f), new Vector3(0.12f, 0.18f, TowerNorthZ - TowerSouthZ + TowerLanding * 2f), wood, keepCollider: false)
                        .transform.localRotation = Quaternion.Euler(14f, 0f, 0f);

            // The lookout: a deck at the top reaching back to the rock ledge, railed except where you step off onto it.
            var lookout = new GameObject("LookoutDeck").transform;
            lookout.SetParent(root, false);
            Platform(lookout, "Deck", LookoutWestX, east, LookoutSouthZ, TowerSouthZ, TowerTop, plank);
            Railing(lookout, new Vector3(LookoutWestX, 0f, LookoutSouthZ), new Vector3(east, 0f, LookoutSouthZ), TowerTop, TowerTop, rope);
            Railing(lookout, new Vector3(east, 0f, LookoutSouthZ), new Vector3(east, 0f, TowerSouthZ), TowerTop, TowerTop, rope);
            Railing(lookout, new Vector3(LookoutWestX + 1.6f, 0f, TowerSouthZ), new Vector3(mid, 0f, TowerSouthZ), TowerTop, TowerTop, rope);
            // Props of a pirates' watch post: a spyglass on a tripod, a lantern, the flag.
            // (In the south-west corner, out of the way of whoever stands at the rail looking at the village.)
            Vector3 glass = new(LookoutWestX + 1.2f, TowerTop, LookoutSouthZ + 0.9f);
            foreach (float a in new[] { 0f, 120f, 240f })
                Primitive(PrimitiveType.Cylinder, "TripodLeg", lookout, glass + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.6f, 0.25f), new Vector3(0.05f, 0.62f, 0.05f), wood, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(-18f, a, 0f);
            Primitive(PrimitiveType.Cylinder, "Spyglass", lookout, glass + new Vector3(0f, 1.25f, 0f), new Vector3(0.09f, 0.45f, 0.09f), GetMaterial("Brass", new Color(0.78f, 0.6f, 0.25f), 0.8f, 0.6f), keepCollider: false)
                .transform.localRotation = Quaternion.Euler(80f, 0f, 0f);
            LanternPost(lookout, new Vector3(east - 0.5f, TowerTop, TowerSouthZ - 0.5f), wood, lamp, "NightLight_Lookout");
            PirateFlag(lookout, new Vector3(east - 0.4f, TowerTop - 0.1f, LookoutSouthZ + 0.4f), 5f, new Vector2(1.6f, 1.1f), wood, GetMaterial("PirateFlag", new Color(0.08f, 0.08f, 0.09f)));

            var point = new GameObject("Lookout").transform;
            point.SetParent(root, false);
            point.SetPositionAndRotation(new Vector3((LookoutWestX + east) * 0.5f + 1f, TowerTop + 0.1f, (LookoutSouthZ + TowerSouthZ) * 0.5f), Quaternion.Euler(0f, 160f, 0f));
            var foot = new GameObject("TowerFoot").transform;
            foot.SetParent(root, false);
            foot.SetPositionAndRotation(new Vector3(TowerLaneE, TowerGround + 0.1f, TowerNorthZ + TowerLanding + 1.5f), Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>A flight of stairs along z: treads you see, a smooth ramp you walk on (no tripping on each step).</summary>
        private static void Flight(Transform parent, string name, float x, float fromZ, float fromY, float toZ, float toY, Material plank)
        {
            var flight = new GameObject(name).transform;
            flight.SetParent(parent, false);
            Ramp(flight, "Ramp", new Vector3(x, fromY, fromZ), new Vector3(x, toY, toZ), TowerLaneHalf * 2f, null);
            int steps = Mathf.Max(2, Mathf.RoundToInt((toY - fromY) / 0.24f));
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                Primitive(PrimitiveType.Cube, "Tread", flight, new Vector3(x, Mathf.Lerp(fromY, toY, t) - 0.04f, Mathf.Lerp(fromZ, toZ, t)),
                    new Vector3(TowerLaneHalf * 2f, 0.08f, Mathf.Abs(toZ - fromZ) / steps + 0.02f), plank, keepCollider: false);
            }
            foreach (float side in new[] { -TowerLaneHalf, TowerLaneHalf })
                Ramp(flight, "Stringer", new Vector3(x + side, fromY - 0.15f, fromZ), new Vector3(x + side, toY - 0.15f, toZ), 0.12f, plank, 0.35f, collide: false);
        }

        /// <summary>A sloped plank (top surface from <paramref name="from"/> to <paramref name="to"/>); no material: an invisible walking surface.</summary>
        private static GameObject Ramp(Transform parent, string name, Vector3 from, Vector3 to, float width, Material material, float thickness = 0.2f, bool collide = true)
        {
            Vector3 d = to - from;
            Quaternion rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            Vector3 centre = (from + to) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
            GameObject go = Primitive(PrimitiveType.Cube, name, parent, Vector3.zero, new Vector3(width, thickness, d.magnitude + 0.1f), material, collide);
            go.transform.SetPositionAndRotation(centre, rotation);
            if (material == null) Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            return go;
        }

        private static void Platform(Transform parent, string name, float x0, float x1, float z0, float z1, float top, Material plank) =>
            Primitive(PrimitiveType.Cube, name, parent, new Vector3((x0 + x1) * 0.5f, top - 0.12f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), 0.24f, Mathf.Abs(z1 - z0)), plank);

        /// <summary>A rail 1 m above a walking line from (a, ya) to (b, yb): a rope you see, a solid block you can't fall through.</summary>
        private static void Railing(Transform parent, Vector3 a, Vector3 b, float ya, float yb, Material rope)
        {
            a.y = ya + 0.95f;
            b.y = yb + 0.95f;
            Ramp(parent, "RailRope", a, b, 0.06f, rope, 0.06f, collide: false);
            Vector3 lowA = a + Vector3.down * 0.4f, lowB = b + Vector3.down * 0.4f;
            Ramp(parent, "RailRope", lowA, lowB, 0.05f, rope, 0.05f, collide: false);
            GameObject block = Ramp(parent, "RailBlock", a + Vector3.up * 0.15f, b + Vector3.up * 0.15f, 0.1f, null, 1.25f);
            block.name = "RailBlock";
        }

        private static void LanternPost(Transform parent, Vector3 foot, Material wood, Material lamp, string lightName)
        {
            var post = new GameObject("LanternPost").transform;
            post.SetParent(parent, false);
            post.position = foot;
            Primitive(PrimitiveType.Cylinder, "Post", post, new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), wood);
            Primitive(PrimitiveType.Cube, "Lantern", post, new Vector3(0f, 2.35f, 0f), new Vector3(0.24f, 0.32f, 0.24f), lamp, keepCollider: false);
            var light = new GameObject(lightName).AddComponent<Light>();
            light.transform.SetParent(post, false);
            light.transform.localPosition = new Vector3(0f, 2.3f, 0f);
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.72f, 0.4f);
            light.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ the sleepers' camp

        private static void BuildCampfireAndSleepers(Transform root, Material wood, Material fire, Material stone)
        {
            var camp = new GameObject("Campfire").transform;
            camp.SetParent(root, false);
            Vector3 c = new(CampfireAt.x, BeachHeight(CampfireAt.x, CampfireAt.y), CampfireAt.y);
            camp.position = c;
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f * Mathf.Deg2Rad;
                Primitive(PrimitiveType.Sphere, "Stone", camp, new Vector3(Mathf.Cos(a) * 0.75f, 0.08f, Mathf.Sin(a) * 0.75f), new Vector3(0.32f, 0.2f, 0.28f), stone, keepCollider: false);
            }
            for (int i = 0; i < 4; i++)
                Primitive(PrimitiveType.Cylinder, "Log", camp, new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.45f, 0.12f), wood, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(90f, i * 45f, 0f);
            Primitive(PrimitiveType.Cube, "Flame", camp, new Vector3(0f, 0.35f, 0f), new Vector3(0.35f, 0.5f, 0.35f), fire, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            Primitive(PrimitiveType.Cube, "Flame", camp, new Vector3(0.05f, 0.55f, 0.02f), new Vector3(0.2f, 0.45f, 0.2f), fire, keepCollider: false);
            var light = new GameObject("NightLight_Campfire").AddComponent<Light>();
            light.transform.SetParent(camp, false);
            light.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            light.type = LightType.Point;
            light.range = 11f;
            light.intensity = 2.6f;
            light.color = new Color(1f, 0.58f, 0.25f);
            light.shadows = LightShadows.None;
            Physics.SyncTransforms();

            // Three asleep round the fire (heads away from it), the rest by porches along the way to the THIEF. Every
            // spot is tested: a lying body must not go through a stilt, a step, a barrel or another sleeper.
            int n = 0;
            var placed = new System.Collections.Generic.List<(Vector3 at, float yaw)>();
            foreach (float a in new[] { 70f, 190f, 310f })
            {
                // (Facing the fire: the body lies behind the feet point, so the head ends up away from the flames.)
                Vector3 at = OnGround(c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 1.9f);
                if (SleeperFits(at, a + 180f, placed)) SleeperPoint(root, n++, at, a + 180f, placed);
            }
            (Vector2 at, float yaw) thief = ThiefHouse();
            var porches = VillageHouses.Where(h => h.at.x > 60f && h.at != thief.at && BeachHeight(h.at.x, h.at.y) < 6f)
                .OrderBy(h => h.at.y > -900f ? 0 : 1).ThenBy(h => Vector2.Distance(h.at, CampfireAt)).Take(6).ToArray();
            for (int i = 0; i < porches.Length && n < 8; i++)
            {
                bool done = false;
                foreach (float out_ in new[] { 5.5f, 6.5f, 7.5f })
                    foreach (float side in new[] { 2.6f, -2.6f, 3.6f, -3.6f, 0f })
                        foreach (float turn in new[] { 90f, -90f, 0f, 180f })
                        {
                            if (done) break;
                            Vector2 spot = Porch(porches[i], out_, side);
                            Vector3 at = OnGround(new Vector3(spot.x, 0f, spot.y));
                            float yaw = porches[i].yaw + turn;
                            if (DistanceToRoads(spot) < 1.6f || !SleeperFits(at, yaw, placed)) continue;
                            SleeperPoint(root, n++, at, yaw, placed);
                            done = true;
                        }
                if (!done) Debug.LogWarning($"[Island3] no clear spot to sleep by the house at {porches[i].at}");
            }
        }

        /// <summary>A body lying here (StoryNpc's lying collider: from 0.45 m ahead of the feet point to 1.35 m behind) touches nothing.</summary>
        private static bool SleeperFits(Vector3 at, float yaw, System.Collections.Generic.List<(Vector3 at, float yaw)> placed)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 a = at + forward * 0.6f + Vector3.up * 0.35f, b = at - forward * 1.5f + Vector3.up * 0.35f;
            foreach (Collider hit in Physics.OverlapCapsule(a, b, 0.32f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.name is "Island3Ground" or "BeachTerrain" || hit is TerrainCollider) continue;
                return false;
            }
            // Nor the fire, nor another sleeper.
            Vector3 mid = at - forward * 0.45f;
            if (Vector2.Distance(new Vector2(mid.x, mid.z), CampfireAt) < 1.3f) return false;
            foreach ((Vector3 p, float y) in placed)
                if ((p - Quaternion.Euler(0f, y, 0f) * Vector3.forward * 0.45f - mid).sqrMagnitude < 1.6f * 1.6f) return false;
            // Lying on (nearly) level ground, not half in a slope.
            float drop = Mathf.Abs(BeachHeight(a.x, a.z) - BeachHeight(b.x, b.z));
            return drop < 0.35f;
        }

        private static void SleeperPoint(Transform root, int n, Vector3 at, float yaw, System.Collections.Generic.List<(Vector3 at, float yaw)> placed)
        {
            var point = new GameObject($"Sleeper_{n}").transform;
            point.SetParent(root, false);
            point.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            placed.Add((at, yaw));
        }

        // ------------------------------------------------------------------ in the open scene (menu / batch)

        [MenuItem("PLEASE DON'T DROWN/Island three: night arrival (mountain dock, lookout, sleepers)")]
        public static void BuildIsland3NightInScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Open the Game scene in edit mode first.");
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Transform island = GameObject.Find("Environment/Island3")?.transform;
            if (island == null) throw new InvalidOperationException("No Environment/Island3 (build island three first)");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-island3-night-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            _villageHouses = null;
            BuildIsland3Night(island);
            AssignSceneIds(scene);
            VerifyIsland3Night(island);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Island3] PASS: night arrival built (mountain dock, stair tower and lookout, campfire, sleepers, THIEF moved)");
        }

        /// <summary>The way up works: a player-sized capsule can go from the dock to the lookout without hitting anything solid.</summary>
        private static void VerifyIsland3Night(Transform island)
        {
            Physics.SyncTransforms();
            Transform night = island.Find("NightArrival");
            foreach (string name in new[] { "Dock", "Lookout", "TowerFoot", "Sleeper_0", "Campfire" })
                if (night.Find(name) == null) throw new InvalidOperationException($"Night arrival has no {name}");
            // Walk the tower's centre lines: each sample stands on something (the ramp or a landing) and has headroom.
            float rise = (TowerTop - TowerGround) / 3f;
            (float x, float z0, float y0, float z1, float y1)[] flights =
            {
                (TowerLaneE, TowerNorthZ, TowerGround, TowerSouthZ, TowerGround + rise),
                (TowerLaneW, TowerSouthZ, TowerGround + rise, TowerNorthZ, TowerGround + rise * 2f),
                (TowerLaneE, TowerNorthZ, TowerGround + rise * 2f, TowerSouthZ, TowerTop),
            };
            var report = new StringBuilder();
            foreach (var f in flights)
                for (float t = 0.05f; t <= 0.95f; t += 0.1f)
                {
                    float z = Mathf.Lerp(f.z0, f.z1, t), y = Mathf.Lerp(f.y0, f.y1, t);
                    if (!Physics.Raycast(new Vector3(f.x, y + 1f, z), Vector3.down, out RaycastHit hit, 1.5f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(hit.point.y - y) > 0.3f)
                        report.Append($"no footing at ({f.x:0.0}, {y:0.0}, {z:0.0}); ");
                    if (Physics.CheckCapsule(new Vector3(f.x, y + 0.6f, z), new Vector3(f.x, y + 1.7f, z), 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        Collider[] hits = Physics.OverlapCapsule(new Vector3(f.x, y + 0.6f, z), new Vector3(f.x, y + 1.7f, z), 0.3f, ~0, QueryTriggerInteraction.Ignore);
                        report.Append($"blocked at ({f.x:0.0}, {y:0.0}, {z:0.0}) by {string.Join("/", hits.Select(h => h.name))}; ");
                    }
                }
            // From the deck you can step onto the rock ledge.
            if (!Physics.Raycast(new Vector3(LookoutWestX - 1.5f, TowerTop + 3f, -829.5f), Vector3.down, out RaycastHit ledge, 6f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(ledge.point.y - TowerTop) > 1.2f)
                Debug.LogWarning($"[Island3] the rock ledge beside the lookout is {(ledge.collider != null ? (ledge.point.y - TowerTop).ToString("0.0") + " m off the deck" : "missing")}");
            if (report.Length > 0) throw new InvalidOperationException("Lookout tower: " + report);
        }

        /// <summary>Batch tool: the height and what's on top over island 3 (2 m grid) to Logs/island3-heights.csv.</summary>
        public static void DumpIsland3Heights()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var csv = new StringBuilder("x,z,y,what\n");
            for (float z = -1030f; z <= -760f; z += 2f)
                for (float x = -90f; x <= 130f; x += 2f)
                {
                    float y = WaterLevel;
                    string what = "sea";
                    if (Physics.Raycast(new Vector3(x, 120f, z), Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > WaterLevel - 0.05f)
                    {
                        y = hit.point.y;
                        what = hit.collider.name.Replace(",", " ");
                    }
                    csv.Append($"{x:0},{z:0},{y:0.00},{what}\n");
                }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/island3-heights.csv", csv.ToString());
            var marks = new StringBuilder();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                string path = t.name;
                for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                if (!path.StartsWith("Environment/Island3/")) continue;
                if (path.Split('/').Length > 4) continue;
                marks.Append($"{path},{t.position.x:0.0},{t.position.y:0.0},{t.position.z:0.0}\n");
            }
            File.WriteAllText("Logs/island3-objects.csv", marks.ToString());
            Debug.Log("[Island3] heights dumped");
        }
    }
}
