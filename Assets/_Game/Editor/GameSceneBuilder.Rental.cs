using PleaseDontDrown.Avatars;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        // Milo's T pier (dock-local): the walkway's 0.45 m planks every 0.48 m from z 10 toward the sea, up to the cross
        // dock (z -11, 3.2 m deep, so its back edge is at -9.4); the last plank fills what's left, 3 cm gaps either side.
        private const int PierPlanks = 41;
        private const float CrossDockBack = -9.4f;
        private static float PierPlankStart(int i) => 10f - i * 0.48f;
        private static float PierLastFrom => PierPlankStart(PierPlanks - 2) - 0.225f - 0.03f;
        private static float PierPlankZ(int i) => i < PierPlanks - 1 ? PierPlankStart(i) : (PierLastFrom + CrossDockBack + 0.03f) * 0.5f;
        private static float PierPlankDepth(int i) => i < PierPlanks - 1 ? 0.45f : PierLastFrom - (CrossDockBack + 0.03f);

        private static void BuildRentalDock(Transform env, Transform story, GameObject npcPrefab,
            out StoryNpc operatorNpc, out Vehicle[] fleet, out Transform[] berths, out Transform keySpot)
        {
            Material wood = GetMaterial("RentalHoneyWood", new Color(0.63f, 0.39f, 0.20f));
            Material light = GetMaterial("RentalPaleWood", new Color(0.81f, 0.61f, 0.35f));
            Material teal = GetMaterial("RentalTeal", new Color(0.035f, 0.43f, 0.46f));
            Material cream = GetMaterial("RentalCream", new Color(1f, 0.91f, 0.66f));
            Material orange = GetMaterial("RentalOrange", new Color(1f, 0.39f, 0.08f));
            var dock = new GameObject("MilosRentalDock").transform;
            dock.SetParent(env, false);
            dock.position = new Vector3(32f, 0f, -3f);
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            // A broad T pier: individual planks, sturdy piles and four open boarding bays. The walkway's planks stop at
            // the cross dock (under it they lay in its plane and flickered through it); the last one fills the gap.
            for (int i = 0; i < PierPlanks; i++)
                Primitive(PrimitiveType.Cube, "PierPlank", dock, new Vector3(0f, 0.26f, PierPlankZ(i)), new Vector3(3f, 0.20f, PierPlankDepth(i)), i % 3 == 0 ? light : wood);
            for (int i = 0; i < 50; i++)
                Primitive(PrimitiveType.Cube, "CrossDockPlank", dock, new Vector3(-12f + i * 0.48f, 0.26f, -11f), new Vector3(0.45f, 0.20f, 3.2f), i % 3 == 0 ? light : wood);
            foreach (float z in new[] { -12f, -7f, -2f, 3f, 8f })
                foreach (float x in new[] { -1.3f, 1.3f })
                    Primitive(PrimitiveType.Cylinder, "TimberPile", dock, new Vector3(x, -1.4f, z), new Vector3(0.25f, 2.0f, 0.25f), wood);
            foreach (float x in new[] { -11f, -5f, 5f, 11f })
                Primitive(PrimitiveType.Cylinder, "EndPile", dock, new Vector3(x, -1.2f, -10f), new Vector3(0.3f, 2.0f, 0.3f), wood);

            var kiosk = new GameObject("RentalKiosk").transform;
            kiosk.SetParent(env, false);
            kiosk.position = OnGround(new Vector3(37f, 0f, 10f));
            Primitive(PrimitiveType.Cube, "CounterBody", kiosk, new Vector3(0f, 0.55f, 0f), new Vector3(3.7f, 1.1f, 1.25f), teal);
            Primitive(PrimitiveType.Cube, "CounterTop", kiosk, new Vector3(0f, 1.14f, 0f), new Vector3(4.0f, 0.17f, 1.45f), light);
            foreach (float x in new[] { -1.8f, 1.8f })
                Primitive(PrimitiveType.Cylinder, "CanopyPost", kiosk, new Vector3(x, 1.55f, 0.3f), new Vector3(0.14f, 1.55f, 0.14f), wood);
            for (int i = 0; i < 8; i++)
                Primitive(PrimitiveType.Cube, "StripedCanopy", kiosk, new Vector3(-1.9f + i * 0.54f, 2.85f, 0f), new Vector3(0.55f, 0.14f, 2.4f), i % 2 == 0 ? cream : orange)
                    .transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "SignPanel", kiosk, new Vector3(0f, 2.30f, -0.62f), new Vector3(3.7f, 0.55f, 0.12f), teal);
            var sign = WorldText(kiosk, "RentalSign", new Vector3(0f, 2.30f, -0.70f), "MILO'S BOAT", 64, 0.042f, cream.color);
            sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var notice = WorldText(kiosk, "RentalNotice", new Vector3(0f, 0.70f, -0.65f), "ONE BOAT. ONE CREW.", 48, 0.024f, cream.color);
            notice.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            operatorNpc = PlaceNpc(npcPrefab, story, "Milo", OnGround(new Vector3(34.4f, 0f, 8.5f)), 180f);
            keySpot = Point(story, "RentalKeyRecovery", OnGround(new Vector3(31f, 0f, 9f)) + Vector3.up * 0.7f, 0f);

            BuildFuelPump(dock, new Vector3(-10.5f, 0.36f, -11f));
            fleet = new Vehicle[1];
            berths = new Transform[1];
            Vector3 at = OnWater(new Vector3(32f, 0f, -21f));
            berths[0] = Point(story, "RentalBerth1", at, 180f);
            fleet[0] = BuildMiloBoat(env, at, 180f);
            fleet[0].name = "MilosBoat";
            var mooring = fleet[0].gameObject.AddComponent<RentalMooring>();
            SetRef(mooring, "_vehicle", fleet[0]);
            SetRef(mooring, "_berth", berths[0]);
        }
    }
}
