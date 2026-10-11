using PleaseDontDrown.World;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The gold vault in the heart of the crags (Shako, 2026-10-11: "the gold inside the mountain, in the centre, where
    /// the waterfall is"). Behind the crag falls a door in the cliff: you swim in under the falling water, walk up a ramp
    /// and along a tunnel into a torch-lit hall in the middle of the mountain, the stolen casino money heaped round a
    /// mound of gold. Old Snapper guards the pond in front of it (chapter 3, beats 3.7-3.8).
    /// The ground mesh has a hole where the door is (InVaultHole); everything else lies under the mountain's surface.
    /// Frame: x across from the falls, z into the mountain from the falls' back wall (CragFalls), y world.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const float VaultDoorHalf = 2.5f, VaultFloor = 0.8f, VaultTunnelTop = 5f, VaultHall = 9f, VaultHallTop = 10.5f;

        /// <summary>Ground cells taken out for the door: the strip of cliff right behind the falls.</summary>
        private static bool InVaultHole(float cx, float cz) =>
            Mathf.Abs(cx - CragFalls.x) < VaultDoorHalf + 1f && Mathf.Abs(cz - CragFalls.y) < 1.5f;

        /// <summary>How far in the hall's middle is: under the crags' middle, but at least a short tunnel away.</summary>
        private static float VaultDepth => Mathf.Clamp(Island3Crags.y - CragFalls.y, 16f, 34f);

        private static void BuildGoldVault(Transform root)
        {
            Material rock = Island3RockMaterial("CaveRock", new Color(0.46f, 0.44f, 0.42f), "Cliff", 7f);
            Material rockDark = Island3RockMaterial("CaveRockDark", new Color(0.3f, 0.29f, 0.29f), "Rock", 4f);
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material flame = GetMaterial("TorchFlame", new Color(1f, 0.55f, 0.15f), emission: new Color(3f, 1.3f, 0.3f));
            Material gold = GetMaterial("PirateGold", new Color(0.95f, 0.72f, 0.2f), metallic: 0.7f, smoothness: 0.6f, emission: new Color(0.25f, 0.16f, 0.02f));
            Vector2 f = CragFalls;
            float D = VaultDepth, hallFrom = D - VaultHall, hallTo = D + VaultHall;
            float Mid(float a, float b) => (a + b) * 0.5f;

            var vault = new GameObject("GoldVault").transform;
            vault.SetParent(root, false);
            vault.position = new Vector3(f.x, 0f, f.y);
            TagSurface(vault.gameObject, SurfaceKind.Rock);

            // The door: two jambs and a lintel fill the hole in the cliff up to its top, round a 5 m wide opening. They
            // stand just behind the falling water (its sheets hang at z -1.4 and -1.85), so the falls hide the door.
            float top = CragFallsTop + 0.3f;
            const float doorFrom = -1.05f, doorTo = 2.6f, holeFrom = -2.6f, jamb = 2.2f;
            foreach (int side in new[] { -1, 1 })
                ResortBox(vault, "DoorJamb", new Vector3(side * (VaultDoorHalf + jamb * 0.5f), Mid(-3f, top), Mid(doorFrom, doorTo)),
                    new Vector3(jamb, top + 3f, doorTo - doorFrom), rock);
            ResortBox(vault, "DoorLintel", new Vector3(0f, Mid(VaultTunnelTop, top), Mid(doorFrom, doorTo)),
                new Vector3(VaultDoorHalf * 2f + 0.1f, top - VaultTunnelTop, doorTo - doorFrom), rock);
            // The pond's floor where the ground was cut away in front of the door.
            ResortBox(vault, "DoorSill", new Vector3(0f, -2.9f, Mid(holeFrom, doorTo)), new Vector3((VaultDoorHalf + jamb) * 2f, 2.4f, doorTo - holeFrom), rockDark);
            // Boulders lean on the cliff either side, so the door's edges don't read as a box.
            foreach (int side in new[] { -1, 1 })
            {
                SkullCoveRock(vault, new Vector3(side * (VaultDoorHalf + jamb + 0.6f), 2.5f, -1.6f), new Vector3(3f, 7f, 3f), side * 20f, side * 6f, localSpace: true);
                SkullCoveRock(vault, new Vector3(side * (VaultDoorHalf + jamb + 0.3f), 9.5f, -1.2f), new Vector3(2.6f, 6f, 2.6f), side * 50f, side * -5f, localSpace: true);
            }
            // A ramp out of the pond (swimmers walk out; nothing to climb), then the tunnel floor.
            float rise = VaultFloor + 1.8f, run = 6f, slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            ResortBox(vault, "VaultRamp", new Vector3(0f, Mid(-1.8f, VaultFloor) - 0.4f, Mid(-2f, -2f + run)),
                    new Vector3(VaultDoorHalf * 2f, 0.8f, Mathf.Sqrt(rise * rise + run * run) + 0.3f), rockDark)
                .transform.localRotation = Quaternion.Euler(-slope, 0f, 0f);

            // The tunnel: floor, walls and roof, all under the mountain.
            float tunnelFrom = -2f + run - 0.2f;
            ResortBox(vault, "TunnelFloor", new Vector3(0f, VaultFloor - 1f, Mid(tunnelFrom, hallFrom + 0.5f)),
                new Vector3(VaultDoorHalf * 2f + 2f, 2f, hallFrom + 0.5f - tunnelFrom), rockDark);
            foreach (int side in new[] { -1, 1 })
                ResortBox(vault, "TunnelWall", new Vector3(side * (VaultDoorHalf + 1f), Mid(VaultFloor - 1f, VaultTunnelTop + 1f), Mid(doorTo, hallFrom)),
                    new Vector3(2f, VaultTunnelTop - VaultFloor + 2f, hallFrom - doorTo), rock);
            ResortBox(vault, "TunnelRoof", new Vector3(0f, VaultTunnelTop + 0.75f, Mid(doorTo, hallFrom)),
                new Vector3(VaultDoorHalf * 2f + 2f, 1.5f, hallFrom - doorTo), rock);

            // The hall: 18 x 18 m, 10 m high, its door in the south wall where the tunnel comes in.
            ResortBox(vault, "HallFloor", new Vector3(0f, VaultFloor - 1f, D), new Vector3(VaultHall * 2f + 4f, 2f, VaultHall * 2f + 4f), rockDark);
            ResortBox(vault, "HallRoof", new Vector3(0f, VaultHallTop + 1f, D), new Vector3(VaultHall * 2f + 4f, 2f, VaultHall * 2f + 4f), rock);
            float wallH = VaultHallTop + 2f - (VaultFloor - 1f), wallY = Mid(VaultFloor - 1f, VaultHallTop + 2f);
            ResortBox(vault, "HallWallN", new Vector3(0f, wallY, hallTo + 1f), new Vector3(VaultHall * 2f + 4f, wallH, 2f), rock);
            foreach (int side in new[] { -1, 1 })
            {
                ResortBox(vault, "HallWall", new Vector3(side * (VaultHall + 1f), wallY, D), new Vector3(2f, wallH, VaultHall * 2f + 4f), rock);
                float w = VaultHall - VaultDoorHalf; // the south wall either side of the tunnel
                ResortBox(vault, "HallWallS", new Vector3(side * (VaultDoorHalf + w * 0.5f), wallY, hallFrom - 1f), new Vector3(w, wallH, 2f), rock);
            }
            ResortBox(vault, "HallWallSTop", new Vector3(0f, Mid(VaultTunnelTop, VaultHallTop + 2f), hallFrom - 1f),
                new Vector3(VaultDoorHalf * 2f, VaultHallTop + 2f - VaultTunnelTop, 2f), rock);

            // Rocks soften the box walls, stalactites hang from the roof.
            var rng = new System.Random(7717);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            foreach (int side in new[] { -1, 1 })
            {
                for (float z = hallFrom + 1.5f; z < hallTo - 1f; z += R(3.5f, 5f))
                    SkullCoveRock(vault, new Vector3(side * (VaultHall - 0.4f), R(1f, 4f), z), new Vector3(R(2f, 3.5f), R(3f, 6f), R(2.5f, 4f)), R(0f, 360f), R(-10f, 10f), localSpace: true);
                for (float z = doorTo + 2f; z < hallFrom - 1f; z += R(4f, 6f))
                    SkullCoveRock(vault, new Vector3(side * (VaultDoorHalf + 0.2f), R(0.6f, 2.5f), z), new Vector3(R(1f, 1.6f), R(1.5f, 2.5f), R(1.5f, 2.5f)), R(0f, 360f), R(-8f, 8f), localSpace: true);
            }
            for (float x = -VaultHall + 2f; x < VaultHall - 1f; x += R(3f, 4.5f))
                SkullCoveRock(vault, new Vector3(x, R(1f, 4f), hallTo - 0.4f), new Vector3(R(2.5f, 4f), R(3f, 6f), R(2f, 3f)), R(0f, 360f), R(-10f, 10f), localSpace: true);
            for (int i = 0; i < 22; i++)
                Cone(vault, "Stalactite", new Vector3(R(-VaultHall + 1f, VaultHall - 1f), VaultHallTop + 0.1f, R(hallFrom + 1f, hallTo - 1f)), R(0.3f, 0.8f), R(1.2f, 3.2f), rockDark, upsideDown: true);
            for (int i = 0; i < 6; i++)
                Cone(vault, "Stalactite", new Vector3(R(-1.8f, 1.8f), VaultTunnelTop + 0.05f, R(doorTo + 1f, hallFrom - 1f)), R(0.15f, 0.35f), R(0.5f, 1.2f), rockDark, upsideDown: true);

            // The gold: a mound in the middle, bars, chests, sacks, coin heaps and the casino's chips round it.
            var loot = new GameObject("StolenMoney").transform;
            loot.SetParent(vault, false);
            Vector3 centre = new(0f, VaultFloor, D + 1f);
            if (TripoModel("gold_hoard", loot, centre, 180f, Vector3.one) is { } hoard)
                foreach (MeshFilter filter in hoard.GetComponentsInChildren<MeshFilter>()) // walk round it, or up it
                    filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            else Primitive(PrimitiveType.Sphere, "GoldMound", loot, centre + Vector3.up * 0.4f, new Vector3(6f, 2.4f, 6f), gold);
            foreach ((float x, float z, float yaw) in new[] { (-5f, -2.5f, 30f), (5.2f, -1.8f, -25f), (-5.8f, 4.5f, 70f), (5.5f, 5f, -60f), (0f, 6.8f, 0f), (-2.5f, -5.5f, 10f) })
                TripoModel("gold_bar_stack", loot, centre + new Vector3(x, 0f, z), yaw, Vector3.one * R(0.9f, 1.2f));
            foreach ((float x, float z, float yaw) in new[] { (-6.8f, 1f, 90f), (6.8f, 1.5f, -90f), (-3.5f, 7.2f, 160f), (3.5f, 7.2f, 200f) })
                if (TripoModel("treasure_chest_open", loot, centre + new Vector3(x, 0f, z), yaw + 180f, Vector3.one) is { } chest)
                {
                    var box = chest.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.45f, 0f);
                    box.size = new Vector3(1.5f, 0.9f, 1.1f);
                }
            foreach ((float x, float z) in new[] { (-3.6f, -4.2f), (3.8f, -4.5f), (-7f, -5.5f), (7f, -5f), (-7.2f, 6.8f), (7.2f, 7f), (1.5f, 8f) })
                TripoModel("money_sack", loot, centre + new Vector3(x, 0f, z), R(0f, 360f), Vector3.one * R(0.9f, 1.15f));
            foreach ((float x, float z, float scale) in new[] { (-6.5f, -3.5f, 0.8f), (6.2f, 3.5f, 0.9f), (-4f, 3.5f, 0.7f) })
                TripoModel("gold_coin_pile", loot, centre + new Vector3(x, 0f, z), R(0f, 360f), Vector3.one * scale);
            foreach ((float x, float z) in new[] { (-2f, -6.8f), (2.2f, -6.5f), (-4.8f, -6.8f), (4.6f, -6.6f) })
                TripoModel("casino_chip_stacks", loot, centre + new Vector3(x, 0f, z), R(0f, 360f), Vector3.one * R(0.9f, 1.3f));

            // Torches along the tunnel and round the hall; the gold glows.
            for (float z = doorTo + 3f; z < hallFrom - 1f; z += 7f)
                Torch(vault, new Vector3(((int)z % 2 == 0 ? -1f : 1f) * (VaultDoorHalf - 0.35f), VaultFloor, z), wood, flame);
            foreach ((float x, float z) in new[] { (-VaultHall + 1f, hallFrom + 2f), (VaultHall - 1f, hallFrom + 2f), (-VaultHall + 1f, hallTo - 2f), (VaultHall - 1f, hallTo - 2f) })
                Torch(vault, new Vector3(x, VaultFloor, z), wood, flame);
            var glow = new GameObject("GoldGlow").AddComponent<Light>();
            glow.transform.SetParent(vault, false);
            glow.transform.localPosition = centre + Vector3.up * 4f;
            glow.type = LightType.Point;
            glow.range = 16f;
            glow.intensity = 2.4f;
            glow.color = new Color(1f, 0.8f, 0.4f);
            glow.shadows = LightShadows.None;

            // The mountain must cover it all: report anywhere its roof would poke out.
            float lowest = float.MaxValue;
            for (float z = doorTo; z <= hallTo + 2f; z += 2f)
                for (float x = -VaultHall - 2f; x <= VaultHall + 2f; x += 2f)
                {
                    if (z < hallFrom - 2f && Mathf.Abs(x) > VaultDoorHalf + 2f) continue;
                    float roof = z < hallFrom - 2f ? VaultTunnelTop + 1.5f : VaultHallTop + 2f;
                    lowest = Mathf.Min(lowest, Island3Height(f.x + x, f.y + z) - roof);
                }
            Debug.Log($"[Island3] gold vault {D:F0} m into the crags; the mountain is at least {lowest:F1} m over its roof" +
                      (lowest < 0.5f ? " (TOO THIN: the roof shows)" : ""));
        }
    }
}
