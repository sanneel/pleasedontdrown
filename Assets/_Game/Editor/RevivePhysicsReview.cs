using System;
using System.Reflection;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    // Runs the actual body controller and joints with PhysX, without requiring a multiplayer session.
    public static class RevivePhysicsReview
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Call(VictimBody body, string method, params object[] args) =>
            typeof(VictimBody).GetMethod(method, Private).Invoke(body, args);
        private static void Set(VictimBody body, string field, object value) =>
            typeof(VictimBody).GetField(field, Private).SetValue(body, value);

        public static void RunBatch()
        {
            var previous = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                foreach (int seed in new[] { 11, 24, 37, 56 })
                    foreach (float roll in new[] { 90f, 180f }) Review(seed, roll);
                ReviewPartnerCollision();
                Debug.Log("[ReviveReview] PASS: eight actual-joint get-up simulations.");
            }
            finally { Physics.simulationMode = previous; }
        }

        private static void ReviewPartnerCollision()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var npc = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Story/Prefabs/StoryNpc.prefab"));
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Player/Prefabs/Player.prefab"), new Vector3(0, 0, 1), Quaternion.identity);
            var actor = npc.GetComponent<StoryNpc>();
            typeof(StoryNpc).GetMethod("Awake", Private | BindingFlags.Public).Invoke(actor, null);
            var probe = typeof(StoryNpc).GetMethod("ObstacleAhead", Private);
            Physics.SyncTransforms();
            bool Blocked() => (bool)probe.Invoke(actor, new object[] { Vector3.zero, Vector3.forward, 2f, null });
            if (!Blocked()) throw new InvalidOperationException("Collision test did not detect the real player collider.");
            typeof(StoryNpc).GetField("_walksWith", Private).SetValue(actor, player.GetComponent<Rigidbody>());
            if (Blocked()) throw new InvalidOperationException("The escort still counts her partner as a wall.");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0, 1, .75f); wall.transform.localScale = new Vector3(1, 2, .2f);
            Physics.SyncTransforms();
            if (!Blocked()) throw new InvalidOperationException("The escort incorrectly ignores solid walls.");
            Debug.Log("[ReviveReview] PASS: real player collider blocks normally, is ignored as the escort partner, and solid walls still block.");
        }

        private static void Review(int seed, float roll)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0, -.25f, 0);
            ground.transform.localScale = new Vector3(40, .5f, 40);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab"),
                new Vector3(0, .55f, 0), Quaternion.Euler(roll, 0, 0));
            var body = root.GetComponent<VictimBody>();
            Call(body, "Awake"); Call(body, "Start");
            body.ApplyLooks(AvatarLook.RandomTourist(seed), seed);
            var rb = root.GetComponent<Rigidbody>(); rb.isKinematic = false;
            foreach (var limb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) limb.isKinematic = false;
            Set(body, "_groundY", 0f); Set(body, "_wadeDirection", Vector3.forward); Set(body, "_walkingAway", true);
            Physics.SyncTransforms();
            float uprightAt = -1, maxEarlySpeed = 0;
            for (int i = 0; i < 600; i++)
            {
                Call(body, "PoseLimbs", VictimState.Saved);
                Call(body, "StandAndWalk", true);
                Physics.Simulate(.02f);
                float up = (rb.rotation * Vector3.up).y;
                if (up > .98f && uprightAt < 0) uprightAt = (i + 1) * .02f;
                if (up < .9f) maxEarlySpeed = Mathf.Max(maxEarlySpeed, new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude);
            }
            float finalUp = (rb.rotation * Vector3.up).y;
            float speed = new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude;
            Call(body, "LateUpdate");
            var rig = root.GetComponentInChildren<AvatarRig>();
            float ankle = Mathf.Min(rig[AvatarRig.Bone.FootL].position.y, rig[AvatarRig.Bone.FootR].position.y);
            Debug.Log($"[ReviveReview] seed {seed} start {roll}: upright {uprightAt:F2}s, final up {finalUp:F3}, chest {rb.position.y:F3}, ankles {ankle:F3}, walking {speed:F2}m/s, early collision drift {maxEarlySpeed:F2}m/s");
            if (uprightAt < 0 || uprightAt > 5 || finalUp < .97f || speed < .8f || rb.position.y > 1.4f || rb.position.y < 1.1f)
                throw new InvalidOperationException("Revive controller failed to settle and walk: seed " + seed);
            // Editor callbacks use Destroy for detached limbs; clean the review scene explicitly.
            body.enabled = false;
            foreach (var limb in Object.FindObjectsByType<ConfigurableJoint>(FindObjectsSortMode.None))
                Object.DestroyImmediate(limb.gameObject);
            Object.DestroyImmediate(root);
        }
    }
}
