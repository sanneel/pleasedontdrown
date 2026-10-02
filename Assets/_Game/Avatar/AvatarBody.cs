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

        [Tooltip("Rest turn of each hand on its forearm. The hand bones are bound the way the game's own hands are " +
                 "(fingers down -Y, thumb toward +Z, palm on -X right / +X left), whichever way the model holds them.")]
        public Quaternion HandRestL = Quaternion.identity, HandRestR = Quaternion.identity;
        [Tooltip("The model's fingers are skinned to the finger bones (one mitten of four fingers; the thumb stays on the hand).")]
        public bool HasFingers;
        [Tooltip("Where each finger starts, in hand space, as a right hand (thumb, index, middle, ring, pinky).")]
        public Vector3[] FingerBasesL, FingerBasesR;
        [Tooltip("Bone lengths, three per finger.")]
        public float[] FingerLengthsL, FingerLengthsR;
        [Tooltip("Eyelids on the eye bones and an open mouth on the mouth bone are part of the mesh: scaling those bones " +
                 "up closes the eyes / opens the mouth over the painted face.")]
        public bool HasFace;
    }
}
