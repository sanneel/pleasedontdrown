using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Keep the short recorded water bank ready to play without lossy transient smearing.</summary>
    public sealed class WaterAudioImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/_Game/Resources/Audio/Water/")) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false; // Files are already downmixed without importer normalization.
            importer.loadInBackground = false;
        }
    }
}
