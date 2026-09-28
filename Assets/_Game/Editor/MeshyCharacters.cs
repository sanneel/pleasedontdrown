using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Avatars;
using UnityEditor;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Bakes generated characters into <see cref="AvatarBody"/> assets. Input: a GLB from
    /// ArtSource/Tools/prepare_character.py (one figure, A-pose, skinned to bones named like AvatarRig.Bone).
    /// The mesh is re-skinned to the game's own skeleton: same bones, placed at this character's joints, and bound so
    /// that the rig's rest pose (arms and legs straight down) turns the A-pose arms down. Every procedural pose and
    /// gesture then works on it unchanged.
    /// </summary>
    internal static class MeshyCharacters
    {
        private const string SourceDir = "Assets/_Game/Art/Characters";
        private const string OutputDir = "Assets/_Game/Avatar/Bodies";
        private const string LibraryPath = "Assets/_Game/Resources/" + AvatarBodyLibrary.ResourcePath + ".asset";

        /// <summary>id, display name, GLB file (in Art/Characters).</summary>
        private static readonly (byte id, string name, string file)[] Bodies =
        {
            (AvatarLook.Bodies.Sandy, "Sandy", "sandy"),
            (AvatarLook.Bodies.SandyBoss, "Sandy (boss)", "sandy_boss"),
            (AvatarLook.Bodies.TouristRed, "Tourist (red bikini)", "tourist_bikini_red"),
            (AvatarLook.Bodies.TouristSporty, "Tourist (sporty)", "tourist_bikini_sporty"),
            (AvatarLook.Bodies.TouristPurple, "Tourist (purple bikini)", "tourist_bikini_purple"),
            (AvatarLook.Bodies.TouristBuddy, "Tourist (sunburnt dad)", "tourist_buddy"),
        };

        private static readonly int BoneTotal = (int)Bone.Count + 2 * HandBones.BoneCount;

        /// <summary>Bakes every body whose GLB is present and writes the library the game loads.</summary>
        public static void BakeAll()
        {
            Directory.CreateDirectory(OutputDir);
            var baked = new List<AvatarBody>();
            foreach (var (id, name, file) in Bodies)
            {
                string glb = $"{SourceDir}/{file}.glb";
                if (!File.Exists(glb)) continue;
                baked.Add(Bake(glb, id, name, file));
            }
            var library = AssetDatabase.LoadAssetAtPath<AvatarBodyLibrary>(LibraryPath);
            if (library == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
                library = ScriptableObject.CreateInstance<AvatarBodyLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Bodies = baked.ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Build] generated bodies: {string.Join(", ", baked.Select(b => b.DisplayName))}");
        }

        private static AvatarBody Bake(string glbPath, byte id, string displayName, string file)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (prefab == null) throw new InvalidOperationException($"GLB import failed: {glbPath}");
            GameObject model = Object.Instantiate(prefab);
            try
            {
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skin == null) throw new InvalidOperationException($"{glbPath}: no skinned mesh (run prepare_character.py)");

                // Joint positions, from the GLB's bones.
                var joint = new Dictionary<Bone, Vector3>();
                var sourceToRig = new int[skin.bones.Length];
                for (int i = 0; i < skin.bones.Length; i++)
                {
                    sourceToRig[i] = -1;
                    if (skin.bones[i] != null && Enum.TryParse(skin.bones[i].name, out Bone bone) && bone < Bone.Count)
                    {
                        joint[bone] = skin.bones[i].position;
                        sourceToRig[i] = (int)bone;
                    }
                }
                foreach (Bone needed in new[] { Bone.Hips, Bone.Spine, Bone.Chest, Bone.Neck, Bone.Head, Bone.UpperArmL, Bone.ForearmL,
                             Bone.HandL, Bone.UpperArmR, Bone.ForearmR, Bone.HandR, Bone.ThighL, Bone.ShinL, Bone.FootL,
                             Bone.ThighR, Bone.ShinR, Bone.FootR })
                    if (!joint.ContainsKey(needed)) throw new InvalidOperationException($"{glbPath}: bone {needed} missing");

                // The mesh as it stands (A-pose), in model space.
                var posed = new Mesh();
                skin.BakeMesh(posed, true);
                Matrix4x4 toModel = skin.transform.localToWorldMatrix;
                Vector3[] vertices = posed.vertices.Select(v => toModel.MultiplyPoint3x4(v)).ToArray();
                Vector3[] normals = posed.normals.Select(n => toModel.MultiplyVector(n).normalized).ToArray();
                Vector4[] tangents = posed.tangents.Select(t =>
                {
                    Vector3 d = toModel.MultiplyVector(t).normalized;
                    return new Vector4(d.x, d.y, d.z, t.w);
                }).ToArray();

                // prepare_character.py always exports facing glTF +Z, which is the rig's +Z here: no turning needed
                // (guessing from the feet fails on flip-flops, whose soles stick out behind as far as in front).
                if (joint[Bone.UpperArmL].x > 0f)
                    throw new InvalidOperationException($"{glbPath}: left arm on the right (not exported by prepare_character.py?)");

                var body = ScriptableObject.CreateInstance<AvatarBody>();
                body.Id = id;
                body.DisplayName = displayName;
                var rest = new Vector3[(int)Bone.Count];
                var bind = new Matrix4x4[BoneTotal];
                Vector3 P(Bone b) => joint[b];
                float Dist(Bone a, Bone b) => Vector3.Distance(P(a), P(b));
                Quaternion Along(Bone from, Bone to) => Quaternion.FromToRotation(Vector3.down, P(to) - P(from));
                void Set(Bone b, Vector3 local, Vector3 world, Quaternion bindRotation)
                {
                    rest[(int)b] = local;
                    bind[(int)b] = Matrix4x4.TRS(world, bindRotation, Vector3.one);
                }

                Set(Bone.Hips, P(Bone.Hips), P(Bone.Hips), Quaternion.identity);
                Set(Bone.Spine, P(Bone.Spine) - P(Bone.Hips), P(Bone.Spine), Quaternion.identity);
                Set(Bone.Chest, P(Bone.Chest) - P(Bone.Spine), P(Bone.Chest), Quaternion.identity);
                Set(Bone.Neck, P(Bone.Neck) - P(Bone.Chest), P(Bone.Neck), Quaternion.identity);
                Set(Bone.Head, P(Bone.Head) - P(Bone.Neck), P(Bone.Head), Quaternion.identity);
                foreach (bool left in new[] { true, false })
                {
                    Bone upper = left ? Bone.UpperArmL : Bone.UpperArmR, fore = left ? Bone.ForearmL : Bone.ForearmR;
                    Bone hand = left ? Bone.HandL : Bone.HandR, thigh = left ? Bone.ThighL : Bone.ThighR;
                    Bone shin = left ? Bone.ShinL : Bone.ShinR, foot = left ? Bone.FootL : Bone.FootR;
                    // Limbs: rest straight down their -Y; bound turned along the model's A-pose limb.
                    Set(upper, P(upper) - P(Bone.Chest), P(upper), Along(upper, fore));
                    Set(fore, new Vector3(0f, -Dist(upper, fore), 0f), P(fore), Along(fore, hand));
                    Set(hand, new Vector3(0f, -Dist(fore, hand), 0f), P(hand), Along(fore, hand));
                    Set(thigh, P(thigh) - P(Bone.Hips), P(thigh), Along(thigh, shin));
                    Set(shin, new Vector3(0f, -Dist(thigh, shin), 0f), P(shin), Along(shin, foot));
                    Set(foot, new Vector3(0f, -Dist(shin, foot), 0f), P(foot), Quaternion.identity); // soles stay flat
                }

                // Face and bust bones carry no mesh here, but gaze, expressions and jiggle still read them.
                float top = vertices.Max(v => v.y);
                float headSize = top - P(Bone.Head).y;
                float face = vertices.Where(v => v.y > P(Bone.Head).y && Mathf.Abs(v.x) < 0.06f).Select(v => v.z).DefaultIfEmpty(0.1f).Max() - P(Bone.Head).z;
                float eyeY = headSize * 0.42f;
                foreach (float side in new[] { -1f, 1f })
                {
                    bool left = side < 0f;
                    Vector3 eye = new Vector3(0.12f * headSize * side, eyeY, face * 0.9f);
                    Set(left ? Bone.EyeL : Bone.EyeR, eye, P(Bone.Head) + eye, Quaternion.identity);
                    Vector3 brow = eye + new Vector3(0f, 0.12f * headSize, 0f);
                    Set(left ? Bone.BrowL : Bone.BrowR, brow, P(Bone.Head) + brow, Quaternion.identity);
                    // Women come with bust bones (prepare_character.py --bust 1) weighted to the chest; else a stand-in spot.
                    Bone bustBone = left ? Bone.BustL : Bone.BustR;
                    Vector3 bust = joint.TryGetValue(bustBone, out Vector3 bustAt)
                        ? bustAt - P(Bone.Chest)
                        : new Vector3(0.08f * side, 0.02f, 0.09f) * (P(Bone.Hips).y / 0.92f);
                    Set(bustBone, bust, P(Bone.Chest) + bust, Quaternion.identity);
                }
                Vector3 mouth = new Vector3(0f, headSize * 0.2f, face * 0.9f);
                Set(Bone.Mouth, mouth, P(Bone.Head) + mouth, Quaternion.identity);
                for (int i = (int)Bone.Count; i < BoneTotal; i++) bind[i] = Matrix4x4.identity; // fingers: no mesh on them

                body.RestPositions = rest;
                body.Scale = P(Bone.Hips).y / 0.92f;
                body.UpperArmLength = Dist(Bone.UpperArmL, Bone.ForearmL);
                body.ForearmLength = Dist(Bone.ForearmL, Bone.HandL);
                body.HandLength = body.ForearmLength * 0.385f; // the code-built hand/forearm ratio
                body.ThighLength = Dist(Bone.ThighL, Bone.ShinL);
                body.ShinLength = Dist(Bone.ShinL, Bone.FootL);
                body.AnkleHeight = P(Bone.FootL).y;
                body.HipHeight = P(Bone.Hips).y;
                body.EyeHeight = P(Bone.Head).y + eyeY;

                // Skin weights: same weights, re-pointed at the rig's bone order.
                Mesh source = skin.sharedMesh;
                BoneWeight[] weights = source.boneWeights;
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    int Map(int index) => index >= 0 && index < sourceToRig.Length && sourceToRig[index] >= 0 ? sourceToRig[index] : (int)Bone.Hips;
                    w.boneIndex0 = Map(w.boneIndex0);
                    w.boneIndex1 = Map(w.boneIndex1);
                    w.boneIndex2 = Map(w.boneIndex2);
                    w.boneIndex3 = Map(w.boneIndex3);
                    weights[i] = w;
                }

                string meshPath = $"{OutputDir}/{file}_mesh.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                bool isNew = mesh == null;
                if (isNew) mesh = new Mesh();
                mesh.Clear();
                mesh.name = file;
                mesh.indexFormat = source.indexFormat;
                mesh.vertices = vertices;
                mesh.normals = normals;
                if (tangents.Length == vertices.Length) mesh.tangents = tangents;
                mesh.uv = source.uv;
                mesh.subMeshCount = source.subMeshCount;
                for (int sm = 0; sm < source.subMeshCount; sm++) mesh.SetTriangles(source.GetTriangles(sm), sm);
                mesh.boneWeights = weights;
                mesh.bindposes = bind.Select(m => m.inverse).ToArray();
                mesh.RecalculateBounds();
                if (isNew) AssetDatabase.CreateAsset(mesh, meshPath);
                else EditorUtility.SetDirty(mesh);
                body.Mesh = mesh;
                body.Material = skin.sharedMaterial;

                string bodyPath = $"{OutputDir}/{file}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<AvatarBody>(bodyPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(body, existing);
                    existing.name = file;
                    Object.DestroyImmediate(body);
                    body = existing;
                    EditorUtility.SetDirty(body);
                }
                else
                {
                    AssetDatabase.CreateAsset(body, bodyPath);
                }
                Object.DestroyImmediate(posed);
                Debug.Log($"[Build] body {displayName}: {vertices.Length} vertices, hips {body.HipHeight:0.00} m, " +
                          $"shoulders {P(Bone.UpperArmL).y:0.00} m");
                return body;
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }
    }
}
