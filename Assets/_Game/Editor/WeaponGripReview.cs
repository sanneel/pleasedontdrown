using System;
using System.IO;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static class WeaponGripReview
    {
        public static void BaselineBatch()
        {
            CaptureHands();
            BuildSavedGameBatch();
        }

        // Build the saved level without regenerating it or overwriting hand-authored scene changes.
        public static void BuildSavedGameBatch()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Game/Scenes/Game.unity" },
                locationPathName = "Builds/Win64/PleaseDontDrown.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception($"Gun review build failed: {report.summary.totalErrors} errors");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[GripReview] Saved-scene player build succeeded.");
        }

        public static void CaptureHands()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode for hand review.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.6f, .64f, .7f);
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f;
            light.transform.rotation = Quaternion.Euler(35, -40, 0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .7f;
            fill.transform.rotation = Quaternion.Euler(10, 160, 0);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.13f,.18f,.23f);
            cam.nearClipPlane = .003f; cam.farClipPlane = 10;
            AvatarRig.SharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Data/Materials/Avatar.mat");
            string folder = Array.IndexOf(Environment.GetCommandLineArgs(), "-gripBefore") >= 0 ? "Before" : "After";
            folder = "Screenshots/GunGrips/" + folder;
            Directory.CreateDirectory(folder);
            foreach (string kind in new[] { "Pistol", "SMG", "Shotgun", "Rifle", "Sniper" })
            {
                var gun = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Items/Prefabs/{kind}.prefab"));
                gun.GetComponent<Rigidbody>().isKinematic = true;
                Item item = gun.GetComponent<Item>();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-gripCandidate") >= 0) WeaponGripAuthoring.Apply(gun.transform, kind);
                var holder = new GameObject("Hands");
                var eye = new GameObject("Eye").AddComponent<Camera>(); eye.enabled = false;
                var arms = holder.AddComponent<FirstPersonArms>();
                arms.Init(null, eye); arms.Build(AvatarLook.Lifeguard); arms.enabled = false;
                var skin = eye.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked = new Mesh();
                var staticHand = new GameObject("ReviewHandMesh");
                staticHand.transform.SetParent(skin.transform, false);
                staticHand.AddComponent<MeshFilter>().sharedMesh = baked;
                staticHand.AddComponent<MeshRenderer>().sharedMaterial = skin.sharedMaterial;
                skin.enabled = false;
                void Place()
                {
                    Transform r = item.GripRight, l = item.GripLeft;
                    arms.PlaceForReview(true, new HandGrip(r.position, r.forward, -r.up, item.GripPose));
                    arms.PlaceForReview(false, l == null ? null : new HandGrip(l.position, l.forward, -l.up, item.GripPoseLeft));
                    skin.BakeMesh(baked);
                }
                Place();
                cam.orthographic = true; cam.orthographicSize = .13f;
                Vector3 target = item.GripRight.position;
                cam.transform.position = target + new Vector3(.6f,.12f,-.18f); cam.transform.LookAt(target);
                Capture(cam, $"{folder}/{kind}_grip.png");
                cam.transform.position = target + new Vector3(-.6f,.09f,.12f); cam.transform.LookAt(target);
                Capture(cam, $"{folder}/{kind}_thumb.png");
                if (item.GripLeft != null)
                {
                    target = item.GripLeft.position;
                    cam.transform.position = target + new Vector3(-.55f,-.16f,-.16f); cam.transform.LookAt(target);
                    Capture(cam, $"{folder}/{kind}_support.png");
                }
                gun.transform.SetPositionAndRotation(item.HoldOffset, item.HoldRotation);
                Place();
                cam.orthographic = false; cam.fieldOfView = 74;
                cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Capture(cam, $"{folder}/{kind}_eye.png");
                Object.DestroyImmediate(gun); Object.DestroyImmediate(holder); Object.DestroyImmediate(eye.gameObject); Object.DestroyImmediate(baked);
            }
            Debug.Log("[GripReview] Hand renders: " + folder);
        }

        private static void Capture(Camera cam, string path)
        {
            var rt = new RenderTexture(1200, 800, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0,0,1200,800),0,0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
