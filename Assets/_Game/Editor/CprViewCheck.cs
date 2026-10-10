using System;
using System.Globalization;
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
    /// What the rescuer sees doing chest compressions: the real victim prefab with a given body lying on its back,
    /// eyes shut, our first-person hands on the chest point the game uses (stacked, palms down, as
    /// FirstPersonArms.Cpr places them), seen from the kneeling eye PlayerLook.KneelAt puts the camera at, plus a
    /// close look at the face. Pictures go to Screenshots/Review/cpr_&lt;body&gt;_*.jpg.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.CprViewCheck.RunBatch -cprbodies 41,3,48 -quit</c>
    /// </summary>
    public static class CprViewCheck
    {
        public static void RunBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-cprbodies");
                string list = at >= 0 && at + 1 < args.Length ? args[at + 1] : "41";
                Run(Array.ConvertAll(list.Split(','), s => byte.Parse(s, CultureInfo.InvariantCulture)));
                Debug.Log("[CprView] done");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CprView] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Run(byte[] bodies)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Review");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.nearClipPlane = 0.02f; // the face close-up is ~30 cm from the face
            AvatarRig.SharedMaterial = GameSceneBuilder.AvatarMaterial();
            var victimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Items/Prefabs/Tourist.prefab");
            int k = 0;
            foreach (byte id in bodies)
            {
                var spot = new Vector3(-40f + k * 5f, 0f, 320f);
                k++;
                GameObject victimObject = Object.Instantiate(victimPrefab);
                foreach (Rigidbody rb in victimObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                var victim = victimObject.GetComponent<VictimBody>();
                // Flat on her back, head toward +x (as KissCheck lays her).
                victimObject.transform.SetPositionAndRotation(spot + Vector3.up * 0.14f, Quaternion.LookRotation(Vector3.up, Vector3.right));
                var look = new AvatarLook { Body = id, Figure = (byte)(AvatarLook.Bodies.IsFeminine(id) ? 1 : 0) };
                victim.ApplyLooks(look, id);
                AvatarRig rig = victimObject.GetComponentInChildren<AvatarRig>();
                rig.SetExpression(0f, 0f); // out cold: eyes shut
                Vector3 chest = victim.ChestPoint, head = victim.HeadPosition;
                float reach = victim.KneelReach;
                Debug.Log($"[CprView] body {id}: chest {chest - spot}, head {head - spot}, reach {reach:F2}");

                // The kneeling eye (PlayerLook.KneelAt): beside the chest on our side (-z), 72 cm up, looking at it.
                Vector3 along = Vector3.ProjectOnPlane(head - chest, Vector3.up).normalized;
                Vector3 side = Vector3.back;
                Vector3 eye = VictimBody.KneelSpot(chest, head) + side * (reach + 0.05f) + Vector3.up * 0.72f;
                Vector3 lookAt = chest - along * 0.04f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(lookAt - eye, Vector3.up));
                camera.fieldOfView = 74f;

                // Our first-person hands, stacked on the chest point as FirstPersonArms places them for CPR.
                var before = new System.Collections.Generic.HashSet<Renderer>(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None));
                var holder = new GameObject("CprReviewHands");
                var eyeCam = new GameObject("CprReviewEye").AddComponent<Camera>();
                eyeCam.enabled = false;
                eyeCam.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                var arms = holder.AddComponent<Player.FirstPersonArms>();
                arms.Init(null, eyeCam);
                arms.Build(AvatarLook.Lifeguard);
                Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                arms.PlaceForReview(true, new HandGrip(chest + Vector3.up * 0.035f + right * 0.01f, forward, Vector3.down, HandPose.Flat));
                arms.PlaceForReview(false, new HandGrip(chest - right * 0.01f, forward, Vector3.down, HandPose.Flat));

                // (The arms build their own root, not under the holder: they are whatever renderers are new.)
                var hands = new System.Collections.Generic.List<Renderer>();
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (!before.Contains(r) && !r.transform.IsChildOf(victimObject.transform)) hands.Add(r);
                Render(camera, $"cpr_{id}_view");
                // Without the hands: what the chest looks like there.
                foreach (Renderer r in hands) r.enabled = false;
                Render(camera, $"cpr_{id}_nohands");
                foreach (Renderer r in hands) r.enabled = true;
                // Straight down on the chest: where the hands are on her.
                camera.fieldOfView = 40f;
                camera.transform.SetPositionAndRotation(chest + Vector3.up * 0.9f, Quaternion.LookRotation(Vector3.down, along));
                Render(camera, $"cpr_{id}_above");
                // The face up close, from our kneeling side and from straight above.
                Vector3 faceAt = head + Vector3.up * 0.06f;
                camera.fieldOfView = 40f;
                foreach ((string view, Vector3 offset) in new[] { ("face_side", new Vector3(-0.1f, 0.42f, -0.32f)), ("face_top", new Vector3(-0.02f, 0.55f, -0.01f)),
                             ("face_close", new Vector3(0.05f, 0.28f, -0.18f)), ("body", new Vector3(-0.9f, 1.3f, -1.1f)) })
                {
                    Vector3 from = (view == "body" ? chest : faceAt) + offset;
                    camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation((view == "body" ? chest : faceAt) - from, along));
                    Render(camera, $"cpr_{id}_{view}");
                }
                foreach (Renderer r in hands) if (r != null) Object.DestroyImmediate(r.gameObject);
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(eyeCam.gameObject);
                Object.DestroyImmediate(victimObject);
            }
        }

        private static void Render(Camera camera, string name)
        {
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            texture.Apply();
            File.WriteAllBytes($"Screenshots/Review/{name}.jpg", texture.EncodeToJPG(88));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
