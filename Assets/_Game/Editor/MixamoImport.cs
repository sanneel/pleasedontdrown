using UnityEditor;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Mixamo animation files dropped into Resources/Mixamo (FBX for Unity, without skin) come in as humanoid clips that
    /// loop in place: the root's turn, height and travel baked into the pose (the game moves the character itself), so
    /// Avatars.AvatarClips can play them on every body.
    /// </summary>
    public sealed class MixamoImport : AssetPostprocessor
    {
        private const string Folder = "Assets/_Game/Resources/Mixamo/";

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
            ModelImporterClipAnimation[] clips = model.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                clip.loopTime = true;
                clip.loopPose = true;
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
