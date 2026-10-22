using System;
using System.IO;
using UnityEditor;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Mixamo animation files dropped into Resources/Mixamo (FBX for Unity, without skin) come in as humanoid clips: the
    /// root's turn, height and travel baked into the pose (the game moves the character itself), so
    /// Avatars.AvatarClips can play them on every body. Most loop (idles, walks, talking, treading water); the ones that
    /// happen once (falling over, getting up) don't, so they hold their last frame.
    /// </summary>
    public sealed class MixamoImport : AssetPostprocessor
    {
        private const string Folder = "Assets/_Game/Resources/Mixamo/";

        /// <summary>Clips played once (file names; the rest loop).</summary>
        private static readonly string[] Once =
        {
            "Knocked Out Back", "Knocked Out Stomach", "Getting Up From Back", "Getting Up From Stomach", "Getting Up Knocked Down",
            "Kneeling Down", "Kneel To Stand", "Picking Up", "Throw", "Jab", "Hook", "Uppercut", "Hit Light", "Hit Big", "Jump",
        };

        private bool Ours => assetPath.Replace('\\', '/').StartsWith(Folder);

        private void OnPreprocessModel()
        {
            if (!Ours || assetImporter is not ModelImporter model) return;
            model.animationType = ModelImporterAnimationType.Human;
            model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importCameras = false;
            model.importLights = false;
            model.importAnimation = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!Ours || assetImporter is not ModelImporter model) return;
            bool loops = Array.IndexOf(Once, Path.GetFileNameWithoutExtension(assetPath)) < 0;
            ModelImporterClipAnimation[] clips = model.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = loops;
                clip.loopPose = loops;
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;
            }
            // Punches come both ways: a "<take> Mirror" copy throws it with the other hand.
            if (Array.IndexOf(Mirrored, Path.GetFileNameWithoutExtension(assetPath)) >= 0 && clips.Length > 0)
            {
                var all = new System.Collections.Generic.List<ModelImporterClipAnimation>(clips);
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    if (clip.name.StartsWith("__preview__")) continue;
                    var copy = new ModelImporterClipAnimation
                    {
                        name = clip.name + " Mirror", takeName = clip.takeName, firstFrame = clip.firstFrame, lastFrame = clip.lastFrame,
                        loopTime = clip.loopTime, loopPose = clip.loopPose, lockRootRotation = true, lockRootHeightY = true,
                        lockRootPositionXZ = true, keepOriginalOrientation = true, keepOriginalPositionY = true,
                        keepOriginalPositionXZ = true, mirror = true
                    };
                    all.Add(copy);
                }
                clips = all.ToArray();
            }
            model.clipAnimations = clips;
        }

        /// <summary>Batch: import the Mixamo files again (after changing the rules above).</summary>
        public static void ReimportBatch()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Folder.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            UnityEngine.Debug.Log("[Mixamo] reimported");
            EditorApplication.Exit(0);
        }

        /// <summary>Clips that also get a mirrored copy (the other hand): file names.</summary>
        private static readonly string[] Mirrored = { "Jab", "Hook", "Uppercut" };
    }
}
