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
            model.clipAnimations = clips;
        }
    }
}
