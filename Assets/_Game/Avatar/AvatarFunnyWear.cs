using System.Collections.Generic;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// What the funny lifeguard wears on top of the model: hat, glasses (over the googly eyes), moustache or beard,
    /// big buck teeth and arm floaties. Blender-authored pieces use the character's head space;
    /// the procedural meshes remain as a fallback and supply the arm floaties.
    /// </summary>
    public static class AvatarFunnyWear
    {
        private static readonly Color Straw = new(0.93f, 0.82f, 0.5f), Lens = new(0.05f, 0.05f, 0.08f), FrameBlack = new(0.07f, 0.07f, 0.08f);
        private static readonly Color HeartPink = new(0.97f, 0.32f, 0.58f), Floatie = new(1f, 0.5f, 0.1f);

        private static readonly Dictionary<ulong, Mesh> _heads = new();
        private static readonly Dictionary<int, Mesh> _floaties = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _heads.Clear();
            _floaties.Clear();
        }

        /// <summary>Puts the look's hat, glasses, facial hair and floaties on the rig (added to <paramref name="parts"/>).</summary>
        public static void Dress(AvatarRig rig, AvatarBody body, AvatarLook look, float eyeScale, List<GameObject> parts)
        {
            // Authored pieces are tinted by the same palettes and follow the existing head-size bone scale.
            AvatarLook fallback = look;
            if (look.Hat != HatStyle.None && AvatarWearLibrary.Attach("hat_" + look.Hat, rig[Bone.Head], look, eyeScale, parts)) fallback.Hat = HatStyle.None;
            if (look.Glasses != GlassesStyle.None && AvatarWearLibrary.Attach("glasses_" + look.Glasses, rig[Bone.Head], look, eyeScale, parts)) fallback.Glasses = GlassesStyle.None;
            if (look.Face != FacialHair.None && AvatarWearLibrary.Attach("face_" + look.Face, rig[Bone.Head], look, eyeScale, parts)) fallback.Face = FacialHair.None;
            if (look.Teeth == 1 && AvatarWearLibrary.Attach("teeth_Bucky", rig[Bone.Head], look, eyeScale, parts)) fallback.Teeth = 0;
            ulong key = (ulong)body.Id | (ulong)look.Hat << 8 | (ulong)look.HatColor << 12 | (ulong)look.Glasses << 16 | (ulong)look.Face << 20 |
                        (ulong)look.HairColor << 24 | (ulong)Mathf.RoundToInt(eyeScale * 100f) << 32 | (ulong)(look.Teeth & 3) << 44;
            if (!_heads.TryGetValue(key, out Mesh head) || head == null) _heads[key] = head = HeadMesh(rig, body, fallback, eyeScale);
            if (head != null) parts.Add(Attach(rig[Bone.Head], head, "FunnyHeadwear"));
            if (!look.Has(AvatarExtras.Floaties)) return;
            int fkey = Mathf.RoundToInt(rig.UpperArmLength * 1000f);
            if (!_floaties.TryGetValue(fkey, out Mesh floatie) || floatie == null)
            {
                var kit = new AvatarMeshKit();
                kit.SetBone(0, Matrix4x4.identity);
                kit.Torus(new Vector3(0f, -rig.UpperArmLength * 0.45f, 0f), 0.05f * rig.Scale, 0.033f * rig.Scale, Floatie, null, null, 18, 8);
                _floaties[fkey] = floatie = kit.ToMesh("Floatie", new[] { Matrix4x4.identity });
            }
            parts.Add(Attach(rig[Bone.UpperArmL], floatie, "Floatie"));
            parts.Add(Attach(rig[Bone.UpperArmR], floatie, "Floatie"));
        }

        private static GameObject Attach(Transform bone, Mesh mesh, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            return go;
        }

        private static Mesh HeadMesh(AvatarRig rig, AvatarBody body, AvatarLook look, float eyeScale)
        {
            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);
            // The skull as a ball: as wide as the head, touching its top and its back.
            float r = rig.HeadHalfWidth * 1.02f, top = rig.HeadTop;
            float cz = -rig.HeadBack + r, cy = top - r;
            float s = r / 0.22f; // sizes below are for the goofy lifeguard's head
            float R = body.GooglyRadius * eyeScale;
            float eyeTop = Mathf.Max(body.GooglyL.y, body.GooglyR.y) + R;
            float rim = Mathf.Max(eyeTop + 0.012f * s, cy - r * 0.1f);
            float cover = Mathf.Acos(Mathf.Clamp((rim - cy) / (r * 1.05f), -1f, 1f)) * Mathf.Rad2Deg;
            float Around(float y) => Mathf.Sqrt(Mathf.Max(0.0001f, r * r - (y - cy) * (y - cy))) + 0.006f * s;
            var crown = new Vector3(0f, cy, cz);
            Color hat = look.HatTint;

            switch (look.Hat)
            {
                case HatStyle.Cap:
                case HatStyle.CapBackwards:
                {
                    float side = look.Hat == HatStyle.Cap ? 1f : -1f;
                    kit.Dome(crown, Vector3.one * r * 1.05f, cover, hat, null, 18);
                    // The peak sticks out well past the googly eyes (they stand proud of the face).
                    kit.Disc(new Vector3(0f, rim + 0.01f * s, cz + side * (Around(rim) + r * 0.3f)), r * 0.62f, 0.014f * s, hat,
                        Quaternion.Euler(side * 6f, 0f, 0f), 0f, new Vector2(0.95f, 1f), 20);
                    kit.Ellipsoid(new Vector3(0f, top + r * 0.04f, cz), Vector3.one * r * 0.08f, hat);
                    break;
                }
                case HatStyle.BucketHat:
                    kit.Dome(crown, Vector3.one * r * 1.07f, cover, hat, null, 18);
                    kit.Turned(new[] { new Vector2(1f, 0.012f * s), new Vector2(1.5f, -0.04f * s), new Vector2(1.5f, -0.05f * s), new Vector2(1f, 0f) },
                        new Vector3(0f, rim, cz), new Vector2(Around(rim), Around(rim)), hat, null, 22);
                    break;
                case HatStyle.StrawHat:
                    kit.Dome(crown, Vector3.one * r * 1.07f, cover, Straw, null, 18);
                    kit.Torus(new Vector3(0f, rim + 0.02f * s, cz), Around(rim + 0.02f * s) * 1.03f, 0.014f * s, hat, null, null, 22);
                    kit.Disc(new Vector3(0f, rim, cz), r * 2.1f, 0.012f * s, Straw, null, 0f, null, 26);
                    break;
                case HatStyle.Visor:
                    kit.Torus(new Vector3(0f, rim + 0.012f * s, cz), Around(rim + 0.012f * s), 0.013f * s, hat, null, null, 22);
                    kit.Disc(new Vector3(0f, rim + 0.01f * s, cz + Around(rim) + r * 0.3f), r * 0.62f, 0.012f * s, hat, Quaternion.Euler(6f, 0f, 0f), 0f, new Vector2(0.95f, 1f), 20);
                    break;
                case HatStyle.Headband:
                {
                    float y = eyeTop + 0.035f * s;
                    kit.Torus(new Vector3(0f, y, cz), Around(y), 0.017f * s, hat, null, null, 22);
                    break;
                }
                case HatStyle.Bandana:
                {
                    kit.Dome(crown, Vector3.one * r * 1.05f, cover, hat, null, 18);
                    float y = rim + 0.02f * s;
                    kit.Ellipsoid(new Vector3(0f, y, cz - Around(y) - 0.01f * s), new Vector3(0.03f, 0.025f, 0.02f) * s, hat * 0.8f);
                    break;
                }
            }

            if (look.Glasses != GlassesStyle.None)
            {
                Color frame = look.Glasses == GlassesStyle.Hearts ? HeartPink : FrameBlack;
                float ring = R * 1.1f;
                var centres = new List<Vector3>();
                foreach (Vector3 eye in new[] { body.GooglyL, body.GooglyR })
                {
                    // In front of the googly eye's dome (which stands a little proud of the painted eye).
                    var c = new Vector3(eye.x, eye.y, eye.z + R * 0.12f + 0.012f * s);
                    centres.Add(c);
                    if (look.Glasses == GlassesStyle.Sunglasses) kit.Disc(c, ring, 0.006f * s, Lens, Quaternion.Euler(90f, 0f, 0f), 0f, null, 24);
                    kit.Torus(c, ring, R * 0.09f, frame, Quaternion.Euler(90f, 0f, 0f), null, 26, 6);
                }
                Vector3 l = centres[0], rr = centres[1]; // left eye is on -X
                float gap = rr.x - ring - (l.x + ring);
                if (gap > 0.002f) kit.Box(new Vector3((l.x + rr.x) * 0.5f, l.y + R * 0.2f, l.z), new Vector3(gap, 0.012f * s, 0.01f * s), frame);
                foreach (Vector3 c in centres)
                {
                    float side = Mathf.Sign(c.x);
                    var from = new Vector3(c.x + side * ring, c.y, c.z);
                    var to = new Vector3(side * Around(c.y), c.y, cz);
                    Vector3 d = to - from;
                    kit.Box((from + to) * 0.5f, new Vector3(0.01f * s, 0.012f * s, d.magnitude), frame, Quaternion.LookRotation(d));
                }
            }

            if (look.Teeth == 1)
            {
                // Bucky: two huge front teeth hanging from under the lip, over the painted ones.
                foreach (float side in new[] { -1f, 1f })
                    kit.Box(new Vector3(body.TeethAt.x + side * 0.024f * s, body.TeethAt.y - 0.045f * s, body.TeethAt.z + 0.004f * s),
                        new Vector3(0.044f, 0.095f, 0.022f) * s, new Color(0.98f, 0.97f, 0.9f), Quaternion.Euler(-8f, 0f, side * -4f));
            }

            Color hair = look.HairTint;
            switch (look.Face)
            {
                case FacialHair.Mustache:
                {
                    // A big handlebar under the nose, curling up at the ends.
                    float y = Mathf.Lerp(body.TeethAt.y, body.NoseTip.y, 0.35f);
                    float z = Mathf.Lerp(body.TeethAt.z, body.NoseTip.z, 0.45f) + 0.006f * s;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        kit.Ellipsoid(new Vector3(side * 0.042f * s, y, z), new Vector3(0.05f, 0.017f, 0.02f) * s, hair, Quaternion.Euler(0f, side * -18f, side * 12f));
                        kit.Ellipsoid(new Vector3(side * 0.09f * s, y + 0.014f * s, z - 0.014f * s), new Vector3(0.015f, 0.024f, 0.014f) * s, hair);
                    }
                    break;
                }
                case FacialHair.Beard:
                    kit.Ellipsoid(new Vector3(0f, body.TeethAt.y - 0.07f * s, body.TeethAt.z - r * 0.5f), new Vector3(r * 0.72f, r * 0.45f, r * 0.55f), hair);
                    break;
                case FacialHair.Stubble:
                    // A pointy little goatee on the chin, under the teeth (stubble doesn't read on a cartoon this size).
                    kit.Ellipsoid(new Vector3(0f, body.TeethAt.y - 0.13f * s, body.TeethAt.z - 0.035f * s), new Vector3(0.026f, 0.045f, 0.02f) * s, hair * 0.75f,
                        Quaternion.Euler(-15f, 0f, 0f));
                    break;
            }
            return kit.VertexCount > 0 ? kit.ToMesh("FunnyHeadwear", new[] { Matrix4x4.identity }) : null;
        }
    }
}
