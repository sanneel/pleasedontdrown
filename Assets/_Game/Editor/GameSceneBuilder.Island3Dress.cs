using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 3 "looks finished" (Docs/Overnight-Prompt-Shako-Island-3.md, 2.4), sparse: pirate signposts at the forks
    /// (Tavern, Guns, Jungle, Castle, Dock: driftwood boards on a post, cream letters), and the bare east pirate dock
    /// dressed: barrels and crates at its land end, a coil of rope, two lantern posts (lit at night: island 3 always
    /// is). Everything under Environment/Island3/Island3Dress, rebuilt on every run; nothing else changes.
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Island3DressBatch</c>
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // Signposts (world x, z, which way the board faces, the words, an arrow: -1 left, 0 none, 1 right).
        private static readonly (string name, Vector2 at, float yaw, string text, int arrow)[] Island3Signs =
        {
            ("SignDock", new(106.5f, -898.6f), 270f, "DOCK", 0),
            ("SignGuns", new(101.5f, -907.0f), 210f, "GUNS", 0),
            ("SignTavern", new(84.0f, -949.5f), 180f, "TAVERN", 0),
            ("SignCastle", new(-1.2f, -959.5f), 180f, "CASTLE", 0),
            ("SignJungle", new(15.5f, -931.0f), 135f, "JUNGLE", 0),
        };

        public static void Island3DressBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            if (island.Find("Island3Dress") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island3Dress").transform;
            root.SetParent(island, false);
            Physics.SyncTransforms();

            Material board = GetMaterial("PirateSignBoard", new Color(0.36f, 0.25f, 0.16f), smoothness: 0.1f);
            Material post = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Color paint = new(0.93f, 0.86f, 0.68f);
            foreach (var s in Island3Signs) PirateSign(root, s.name, s.at, s.yaw, s.text, board, post, paint);

            DressPirateDock(island, root, post);
            Debug.Log($"[Island3Dress] PASS: {Island3Signs.Length} signs, the east dock dressed ({root.GetComponentsInChildren<Transform>().Length} objects)");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>A rough driftwood board on a post, the words painted on both faces, feet on the ground.</summary>
        private static void PirateSign(Transform parent, string name, Vector2 at, float yaw, string text, Material board, Material post, Color paint)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var feet = new Vector3(at.x, 0f, at.y);
            feet.y = SurfaceUnder(feet, 60f);
            root.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            Primitive(PrimitiveType.Cube, "Post", root, new Vector3(0f, 1.05f, -0.06f), new Vector3(0.14f, 2.1f, 0.14f), post);
            GameObject b = Primitive(PrimitiveType.Cube, "Board", root, new Vector3(0f, 1.9f, 0.03f), new Vector3(1.35f, 0.42f, 0.06f), board, keepCollider: false);
            b.transform.localRotation = Quaternion.Euler(0f, 0f, 3f); // nailed on a little crooked
            foreach (float side in new[] { 1f, -1f })
            {
                TextMesh t = WorldText(root, "Words", new Vector3(0f, 1.9f, 0.03f + 0.035f * side), text, 80, 0.026f, paint);
                t.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, side > 0 ? -3f : 3f);
            }
        }

        private static void DressPirateDock(Transform island, Transform root, Material wood)
        {
            Transform dock = island.Find("PirateDock");
            if (dock == null) return;
            Transform deck = dock.Find("Deck");
            var group = new GameObject("PirateDockDress").transform;
            group.SetParent(root, false);
            Material barrelMat = GetMaterial("TavernBarrel", new Color(0.46f, 0.3f, 0.16f));
            Material hoop = GetMaterial("GateIron", new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.35f);
            Material rope = GetMaterial("MarinaRope", new Color(0.9f, 0.85f, 0.7f));
            Material lamp = GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f));
            float deckTop = deck != null ? SurfaceUnder(deck.position, 5f) : 0.35f;
            // The dock runs along x (land at its west end, about x 110); barrels and crates on the sand beside its root.
            float landX = 109.5f, z = dock.position.z;
            void Barrel(string n, Vector3 feet, bool lying = false)
            {
                feet.y = SurfaceUnder(feet, 10f);
                var b = new GameObject(n).transform;
                b.SetParent(group, false);
                b.position = feet + Vector3.up * (lying ? 0.4f : 0.5f);
                if (lying) b.rotation = Quaternion.Euler(0f, 30f, 90f);
                Primitive(PrimitiveType.Cylinder, "Body", b, Vector3.zero, new Vector3(0.75f, 0.5f, 0.75f), barrelMat);
                foreach (float y in new[] { -0.32f, 0.32f })
                    Primitive(PrimitiveType.Cylinder, "Hoop", b, new Vector3(0f, y, 0f), new Vector3(0.78f, 0.03f, 0.78f), hoop, keepCollider: false);
            }
            Barrel("Barrel", new Vector3(landX - 1.4f, 0f, z + 2.3f));
            Barrel("Barrel", new Vector3(landX - 0.6f, 0f, z + 2.9f));
            Barrel("BarrelLying", new Vector3(landX - 2.4f, 0f, z + 3.1f), lying: true);
            foreach ((Vector3 p, float yaw) in new[] { (new Vector3(landX - 1.2f, 0f, z - 2.4f), 12f), (new Vector3(landX - 2.5f, 0f, z - 2.6f), -20f) })
            {
                var c = new GameObject("Crate").transform;
                c.SetParent(group, false);
                Vector3 feet = p;
                feet.y = SurfaceUnder(p, 10f);
                c.position = feet;
                if (PropModel("crate", c, Vector3.zero, yaw) == null)
                    Primitive(PrimitiveType.Cube, "Box", c, new Vector3(0f, 0.45f, 0f), Vector3.one * 0.9f, wood);
                else Collider(c, "CrateBox", new Vector3(0f, 0.48f, 0f), new Vector3(1.4f, 0.96f, 1.4f), Quaternion.Euler(0f, yaw, 0f));
            }
            // A coil of rope on the deck by the bollard.
            GameObject coil = Primitive(PrimitiveType.Cylinder, "RopeCoil", group, new Vector3(landX + 3f, deckTop + 0.06f, z + 0.7f), new Vector3(0.7f, 0.06f, 0.7f), rope, keepCollider: false);
            _ = coil;
            // Lantern posts: at the dock's root and two thirds out, on the north edge.
            LanternPost(group, new Vector3(landX + 0.6f, deckTop, z + 1.05f), wood, lamp, "NightLight_PirateDock");
            LanternPost(group, new Vector3(landX + 16f, deckTop, z + 1.05f), wood, lamp, "NightLight_PirateDock");
        }
    }
}
