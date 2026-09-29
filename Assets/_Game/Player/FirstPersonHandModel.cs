using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// The modelled first-person hand (a Meshy sculpt, thinned and skinned by ArtSource/Tools/prepare_hand.py):
    /// a right hand in HandBones space with each vertex weighted to the wrist or a finger bone, plus the finger
    /// layout the bones must follow. Both hands are built from it (the left one mirrored), in the skin colour.
    /// </summary>
    public sealed class FirstPersonHandModel
    {
        [System.Serializable]
        private sealed class Data
        {
            public float[] vertices;
            public int[] triangles;
            public int[] bone0;
            public int[] bone1;
            public float[] weight0;
            public float[] bases;
            public float[] lengths;
            public float[] directions;
            public float[] palm;
        }

        private static FirstPersonHandModel _loaded;
        private static bool _tried;

        public readonly HandBones.Shape Shape = new();
        private readonly Vector3[] _vertices;
        private readonly int[] _triangles;
        private readonly int[] _bone0, _bone1;
        private readonly float[] _weight0;

        /// <summary>The model from Resources/FirstPersonHand, or null (then the hands stay code-built).</summary>
        public static FirstPersonHandModel Load()
        {
            if (_tried) return _loaded;
            _tried = true;
            var text = Resources.Load<TextAsset>("FirstPersonHand");
            if (text == null) return null;
            try
            {
                _loaded = new FirstPersonHandModel(JsonUtility.FromJson<Data>(text.text));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Hands] FirstPersonHand unusable, using code-built hands: {e.Message}");
            }
            return _loaded;
        }

        private FirstPersonHandModel(Data d)
        {
            int count = d.vertices.Length / 3;
            _vertices = new Vector3[count];
            for (int i = 0; i < count; i++) _vertices[i] = new Vector3(d.vertices[3 * i], d.vertices[3 * i + 1], d.vertices[3 * i + 2]);
            _triangles = d.triangles;
            _bone0 = d.bone0;
            _bone1 = d.bone1;
            _weight0 = d.weight0;
            if (_bone0.Length != count || _bone1.Length != count || _weight0.Length != count)
                throw new System.FormatException("weights don't match the vertices");
            for (int f = 0; f < HandBones.Fingers; f++)
            {
                Shape.Bases[f] = new Vector3(d.bases[3 * f], d.bases[3 * f + 1], d.bases[3 * f + 2]);
                Shape.Directions[f] = new Vector3(d.directions[3 * f], d.directions[3 * f + 1], d.directions[3 * f + 2]);
            }
            System.Array.Copy(d.lengths, Shape.Lengths, HandBones.BoneCount);
            Shape.Palm = new Vector3(d.palm[0], d.palm[1], d.palm[2]);
        }

        /// <summary>
        /// Adds one hand to the lists, in the skinned mesh's root space. <paramref name="wristToRoot"/> takes points
        /// from the wrist bone (at bind time) to the root; <paramref name="wristBone"/> and <paramref name="firstFinger"/>
        /// are indices into the renderer's bones.
        /// </summary>
        public void AddHand(float side, float scale, Matrix4x4 wristToRoot, int wristBone, int firstFinger, Color32 skin,
            List<Vector3> vertices, List<Color32> colors, List<BoneWeight> weights, List<int> triangles)
        {
            int start = vertices.Count;
            for (int i = 0; i < _vertices.Length; i++)
            {
                Vector3 v = _vertices[i];
                v.x *= side;
                vertices.Add(wristToRoot.MultiplyPoint3x4(v * scale));
                colors.Add(skin);
                float w = _weight0[i];
                int a = _bone0[i] < 0 ? wristBone : firstFinger + _bone0[i];
                int b = _bone1[i] < 0 ? wristBone : firstFinger + _bone1[i];
                weights.Add(a == b || w >= 0.999f
                    ? new BoneWeight { boneIndex0 = a, weight0 = 1f }
                    : new BoneWeight { boneIndex0 = a, weight0 = w, boneIndex1 = b, weight1 = 1f - w });
            }
            // The left hand is a mirror image: turn its faces round so they still face out.
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                triangles.Add(start + _triangles[i]);
                if (side > 0f)
                {
                    triangles.Add(start + _triangles[i + 1]);
                    triangles.Add(start + _triangles[i + 2]);
                }
                else
                {
                    triangles.Add(start + _triangles[i + 2]);
                    triangles.Add(start + _triangles[i + 1]);
                }
            }
        }
    }
}
