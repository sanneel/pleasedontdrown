using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// A generated (Meshy) character body for <see cref="AvatarRig"/>: a textured mesh skinned to the rig's own bones,
    /// plus that character's joint positions. The procedural animation drives it like any code-built avatar.
    /// Baked in the editor from a rigged GLB (Editor/MeshyCharacters.cs, made by ArtSource/Tools/prepare_character.py).
    /// Picked by <see cref="AvatarLook.Body"/> (0 = the code-built body).
    /// </summary>
    public class AvatarBody : ScriptableObject
    {
        public int Id;
        public string DisplayName;
        public Mesh Mesh;               // bone order = AvatarRig's (body bones, then left and right fingers)
        public Material Material;
        [Tooltip("Rest local position of every AvatarRig.Bone (limbs point down their -Y, like the code-built rig).")]
        public Vector3[] RestPositions;
        public float Scale = 1f;        // as AvatarRig.Scale: hip height / 0.92
        public float UpperArmLength, ForearmLength, HandLength, ThighLength, ShinLength, AnkleHeight, HipHeight, EyeHeight;
    }
}
