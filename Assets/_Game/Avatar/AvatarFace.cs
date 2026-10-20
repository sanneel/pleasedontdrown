using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// What a face shows this frame: the lids, the jaw, the mouth's corners, the brows and where the eyes look.
    /// <see cref="AvatarAnimator"/> works it out (PoseFace) from what every copy of the game already knows (mood,
    /// talking, pose, gestures, who is near), so each player sees the same faces without anything sent over the network.
    /// </summary>
    public struct FaceState
    {
        public float LidL, LidR;          // upper lids: 0 open .. 1 shut
        public float LowL, LowR;          // lower lids up: 0 .. 1 (they meet the upper ones at AvatarFace.ShutAt)
        public float Jaw;                 // 0 shut .. 1 wide open
        public float Smile, Frown;        // the mouth's corners up / down
        public float BrowUp, BrowAngry, BrowSad;
        public Vector2 Gaze;              // where the eyes look, -1..1 of how far they go (x toward the face's +x, y up)

        /// <summary>The old three numbers (eyes 1 open, 0 shut, over 1 wide; mouth 0 shut .. 1 open; brows + angry, - worried).</summary>
        public static FaceState From(float eyes, float mouth, float brows)
        {
            float lid = eyes >= 1f ? 0f : Mathf.Clamp01((1f - eyes) / 0.9f);
            return new FaceState
            {
                LidL = lid, LidR = lid,
                LowL = lid * 0.6f, LowR = lid * 0.6f,
                Jaw = Mathf.Clamp01((mouth - 0.08f) / 0.85f),
                BrowAngry = Mathf.Clamp01(brows),
                BrowSad = Mathf.Clamp01(-brows),
                BrowUp = Mathf.Clamp01(-brows * 0.5f) + Mathf.Clamp01((eyes - 1.1f) * 2f)
            };
        }

        /// <summary>Back to the old three numbers (the code-built body's face).</summary>
        public void ToOld(out float eyes, out float mouth, out float brows)
        {
            eyes = 1f - Mathf.Max(LidL, LidR) * 0.92f + BrowUp * 0.3f;
            mouth = 0.08f + Jaw * 0.85f;
            brows = BrowAngry - BrowSad;
        }
    }

    /// <summary>
    /// Shows a <see cref="FaceState"/> on a generated body's living face (Editor/MeshyCharacters.FaceRig.cs): the jaw,
    /// smile, frown and brow shape keys, and the lids and gaze for the face shader (<see cref="ShaderName"/>), per
    /// renderer. Other renderer properties set by others (a hit's red flash) are kept.
    /// </summary>
    public sealed class AvatarFace
    {
        public const string ShaderName = "PleaseDontDrown/AvatarFace";
        public const string JawShape = "FaceJaw", SmileShape = "FaceSmile", FrownShape = "FaceFrown";
        public const string BrowUpShape = "FaceBrowUp", BrowAngryShape = "FaceBrowAngry", BrowSadShape = "FaceBrowSad";
        /// <summary>Each modelled eye pressed into the smooth skin of a shut lid (the shader paints the lid on it).</summary>
        public const string ShutLShape = "FaceShutL", ShutRShape = "FaceShutR";
        /// <summary>Where on an eye the lids meet when it shuts (0 its lower edge, 1 its upper): a little below the middle.</summary>
        public const float ShutAt = 0.3f;
        /// <summary>How far the jaw turns down, wide open.</summary>
        public const float JawDegrees = 15f;

        private static readonly int LidsId = Shader.PropertyToID("_FaceLids");
        private static readonly int GazeId = Shader.PropertyToID("_FaceGaze");

        private readonly SkinnedMeshRenderer _renderer;
        private readonly int _jaw, _smile, _frown, _browUp, _browAngry, _browSad, _shutL, _shutR;
        private readonly bool _eyes;
        private readonly float[] _weights = new float[8];
        private MaterialPropertyBlock _block;

        public AvatarFace(SkinnedMeshRenderer renderer, AvatarBody body)
        {
            _renderer = renderer;
            Mesh mesh = body.Mesh;
            _jaw = mesh.GetBlendShapeIndex(JawShape);
            _smile = mesh.GetBlendShapeIndex(SmileShape);
            _frown = mesh.GetBlendShapeIndex(FrownShape);
            _browUp = mesh.GetBlendShapeIndex(BrowUpShape);
            _browAngry = mesh.GetBlendShapeIndex(BrowAngryShape);
            _browSad = mesh.GetBlendShapeIndex(BrowSadShape);
            _shutL = mesh.GetBlendShapeIndex(ShutLShape);
            _shutR = mesh.GetBlendShapeIndex(ShutRShape);
            _eyes = body.FaceEyes;
            for (int i = 0; i < _weights.Length; i++) _weights[i] = -1f;
        }

        public void Show(in FaceState s)
        {
            Weight(0, _jaw, s.Jaw);
            Weight(1, _smile, s.Smile);
            Weight(2, _frown, s.Frown);
            Weight(3, _browUp, s.BrowUp);
            Weight(4, _browAngry, s.BrowAngry);
            Weight(5, _browSad, s.BrowSad);
            // The eye pressed flat only as the lids come right down (half-shut eyes keep their shape).
            Weight(6, _shutL, Pressed(Mathf.Max(s.LidL, s.LowL)));
            Weight(7, _shutR, Pressed(Mathf.Max(s.LidR, s.LowR)));
            if (!_eyes) return;
            // Every frame, over whatever else is in the block (another script may have replaced it since).
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetVector(LidsId, new Vector4(Mathf.Clamp01(s.LidL), Mathf.Clamp01(s.LidR), Mathf.Clamp01(s.LowL), Mathf.Clamp01(s.LowR)));
            _block.SetVector(GazeId, new Vector4(Mathf.Clamp(s.Gaze.x, -1f, 1f), Mathf.Clamp(s.Gaze.y, -1f, 1f), 0f, 0f));
            _renderer.SetPropertyBlock(_block);
        }

        private static float Pressed(float shut)
        {
            float k = Mathf.Clamp01((shut - 0.3f) / 0.7f);
            return k * Mathf.Sqrt(k);
        }

        private void Weight(int slot, int index, float value)
        {
            if (index < 0) return;
            float w = Mathf.Clamp01(value) * 100f;
            if (Mathf.Abs(w - _weights[slot]) < 0.05f) return;
            _weights[slot] = w;
            _renderer.SetBlendShapeWeight(index, w);
        }

        /// <summary>Everything back to rest (the body is being swapped for another).</summary>
        public void Clear()
        {
            Show(default);
        }
    }
}
