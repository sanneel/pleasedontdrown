using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Story;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Chapter 3's places on island 3 (Docs/16-Plan-Islands-2-3.md), found by name by StoryDirector.Chapter3:
    /// "Island3/Story/..." points (the THIEF's shack, the parrots' perches, the guards' beats, the jungle route, the river
    /// banks, the alligators, Old Snapper's pond, the money cave), the gun stall by the dock (a shop counter) and the rope
    /// swing over the river.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        /// <summary>Bosun Barnaby's beat (the lane guard of the sneak in): from the lane's first bend (just out of sight of
        /// the landing: guards see 22 m) a third of the way up the next stretch.</summary>
        private static Vector2[] GuardABeat => new[] { VillageLaneNorth[1], Vector2.Lerp(VillageLaneNorth[1], VillageLaneNorth[2], 0.35f) };

        private static void BuildIsland3Story(Transform root, Vector3 arrival, List<Vector3> taken)
        {
            var story = new GameObject("Story").transform;
            story.SetParent(root, false);
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material rope = GetMaterial("Rope", new Color(0.72f, 0.6f, 0.4f));
            Vector3 Ground(Vector2 p) => new(p.x, BeachHeight(p.x, p.y), p.y);

            // The THIEF's shack: the village house furthest from the night landing under the crags (a proper sneak
            // past the sleepers: Island3Night).
            (Vector2 at, float yaw) shack = ThiefHouse();
            Vector2 porch = shack.at + new Vector2(Mathf.Sin(shack.yaw * Mathf.Deg2Rad), Mathf.Cos(shack.yaw * Mathf.Deg2Rad)) * 5f;
            Point(story, "Thief", Ground(porch) + Vector3.up * 0.1f, shack.yaw);

            // Parrot perches up the village lane, past the square (the landing itself is out of their sight: you have to
            // be able to start sneaking): posts with a crossbar, either side of the lane.
            var route = new List<Vector2>(VillageLaneNorth.Skip(1)); // from the first bend on: the square is next to the landing
            int perch = 0;
            for (int i = 0; i < route.Count - 1 && perch < 6; i++)
            {
                Vector2 a = route[i], b = route[i + 1], dir = (b - a).normalized, side = new(-dir.y, dir.x);
                foreach (float k in new[] { 0.3f, 0.8f })
                {
                    Vector2 spot = Vector2.Lerp(a, b, k) + side * ((perch % 2 == 0 ? 1f : -1f) * 6.5f);
                    if (VillageHouses.Any(h => Vector2.Distance(h.at, spot) < 5f)) spot += side * (perch % 2 == 0 ? 3f : -3f);
                    Vector3 foot = Ground(spot);
                    Primitive(PrimitiveType.Cylinder, "PerchPost", story, foot + Vector3.up * 2.1f, new Vector3(0.16f, 2.1f, 0.16f), wood);
                    Primitive(PrimitiveType.Cylinder, "PerchBar", story, foot + Vector3.up * 4.15f, new Vector3(0.07f, 0.45f, 0.07f), wood, keepCollider: false)
                        .transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg, 90f);
                    Point(story, $"Perch_{perch++}", foot + Vector3.up * 4.25f, Mathf.Atan2(-side.x, -side.y) * Mathf.Rad2Deg);
                    taken.Add(new Vector3(spot.x, spot.y, 1.2f));
                }
            }
            // The guards' beats: up and down the lane from its first bend (not at the square: from there he saw the landing
            // itself, and you have to be able to start sneaking), and across between the houses.
            Point(story, "GuardA_0", Ground(GuardABeat[0]), 0f);
            Point(story, "GuardA_1", Ground(GuardABeat[1]), 0f);
            Point(story, "GuardB_0", Ground(new Vector2(80f, -879f)), 90f);
            Point(story, "GuardB_1", Ground(new Vector2(98f, -879f)), -90f);

            // The gun stall by the dock: a plank counter under an awning, "GUNS & SILENCERS".
            // On open ground near the landing: clear of the houses (and their porches) and off the road.
            Vector3 stallAt = OnGround(arrival + new Vector3(-7f, 0f, 9f));
            foreach (Vector3 offset in new[] { new Vector3(-6f, 0f, 10f), new Vector3(-6f, 0f, -10f), new Vector3(-10f, 0f, 12f), new Vector3(-10f, 0f, -12f),
                         new Vector3(-3f, 0f, 14f), new Vector3(-3f, 0f, -14f), new Vector3(-14f, 0f, 16f), new Vector3(-14f, 0f, -16f) })
            {
                Vector3 c = arrival + offset;
                var c2 = new Vector2(c.x, c.z);
                if (VillageHouses.Any(h => Vector2.Distance(h.at, c2) < 9f) || DistanceToRoads(c2) < 3.5f) continue;
                if (BeachHeight(c.x, c.z) < WaterLevel + 0.4f) continue;
                stallAt = OnGround(c);
                break;
            }
            BuildGunStall(story, stallAt, Quaternion.LookRotation(new Vector3(arrival.x - stallAt.x, 0f, arrival.z - stallAt.z)).eulerAngles.y, wood);
            taken.Add(new Vector3(stallAt.x, stallAt.z, 4f));

            // The jungle walk: from the end of the west road, in among the trees, to the river.
            Vector2 roadEnd = I3(5f, -952f);
            Point(story, "JungleStart", Ground(roadEnd), 0f);
            Vector2 r0 = Island3River[2], r1 = Island3River[3], cross = Vector2.Lerp(r0, r1, 0.5f);
            Vector2 riverDir = (r1 - r0).normalized, across = new(-riverDir.y, riverDir.x);
            if (Vector2.Dot(roadEnd - cross, across) < 0f) across = -across; // bank A faces the road end
            Vector2 bankA = cross + across * 8.5f, bankB = cross - across * 8.5f;
            for (int i = 0; i < 3; i++) // the jungle guards' beats, across the way
            {
                Vector2 c = Vector2.Lerp(roadEnd, bankA, 0.3f + i * 0.25f), s = new(-(bankA - roadEnd).normalized.y, (bankA - roadEnd).normalized.x);
                Point(story, $"JungleGuard_{i}_0", Ground(c + s * 7f), 0f);
                Point(story, $"JungleGuard_{i}_1", Ground(c - s * 7f), 0f);
                Point(story, $"MonkeyHome_{i}", Ground(c + s * (i % 2 == 0 ? 4f : -4f)), 0f);
            }
            Point(story, "RiverBankA", Ground(bankA) + Vector3.up * 0.2f, Mathf.Atan2(-across.x, -across.y) * Mathf.Rad2Deg);
            Point(story, "RiverBankB", Ground(bankB) + Vector3.up * 0.2f, Mathf.Atan2(-across.x, -across.y) * Mathf.Rad2Deg);
            Point(story, "Gator_0", new Vector3(cross.x + riverDir.x * 9f, WaterLevel - 0.45f, cross.y + riverDir.y * 9f), 0f);
            Point(story, "Gator_1", new Vector3(cross.x - riverDir.x * 10f, WaterLevel - 0.45f, cross.y - riverDir.y * 10f), 0f);
            BuildRopeSwing(story, Ground(bankA), Ground(bankB), wood, rope);
            taken.Add(new Vector3(bankA.x, bankA.y, 4f));
            taken.Add(new Vector3(bankB.x, bankB.y, 4f));

            // Old Snapper's pond at the foot of the crag falls; the gold vault behind those falls, in the mountain.
            Point(story, "SnapperPond", new Vector3(Island3Spring.x, WaterLevel - 0.9f, Island3Spring.y), 180f);
            Point(story, "UpRiver", Ground(Island3Spring + (bankB - Island3Spring).normalized * 14f) + Vector3.up * 0.2f, 0f);
            Point(story, "CaveMouth", new Vector3(CragFalls.x, WaterLevel, CragFalls.y - 5f), 0f);
            Point(story, "CaveLedge", new Vector3(CragFalls.x, VaultFloor + 0.1f, CragFalls.y + VaultDepth - 5f), 0f);
        }

        /// <summary>Review pictures of chapter 3's places (added to the island 3 set).</summary>
        private static IEnumerable<(string, Vector3, Vector3)> Chapter3Views()
        {
            Transform Find(string name) => GameObject.Find("Environment/Island3/Story/" + name)?.transform;
            if (Find("Island3GunShop") is { } stall)
                yield return ("ch3-gun-stall", stall.TransformPoint(new Vector3(2.5f, 1.7f, 5f)), stall.position + Vector3.up * 1.4f);
            if (Find("RiverBankA") is { } a && Find("RiverBankB") is { } b)
            {
                Vector3 side = Vector3.Cross(Vector3.up, (b.position - a.position).normalized);
                yield return ("ch3-rope-swing", (a.position + b.position) * 0.5f + side * 14f + Vector3.up * 4f, (a.position + b.position) * 0.5f + Vector3.up * 3f);
            }
            if (Find("Perch_1") is { } perch)
                yield return ("ch3-perch", perch.position + new Vector3(9f, -1.5f, -12f), perch.position);
            if (Find("Thief") is { } thief)
                yield return ("ch3-thief-shack", thief.position + thief.forward * 9f + Vector3.up * 2.5f, thief.position + Vector3.up * 1.5f);
            if (Find("SnapperPond") is { } pond)
                yield return ("ch3-snapper-pond", pond.position + new Vector3(10f, 6f, -14f), pond.position + Vector3.up);
            if (Find("CaveMouth") is { } door)
                yield return ("ch3-vault-door", door.position + new Vector3(4f, 3f, -9f), door.position + new Vector3(0f, 2.5f, 5f));
            if (Find("CaveLedge") is { } hall)
            {
                yield return ("ch3-vault-hall", hall.position + new Vector3(-6f, 4.5f, -2.5f), hall.position + new Vector3(0f, 1f, 6f));
                yield return ("ch3-vault-tunnel", hall.position + new Vector3(0f, 1.7f, -14f), hall.position + new Vector3(0f, 1.5f, 4f));
            }
        }

        private static void BuildGunStall(Transform parent, Vector3 at, float yaw, Material wood)
        {
            Material canvas = GetMaterial("PirateTarpRed", new Color(0.62f, 0.17f, 0.14f));
            var stall = new GameObject("Island3GunShop").transform;
            stall.SetParent(parent, false);
            stall.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            GameObject counter = Primitive(PrimitiveType.Cube, "Counter", stall, new Vector3(0f, 0.55f, 0f), new Vector3(3.2f, 1.1f, 0.9f), wood);
            foreach (float x in new[] { -1.5f, 1.5f })
                foreach (float z in new[] { -1.4f, 0.4f })
                    Primitive(PrimitiveType.Cube, "AwningPost", stall, new Vector3(x, 1.4f, z), new Vector3(0.12f, 2.8f, 0.12f), wood);
            Primitive(PrimitiveType.Cube, "Awning", stall, new Vector3(0f, 2.85f, -0.5f), new Vector3(3.6f, 0.08f, 2.4f), canvas, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            TextMesh sign = WorldText(stall, "Sign", new Vector3(0f, 2.45f, 0.48f), "GUNS & SILENCERS", 80, 0.035f, new Color(1f, 0.9f, 0.6f));
            sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // The shop lives on the stall's root so the director finds it by name; the counter is what you look at.
            var shopGo = counter;
            shopGo.AddComponent<NetworkObject>();
            ShopCounter shop = shopGo.AddComponent<ShopCounter>();
            var spawn = new GameObject("ShopSpawn").transform;
            spawn.SetParent(stall, false);
            spawn.localPosition = new Vector3(0.6f, 1.3f, 0f);
            SetRef(shop, "_spawnPoint", spawn);
            var so = new SerializedObject(shop);
            Require(so, "_title").stringValue = "Skull Cove guns";
            SerializedProperty products = Require(so, "_products");
            (string item, int price, string blurb)[] stock =
            {
                ("Pistol", 250, "Every pirate has one. Hold it and buy a suppressor from the parts list."),
                ("Shotgun", 500, "For the big ones. Takes a suppressor too."),
                ("SMG", 700, "Sprays. Loud without a suppressor."),
            };
            products.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++)
            {
                SerializedProperty product = products.GetArrayElementAtIndex(i);
                product.FindPropertyRelative("Item").stringValue = stock[i].item;
                product.FindPropertyRelative("Price").intValue = stock[i].price;
                product.FindPropertyRelative("Blurb").stringValue = stock[i].blurb;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            ConfigureInteractable(shopGo.AddComponent<Interactable>(), new[] { counter.GetComponent<Collider>() }, new[] { counter.GetComponent<Renderer>() }, 3f);
        }

        /// <summary>A rope tree over the river: a post on each bank, a beam across, a rope hanging near each bank.</summary>
        private static void BuildRopeSwing(Transform parent, Vector3 bankA, Vector3 bankB, Material wood, Material rope)
        {
            var swing = new GameObject("RopeSwing").transform;
            swing.SetParent(parent, false);
            float top = Mathf.Max(bankA.y, bankB.y) + 7.5f;
            foreach (Vector3 bank in new[] { bankA, bankB })
                Primitive(PrimitiveType.Cylinder, "RopePost", swing, new Vector3(bank.x, (bank.y + top) * 0.5f, bank.z), new Vector3(0.35f, (top - bank.y) * 0.5f, 0.35f), wood);
            Vector3 mid = (bankA + bankB) * 0.5f;
            Vector3 span = new(bankB.x - bankA.x, 0f, bankB.z - bankA.z);
            Primitive(PrimitiveType.Cylinder, "RopeBeam", swing, new Vector3(mid.x, top, mid.z), new Vector3(0.3f, span.magnitude * 0.5f + 0.3f, 0.3f), wood, keepCollider: false)
                .transform.rotation = Quaternion.FromToRotation(Vector3.up, span.normalized);
            foreach ((Vector3 from, Vector3 to, string name) in new[] { (bankA, bankB, "RopeA"), (bankB, bankA, "RopeB") })
            {
                // The rope hangs from the beam a third of the way out, its end pulled in to the bank (tied to the post).
                Vector3 pivot = Vector3.Lerp(new Vector3(from.x, top, from.z), new Vector3(to.x, top, to.z), 0.5f);
                Vector3 grip = from + Vector3.up * 1.6f + (to - from).normalized * 0.8f;
                Vector3 d = grip - pivot;
                GameObject r = Primitive(PrimitiveType.Cylinder, name, swing, (pivot + grip) * 0.5f, new Vector3(0.12f, d.magnitude * 0.5f, 0.12f), rope);
                r.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                // Its collider is what you look at to grab it (a thicker one than the rope you see).
                var capsule = r.GetComponent<CapsuleCollider>();
                if (capsule != null) { capsule.radius = 2.5f; capsule.isTrigger = false; }
                var handler = r.AddComponent<RopeSwing>();
                var target = new GameObject(name + "_Lands").transform;
                target.SetParent(swing, false);
                target.position = to + Vector3.up * 0.3f + (from - to).normalized * 0.8f;
                var pivotPoint = new GameObject(name + "_Pivot").transform;
                pivotPoint.SetParent(swing, false);
                pivotPoint.position = pivot;
                SetRef(handler, "_landing", target);
                SetRef(handler, "_pivot", pivotPoint);
                ConfigureInteractable(r.AddComponent<Interactable>(), new[] { r.GetComponent<Collider>() }, new[] { r.GetComponent<Renderer>() }, 3.5f);
            }
        }
    }
}
