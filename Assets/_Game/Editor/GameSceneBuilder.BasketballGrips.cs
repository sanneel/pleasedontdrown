using System;
using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        public static void ReviewBasketballGripsBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            // Remove only an earlier review clone accidentally saved by the build routine.
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Basketball(Clone)" && Vector2.Distance(new Vector2(root.transform.position.x, root.transform.position.z), new Vector2(-2f, 32.5f)) < .1f)
                {
                    Object.DestroyImmediate(root);
                    EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                }
            Directory.CreateDirectory("Logs");
            string folder = "Screenshots/Review/BasketballGrips";
            Directory.CreateDirectory(folder);
            var actor = new GameObject("Temporary basketball hand review");
            GameObject ball = null;
            float? oldTime = AvatarAnimator.TimeOverride;
            try
            {
                Vector3 at = Ground(new Vector3(-2f, 0f, 32f));
                actor.transform.position = at;
                AvatarRig.SharedMaterial = AvatarMaterial();
                var rig = actor.AddComponent<AvatarRig>(); rig.Build(AvatarLook.Lifeguard);
                var animator = actor.AddComponent<AvatarAnimator>(); animator.Rig = rig;
                ball = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Basketball.prefab"));
                ball.name = "Temporary basketball review ball";
                ball.GetComponent<Rigidbody>().isKinematic = true;
                Transform visual = ball.transform.Find("Model_basketball");
                Vector3 rest = visual.localPosition;
                float maxError = 0f;
                int failures = 0;
                Vector3 standingCentre = at, shootingCentre = at;
                Debug.Log($"[BasketballGrips] rig eye {rig.EyeHeight:F3}, scale {rig.Scale:F3}, upper {rig.UpperArmLength:F3}, forearm {rig.ForearmLength:F3}, R shoulder {rig[AvatarRig.Bone.UpperArmR].position - at}");
                foreach (var state in new[] {
                    ("standing", false, false, false, 0f), ("dribble-catch", true, false, false, 0f),
                    ("dribble-push", true, false, false, .12f), ("dribble-floor", true, false, false, .5f),
                    ("shooting", false, false, true, 0f), ("jump-rise", true, true, false, 0f),
                    ("jump-apex", true, true, false, 0f) })
                {
                    string name = state.Item1;
                    bool moving = state.Item2, airborne = state.Item3, shooting = state.Item4;
                    float phase = state.Item5;
                    Vector3 feet = at + Vector3.up * (airborne ? 1f : 0f);
                    actor.transform.position = feet;
                    Vector3 held = BasketballDribble.HoldCentre(rig, feet + Vector3.up * 1.65f, Quaternion.identity, moving, airborne, shooting);
                    ball.transform.SetPositionAndRotation(held, Quaternion.identity);
                    bool two = !moving || airborne || shooting;
                    float blend = two ? 0f : 1f;
                    Vector3 centre = BasketballDribble.BounceCentre(held, Vector3.forward, Vector3.right,
                        held.y - at.y - BasketballDribble.Radius, blend, phase);
                    visual.position = centre;
                    BasketballDribble.BallGrips(held, centre, Vector3.forward, Vector3.right, two, blend, out HandGrip left, out HandGrip right);
                    if (left.Active != two || !right.Active) throw new InvalidOperationException(name + " hand mode incorrect.");
                    animator.Motion = new AvatarMotion { FacingYaw = 0f, Grounded = !airborne,
                        Velocity = moving ? Vector3.forward * 3f : Vector3.zero, Holding = true, TwoHanded = two,
                        Basketball = true, GripLeft = left, GripRight = right, Charge = shooting ? .8f : 0f };
                    for (int i = 0; i < 75; i++)
                    {
                        held = BasketballDribble.HoldCentre(rig, feet + Vector3.up * 1.65f, Quaternion.identity, moving, airborne, shooting);
                        ball.transform.position = held;
                        centre = BasketballDribble.BounceCentre(held, Vector3.forward, Vector3.right,
                            held.y - at.y - BasketballDribble.Radius, blend, phase);
                        visual.position = centre;
                        BasketballDribble.BallGrips(held, centre, Vector3.forward, Vector3.right, two, blend, out left, out right);
                        var motion = animator.Motion; motion.GripLeft = left; motion.GripRight = right; animator.Motion = motion;
                        AvatarAnimator.TimeOverride = 100f + i / 30f; animator.Tick(1f / 30f);
                    }
                    if (name == "standing") standingCentre = held;
                    if (name == "shooting") shootingCentre = held;
                    foreach (bool rightHand in new[] { false, true })
                    {
                        HandGrip g = rightHand ? right : left;
                        if (!g.Active) continue;
                        HandBones hand = rig.Hand(rightHand);
                        Vector3 actual = hand.Hand.TransformPoint(hand.PalmContact);
                        float error = Vector3.Distance(actual, g.Point);
                        maxError = Mathf.Max(error, maxError);
                        Debug.Log($"[BasketballGrips] {name} {(rightHand ? "right" : "left")} palm error {error:F4} m");
                        if (error > .035f) failures++;
                    }
                    CaptureBasketballSkin(rig.Renderer, folder, new[] {
                        (name + "-front", feet + new Vector3(.9f, 1.8f, 2.8f), feet + Vector3.up * 1.3f),
                        (name + "-side", feet + new Vector3(2.4f, 1.8f, .9f), feet + new Vector3(0f, 1.3f, .2f)),
                        (name + "-hands", held + new Vector3(.65f, .38f, .85f), held) });
                }
                Object.DestroyImmediate(actor);
                actor = null;
                var camera = new GameObject("Temporary first-person review").AddComponent<Camera>();
                var arms = camera.gameObject.AddComponent<FirstPersonArms>();
                try
                {
                    camera.transform.position = at + Vector3.up * 1.65f;
                    arms.Init(null, camera); arms.Build(AvatarLook.Lifeguard);
                    foreach (var fp in new[] { ("hold", false, false, 0f), ("shot", false, true, 0f),
                        ("dribble-catch", true, false, 0f), ("dribble-floor", true, false, .5f) })
                    {
                        bool moving = fp.Item2, shooting = fp.Item3;
                        Vector3 held = camera.transform.position + BasketballDribble.HoldOffset(moving, false, shooting);
                        ball.transform.position = held; visual.localPosition = rest;
                        Vector3 centre = moving ? BasketballDribble.VisibleDribbleCentre(camera,
                            BasketballDribble.BounceCentre(held, Vector3.forward, Vector3.right,
                                held.y - at.y - BasketballDribble.Radius, 1f, fp.Item4)) : held;
                        visual.position = centre;
                        Vector3 viewport = camera.WorldToViewportPoint(centre);
                        if (viewport.y < .1f || viewport.y > .9f || viewport.x < .1f || viewport.x > .9f)
                            throw new InvalidOperationException("First-person ball outside the view: " + fp.Item1);
                        BasketballDribble.BallGrips(held, centre, Vector3.forward, Vector3.right, !moving, moving ? 1f : 0f,
                            out HandGrip left, out HandGrip right);
                        arms.PlaceForReview(false, moving ? null : left); arms.PlaceForReview(true, right);
                        CaptureBasketballSkin(arms.GetComponentInChildren<SkinnedMeshRenderer>(), folder, new[] { ("first-person-" + fp.Item1, camera.transform.position,
                            camera.transform.position + Vector3.forward * 5f) });
                    }
                }
                finally { Object.DestroyImmediate(camera.gameObject); }
                if (failures > 0) throw new InvalidOperationException($"{failures} hand contact checks failed; max error {maxError:F4} m. Review rendered poses.");
                File.WriteAllText("Logs/basketball-grips-result.txt", $"PASS: standing, right-hand dribble catch/push/floor, two-hand shooting/jump rise/apex; max palm error {maxError:F4} m.\n");
                Object.DestroyImmediate(ball); ball = null;
                EditorSceneManager.OpenScene(ScenePath); // discard all review-only camera/material state before a build
                if (Array.Exists(Environment.GetCommandLineArgs(), a => a == "-pddBuild")) BuildResortPlayer();
            }
            catch (Exception e) { File.WriteAllText("Logs/basketball-grips-result.txt", "FAIL: " + e); throw; }
            finally
            {
                AvatarAnimator.TimeOverride = oldTime;
                if (actor != null) Object.DestroyImmediate(actor);
                if (ball != null) Object.DestroyImmediate(ball);
            }
        }

        // Multiple editor poses are captured in one frame. Bake each fresh skeleton so Unity's per-frame
        // skinning cache cannot accidentally show the first pose again alongside a moved ball.
        private static void CaptureBasketballSkin(SkinnedMeshRenderer skin, string folder,
            (string, Vector3, Vector3)[] views)
        {
            if (skin == null) throw new InvalidOperationException("Review skin missing.");
            var mesh = new Mesh(); skin.BakeMesh(mesh);
            var baked = new GameObject("Temporary baked basketball review skin");
            baked.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            baked.transform.localScale = skin.transform.lossyScale;
            baked.AddComponent<MeshFilter>().sharedMesh = mesh;
            baked.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            bool enabled = skin.enabled; skin.enabled = false;
            try { CaptureViews(folder, views); }
            finally { skin.enabled = enabled; Object.DestroyImmediate(baked); Object.DestroyImmediate(mesh); }
        }
    }
}
