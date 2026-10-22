using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Part A's scene additions on the hotel island: the drivable golf cart by the hotel bar. (The bar's tables, stools,
    /// menu and the blackjack table were cut with the old game, Docs/23-Scope-Change.md.)
    /// </summary>
    public static partial class GameSceneBuilder
    {
        [MenuItem("PLEASE DON'T DROWN/Part A: scene additions (golf cart)")]
        public static void BuildBarDining()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            Transform bar = FindByPath("Environment/Hotel/HotelBeachBar") ?? throw new InvalidOperationException("No hotel bar in the scene.");
            AddGolfCart(bar, bar.parent);
            AssignSceneIds(scene); // the cart is a networked scene object
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[BarDining] PASS: golf cart in place");
        }

        /// <summary>Batch: the same.</summary>
        public static void BuildBarDiningBatch() => BuildBarDining();

        /// <summary>
        /// The golf cart (A9): a land <see cref="Vehicle"/> by the hotel bar, the driver's bench seat and a passenger's
        /// beside it. A greybox body for now (the Tripo model is part C's): white chassis and roof, four wheels.
        /// </summary>
        /// <summary>
        /// How far ahead of the cart's middle the steering wheel is: within reach of the lifeguard's short cartoon arms
        /// from the seat (at 0.42 they leant 45 degrees and still fell 13 cm short; see PatchGolfCartReach).
        /// </summary>
        public const float CartWheelZ = 0.22f;

        private static void AddGolfCart(Transform bar, Transform hotel)
        {
            Transform old = hotel.Find("GolfCart");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            Vector3? spot = null;
            float spotYaw = 0f;
            // In a bay of the parking by the hotel at the end of Shako's cart road (B10), facing back up the road.
            if (GameObject.Find("CartParkingHotel") is { } bay)
            {
                spot = bay.transform.TransformPoint(new Vector3(1.7f, 0f, 0f));
                spotYaw = bay.transform.eulerAngles.y;
            }
            // (Without it: flat, free sand by the bar.)
            foreach (Vector2 p in spot != null ? new Vector2[0] : new[] { new Vector2(0f, -12f), new Vector2(6f, -12f), new Vector2(-6f, -12f), new Vector2(15f, -3f), new Vector2(-15f, -3f), new Vector2(0f, 12f) })
            {
                Vector3 at = bar.TransformPoint(new Vector3(p.x, 0f, p.y));
                if (!Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out RaycastHit ground, 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (ground.normal.y < 0.95f || ground.point.y < 0.6f) continue; // a slope, or at the water
                Vector3 centre = ground.point + Vector3.up * 0.9f;
                if (Physics.CheckBox(centre, new Vector3(1.0f, 0.6f, 1.6f), bar.rotation, ~0, QueryTriggerInteraction.Ignore)) continue; // something there
                spot = ground.point + Vector3.up * 0.05f;
                spotYaw = bar.eulerAngles.y + 90f;
                break;
            }
            if (spot == null) throw new InvalidOperationException("No flat, free sand by the hotel bar for the golf cart.");

            Material white = GetMaterial("CartWhite", new Color(0.95f, 0.95f, 0.92f));
            Material cream = GetMaterial("CartSeat", new Color(0.85f, 0.75f, 0.55f));
            Material black = GetMaterial("CartTyre", new Color(0.06f, 0.06f, 0.07f));
            Material green = GetMaterial("CartGreen", new Color(0.15f, 0.45f, 0.3f));
            var root = new GameObject("GolfCart");
            root.transform.SetParent(hotel, false);
            root.transform.SetPositionAndRotation(spot.Value, Quaternion.Euler(0f, spotYaw, 0f));
            var body = root.AddComponent<Rigidbody>();
            body.mass = 300f;
            body.linearDamping = 0.2f;
            body.angularDamping = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            Transform r = root.transform;
            GameObject hull = Primitive(PrimitiveType.Cube, "Hull", r, new Vector3(0f, 0.55f, 0f), new Vector3(1.3f, 0.5f, 2.4f), white);
            // It slides on its hull (the wheels are drawn): no friction, or the sand holds it like a brick. Grip, steering
            // and rolling to a stop are Vehicle.DriveOnLand's.
            hull.GetComponent<Collider>().sharedMaterial = GetPhysicsMaterial("CartSlide", 0f, 0f, PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);
            Primitive(PrimitiveType.Cube, "Bench", r, new Vector3(0f, 0.86f, -0.2f), new Vector3(1.15f, 0.12f, 0.55f), cream, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Backrest", r, new Vector3(0f, 1.15f, -0.48f), new Vector3(1.15f, 0.5f, 0.1f), cream, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Dash", r, new Vector3(0f, 0.95f, 0.7f), new Vector3(1.25f, 0.35f, 0.25f), green, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Roof", r, new Vector3(0f, 2.0f, -0.05f), new Vector3(1.4f, 0.06f, 2.0f), white);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -0.55f, 0.8f })
                    Primitive(PrimitiveType.Cube, "Post", r, new Vector3(x, 1.45f, z), new Vector3(0.05f, 1.1f, 0.05f), white, keepCollider: false);
            foreach (float x in new[] { -0.62f, 0.62f })
                foreach (float z in new[] { -0.8f, 0.8f })
                    Primitive(PrimitiveType.Cylinder, "Wheel", r, new Vector3(x, 0.25f, z), new Vector3(0.5f, 0.1f, 0.5f), black, keepCollider: false)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Primitive(PrimitiveType.Cylinder, "SteeringWheel", r, new Vector3(-0.28f, 1.22f, CartWheelZ), new Vector3(0.36f, 0.02f, 0.36f), black, keepCollider: false)
                .transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);
            Transform P(string n, Vector3 p, Quaternion q)
            {
                var t = new GameObject(n).transform;
                t.SetParent(r, false);
                t.localPosition = p;
                t.localRotation = q;
                return t;
            }
            Transform seat = P("Seat", new Vector3(-0.28f, 0.92f, -0.22f), Quaternion.identity);
            Transform passenger = P("PassengerSeat", new Vector3(0.28f, 0.92f, -0.22f), Quaternion.identity);
            Transform gripL = P("GripLeft", new Vector3(-0.4f, 1.24f, CartWheelZ - 0.03f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform gripR = P("GripRight", new Vector3(-0.16f, 1.24f, CartWheelZ - 0.03f), Quaternion.LookRotation(Vector3.up, Vector3.back));
            Transform thrust = P("Thrust", new Vector3(0f, 0.25f, -1.0f), Quaternion.identity);
            Vehicle cart = FinishVehicle(root, "golf cart", "", seat, gripL, gripR, thrust, hull.GetComponent<Collider>(), 0.3f,
                thrustForce: 6f, maxSpeed: 8f, turnRate: 1.4f);
            SetRefs(cart, "_backSeats", passenger);
            SetBool(cart, "_land", true);
            SetField(cart, "_sideGrip", p => p.floatValue = 9f);
            SetField(cart, "_reverseThrust", p => p.floatValue = 3f);
            Debug.Log($"[BarDining] golf cart at {spot.Value:F1}");
        }

    }
}
