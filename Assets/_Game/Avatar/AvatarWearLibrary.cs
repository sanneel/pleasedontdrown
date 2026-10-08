using System;
using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>Blender-authored accessories, fitted in the Goofy body's head space.</summary>
    public sealed class AvatarWearLibrary : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string Name;
            public Mesh Mesh;
            public int[] TintRegions; // 0 original colour, 1 hat palette, 2 hair palette
        }
        public Entry[] Entries;
        private static AvatarWearLibrary _instance;
        private static readonly Dictionary<string, Mesh> Cache = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _instance = null;
            Cache.Clear();
        }

        public static bool Attach(string name, Transform head, AvatarLook look, float eyeScale, List<GameObject> parts)
        {
            if (_instance == null) _instance = Resources.Load<AvatarWearLibrary>("AvatarWear");
            if (_instance == null) return false;
            Entry entry = Array.Find(_instance.Entries, e => e.Name == name);
            if (entry == null || entry.Mesh == null) return false;
            bool eyewear = name.StartsWith("glasses_");
            int tint = name.StartsWith("hat_") ? look.HatColor : name.StartsWith("face_") ? look.HairColor : 0;
            string key = name + ":" + tint + ":" + (eyewear ? eyeScale : 1f);
            if (!Cache.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                mesh = Instantiate(entry.Mesh);
                mesh.name = name;
                var colors = mesh.colors;
                for (int i = 0; i < colors.Length; i++)
                {
                    int region = entry.TintRegions[i];
                    if (region != 0) colors[i] *= region == 1 ? look.HatTint : look.HairTint;
                }
                mesh.colors = colors;
                if (eyewear && eyeScale > 1f)
                {
                    var vertices = mesh.vertices;
                    var normals = mesh.normals;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 v = vertices[i];
                        // A continuous stretch keeps the bridge intact between larger eyes.
                        float front = Mathf.InverseLerp(0.08f, 0.30f, v.z);
                        float scale = Mathf.Lerp(1f, 1f + (eyeScale - 1f) * .35f, front);
                        v.x *= scale;
                        v.y = 0.302f + (v.y - 0.302f) * scale;
                        v.z += 0.08f * (eyeScale - 1f) * front;
                        vertices[i] = v;
                        normals[i] = new Vector3(normals[i].x / scale, normals[i].y / scale, normals[i].z).normalized;
                    }
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.RecalculateBounds();
                }
                Cache[key] = mesh;
            }
            var go = new GameObject("Blender " + name);
            go.transform.SetParent(head, false);
            if (name.StartsWith("hat_") && name != "hat_Headphones" && name != "hat_Headband" && name != "hat_Bandana")
                go.transform.localPosition = Vector3.up * .08f * (eyeScale - 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            parts.Add(go);
            return true;
        }
    }
}
