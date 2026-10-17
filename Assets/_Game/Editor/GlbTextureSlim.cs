using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Build size and video memory. glTFast imports the pictures packed inside a .glb raw, uncompressed (RGBA32 + mips):
    /// one 2048 picture is 21 MB, so each Tripo/Meshy prop cost 40-55 MB and textures were 2.3 of the 2.5 GB build.
    /// This takes each packed picture out into an ordinary image file beside the model
    /// (<c>&lt;model folder&gt;/Textures/&lt;model&gt;_&lt;role&gt;.png|jpg</c>), which Unity compresses (DXT/BC: 10-20x smaller),
    /// points the .glb at that file (glTFast then uses the compressed asset), and re-points materials, prefabs and the
    /// scene that used the old packed picture. Models already done are left alone, so it can run again after new
    /// models arrive. Character skins (Avatar/Bodies) are capped at 2048 too.
    /// Batch: -executeMethod PleaseDontDrown.Editor.GlbTextureSlim.Run [-pdd-dry] [-pdd-only &lt;path part&gt;]
    /// </summary>
    public static class GlbTextureSlim
    {
        private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        // Seen big or from up close (buildings, bosses, characters, vehicles): 2048. Everything else: 1024.
        private static readonly string[] BigModels =
        {
            "castle_", "pirate_tavern", "pirate_shack", "sea_cave", "sea_rock", "gun_shop", "old_snapper", "kleptopus",
            "alligator", "monkey", "shark", "pirate_boat", "palm_tall", "tower", "station_rusty", "lifeguard",
            "/characters/", "jetski", "flamingo", "beach_bar", "hotel",
        };

        [MenuItem("PLEASE DON'T DROWN/Performance/Compress GLB pictures and character skins")]
        public static void Run()
        {
            string[] cl = Environment.GetCommandLineArgs();
            bool dry = cl.Contains("-pdd-dry");
            int onlyAt = Array.IndexOf(cl, "-pdd-only");
            string only = onlyAt >= 0 && onlyAt + 1 < cl.Length ? cl[onlyAt + 1] : null;
            var remap = new Dictionary<string, string>();
            var glbGuids = new HashSet<string>();
            int models = 0, pictures = 0;
            foreach (string file in Directory.GetFiles("Assets", "*.glb", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (only != null && !path.Contains(only)) continue;
                try
                {
                    int n = Slim(path, dry, remap, glbGuids);
                    if (n > 0) { models++; pictures += n; }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[GlbSlim] {path}: {e.Message}\n{e.StackTrace}");
                }
            }
            int files = dry ? 0 : Repoint(remap, glbGuids);
            int skins = only == null ? CapCharacterSkins(dry) : 0;
            Debug.Log($"[GlbSlim] {(dry ? "DRY RUN: would move" : "moved")} {pictures} pictures out of {models} models, " +
                      $"re-pointed {remap.Count} references in {files} files, capped {skins} character skins at 2048");
        }

        private static int Slim(string path, bool dry, Dictionary<string, string> remap, HashSet<string> glbGuids)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != Magic) return 0;
            int jsonLength = BitConverter.ToInt32(bytes, 12);
            if (BitConverter.ToUInt32(bytes, 16) != JsonChunk) return 0;
            var json = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, jsonLength));
            if (json["images"] is not JArray images || !images.Any(i => i["bufferView"] != null)) return 0;

            int binStart = 20 + jsonLength;
            if (binStart + 8 > bytes.Length || BitConverter.ToUInt32(bytes, binStart + 4) != BinChunk)
                throw new InvalidDataException("no BIN chunk");
            int binLength = BitConverter.ToInt32(bytes, binStart);
            binStart += 8;
            var views = (JArray)json["bufferViews"];

            string[] roles = Roles(json, images.Count);
            string name = Path.GetFileNameWithoutExtension(path);
            string folder = Path.GetDirectoryName(path)!.Replace('\\', '/') + "/Textures";
            bool big = BigModels.Any(b => path.ToLowerInvariant().Contains(b));

            // The packed pictures as glTFast added them (named after the image, "image_N" when unnamed).
            string glbGuid = AssetDatabase.AssetPathToGUID(path);
            var packed = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().ToList();

            var outPaths = new string[images.Count];
            var used = new HashSet<string>();
            for (int i = 0; i < images.Count; i++)
            {
                if (images[i]["bufferView"] == null) continue;
                string ext = (string)images[i]["mimeType"] switch { "image/png" => "png", "image/jpeg" => "jpg", _ => null };
                if (ext == null) throw new InvalidDataException($"image {i} is {(string)images[i]["mimeType"]}, Unity can't import that");
                string stem = $"{folder}/{name}_{roles[i]}";
                string candidate = $"{stem}.{ext}";
                for (int k = 2; used.Contains(candidate) || (File.Exists(candidate) && !dry); k++) candidate = $"{stem}{k}.{ext}";
                used.Add(candidate);
                outPaths[i] = candidate;
            }
            int count = outPaths.Count(p => p != null);
            if (dry)
            {
                Debug.Log($"[GlbSlim] would move {count} pictures out of {path}: {string.Join(", ", outPaths.Where(p => p != null).Select((p, i) => $"{Path.GetFileName(p)}"))}");
                return count;
            }

            // 1. The pictures as files, imported compressed (before the model, which looks them up while importing).
            Directory.CreateDirectory(folder);
            for (int i = 0; i < images.Count; i++)
            {
                if (outPaths[i] == null) continue;
                var view = views[(int)images[i]["bufferView"]];
                int offset = (int?)view["byteOffset"] ?? 0, length = (int)view["byteLength"];
                using (var stream = File.Create(outPaths[i])) stream.Write(bytes, binStart + offset, length);
                AssetDatabase.ImportAsset(outPaths[i], ImportAssetOptions.ForceSynchronousImport);
                Configure(outPaths[i], roles[i], big);
            }

            // 2. Old packed picture -> new file, for materials/prefabs/scene that point at it.
            for (int i = 0; i < images.Count; i++)
            {
                if (outPaths[i] == null) continue;
                string imageName = (string)images[i]["name"];
                if (string.IsNullOrEmpty(imageName)) imageName = $"image_{i}";
                string newGuid = AssetDatabase.AssetPathToGUID(outPaths[i]);
                foreach (Texture2D old in packed.Where(t => t.name == imageName || t.name.StartsWith(imageName + "_sampler")).ToList())
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(old, out string _, out long id)) continue;
                    remap[$"fileID: {id}, guid: {glbGuid}"] = $"fileID: 2800000, guid: {newGuid}";
                    packed.Remove(old);
                }
            }
            glbGuids.Add(glbGuid);

            // 3. The .glb without the picture bytes: images point at the files, the other buffer views move up.
            var drop = new HashSet<int>(images.Where(i => i["bufferView"] != null).Select(i => (int)i["bufferView"]));
            foreach (JValue v in BufferViewRefs(json).Where(v => v.Parent?.Parent?.Parent != images))
                drop.Remove((int)v); // still used by something other than a picture: keep
            var newIndex = new int[views.Count];
            var newViews = new JArray();
            var bin = new MemoryStream();
            for (int v = 0; v < views.Count; v++)
            {
                if (drop.Contains(v)) { newIndex[v] = -1; continue; }
                var view = (JObject)views[v];
                int offset = (int?)view["byteOffset"] ?? 0, length = (int)view["byteLength"];
                // Keep each view's offset the same modulo 16, so accessor alignment stays valid.
                while (bin.Length % 16 != offset % 16) bin.WriteByte(0);
                view["byteOffset"] = bin.Length;
                bin.Write(bytes, binStart + offset, length);
                newIndex[v] = newViews.Count;
                newViews.Add(view);
            }
            while (bin.Length % 4 != 0) bin.WriteByte(0);
            foreach (JValue v in BufferViewRefs(json).ToList())
                if (v.Parent?.Parent?.Parent != images) v.Value = newIndex[(int)v];
            json["bufferViews"] = newViews;
            json["buffers"]![0]!["byteLength"] = bin.Length;
            for (int i = 0; i < images.Count; i++)
            {
                if (outPaths[i] == null) continue;
                var image = (JObject)images[i];
                image.Remove("bufferView");
                image.Remove("mimeType");
                image["uri"] = "Textures/" + Uri.EscapeDataString(Path.GetFileName(outPaths[i]));
            }
            WriteGlb(path, json, bin.ToArray());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            int left = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Texture2D>().Count();
            if (left > 0) Debug.LogWarning($"[GlbSlim] {path} still has {left} packed pictures after the move");
            Debug.Log($"[GlbSlim] {path}: {count} pictures out ({bytes.Length / 1024} KB -> {new FileInfo(path).Length / 1024} KB file), {(big ? 2048 : 1024)} max");
            return count;
        }

        /// <summary>What each picture is used as: color/normal/orm/occlusion/emissive (decides sRGB and normal map import).</summary>
        private static string[] Roles(JObject json, int count)
        {
            var roles = new string[count];
            var textures = json["textures"] as JArray;
            void Mark(JToken info, string role)
            {
                if (info?["index"] is not JValue index || textures == null) return;
                var source = textures[(int)index]?["source"];
                if (source != null && roles[(int)source] == null) roles[(int)source] = role;
            }
            foreach (JToken material in json["materials"] as JArray ?? new JArray())
            {
                Mark(material["normalTexture"], "normal");
                Mark(material["pbrMetallicRoughness"]?["baseColorTexture"], "color");
                Mark(material["pbrMetallicRoughness"]?["metallicRoughnessTexture"], "orm");
                Mark(material["occlusionTexture"], "occlusion");
                Mark(material["emissiveTexture"], "emissive");
            }
            for (int i = 0; i < count; i++) roles[i] ??= "image" + i;
            return roles;
        }

        private static void Configure(string path, string role, bool big)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = role == "normal", color = role is "color" or "emissive";
            int max = big ? 2048 : 1024;
            if (!normal && !color) max /= 2; // metal/roughness/occlusion: soft data, half the size is invisible
            // (Not Unity's "Normal map" type: glTFast's shader reads the raw R and G as X and Y, and that type moves X to
            // alpha. Plain linear BC5 keeps exactly R and G at full quality.)
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = color;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = normal, maxTextureSize = max,
                format = TextureImporterFormat.BC5, textureCompression = TextureImporterCompression.Compressed,
            });
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;
            importer.isReadable = false;
            importer.alphaSource = color && path.EndsWith(".png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.maxTextureSize = max;
            // Faces: BC7 (no DXT1 blocks on skin). Everything else: DXT1/5.
            importer.textureCompression = color && path.ToLowerInvariant().Contains("/characters/")
                ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            importer.crunchedCompression = false;
            importer.SaveAndReimport();
        }

        private static IEnumerable<JValue> BufferViewRefs(JToken token) =>
            token.SelectTokens("$..bufferView").OfType<JValue>().Where(v => v.Type == JTokenType.Integer);

        private static void WriteGlb(string path, JObject json, byte[] bin)
        {
            byte[] text = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
            int jsonPadded = (text.Length + 3) & ~3;
            using var w = new BinaryWriter(File.Create(path));
            w.Write(Magic);
            w.Write(2u);
            w.Write(12 + 8 + jsonPadded + 8 + bin.Length);
            w.Write(jsonPadded);
            w.Write(JsonChunk);
            w.Write(text);
            for (int i = text.Length; i < jsonPadded; i++) w.Write((byte)0x20);
            w.Write(bin.Length);
            w.Write(BinChunk);
            w.Write(bin);
        }

        /// <summary>Text assets that pointed at a packed picture now point at its file.</summary>
        private static int Repoint(Dictionary<string, string> remap, HashSet<string> glbGuids)
        {
            if (remap.Count == 0) return 0;
            string[] kinds = { ".mat", ".asset", ".prefab", ".unity", ".controller", ".anim", ".playable" };
            int changed = 0;
            foreach (string file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
            {
                if (!kinds.Contains(Path.GetExtension(file))) continue;
                string text = File.ReadAllText(file);
                if (!glbGuids.Any(text.Contains)) continue;
                string after = text;
                foreach (var (from, to) in remap) after = after.Replace(from, to);
                if (after == text) continue;
                File.WriteAllText(file, after);
                changed++;
                Debug.Log($"[GlbSlim] re-pointed pictures in {file}");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return changed;
        }

        /// <summary>
        /// Import settings of the pictures already moved out, again (after the size/format rules above change). Batch:
        /// -executeMethod PleaseDontDrown.Editor.GlbTextureSlim.Reconfigure [-pdd-only &lt;path part&gt;]
        /// </summary>
        public static void Reconfigure()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int onlyAt = Array.IndexOf(cl, "-pdd-only");
            string only = onlyAt >= 0 && onlyAt + 1 < cl.Length ? cl[onlyAt + 1] : null;
            int count = 0;
            foreach (string file in Directory.GetFiles("Assets", "*.glb", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (only != null && !path.Contains(only)) continue;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != Magic) continue;
                var json = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, BitConverter.ToInt32(bytes, 12)));
                if (json["images"] is not JArray images) continue;
                string[] roles = Roles(json, images.Count);
                bool big = BigModels.Any(b => path.ToLowerInvariant().Contains(b));
                for (int i = 0; i < images.Count; i++)
                {
                    if ((string)images[i]["uri"] is not string uri || !uri.StartsWith("Textures/")) continue;
                    Configure($"{Path.GetDirectoryName(path)!.Replace('\\', '/')}/{Uri.UnescapeDataString(uri)}", roles[i], big);
                    count++;
                }
            }
            Debug.Log($"[GlbSlim] re-applied import settings to {count} model pictures");
        }

        /// <summary>
        /// Every model's material pictures: where they live, their format and memory. Batch:
        /// -executeMethod PleaseDontDrown.Editor.GlbTextureSlim.Report [-pdd-only &lt;path part&gt;]
        /// </summary>
        public static void Report()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int onlyAt = Array.IndexOf(cl, "-pdd-only");
            string only = onlyAt >= 0 && onlyAt + 1 < cl.Length ? cl[onlyAt + 1] : null;
            long total = 0;
            int packedLeft = 0;
            foreach (string file in Directory.GetFiles("Assets", "*.glb", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (only != null && !path.Contains(only)) continue;
                Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                packedLeft += all.OfType<Texture2D>().Count();
                var used = new HashSet<Texture>();
                foreach (Material m in all.OfType<Material>())
                    foreach (string p in m.GetTexturePropertyNames())
                        if (m.GetTexture(p) is Texture t) used.Add(t);
                if (used.Count == 0) continue;
                var line = new StringBuilder($"[GlbSlim] {path}:");
                foreach (Texture t in used)
                {
                    long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                    total += bytes;
                    string at = AssetDatabase.GetAssetPath(t);
                    line.Append($" {(at == path ? "PACKED " + t.name : Path.GetFileName(at))} {t.width}x{t.height} {(t as Texture2D)?.format} {bytes / 1024} KB;");
                }
                Debug.Log(line.ToString());
            }
            Debug.Log($"[GlbSlim] report: model pictures {total / (1024 * 1024)} MB in memory, {packedLeft} still packed");
        }

        /// <summary>The baked character skins were 4096 BC7 (21 MB each, ~30 of them): 2048 looks the same in game.</summary>
        private static int CapCharacterSkins(bool dry)
        {
            int capped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Game/Avatar/Bodies" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path); // (null: a failed import, redo it)
                if (importer.maxTextureSize <= 2048 && imported != null && imported.width <= 2048) continue;
                capped++;
                if (dry) continue;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            return capped;
        }
    }
}
