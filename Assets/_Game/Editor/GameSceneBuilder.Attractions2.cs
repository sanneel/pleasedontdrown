using System.Collections.Generic;
using System.IO;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// More of island 1's attractions: the banana boat the lifeguards' jet ski tows.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector3 BananaSpot = new(1.5f, 0f, -12.5f);

        private static void BuildMoreAttractions(Transform parent)
        {
            BuildBananaBoat(parent);
        }

        // ------------------------------------------------------------------ banana boat

        private static void BuildBananaBoat(Transform parent)
        {
            // The lifeguards' own jet ski (no keys), parked just in front of the banana's nose: sit on it and the
            // banana hitches on. (The robber's jet ski, with keys, is still the story's way off the island.)
            BuildJetSki(parent, OnWater(BananaSpot + new Vector3(0f, 0f, -6.3f)), 180f, key: "", displayName: "Lifeguard Jet Ski");

            Material yellow = GetMaterial("BananaYellow", new Color(1f, 0.85f, 0.2f));
            Material rope = GetMaterial("TowRope", new Color(0.85f, 0.78f, 0.55f));
            var root = new GameObject("BananaBoat");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(OnWater(BananaSpot), Quaternion.Euler(0f, 180f, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 120f;
            body.linearDamping = 0.3f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", root.transform, new Vector3(0f, 0.3f, 0f), new Vector3(1.3f, 0.6f, 4.4f), yellow);
            Transform P(string n, Vector3 p)
            {
                var tr = new GameObject(n).transform;
                tr.SetParent(root.transform, false);
                tr.localPosition = p;
                return tr;
            }
            Transform seat = P("Seat", new Vector3(0f, 0.72f, 1.2f));
            Transform gripL = P("GripLeft", new Vector3(-0.14f, 0.82f, 1.55f));
            Transform gripR = P("GripRight", new Vector3(0.14f, 0.82f, 1.55f));
            gripL.localRotation = gripR.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            Transform thrust = P("Thrust", new Vector3(0f, 0f, -2f));
            Transform nose = P("Nose", new Vector3(0f, 0.75f, 2.45f));
            GameObject model = PropModel("banana_boat", root.transform);
            if (model != null)
            {
                Object.DestroyImmediate(hull.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(hull.GetComponent<MeshFilter>());
            }
            Vehicle vehicle = FinishVehicle(root, "Banana Boat", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.15f,
                thrustForce: 0f, maxSpeed: 1f, turnRate: 0f); // (max speed must not be 0: Vehicle divides by it)
            Object engine = new SerializedObject(vehicle).FindProperty("_engineAudio").objectReferenceValue;
            SetRef(vehicle, "_engineAudio", null);
            if (engine != null) Object.DestroyImmediate(engine);

            var line = root.AddComponent<LineRenderer>();
            line.positionCount = 9;
            line.widthMultiplier = 0.035f;
            line.sharedMaterial = rope;
            line.useWorldSpace = true;
            line.enabled = false;
            var banana = root.AddComponent<BananaBoat>();
            SetRef(banana, "_vehicle", vehicle);
            SetRef(banana, "_nose", nose);
            SetRef(banana, "_rope", line);
        }
    }
}
