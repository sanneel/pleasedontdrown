using System;
using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Rescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// "Do the lips meet in a rescue breath?" A tourist (the real victim prefab, a range of bodies) lies on the sand,
    /// a lifeguard (every head size) kneels at her chest as in CPR and gives the breath; the check measures how far
    /// apart the two mouths end up (the mouth bones, which sit on the lips). A picture of each goes to
    /// Screenshots/Review/kiss_*.jpg; any gap over 3 cm fails the batch run.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.KissCheck.RunBatch -quit</c>
    /// </summary>
    public static class KissCheck
    {
        private const float Tolerance = 0.03f;

        public static void RunBatch()
        {
            int failures;
            try { failures = Run(); }
            catch (Exception e)
            {
                Debug.LogError($"[KissCheck] FAILED to run: {e}");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log(failures == 0 ? "[KissCheck] ALL PASS" : $"[KissCheck] {failures} FAILED");
            if (failures > 0) EditorApplication.Exit(1);
        }

        [MenuItem("PLEASE DON'T DROWN/Check rescue-breath kisses")]
        private static void FromMenu() => Debug.Log($"[KissCheck] {Run()} failed");

        private static int Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Review");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            var victimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab");
            if (victimPrefab == null) throw new InvalidOperationException("no Tourist prefab");
            int failures = 0, k = 0;
            for (int tourist = 0; tourist < 8; tourist++)
                for (byte head = 0; head < 4; head += 3) // the smallest and the biggest goofy head
                    failures += Check(victimPrefab, tourist, head, camera, k++) ? 0 : 1;
            Debug.Log($"[KissCheck] checked {k} kisses");
            return failures;
        }

        private static bool Check(GameObject victimPrefab, int tourist, byte head, Camera camera, int k)
        {
            var spot = new Vector3(-40f + (k % 8) * 4f, 0f, 300f + (k / 8) * 4f); // open air, away from everything

            // The tourist, flat on her back, head toward +x (a victim's chest faces its forward, its head its up).
            GameObject victimObject = Object.Instantiate(victimPrefab);
            foreach (Rigidbody rb in victimObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            var victim = victimObject.GetComponent<VictimBody>();
            victimObject.transform.SetPositionAndRotation(spot + Vector3.up * 0.14f, Quaternion.LookRotation(Vector3.up, Vector3.right));
            victim.ApplyLooks(AvatarLook.RandomTourist(tourist), tourist);
            Vector3 chest = victim.ChestPoint, mouth = victim.MouthPoint;

            // The lifeguard kneeling at the chest, facing it.
            var body = new GameObject("KissCheckLifeguard");
            Vector3 feet = new Vector3(chest.x, spot.y, chest.z - 0.62f);
            body.transform.SetPositionAndRotation(feet, Quaternion.identity);
            AvatarLook look = AvatarLook.Lifeguard;
            look.HeadSize = head;
            var rig = body.AddComponent<AvatarRig>();
            rig.Build(look);
            var animator = body.AddComponent<AvatarAnimator>();
            animator.Rig = rig;
            animator.Motion = new AvatarMotion { FacingYaw = 0f, Grounded = true, Cpr = true, CprPoint = chest };
            for (int i = 0; i < 60; i++) { AvatarAnimator.TimeOverride = 100f + i / 30f; animator.Tick(1f / 30f); }
            animator.Play(AvatarGesture.Breath, mouth);
            // The lips down on theirs: the middle of the breath.
            for (int i = 0; i <= 18; i++) { AvatarAnimator.TimeOverride = 102f + i / 30f; animator.Tick(1f / 30f); }
            float gap = Vector3.Distance(animator.Lips, mouth);
            AvatarAnimator.TimeOverride = null;

            string name = $"kiss_t{tourist}_h{head}";
            Vector3 eye = Vector3.Lerp(mouth, animator.Lips, 0.5f) + new Vector3(0.85f, 0.3f, -0.15f); // from beyond her head
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(Vector3.Lerp(mouth, animator.Lips, 0.5f) - eye));
            camera.fieldOfView = 35f;
            Render(camera, name);
            Object.DestroyImmediate(victimObject);
            Object.DestroyImmediate(body);

            if (gap <= Tolerance)
            {
                Debug.Log($"[KissCheck] PASS tourist {tourist}, head {head}: lips {gap * 100f:F1} cm apart");
                return true;
            }
            Debug.LogError($"[KissCheck] FAIL tourist {tourist}, head {head}: lips {gap * 100f:F1} cm apart ({name}.jpg)");
            return false;
        }

        private static void Render(Camera camera, string name)
        {
            const int w = 800, h = 600;
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
