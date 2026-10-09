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

        /// <summary>
        /// -executeMethod ...BakeNamed -bodies tourist_bikini_red_v10,tourist_buddy [-shots file]: re-bake only those
        /// bodies (after a texture pass on their GLBs), then optionally render review shots.
        /// </summary>
        public static void BakeNamed()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(args, "-bodies");
                if (at < 0 || at + 1 >= args.Length) throw new ArgumentException("-bodies a,b,c missing");
                if (!CanBake("named bodies")) return;
                foreach (string file in args[at + 1].Split(','))
                {
                    string baseFile = BaseOf(file);
                    var entry = Array.Find(Bodies, b => b.file == baseFile);
                    if (entry.file == null) throw new ArgumentException($"no body {file}");
                    int n = baseFile == file ? 0 : int.Parse(file.Substring(file.Length - 2));
                    // A look-alike's face is found where its base model's is: bake the base first (into memory).
                    if (n > 0 && !BaseFaces.ContainsKey(baseFile)) Bake($"{SourceDir}/{baseFile}.glb", entry.id, entry.name, baseFile);
                    Bake($"{SourceDir}/{file}.glb", n > 0 ? Avatars.AvatarLook.Bodies.Variant(entry.id, n) : entry.id, n > 0 ? $"{entry.name} #{n}" : entry.name, file);
                }
                AssetDatabase.SaveAssets();
                if (Array.IndexOf(args, "-shots") >= 0) ReviewCapture.CaptureBatch();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Build] named bake FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        public static void BakeFaceRepairBases()
        {
            foreach (var (id, name, file) in Bodies)
                if (file.StartsWith("tourist_")) Bake($"{SourceDir}/{file}.glb", id, name, file);
            AssetDatabase.SaveAssets();
        }
    }
}
