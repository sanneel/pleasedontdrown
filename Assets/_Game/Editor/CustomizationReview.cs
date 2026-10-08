using System;
using System.IO;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static class CustomizationReview
    {
        public static void ReviewAndBuild()
        {
            ImportAndCapture();
            BuildReviewBatch();
        }

        [MenuItem("Tools/PDD/Build customization review player")]
        public static void BuildReviewBatch()
        {
            const string stampPath = "Assets/_Game/Resources/BuildStamp.txt";
            byte[] originalStamp = File.ReadAllBytes(stampPath);
            bool failed = false;
            try
            {
                AvatarWearImport.ValidateLooks();
                File.WriteAllText(stampPath, "customization-" + Guid.NewGuid().ToString("N"));
                AssetDatabase.ImportAsset(stampPath);
                const string output = "Builds/CustomizationReview/PleaseDontDrown.exe";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/_Game/Scenes/Game.unity" },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException("Customization build failed: " + report.summary.result);
                File.Copy("steam_appid.txt", "Builds/CustomizationReview/steam_appid.txt", true);
                Debug.Log("[Customization] BUILD PASS: " + Path.GetFullPath(output));
            }
            catch (Exception e) { Debug.LogException(e); failed = true; }
            finally
            {
                File.WriteAllBytes(stampPath, originalStamp);
                AssetDatabase.ImportAsset(stampPath);
            }
            if (failed && Application.isBatchMode) EditorApplication.Exit(1);
        }

        public static void ImportAndCapture()
        {
            AvatarWearImport.ImportBatch();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.65f,.7f,.8f);
                RenderSettings.fog = false;
                var light = new GameObject("Studio key").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.4f;
                light.transform.rotation = Quaternion.Euler(35,-35,0);
                var fill = new GameObject("Studio fill").AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = .55f;
                fill.transform.rotation = Quaternion.Euler(10,135,0);
                AvatarRig.SharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Data/Materials/Avatar.mat");
                var go = new GameObject("Accessory review");
                var rig = go.AddComponent<AvatarRig>();
                var cam = new GameObject("Review camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(.16f,.24f,.27f);
                cam.orthographic = true; cam.nearClipPlane = .01f; cam.farClipPlane = 20f;
                const int width = 384, height = 448;
                var rt = new RenderTexture(width,height,24) { antiAliasing = 4 };
                cam.targetTexture = rt;
                var tile = new Texture2D(width,height,TextureFormat.RGB24,false);
                var sheet = new Texture2D(width*4,height*4,TextureFormat.RGB24,false);
                Directory.CreateDirectory("Screenshots/Review/Customization");
                for (int i = 0; i < 16; i++)
                {
                    var look = AvatarPresets.Apply(i % 8, AvatarLook.Lifeguard);
                    if (i >= 8)
                    {
                        look.Hat = new[] { HatStyle.BucketHat,HatStyle.StrawHat,HatStyle.Bandana,HatStyle.Visor,HatStyle.Beanie,HatStyle.Crown,HatStyle.Headphones,HatStyle.Cap }[i-8];
                        look.HeadSize = (byte)(i >= 12 ? i%4 : 0);
                        look.Eyes = (byte)(i >= 12 ? 1 : 0);
                        look.Glasses = new[] { GlassesStyle.Round,GlassesStyle.Hearts,GlassesStyle.Sport,GlassesStyle.Aviators,GlassesStyle.Goggles,GlassesStyle.Stars,GlassesStyle.Hearts,GlassesStyle.Sunglasses }[i-8];
                        look.Face = (FacialHair)((i-8)%4);
                    }
                    rig.Build(look);
                    rig.SetExpression(1f, 0f);
                    // Baking makes each editor capture use this pose immediately, without the
                    // skinned renderer's previous-frame bone buffer between successive looks.
                    var baked = new Mesh();
                    rig.Renderer.BakeMesh(baked);
                    var body = new GameObject("Baked review body");
                    body.AddComponent<MeshFilter>().sharedMesh = baked;
                    body.AddComponent<MeshRenderer>().sharedMaterial = rig.Renderer.sharedMaterial;
                    rig.Renderer.enabled = false;
                    Vector3 center = rig[AvatarRig.Bone.Head].position + Vector3.up * .37f * AvatarFunny.HeadScales[look.HeadSize];
                    cam.orthographicSize = .67f * AvatarFunny.HeadScales[look.HeadSize];
                    cam.transform.position = center + new Vector3(.22f,.06f,3f);
                    cam.transform.LookAt(center);
                    cam.Render(); RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0,0,width,height),0,0);tile.Apply();
                    File.WriteAllBytes($"Screenshots/Review/Customization/look-{i:00}.png",tile.EncodeToPNG());
                    sheet.SetPixels(i%4*width,(3-i/4)*height,width,height,tile.GetPixels());
                    Object.DestroyImmediate(body); Object.DestroyImmediate(baked);
                    rig.Renderer.enabled = true;
                }
                sheet.Apply();
                File.WriteAllBytes("Screenshots/Review/Customization/lineup.png",sheet.EncodeToPNG());
                RenderTexture.active = null;cam.targetTexture=null;
                Object.DestroyImmediate(tile);Object.DestroyImmediate(sheet);Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
                Debug.Log("[Customization] REVIEW PASS: 16 looks, including large eyes and all head sizes.");
            }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }
    }
}
