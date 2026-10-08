using System;
using System.IO;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    public static class AvatarWearImport
    {
        [Serializable] private class Export { public Piece[] pieces; }
        [Serializable] private class Piece
        {
            public string name;
            public Vector3[] vertices;
            public Vector3[] normals;
            public int[] triangles;
            public Color[] colors;
            public int[] regions;
        }

        [MenuItem("Tools/PDD/Import Blender customization")]
        public static void ImportBatch()
        {
            try
            {
                var data = JsonUtility.FromJson<Export>(File.ReadAllText("ArtSource/Customization/wearables.json"));
                const string folder = "Assets/_Game/Art/Customization";
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                var entries = new AvatarWearLibrary.Entry[data.pieces.Length];
                for (int i = 0; i < data.pieces.Length; i++)
                {
                    var p = data.pieces[i];
                    if (p.vertices.Length != p.colors.Length || p.vertices.Length != p.regions.Length || p.vertices.Length != p.normals.Length)
                        throw new InvalidOperationException("Bad Blender attributes: " + p.name);
                    string path = folder + "/" + p.name + ".asset";
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                    mesh.Clear();
                    mesh.name = p.name;
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.vertices = p.vertices;
                    mesh.triangles = p.triangles;
                    mesh.normals = p.normals;
                    mesh.colors = p.colors;
                    mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);
                    entries[i] = new AvatarWearLibrary.Entry { Name = p.name, Mesh = mesh, TintRegions = p.regions };
                }
                const string libraryPath = "Assets/_Game/Resources/AvatarWear.asset";
                var library = AssetDatabase.LoadAssetAtPath<AvatarWearLibrary>(libraryPath);
                if (library == null) { library = ScriptableObject.CreateInstance<AvatarWearLibrary>(); AssetDatabase.CreateAsset(library, libraryPath); }
                library.Entries = entries;
                void Require(string name)
                {
                    if (!Array.Exists(entries, e => e.Name == name && e.Mesh.vertexCount > 0))
                        throw new InvalidOperationException("Missing customization mesh: " + name);
                }
                foreach (HatStyle hat in Enum.GetValues(typeof(HatStyle))) if (hat != HatStyle.None) Require("hat_" + hat);
                foreach (GlassesStyle glasses in Enum.GetValues(typeof(GlassesStyle))) if (glasses != GlassesStyle.None) Require("glasses_" + glasses);
                foreach (FacialHair face in Enum.GetValues(typeof(FacialHair))) if (face != FacialHair.None) Require("face_" + face);
                Require("teeth_Bucky");
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssets();
                ValidateLooks();
                Debug.Log("[Customization] Imported " + entries.Length + " Blender accessories; saved-look compatibility PASS.");
            }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); else throw; }
        }

        public static void ValidateLooks()
        {
            var random = new System.Random(52721);
            for (int i = 0; i < 512; i++)
            {
                var look = AvatarLook.Random(random);
                if (!AvatarLook.Unpack(look.Pack()).Equals(look))
                    throw new InvalidOperationException("Random look save round-trip failed");
            }
            foreach (HatStyle hat in Enum.GetValues(typeof(HatStyle)))
                foreach (GlassesStyle glasses in Enum.GetValues(typeof(GlassesStyle)))
                    for (byte head = 0; head < 4; head++)
                    {
                        var look = AvatarLook.Lifeguard;
                        look.Hat = hat; look.Glasses = glasses; look.HeadSize = head; look.HatColor = 15;
                        var restored = AvatarLook.Unpack(look.Pack());
                        if (restored.Hat != hat || restored.Glasses != glasses || restored.HeadSize != head || restored.HatColor != 15)
                            throw new InvalidOperationException("New cosmetics did not survive packing");
                    }
            // Independently encoded fixtures for the two existing save layouts.
            int[] old = { 2,2,3,3,3,3,4,2,4,3,4,2,2,3,1,8 };
            int[] funny = { 2,2,3,3,3,3,4,2,4,3,4,2,2,3,1,7,2,2,2,2,2 };
            int[] values = { 2,3,5,4,6,2,9,2,7,7,13,3,2,5,0,8,3,2,1,3,2 };
            foreach (var widths in new[] { old, funny })
            {
                ulong packed = widths.Length == 16 ? 0xA7UL << 56 : 0xBUL << 60;
                int shift = 0;
                for (int i = 0; i < widths.Length; i++) { packed |= (ulong)values[i] << shift; shift += widths[i]; }
                var restored = AvatarLook.Unpack(packed);
                if (restored.Hat != HatStyle.Bandana || restored.Glasses != GlassesStyle.Hearts || restored.Body != 8 || restored.Skin != 5 || restored.HatColor != 13 || (widths.Length > 16 && restored.Teeth != 2))
                    throw new InvalidOperationException("Existing saved look migration failed");
            }
        }
    }
}
