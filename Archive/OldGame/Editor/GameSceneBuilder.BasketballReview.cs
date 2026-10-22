using System;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        /// <summary>Updates just the hoop's painted court, preserving hotel, island three and all other scene edits.</summary>
        [MenuItem("PLEASE DON'T DROWN/Finish basketball court only")]
        public static void RefreshBasketballCourt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before updating the court.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open the Game scene before updating the court.");
            var hoops = Object.FindObjectsByType<BasketballHoop>(FindObjectsSortMode.None);
            if (hoops.Length != 1) throw new InvalidOperationException($"Expected one basketball hoop, found {hoops.Length}.");
            Transform hoop = hoops[0].transform, parent = hoop.parent;
            if (parent == null) throw new InvalidOperationException("Basketball hoop has no fun-area parent.");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, $"Logs/Game-before-court-finish-{DateTime.Now:yyyyMMdd-HHmmss}.unity", true);
            // Replace only the sibling paint mesh. The hoop's collider, rack and network scene identity survive.
            foreach (Transform old in parent.Cast<Transform>().Where(t => t.name == "Court").ToArray())
                Object.DestroyImmediate(old.gameObject);
            int rocks = ClearBasketballCourtRocks(hoop);
            BuildBasketballCourt(hoop, parent);
            string report = CheckBasketballCourt(hoop, parent.Find("Court"));
            if (rocks > 0) BakeNavMeshes(only: "Island1");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureViews("Screenshots/Review/BasketballFinish", new[] {
                ("court_top", hoop.position + Vector3.up * 21f + hoop.forward * 3f, hoop.position + hoop.forward * 3f),
                ("court_angle", hoop.position + Vector3.up * 9f - hoop.forward * 11f, hoop.position + hoop.forward * 3f),
                ("court_low", hoop.position + Vector3.up * 3f + hoop.right * 6f - hoop.forward * 6f, hoop.position + hoop.forward * 3f) });
            CaptureBasketballPushViews(hoop);
            File.WriteAllText("Logs/basketball-finish-result.txt", $"PASS: selective court update; removed {rocks} court-blocking decorative rocks.\n{report}\n");
            Debug.Log("[BasketballFinish] " + report);
        }

        private static void CaptureBasketballPushViews(Transform hoop)
        {
            var actor = new GameObject("Temporary basketball dribble review");
            GameObject ball = null;
            float? oldTime = AvatarAnimator.TimeOverride;
            try
            {
                Vector3 position = Ground(hoop.TransformPoint(new Vector3(3.8f, 0f, 3.5f)));
                Quaternion view = Quaternion.Euler(0f, 145f, 0f);
                actor.transform.SetPositionAndRotation(position, view);
                AvatarRig.SharedMaterial = AvatarMaterial();
                var rig = actor.AddComponent<AvatarRig>();
                rig.Build(AvatarLook.Lifeguard);
                var animator = actor.AddComponent<AvatarAnimator>();
                animator.Rig = rig;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Basketball.prefab");
                ball = Object.Instantiate(prefab);
                Item item = ball.GetComponent<Item>();
                if (ball.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
                Vector3 held = position + Vector3.up * 1.65f + view * Vector3.Scale(item.HoldOffset, new Vector3(.9f, .85f, .66f));
                ball.transform.SetPositionAndRotation(held, view * item.HoldRotation);
                Transform visual = ball.transform.Find("Model_basketball");
                Vector3 restVisual = visual != null ? visual.localPosition : Vector3.zero;
                Vector3 ahead = view * Vector3.forward, side = view * Vector3.right;
                Transform rest = item.GripRight;
                HandGrip original = new(rest.position, rest.forward, -rest.up, item.GripPose);
                foreach ((string name, float phase) in new[] { ("right_hand_catch", 0f), ("right_hand_push", .5f) })
                {
                    float tri = 1f - Mathf.Abs(2f * Mathf.Repeat(phase, 1f) - 1f);
                    float drop = tri * tri;
                    if (visual != null) visual.position = ball.transform.TransformPoint(restVisual) +
                        Vector3.down * ((held.y - position.y - .12f) * drop) + side * (.14f * drop) + ahead * (.12f * drop);
                    var motion = new AvatarMotion { FacingYaw = 145f, Grounded = true, Velocity = ahead * 3f,
                        Holding = true, TwoHanded = false, GripLeft = default,
                        GripRight = BasketballDribble.RightPushGrip(held, original, ahead, side, 1f, phase) };
                    animator.Motion = motion;
                    for (int i = 0; i < 60; i++)
                    {
                        AvatarAnimator.TimeOverride = 100f + i / 30f;
                        animator.Tick(1f / 30f);
                    }
                    if (animator.Motion.GripLeft.Active || animator.Motion.TwoHanded)
                        throw new InvalidOperationException("Dribble review unexpectedly uses the left hand.");
                    CaptureViews("Screenshots/Review/BasketballFinish", new[] {
                        (name, position + ahead * 3f + side * 2.1f + Vector3.up * 1.85f, position + Vector3.up * 1f) });
                }
            }
            finally
            {
                AvatarAnimator.TimeOverride = oldTime;
                if (ball != null) Object.DestroyImmediate(ball);
                Object.DestroyImmediate(actor);
            }
        }

        // Only the non-networked decorative Rock objects inside this shooting area are removed. No story props.
        private static int ClearBasketballCourtRocks(Transform hoop)
        {
            int removed = 0;
            foreach (Transform rock in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Rock").ToArray())
            {
                if (rock.GetComponentInChildren<NetworkObject>() != null || rock.GetComponentsInChildren<Collider>().Length == 0) continue;
                var renderers = rock.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                Vector3 centre = hoop.InverseTransformPoint(bounds.center);
                float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
                // Includes the white arc and room to approach/shoot; excludes rocks behind the basket.
                if (centre.z + radius < -1.3f || centre.z - radius > 8f || Mathf.Abs(centre.x) - radius > 7.3f) continue;
                Debug.Log($"[BasketballFinish] Removed decorative rock blocking the court at {rock.position:F2}.");
                Object.DestroyImmediate(rock.gameObject);
                removed++;
            }
            Physics.SyncTransforms();
            return removed;
        }

        private static string CheckBasketballCourt(Transform hoop, Transform court)
        {
            if (court == null) throw new InvalidOperationException("Court missing.");
            var terrain = GameObject.Find("Environment/BeachTerrain")?.GetComponent<MeshCollider>();
            if (terrain == null) throw new InvalidOperationException("Beach terrain collider missing.");
            int triangles = 0, vertices = 0;
            foreach (string name in new[] { "CourtKey", "CourtLines" })
            {
                Transform paint = court.Find(name);
                Mesh mesh = paint != null ? paint.GetComponent<MeshFilter>()?.sharedMesh : null;
                if (mesh == null) throw new InvalidOperationException($"{name} mesh missing.");
                if (paint.GetComponent<Collider>() != null) throw new InvalidOperationException("Paint should not collide with players or the ball.");
                Vector3[] world = mesh.vertices.Select(paint.TransformPoint).ToArray();
                int[] indices = mesh.triangles;
                float expectedLift = name == "CourtKey" ? .025f : .035f;
                foreach (Vector3 point in world)
                {
                    if (!terrain.Raycast(new Ray(new Vector3(point.x, 100f, point.z), Vector3.down), out RaycastHit hit, 200f))
                        throw new InvalidOperationException($"{name} vertex has no beach underneath.");
                    float lift = point.y - hit.point.y;
                    if (Mathf.Abs(lift - expectedLift) > .002f)
                        throw new InvalidOperationException($"{name} paint does not follow the ground: lift {lift:F4} at {point:F2}.");
                }
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 normal = Vector3.Cross(world[indices[i + 1]] - world[indices[i]], world[indices[i + 2]] - world[indices[i]]);
                    if (normal.y <= 0f) throw new InvalidOperationException($"{name} triangle {i / 3} faces down or is degenerate.");
                }
                vertices += world.Length; triangles += indices.Length / 3;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Basketball.prefab");
            Item item = prefab != null ? prefab.GetComponent<Item>() : null;
            if (item == null || item.Grip != ItemGrip.OneHand || item.GripRight == null || item.GripLeft != null)
                throw new InvalidOperationException("Basketball must have only a right-hand grip.");
            if (prefab.GetComponent<BasketballDribble>() == null) throw new InvalidOperationException("Basketball dribble component missing.");
            // Check the shared right-hand push maths at the catch and floor-contact phases.
            HandGrip rest = new(Vector3.zero, Vector3.forward, Vector3.up, HandPose.BallGrip);
            HandGrip top = BasketballDribble.RightPushGrip(Vector3.zero, rest, Vector3.forward, Vector3.right, 1f, 0f);
            HandGrip bottom = BasketballDribble.RightPushGrip(Vector3.zero, rest, Vector3.forward, Vector3.right, 1f, .5f);
            if (!top.Active || !bottom.Active || Vector3.Dot(top.Palm, Vector3.down) < .99f ||
                Mathf.Abs(top.Point.y - bottom.Point.y - .28f) > .001f)
                throw new InvalidOperationException("Right-hand dribble push check failed.");
            return $"{vertices} paint vertices follow the terrain; {triangles} upward-facing triangles; basketball right hand only; 28 cm palm push verified.";
        }

        public static void RefreshBasketballCourtBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry point only.");
            try { EditorSceneManager.OpenScene(ScenePath); RefreshBasketballCourt(); }
            catch (Exception e) { File.WriteAllText("Logs/basketball-finish-result.txt", "FAIL: " + e); throw; }
        }
    }

    [InitializeOnLoad]
    internal static class BasketballFinishRequest
    {
        private const string Request = "Logs/basketball-finish-request.txt";
        static BasketballFinishRequest() => EditorApplication.update += Poll;
        private static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(Request);
            try { GameSceneBuilder.RefreshBasketballCourt(); }
            catch (Exception e)
            {
                File.WriteAllText("Logs/basketball-finish-result.txt", "FAIL: " + e);
                Debug.LogException(e);
            }
        }
    }
}
