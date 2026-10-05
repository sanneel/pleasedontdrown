using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace PleaseDontDrown.Editor
{
    public static partial class MeshyCharacters
    {
        [MenuItem("PLEASE DON'T DROWN/Repair tourist eyelid textures")]
        public static void BakeFaceRepairAll()
        {
            BakeTourists();
            foreach (int seed in new[] { 0, 3, 5, 9, 1, 2, 7, 11 })
            {
                byte id = Avatars.AvatarLook.RandomTourist(seed).Body;
                var body = Avatars.AvatarBodyLibrary.Get(id);
                Debug.Log($"[FaceRepairReview] seed={seed} body={id} asset={AssetDatabase.GetAssetPath(body)}");
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-shots") >= 0) ReviewCapture.CaptureBatch();
        }

        public static void BakeFaceRepairBases()
        {
            foreach (var (id, name, file) in Bodies)
                if (file.StartsWith("tourist_")) Bake($"{SourceDir}/{file}.glb", id, name, file);
            AssetDatabase.SaveAssets();
        }
    }
}
