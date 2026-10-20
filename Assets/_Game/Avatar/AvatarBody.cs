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
        [Tooltip("Which way each finger points from its knuckle, in hand space, as a right hand (empty: straight down). " +
                 "Cartoon hands fan their fingers out; bent the straight-down way they curled sideways into a claw.")]
        public Vector3[] FingerDirsL, FingerDirsR;
        [Tooltip("A face was found in the texture: the eye and mouth bones are on it.")]
        public bool HasFace;
        [Tooltip("The living face (Editor/MeshyCharacters.FaceRig.cs, AvatarFace): the material's face shader can shut the " +
                 "eyes and move them about; the mesh is cut along the lips with a jaw shape key (and smile, frown, brows).")]
        public bool FaceEyes, FaceMouth;
        [Tooltip("The inside of the mouth (the last submesh): the body's material with front faces culled. Null: no mouth inside.")]
        public Material MouthMaterial;
        [Tooltip("The head's size from the head bone: up to its top, out to its widest, forward to the face, back to the back of the skull (hair included).")]
        public float HeadTop, HeadHalfWidth, HeadFront, HeadBack;

        [Header("Funny body (recoloured and reshaped per look, see AvatarFunny)")]
        [Tooltip("RGB = the model's colours, A = which part each texel is (AvatarFunny.Region * 32). Readable. Null = not a funny body.")]
        public Texture2D Recolor;
        [Tooltip("Typical brightness of each region in the texture (index = AvatarFunny.Region): the tint keeps the shading relative to it.")]
        public float[] RegionLuma;
        [Tooltip("The painted googly eyes, in head-bone space at rest: centre of each eye's front and the eye's radius.")]
        public Vector3 GooglyL, GooglyR;
        public float GooglyRadius;
        [Tooltip("Tip of the nose and the mouth (teeth), in head-bone space.")]
        public Vector3 NoseTip, TeethAt;
        public bool IsFunny => Recolor != null;
    }
}
