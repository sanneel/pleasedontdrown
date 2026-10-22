using System;
using System.Collections.Generic;
using System.Linq;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 3's sewer and the Pirate King's throne room (A11 / C5, Docs/Overnight-Prompt-Shako-Island-3.md), so beat
    /// 3.7b plays: the story points <c>Environment/Island3/Story/SewerEntrance</c> and <c>PalaceThrone</c>.
    ///  * The sewer: a stone tunnel from the sea cave behind the south waterfall (its west wall, at sea level) straight
    ///    up under the plateau to the castle yard's drain (C5's SewerExitDrain). Lanterns along it, a channel of dark
    ///    water down the middle, a ladder up the shaft at the end (World.SewerLadder: E climbs out at SewerExit; the
    ///    grate in the yard climbs back down).
    ///  * The throne room: the keep's hall made 3 m deeper (back wall, dais, throne, gold and banners move back) for a
    ///    13.6 x 11 m fight floor, the carpet a proper red to the dais, the torches by the door lit.
    /// Runs again cleanly: its own pieces live under Environment/Island3/Island3Sewer (rebuilt each time); the cave
    /// wall it opens and the rocks in the doorway are switched off, not deleted; the keep's pieces are put at absolute
    /// places. Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Island3PalaceBatch</c>
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // The tunnel: floor centre at the cave end and under the drain (world), inner width and height.
        private static readonly Vector3 SewerCaveEnd = new(71.6f, 0.6f, -994.0f), SewerCastleEnd = new(-15.8f, 9.0f, -982.6f);
        private const float SewerWidth = 3f, SewerHeight = 3.2f, SewerWall = 0.6f;
        private const float KeepBackZ = -1002.0f; // the keep's back wall (centre), 3 m further back than C built it

        public static void Island3PalaceBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            Transform castle = island.Find("PirateCastle") ?? throw new InvalidOperationException("No castle");
            Transform cave = island.Find("MoneyCave") ?? throw new InvalidOperationException("No sea cave");
            Transform story = island.Find("Story") ?? throw new InvalidOperationException("No island 3 story points");
            if (island.Find("Island3Sewer") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island3Sewer").transform;
            root.SetParent(island, false);

            BuildSewer(root, cave, castle);
            BuildThroneRoom(castle);
            Transform throne = castle.Find("Tripo_pirate_throne") ?? castle.Find("ThroneSeat");
            Vector3 front = new(throne.position.x, castle.Find("Carpet").position.y - 0.02f, castle.Find("Dais").position.z + 1.8f + 1.2f);
            StoryPoint(story, "PalaceThrone", front, 0f);
            StoryPoint(story, "SewerEntrance", SewerCaveEnd + Vector3.up * 0.05f, Yaw(SewerCastleEnd - SewerCaveEnd));

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Island3Palace] PASS: sewer {Vector3.Distance(SewerCaveEnd, SewerCastleEnd):F0} m, PalaceThrone at {front}, " +
                      $"{root.GetComponentsInChildren<Transform>().Length} objects");
        }

        private static float Yaw(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        private static void StoryPoint(Transform story, string name, Vector3 at, float yaw)
        {
            Transform p = story.Find(name);
            if (p == null)
            {
                p = new GameObject(name).transform;
                p.SetParent(story, false);
            }
            p.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        }

        // ------------------------------------------------------------------ the sewer

        private static void BuildSewer(Transform root, Transform cave, Transform castle)
        {
            // The island's own stone (world-space textured: nothing stretches down an 89 m tunnel): the cave's rock for the
            // walls and roof, the castle's dark paving underfoot, its masonry for the arches and the shaft.
            Material rock = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Data/Materials/CaveRock.mat");
            Material wet = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Data/Materials/CastleStoneDark.mat");
            Material stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Data/Materials/CastleStone.mat");
            if (rock == null || wet == null || stone == null) throw new InvalidOperationException("Island 3 stone materials missing");
            Material water = GetMaterial("SewerWater", new Color(0.05f, 0.09f, 0.08f), 0.2f, 0.95f, new Color(0.01f, 0.03f, 0.03f));
            Material iron = GetMaterial("GateIron", new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.35f);
            Material flame = GetMaterial("SewerFlame", new Color(1f, 0.6f, 0.25f), emission: new Color(3.2f, 1.5f, 0.45f));

            // The doorway in the cave's west wall: the wall in four pieces round the tunnel (the original switched off).
            Transform caveWall = cave.Cast<Transform>().FirstOrDefault(t => t.name == "CaveWall" && Mathf.Abs(t.position.x - 68.3f) < 1f);
            if (caveWall == null) throw new InvalidOperationException("No west cave wall");
            caveWall.gameObject.SetActive(true);
            Bounds w = caveWall.GetComponent<Renderer>().bounds;
            Material wallMat = caveWall.GetComponent<Renderer>().sharedMaterial;
            caveWall.gameObject.SetActive(false);
            float dz0 = -996.4f, dz1 = -991.0f, sill = 0.25f, head = 5.2f;
            var cut = new GameObject("CaveWallCut").transform;
            cut.SetParent(root, false);
            void WallPiece(string n, float z0, float z1, float y0, float y1) =>
                ResortBox(cut, n, new Vector3(w.center.x, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(w.size.x, y1 - y0, z1 - z0), wallMat);
            WallPiece("CaveWallSouth", w.min.z, dz0, w.min.y, w.max.y);
            WallPiece("CaveWallNorth", dz1, w.max.z, w.min.y, w.max.y);
            WallPiece("CaveWallBelow", dz0, dz1, w.min.y, sill);
            WallPiece("CaveWallAbove", dz0, dz1, head, w.max.y);
            // The cave's wall rocks in the doorway: switched off.
            foreach (Transform caveRock in cave.Cast<Transform>().Where(t => t.name == "Rock"))
            {
                Vector3 p = caveRock.position;
                bool inDoor = Mathf.Abs(p.x - 71.2f) < 1f && p.z > dz0 - 2.2f && p.z < dz1 + 2.2f;
                caveRock.gameObject.SetActive(!inDoor);
            }

            // The tunnel: one straight run from the cave up to under the drain, plus the shaft at its end.
            Vector3 a = SewerCaveEnd, b = SewerCastleEnd;
            Vector3 dir = (b - a).normalized;
            float length = Vector3.Distance(a, b);
            var tube = new GameObject("SewerTunnel").transform;
            tube.SetParent(root, false);
            tube.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(dir));
            float half = SewerWidth * 0.5f;
            // Floor and walls the full length (plus a little into the cave); the roof stops short of the shaft.
            ResortBox(tube, "Floor", new Vector3(0f, -0.15f, -0.4f), new Vector3(SewerWidth + 2f * SewerWall, 0.3f, length + 0.8f), wet);
            foreach (float side in new[] { -1f, 1f })
            {
                ResortBox(tube, "Wall", new Vector3(side * (half + SewerWall * 0.5f), SewerHeight * 0.5f, -0.4f), new Vector3(SewerWall, SewerHeight + 0.6f, length + 0.8f), rock);
                // A walkway each side, the dark water channel between (a hand lower, no collision: you can walk in it).
            }
            float shaft = 1.8f;
            ResortBox(tube, "Roof", new Vector3(0f, SewerHeight + 0.15f, -shaft * 0.5f - 0.4f), new Vector3(SewerWidth + 2f * SewerWall, 0.3f, length - shaft + 0.8f), rock);
            ResortBox(tube, "EndWall", new Vector3(0f, SewerHeight * 0.5f, length * 0.5f + SewerWall * 0.5f), new Vector3(SewerWidth + 2f * SewerWall, SewerHeight + 0.6f, SewerWall), stone);
            GameObject channel = Primitive(PrimitiveType.Cube, "Water", tube, new Vector3(0f, 0.012f, 0f), new Vector3(0.9f, 0.02f, length), water, keepCollider: false);
            channel.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (float side in new[] { -1f, 1f })
                Primitive(PrimitiveType.Cube, "Kerb", tube, new Vector3(side * 0.5f, 0.04f, 0f), new Vector3(0.12f, 0.08f, length), stone, keepCollider: false);
            // Stone ribs every 6 m (arches seen down the tunnel).
            for (float z = -length * 0.5f + 3f; z < length * 0.5f - shaft; z += 6f)
            {
                foreach (float side in new[] { -1f, 1f })
                    Primitive(PrimitiveType.Cube, "Rib", tube, new Vector3(side * (half - 0.12f), SewerHeight * 0.5f, z), new Vector3(0.24f, SewerHeight, 0.4f), stone, keepCollider: false);
                Primitive(PrimitiveType.Cube, "RibTop", tube, new Vector3(0f, SewerHeight - 0.12f, z), new Vector3(SewerWidth, 0.24f, 0.4f), stone, keepCollider: false);
            }

            // The shaft up from the tunnel's end to just under the drain (looking up: walls and the ladder into the dark).
            Vector3 shaftFoot = b + Vector3.up * SewerHeight;
            float shaftTop = castle.Find("SewerExitDrain") is { } drainT ? drainT.position.y - 0.2f : 18.1f;
            float shaftH = shaftTop - shaftFoot.y;
            var shaftT = new GameObject("SewerShaft").transform;
            shaftT.SetParent(root, false);
            Vector3 shaftAt = b - Vector3.ProjectOnPlane(dir, Vector3.up).normalized * (shaft * 0.5f); // over the tunnel's last stretch (no roof there)
            shaftT.SetPositionAndRotation(new Vector3(shaftAt.x, shaftFoot.y + shaftH * 0.5f, shaftAt.z), Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Vector3.up)));
            float sh = shaft * 0.5f;
            ResortBox(shaftT, "ShaftWallE", new Vector3(sh + 0.3f, 0f, 0f), new Vector3(0.6f, shaftH, shaft + 1.2f), stone);
            ResortBox(shaftT, "ShaftWallW", new Vector3(-sh - 0.3f, 0f, 0f), new Vector3(0.6f, shaftH, shaft + 1.2f), stone);
            ResortBox(shaftT, "ShaftWallN", new Vector3(0f, 0f, sh + 0.3f), new Vector3(shaft, shaftH, 0.6f), stone);
            ResortBox(shaftT, "ShaftWallS", new Vector3(0f, 0f, -sh - 0.3f), new Vector3(shaft, shaftH, 0.6f), stone);
            ResortBox(shaftT, "ShaftCap", new Vector3(0f, shaftH * 0.5f + 0.05f, 0f), new Vector3(shaft + 1.2f, 0.1f, shaft + 1.2f), GetMaterial("SewerDark", new Color(0.02f, 0.02f, 0.02f)));

            // The ladder: on the end wall, from the floor up the shaft.
            var ladder = new GameObject("SewerLadder").transform;
            ladder.SetParent(root, false);
            Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            ladder.SetPositionAndRotation(b - flat * 0.12f, Quaternion.LookRotation(-flat)); // on the end wall's inside face
            float ladderH = shaftTop - b.y;
            foreach (float side in new[] { -0.25f, 0.25f })
                Primitive(PrimitiveType.Cube, "Rail", ladder, new Vector3(side, ladderH * 0.5f, 0f), new Vector3(0.05f, ladderH, 0.05f), iron, keepCollider: false);
            for (float y = 0.3f; y < ladderH; y += 0.3f)
                Primitive(PrimitiveType.Cube, "Rung", ladder, new Vector3(0f, y, -0.02f), new Vector3(0.5f, 0.035f, 0.035f), iron, keepCollider: false);
            // A lamp over the ladder (you see where it goes up).
            var ladderLight = new GameObject("LadderLight").AddComponent<Light>();
            ladderLight.transform.SetParent(ladder, false);
            ladderLight.transform.localPosition = new Vector3(0f, 2.6f, -0.9f);
            ladderLight.type = LightType.Point;
            ladderLight.range = 7f;
            ladderLight.intensity = 2.4f;
            ladderLight.color = new Color(1f, 0.62f, 0.3f);
            ladderLight.shadows = LightShadows.None;
            GameObject lamp = Primitive(PrimitiveType.Cube, "LadderLamp", ladder, new Vector3(0.55f, 2.4f, -0.1f), new Vector3(0.2f, 0.28f, 0.2f), flame, keepCollider: false);
            lamp.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var grab = new GameObject("LadderGrab").AddComponent<BoxCollider>();
            grab.transform.SetParent(ladder, false);
            grab.center = new Vector3(0f, 1.3f, -0.05f);
            grab.size = new Vector3(0.7f, 2.6f, 0.15f);

            // Climbing: up out of the drain at SewerExit; down from the drain to the ladder's foot.
            Transform exit = castle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SewerExit");
            Transform grate = castle.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SewerGrateHinge");
            if (exit == null) throw new InvalidOperationException("No SewerExit in the castle yard (TeamCPalaceBatch)");
            var down = new GameObject("SewerLadderFoot").transform;
            down.SetParent(root, false);
            down.SetPositionAndRotation(b - flat * 2.2f + Vector3.up * 0.1f, Quaternion.LookRotation(-flat));
            var up = ladder.gameObject.AddComponent<SewerLadder>();
            SetRef(up, "_to", exit);
            SetRef(up, "_grate", grate);
            SetField(up, "_prompt", p => p.stringValue = "Climb up the ladder");
            ConfigureInteractable(ladder.gameObject.AddComponent<Interactable>(), new Collider[] { grab }, ladder.GetComponentsInChildren<Renderer>(), 3f);
            // The yard side: a thin lid on the drain's kerb to look at (the kerb itself is C5's).
            Transform drain = castle.Find("SewerExitDrain");
            if (drain != null)
            {
                var hatch = new GameObject("SewerHatch").transform;
                hatch.SetParent(root, false);
                hatch.position = drain.position;
                var lid = hatch.gameObject.AddComponent<BoxCollider>();
                lid.center = new Vector3(0f, 0.3f, 0f);
                lid.size = new Vector3(1.6f, 0.06f, 1.6f);
                var downLadder = hatch.gameObject.AddComponent<SewerLadder>();
                SetRef(downLadder, "_to", down);
                SetRef(downLadder, "_grate", grate);
                SetField(downLadder, "_prompt", p => p.stringValue = "Climb down into the sewer");
                // The kerb itself counts too (you look at its stone rim as often as at the grate).
                var colliders = new List<Collider> { lid };
                colliders.AddRange(drain.GetComponentsInChildren<Collider>());
                ConfigureInteractable(hatch.gameObject.AddComponent<Interactable>(), colliders.ToArray(), drain.GetComponentsInChildren<Renderer>(), 3f);
            }

            // Lanterns on the walls every 9 m, turn about; a warm light every second one (no shadows).
            int n = 0;
            for (float s = 4f; s < length - 3f; s += 9f, n++)
            {
                Vector3 floorAt = a + dir * s;
                float side = n % 2 == 0 ? 1f : -1f;
                Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
                Vector3 at = floorAt + right * side * (half - 0.15f) + Vector3.up * 2.1f;
                var lantern = new GameObject("SewerLantern").transform;
                lantern.SetParent(root, false);
                lantern.SetPositionAndRotation(at, Quaternion.LookRotation(-right * side));
                Primitive(PrimitiveType.Cube, "Bracket", lantern, new Vector3(0f, 0f, -0.08f), new Vector3(0.08f, 0.08f, 0.3f), iron, keepCollider: false);
                // An open iron cage round the flame: a cap, a base and four corner bars.
                Primitive(PrimitiveType.Cube, "CageTop", lantern, new Vector3(0f, 0.12f, 0.08f), new Vector3(0.24f, 0.04f, 0.24f), iron, keepCollider: false);
                Primitive(PrimitiveType.Cube, "CageBase", lantern, new Vector3(0f, -0.2f, 0.08f), new Vector3(0.24f, 0.04f, 0.24f), iron, keepCollider: false);
                foreach (Vector2 c in new[] { new Vector2(-0.1f, -0.02f), new Vector2(0.1f, -0.02f), new Vector2(-0.1f, 0.18f), new Vector2(0.1f, 0.18f) })
                    Primitive(PrimitiveType.Cube, "CageBar", lantern, new Vector3(c.x, -0.04f, c.y), new Vector3(0.02f, 0.3f, 0.02f), iron, keepCollider: false);
                GameObject f = Primitive(PrimitiveType.Cube, "Flame", lantern, new Vector3(0f, -0.05f, 0.08f), new Vector3(0.14f, 0.22f, 0.14f), flame, keepCollider: false);
                f.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(lantern, false);
                light.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                light.type = LightType.Point;
                light.range = 9f;
                light.intensity = 2.2f;
                light.color = new Color(1f, 0.62f, 0.3f);
                light.shadows = LightShadows.None;
            }
            Debug.Log($"[Island3Palace] sewer: {length:F1} m, slope {Mathf.Asin((b.y - a.y) / length) * Mathf.Rad2Deg:F1} deg, {n} lanterns, shaft {shaftH:F1} m");
        }

        // ------------------------------------------------------------------ the throne room

        private static void BuildThroneRoom(Transform castle)
        {
            Transform back = castle.Find("KeepBack") ?? throw new InvalidOperationException("No keep");
            float dz = KeepBackZ - back.position.z; // 0 when it has been moved already
            if (Mathf.Abs(dz) > 0.01f)
            {
                var moving = new List<Transform> { back };
                foreach (Transform t in castle)
                {
                    if (t == back) continue;
                    bool backPart = t.name is "Dais" or "ThroneSeat" or "ThroneBack" or "ThroneArm" or "GoldHeap" or "Banner" or "Tripo_pirate_throne" or "Tripo_gold_coin_pile";
                    bool backTorch = t.name == "Torch" && t.position.z < back.position.z + 4f && t.position.y < 19f && Mathf.Abs(t.position.x - back.position.x) < 8f;
                    if (backPart || backTorch) moving.Add(t);
                }
                foreach (Transform t in moving) t.position += new Vector3(0f, 0f, dz);
                Debug.Log($"[Island3Palace] throne room: {moving.Count} pieces moved {dz:F1} m back");
            }
            float front = castle.Find("KeepFront").position.z;
            float e0 = front + 0.6f, e1 = KeepBackZ - 0.6f, span = e0 - e1;
            foreach (Transform t in castle.Cast<Transform>().Where(t => t.name is "KeepSide" or "KeepRoof"))
            {
                t.position = new Vector3(t.position.x, t.position.y, (e0 + e1) * 0.5f);
                t.localScale = new Vector3(t.localScale.x, t.localScale.y, span);
            }
            // The carpet: a proper red, from the door to the dais.
            Transform carpet = castle.Find("Carpet");
            Transform dais = castle.Find("Dais");
            float daisFront = dais.position.z + dais.localScale.z * 0.5f;
            float c0 = front - 0.6f, c1 = daisFront;
            carpet.position = new Vector3(carpet.position.x, carpet.position.y, (c0 + c1) * 0.5f);
            carpet.localScale = new Vector3(carpet.localScale.x, carpet.localScale.y, c0 - c1);
            carpet.GetComponent<Renderer>().sharedMaterial = GetMaterial("ThroneCarpet", new Color(0.45f, 0.04f, 0.05f), smoothness: 0.05f);
            // The torches by the door burn too (the room was too dark to fight in).
            foreach (Transform t in castle.Cast<Transform>().Where(t => t.name == "Torch" && Mathf.Abs(t.position.z - (front + 2.2f)) < 1.5f))
            {
                if (t.Find("Light") != null) continue;
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(t, false);
                light.transform.localPosition = new Vector3(0f, 3f, 0f);
                light.type = LightType.Point;
                light.range = 10f;
                light.intensity = 2.2f;
                light.color = new Color(1f, 0.62f, 0.3f);
                light.shadows = LightShadows.None;
            }
            // Two more standing torches halfway down the side walls (the middle of the hall was dark).
            foreach (Transform t in castle.Cast<Transform>().Where(t => t.name == "ThroneTorchMid").ToArray()) Object.DestroyImmediate(t.gameObject);
            Material torchWood = GetMaterial("PirateTorchWood", new Color(0.24f, 0.16f, 0.1f));
            Material torchFlame = GetMaterial("SewerFlame", new Color(1f, 0.6f, 0.25f), emission: new Color(3.2f, 1.5f, 0.45f));
            Transform sideL = castle.Cast<Transform>().Where(t => t.name == "KeepSide").OrderBy(t => t.position.x).First();
            Transform sideR = castle.Cast<Transform>().Where(t => t.name == "KeepSide").OrderBy(t => t.position.x).Last();
            float midZ = (front + KeepBackZ) * 0.5f;
            foreach (Transform side in new[] { sideL, sideR })
            {
                float inward = side == sideL ? 1f : -1f;
                Vector3 foot = new(side.position.x + inward * (side.localScale.x * 0.5f + 0.5f), castle.Find("Carpet").position.y - 0.02f, midZ);
                Torch(castle, castle.InverseTransformPoint(foot), torchWood, torchFlame);
                castle.GetChild(castle.childCount - 1).name = "ThroneTorchMid";
            }
            Debug.Log($"[Island3Palace] throne room: floor {castle.Find("KeepSide").position.z:F1}, {span:F1} m deep inside the walls, carpet {c0 - c1:F1} m");
        }
    }
}
