using System;
using System.Collections.Generic;
using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// "Is every gun held right, as other players see it?" For each gun (and bat) in the item prefabs, a lifeguard
    /// looks down, ahead and up holding it exactly the way the game places it for remote players
    /// (<see cref="PlayerHands.ThirdPersonHold"/>), the arms reach for the grips, and it checks: both palms on their
    /// grips, the gun in front of the chest (not up by the head), the barrel along the look. A picture of each goes to
    /// Screenshots/Review/gunhold_*.jpg. Any failure fails the batch run (exit code 1).
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.GunHoldCheck.RunBatch -quit</c>
    /// </summary>
    public static class GunHoldCheck
    {
        private const float HandTolerance = 0.06f;
        private const float StockSinkTolerance = 0.01f;

        public static void RunBatch()
        {
            int failures;
            try { failures = Run(); }
            catch (Exception e)
            {
                Debug.LogError($"[GunHold] FAILED to run: {e}");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log(failures == 0 ? "[GunHold] ALL PASS" : $"[GunHold] {failures} FAILED");
            if (failures > 0) EditorApplication.Exit(1);
        }

        [MenuItem("PLEASE DON'T DROWN/Check how guns are held")]
        private static void FromMenu() => Debug.Log($"[GunHold] {Run()} failed");

        private static int Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Review");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            int failures = 0, k = 0;
            foreach (string path in Directory.GetFiles("Assets/_Game/Items/Prefabs", "*.prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                Item itemPrefab = prefab != null ? prefab.GetComponent<Item>() : null;
                if (itemPrefab == null || !itemPrefab.RigidInHand || itemPrefab.GripRight == null) continue;
                foreach (float pitch in new[] { -35f, 0f, 35f })
                    failures += Check(prefab, pitch, camera, k++) ? 0 : 1;
            }
            Debug.Log($"[GunHold] checked {k} holds");
            return failures;
        }

        private static bool Check(GameObject prefab, float pitch, Camera camera, int k)
        {
            // Out on the open beach, each in its own spot.
            var at = new Vector3(-30f + (k % 12) * 3f, 0f, 52f + (k / 12) * 3f);
            Physics.SyncTransforms();
            if (Physics.Raycast(at + Vector3.up * 60f, Vector3.down, out RaycastHit ground, 200f, ~0, QueryTriggerInteraction.Ignore)) at.y = ground.point.y;
            const float yaw = 180f;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0f);

            var body = new GameObject("GunHoldAvatar");
            body.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            var rig = body.AddComponent<AvatarRig>();
            rig.Build(AvatarLook.Lifeguard);
            var animator = body.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            var motion = new AvatarMotion { FacingYaw = yaw, LookPitch = pitch, Grounded = true };
            animator.Motion = motion;
            for (int i = 0; i < 10; i++) { AvatarAnimator.TimeOverride = 100f + i / 30f; animator.Tick(1f / 30f); }

            GameObject gunObject = Object.Instantiate(prefab);
            if (gunObject.TryGetComponent(out Rigidbody gunBody)) gunBody.isKinematic = true;
            var item = gunObject.GetComponent<Item>();
            Transform shoulder = rig[AvatarRig.Bone.UpperArmR];
            float reach = rig.UpperArmLength + rig.ForearmLength + rig.HandLength * 0.5f;
            Transform gr = item.GripRight, gl = item.GripLeft;
            motion.Holding = true;
            motion.TwoHanded = gl != null;
            motion.Shouldered = PlayerHands.Shoulders(item);
            // Placed again every frame from where the shoulders are now, the way the game does it.
            for (int i = 0; i < 60; i++)
            {
                PlayerHands.ThirdPersonHold(item, shoulder.position, rig[AvatarRig.Bone.UpperArmL].position, reach, look * Vector3.forward,
                    out Vector3 position, out Quaternion rotation, rig);
                gunObject.transform.SetPositionAndRotation(position, rotation);
                motion.GripRight = new HandGrip(gr.position, gr.forward, -gr.up, item.GripPose);
                if (gl != null) motion.GripLeft = new HandGrip(PlayerHands.ReachableLeftGrip(gl.position, gunObject.transform.forward,
                    rig[AvatarRig.Bone.UpperArmL].position, reach), gl.forward, -gl.up, item.GripPoseLeft);
                animator.Motion = motion;
                AvatarAnimator.TimeOverride = 101f + i / 30f;
                animator.Tick(1f / 30f);
            }
            AvatarAnimator.TimeOverride = null;

            // The checks.
            var problems = new List<string>();
            float Miss(bool right, Vector3 grip)
            {
                HandBones hand = rig.Hand(right);
                return hand == null ? 0f : Vector3.Distance(hand.Hand.TransformPoint(hand.PalmContact), grip);
            }
            float missR = Miss(true, gr.position), missL = gl != null ? Miss(false, motion.GripLeft.Point) : 0f;
            if (missR > HandTolerance) problems.Add($"right palm {missR * 100f:F0} cm off its grip");
            if (missL > HandTolerance) problems.Add($"left palm {missL * 100f:F0} cm off its grip");
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer r in gunObject.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            Transform head = rig[AvatarRig.Bone.Head], chest = rig[AvatarRig.Bone.Chest];
            Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // The bug this guards against: a gun up against the face (pointing at the sky beside the head).
            Vector3 face = head.position + head.up * (0.12f * rig.Scale);
            float toFace = DistanceToGun(gunObject.transform, face);
            // (A shouldered gun is meant to be up under the cheek, scope in front of the eye: only touching is wrong.)
            if (toFace < (motion.Shouldered ? 0.03f : 0.06f)) problems.Add($"gun against the face ({toFace * 100f:F0} cm)");
            if (bounds.center.y > face.y + 0.1f) problems.Add("gun held above the head");
            if (Vector3.Dot(bounds.center - chest.position, flat) < 0.08f) problems.Add("gun not in front of the body");
            // And the one where a shouldered stock went in through the right shoulder and out the back.
            Vector3 butt = gunObject.transform.position - gunObject.transform.forward * PlayerHands.StockBack(item);
            // Measured against the body itself: how deep the back of the gun (the stock) goes into the skin.
            float sunk = gl != null ? StockDepthInBody(gunObject, butt, rig) : 0f;
            if (sunk > StockSinkTolerance) problems.Add($"stock {sunk * 100f:F0} cm into the body");
            bool gun = gunObject.TryGetComponent(out Combat.Weapon _);
            float limit = gl != null ? PlayerHands.LongGunMaxPitch : PlayerHands.ThirdPersonMaxPitch;
            Vector3 aimed = Quaternion.Euler(Mathf.Clamp(pitch, -limit, limit), yaw, 0f) * Vector3.forward;
            if (gun && Vector3.Dot(gunObject.transform.forward, aimed) < 0.9f) problems.Add("barrel not along the look");

            string name = $"gunhold_{prefab.name}_{(pitch < 0f ? "up" : pitch > 0f ? "down" : "level")}";
            // Three-quarter view from the front right, the gun's side.
            Vector3 eye = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(1.5f, 1.55f, 2.1f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at + Vector3.up * 1.2f - eye));
            camera.fieldOfView = 40f;
            Render(camera, name);
            // And straight from the right side, where a stock going into the shoulder shows.
            eye = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(2.4f, 1.3f, 0.25f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at + Vector3.up * 1.2f + flat * 0.25f - eye));
            Render(camera, name + "_side");
            Object.DestroyImmediate(gunObject);
            Object.DestroyImmediate(body);

            if (problems.Count == 0)
            {
                Debug.Log($"[GunHold] PASS {prefab.name} looking {(pitch < 0f ? "up" : pitch > 0f ? "down" : "level")}: palms {missR * 100f:F1} / {missL * 100f:F1} cm, stock {sunk * 100f:F1} cm into the body");
                return true;
            }
            Debug.LogError($"[GunHold] FAIL {prefab.name} looking {(pitch < 0f ? "up" : pitch > 0f ? "down" : "level")}: {string.Join("; ", problems)} ({name}.jpg)");
            return false;
        }

        /// <summary>
        /// The deepest any point of the back 10 cm of the gun goes under the body's skin (0 if none does): each gun
        /// vertex there against the nearest triangle of the posed body, inside when it's behind that triangle.
        /// </summary>
        private static float StockDepthInBody(GameObject gun, Vector3 butt, AvatarRig rig)
        {
            if (rig.Renderer == null) return 0f;
            var baked = new Mesh();
            rig.Renderer.BakeMesh(baked, true);
            Transform rt = rig.Renderer.transform;
            Matrix4x4 toWorld = Matrix4x4.TRS(rt.position, rt.rotation, Vector3.one);
            Vector3[] bodyVerts = baked.vertices;
            for (int i = 0; i < bodyVerts.Length; i++) bodyVerts[i] = toWorld.MultiplyPoint3x4(bodyVerts[i]);
            int[] tris = baked.triangles;
            Object.DestroyImmediate(baked);
            var near = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
                if ((bodyVerts[tris[i]] - butt).sqrMagnitude < 0.6f * 0.6f) near.Add(i);

            Vector3 along = gun.transform.forward;
            float deepest = 0f;
            foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out Renderer shown) || !shown.enabled) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 p = filter.transform.TransformPoint(v);
                    if (Vector3.Dot(p - butt, along) > 0.1f) continue;
                    if (Near(p, rig.Hand(true)) || Near(p, rig.Hand(false))) continue; // (the bits of gun in the hands)
                    float best = float.MaxValue;
                    bool inside = false;
                    foreach (int i in near)
                    {
                        Vector3 a = bodyVerts[tris[i]], b = bodyVerts[tris[i + 1]], c = bodyVerts[tris[i + 2]];
                        Vector3 q = ClosestOnTriangle(p, a, b, c);
                        float d = (p - q).sqrMagnitude;
                        if (d >= best) continue;
                        best = d;
                        inside = Vector3.Dot(p - q, Vector3.Cross(b - a, c - a)) < 0f;
                    }
                    if (inside) deepest = Mathf.Max(deepest, Mathf.Sqrt(best));
                }
            }
            return deepest;
        }

        private static bool Near(Vector3 p, HandBones hand) => hand != null && (hand.Hand.TransformPoint(hand.PalmContact) - p).sqrMagnitude < 0.08f * 0.08f;

        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>How close a point comes to the gun's own (oriented) parts: each mesh's box in its own frame.</summary>
        private static float DistanceToGun(Transform gun, Vector3 point)
        {
            float best = float.MaxValue;
            foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds b = filter.sharedMesh.bounds;
                Transform t = filter.transform;
                Vector3 local = t.InverseTransformPoint(point);
                Vector3 closest = new(Mathf.Clamp(local.x, b.min.x, b.max.x), Mathf.Clamp(local.y, b.min.y, b.max.y), Mathf.Clamp(local.z, b.min.z, b.max.z));
                best = Mathf.Min(best, Vector3.Distance(t.TransformPoint(closest), point));
            }
            return best;
        }

        private static void Render(Camera camera, string name)
        {
            const int w = 800, h = 800;
            var rt = new RenderTexture(w, h, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            texture.Apply();
            File.WriteAllBytes($"Screenshots/Review/{name}.jpg", texture.EncodeToJPG(85));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
