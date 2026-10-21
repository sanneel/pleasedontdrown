using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using UnityEngine;

namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// The physics body fitted to the drawn one, and kept out of the ground.
    ///
    /// The tourist prefab's colliders were one size for everybody (a 0.8 m stick per leg, a 0.6 m stick per arm, a
    /// 15 cm ball for the head). The modelled bodies are longer and rounder than that: lying on the sand their feet,
    /// hands, heads and hair went 14-21 cm into it. Once a body is built, every collider is measured off its own mesh
    /// (each vertex goes with the bone that moves it most), and the arm and leg joints move to where the drawn
    /// shoulders and hips are.
    ///
    /// A collider that starts under the ground's surface never comes back out (a mesh collider only pushes on its
    /// surface): tourists put down standing on the sand had their legs under it and sat buried to the hips. Every
    /// physics step a limb that reaches through the ground is turned back above it, and a torso cut by the ground is
    /// lifted onto it.
    /// </summary>
    public partial class VictimBody
    {
        private int _fitIn;
        private float _footGap;    // standing: how far the lower sole is above the ground (from the drawn feet)
        private float _heightTrim; // standing height correction that puts that sole on the ground
        private bool _standing;    // up on their feet (walking out, waving, heading off): the torso holds them up
        private readonly List<Collider> _groundIgnored = new();
        /// <summary>Torso centre above the ground with the feet just on it (standing, walking): from the fitted legs.</summary>
        private float _standHeight = 1.24f;
        private int _groundStep;
        private SphereCollider _headBall;
        private CapsuleCollider _torsoStick;
        private BoxCollider _hairBox;
        private static readonly Dictionary<Mesh, byte[]> DominantBones = new();
        private static readonly List<Vector3> FitVerts = new();
        private static readonly List<Vector2> FitUvs = new();
        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];
        private static Mesh _fitMesh;
        /// <summary>Every fitted collider is this much rounder than the skin it was measured on: the drawn body rests on
        /// the sand instead of dipping into it between the measured points.</summary>
        private const float SkinMargin = 0.012f;

        /// <summary>Tests: the fitted sizes, one line.</summary>
        public string FitReport { get; private set; } = "not fitted";

        private static byte[] Dominant(Mesh mesh)
        {
            if (DominantBones.TryGetValue(mesh, out byte[] bones)) return bones;
            BoneWeight[] w = mesh.boneWeights;
            bones = new byte[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                BoneWeight b = w[i];
                int best = b.boneIndex0;
                float most = b.weight0;
                if (b.weight1 > most) { most = b.weight1; best = b.boneIndex1; }
                if (b.weight2 > most) { most = b.weight2; best = b.boneIndex2; }
                if (b.weight3 > most) { best = b.boneIndex3; }
                bones[i] = (byte)best;
            }
            DominantBones[mesh] = bones;
            return bones;
        }

        private static float Percentile(List<float> values, float q)
        {
            if (values.Count == 0) return 0f;
            values.Sort();
            return values[Mathf.Clamp(Mathf.RoundToInt(q * (values.Count - 1)), 0, values.Count - 1)];
        }

        private void FitColliders()
        {
            SkinnedMeshRenderer r = _avatar != null ? _avatar.Renderer : null;
            if (r == null || r.sharedMesh == null) return;
            if (_headBall == null) _headBall = GetComponent<SphereCollider>();
            if (_torsoStick == null) _torsoStick = GetComponent<CapsuleCollider>();
            byte[] bone = Dominant(r.sharedMesh);
            if (_fitMesh == null) _fitMesh = new Mesh();
            r.BakeMesh(_fitMesh, true);
            _fitMesh.GetVertices(FitVerts);
            if (FitVerts.Count != bone.Length) return;
            Matrix4x4 toWorld = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
            var sb = new System.Text.StringBuilder();
            float standing = 0f;

            foreach (Limb limb in _limbs)
            {
                if (limb.Stick == null) continue;
                AvatarRig.Bone upper = limb.Kind switch
                {
                    LimbKind.ArmL => AvatarRig.Bone.UpperArmL, LimbKind.ArmR => AvatarRig.Bone.UpperArmR,
                    LimbKind.LegL => AvatarRig.Bone.ThighL, _ => AvatarRig.Bone.ThighR
                };
                int u = (int)upper, l = u + 1, end = u + 2; // upper, forearm / shin, hand / foot
                Transform root = _avatar[upper];
                if (root == null) continue;

                // The joint goes where the drawn shoulder or hip is.
                Vector3 anchor = transform.InverseTransformPoint(root.position);
                limb.Joint.connectedAnchor = anchor;
                Transform t = limb.Body.transform;
                t.position = transform.TransformPoint(anchor);
                limb.Body.position = t.position;

                var along = new List<float>();
                var upperPts = new List<Vector3>();
                var lowerPts = new List<Vector3>();
                Vector3 endSum = Vector3.zero;
                int endCount = 0;
                var endPoints = new List<Vector3>();
                for (int i = 0; i < FitVerts.Count; i++)
                {
                    int b = bone[i];
                    if (b != u && b != l && b != end) continue;
                    Vector3 p = t.InverseTransformPoint(toWorld.MultiplyPoint3x4(FitVerts[i]));
                    along.Add(-p.y);
                    if (b == end)
                    {
                        endSum += p;
                        endCount++;
                        endPoints.Add(p);
                    }
                    else if (b == u) upperPts.Add(p);
                    else lowerPts.Add(p);
                }
                if (upperPts.Count < 20 || lowerPts.Count < 20 || endCount < 10) continue;
                float reach = Percentile(along, 0.995f);
                // Thigh and shin (upper arm and forearm) each get their own thickness: one stick as thick as the
                // thigh held the calves and ankles off the sand.
                float radius = FitSegment(upperPts, limb.Stick);
                float lowerRadius = FitSegment(lowerPts, limb.Lower);
                Vector3 endCentre = endSum / endCount;
                // The hand or foot: a capsule along its longest side (heel to toe, wrist to fingertips).
                var ex = new List<float>(endPoints.Count);
                var ey = new List<float>(endPoints.Count);
                var ez = new List<float>(endPoints.Count);
                foreach (Vector3 p in endPoints) { ex.Add(p.x); ey.Add(p.y); ez.Add(p.z); }
                Vector3 lo = new(Percentile(ex, 0.005f), Percentile(ey, 0.005f), Percentile(ez, 0.005f));
                Vector3 hi = new(Percentile(ex, 0.995f), Percentile(ey, 0.995f), Percentile(ez, 0.995f));
                Vector3 size = hi - lo;
                int dir = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Vector3 mid = (lo + hi) * 0.5f;
                var endDistance = new List<float>(endPoints.Count);
                foreach (Vector3 p in endPoints)
                {
                    Vector3 off = p - mid;
                    off[dir] = 0f;
                    endDistance.Add(off.magnitude);
                }
                float endRadius = Mathf.Clamp(Percentile(endDistance, 0.97f), 0.025f, 0.1f) + 0.006f; // (thin: a standing foot rests on it)
                float endLength = Mathf.Max(size[dir] + endRadius, 2f * endRadius);

                limb.End.direction = dir;
                limb.End.radius = endRadius;
                limb.End.height = endLength;
                limb.End.center = mid;
                limb.Reach = reach;
                Transform foot = _avatar[(AvatarRig.Bone)end];
                limb.SoleBelowEnd = foot != null ? reach + t.InverseTransformPoint(foot.position).y : 0.08f;
                if (!limb.IsArm) standing = Mathf.Max(standing, reach - anchor.y);
                sb.Append($"{limb.Kind} reach {reach:F2} r {radius:F3}/{lowerRadius:F3} end {endRadius:F3}x{endLength:F2}  ");
            }

            // Head (and the hair, weighted to it) above the neck: one ball. The torso: a capsule round the rest.
            Transform neck = _avatar[AvatarRig.Bone.Neck];
            var head = new List<Vector3>();
            var headHair = new List<bool>();
            r.sharedMesh.GetUVs(0, FitUvs);
            List<Vector2> uvs = FitUvs;
            bool hairStrip = HasHairStrip(r.sharedMesh, bone, uvs);
            var torso = new List<Vector3>();
            float neckY = neck != null ? transform.InverseTransformPoint(neck.position).y : _headLocal.y - 0.15f;
            for (int i = 0; i < FitVerts.Count; i++)
            {
                var b = (AvatarRig.Bone)bone[i];
                bool limbVertex = b is >= AvatarRig.Bone.UpperArmL and <= AvatarRig.Bone.FootR;
                if (limbVertex) continue;
                bool headVertex = b is AvatarRig.Bone.Neck or AvatarRig.Bone.Head or (>= AvatarRig.Bone.EyeL and <= AvatarRig.Bone.BrowR);
                Vector3 p = transform.InverseTransformPoint(toWorld.MultiplyPoint3x4(FitVerts[i]));
                bool headBone = b is AvatarRig.Bone.Head or (>= AvatarRig.Bone.EyeL and <= AvatarRig.Bone.BrowR);
                if (headBone && p.y > neckY - 0.05f) { head.Add(p); headHair.Add(hairStrip && IsHairUv(uvs[i])); }
                else if (!headVertex && !(hairStrip && IsHairUv(uvs[i]))) torso.Add(p); // (not the hair down the back)
            }
            // The head: a ball round the skull and face, plus a thin layer of hair. Hair further out (a ponytail, curls,
            // hair down the back) doesn't hold the body up: lying on it, the girl floated a hand's width over the sand.
            // (The girls' hair has its own corner of the texture, below u 0.25 and v 0.2: that tells it from skin.)
            if (_headBall != null && head.Count > 50)
            {
                var skull = new List<Vector3>(head.Count);
                for (int i = 0; i < head.Count; i++)
                    if (!headHair[i]) skull.Add(head[i]);
                List<Vector3> fit = skull.Count > 50 ? skull : head;
                Vector3 c = Vector3.zero;
                foreach (Vector3 p in fit) c += p;
                c /= fit.Count;
                var d = new List<float>(fit.Count);
                foreach (Vector3 p in fit) d.Add((p - c).magnitude);
                // Round the skull plus a 5 cm layer of hair. Thicker hair (curls, a ponytail, hair hanging behind the
                // nape) rests a little into the sand: a ball round all of it held the body up off the ground.
                float hairLayer = skull.Count > 50 && skull.Count < head.Count ? 0.05f : 0f;
                float radius = Percentile(d, fit == skull ? 0.95f : 0.9f) + hairLayer;
                _headBall.center = c;
                _headBall.radius = Mathf.Clamp(radius, 0.08f, 0.26f) + SkinMargin;
                sb.Append($"head r {_headBall.radius:F3} at {c.y:F2}{(hairLayer > 0f ? " (skull)" : "")}  ");
            }
            if (_torsoStick != null && torso.Count > 100)
            {
                var ys = new List<float>(torso.Count);
                var xs = new List<float>(torso.Count);
                var zs = new List<float>(torso.Count);
                foreach (Vector3 p in torso) { ys.Add(p.y); xs.Add(p.x); zs.Add(p.z); }
                float top = Percentile(ys, 0.99f), bottom = Percentile(ys, 0.01f);
                float midZ = (Percentile(zs, 0.98f) + Percentile(zs, 0.02f)) * 0.5f;
                var depth = new List<float>(torso.Count);
                foreach (Vector3 p in torso) depth.Add(Mathf.Abs(p.z - midZ));
                // Round to the depth of the body (it lies on its back or front): the hips and shoulders are wider,
                // but a capsule that wide would hold the chest off the sand.
                float radius = Mathf.Clamp(Percentile(depth, 0.97f), 0.1f, 0.32f) + SkinMargin;
                _torsoStick.direction = 1;
                _torsoStick.radius = radius;
                _torsoStick.height = Mathf.Max(top - bottom, 2f * radius);
                _torsoStick.center = new Vector3(0f, (top + bottom) * 0.5f, midZ);
                sb.Append($"torso r {radius:F3} {bottom:F2}..{top:F2}");
            }
            if (standing > 0.6f) _standHeight = standing;
            sb.Append($"  stands {_standHeight:F2}");
            Physics.SyncTransforms();
            FitReport = sb.ToString();
        }

        private static bool IsHairUv(Vector2 uv) => uv.x < 0.25f && uv.y < 0.2f;

        /// <summary>A girl made by attach_hair.py: her hair in its corner of the texture, none of her arms or legs there.
        /// (Hair down the back is weighted to the chest, so only the limbs tell.)</summary>
        private static bool HasHairStrip(Mesh mesh, byte[] bone, List<Vector2> uvs)
        {
            if (uvs.Count != bone.Length) return false;
            int headIn = 0, head = 0, limbIn = 0, limbs = 0;
            for (int i = 0; i < uvs.Count; i++)
            {
                bool inStrip = IsHairUv(uvs[i]);
                var b = (AvatarRig.Bone)bone[i];
                if (b == AvatarRig.Bone.Head) { head++; if (inStrip) headIn++; }
                else if (b is >= AvatarRig.Bone.UpperArmL and <= AvatarRig.Bone.FootR) { limbs++; if (inStrip) limbIn++; }
            }
            return head > 0 && headIn > head / 4 && limbIn <= limbs / 100;
        }

        /// <summary>A capsule along the limb (its -Y) round these points, in the limb's own space; returns its radius.</summary>
        private static float FitSegment(List<Vector3> points, CapsuleCollider capsule)
        {
            var ys = new List<float>(points.Count);
            Vector2 mid = Vector2.zero;
            foreach (Vector3 p in points) { ys.Add(-p.y); mid += new Vector2(p.x, p.z); }
            mid /= points.Count;
            var round = new List<float>(points.Count);
            foreach (Vector3 p in points) round.Add((new Vector2(p.x, p.z) - mid).magnitude);
            float r = Mathf.Clamp(Percentile(round, 0.88f), 0.025f, 0.14f) + SkinMargin;
            float lo = Percentile(ys, 0.01f), hi = Percentile(ys, 0.99f);
            if (capsule == null) return r;
            capsule.direction = 1;
            capsule.radius = r;
            capsule.height = Mathf.Max(hi - lo + r, 2f * r);
            capsule.center = new Vector3(mid.x, -(lo + hi) * 0.5f, mid.y);
            return r;
        }

        /// <summary>The nearest static (no rigidbody) surface along a ray, or false.</summary>
        private static bool StaticHit(Vector3 from, Vector3 direction, float distance, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(from, direction, GroundHits, distance, ~0, QueryTriggerInteraction.Ignore);
            hit = default;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (GroundHits[i].collider.attachedRigidbody != null || GroundHits[i].distance >= best) continue;
                best = GroundHits[i].distance;
                hit = GroundHits[i];
            }
            return best < float.MaxValue;
        }

        /// <summary>Every other physics step: no part of this body inside the ground (or a wall, a deck, a rock).</summary>
        private void KeepOutOfGround()
        {
            if ((++_groundStep & 1) == 1) return;
            if (_rb.IsSleeping()) return;

            // The torso: the ground cutting through its lower half lifts it onto it.
            if (_torsoStick != null)
            {
                Vector3 centre = transform.TransformPoint(_torsoStick.center);
                float half = Mathf.Max(0f, _torsoStick.height * 0.5f - _torsoStick.radius);
                Vector3 axis = transform.up * half;
                float into = 0f;
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 point = centre + axis * e;
                    float bottom = point.y - _torsoStick.radius;
                    if (StaticHit(new Vector3(point.x, centre.y + 0.1f, point.z), Vector3.down, centre.y + 0.1f - bottom, out RaycastHit hit)
                        && hit.normal.y > 0.5f)
                        into = Mathf.Max(into, hit.point.y - bottom);
                }
                if (into > 0.01f)
                {
                    _rb.position += Vector3.up * into;
                    Vector3 v = _rb.linearVelocity;
                    if (v.y < 0f) _rb.linearVelocity = new Vector3(v.x, 0f, v.z);
                }
            }

            // Arms and legs: one that reaches through a surface is turned back out of it.
            foreach (Limb limb in _limbs)
            {
                Rigidbody body = limb.Body;
                if (body == null || body.isKinematic) continue;
                if (_standing && !limb.IsArm) continue; // (standing legs pass through the ground: see UpdateStanding)
                Transform t = body.transform;
                Vector3 joint = t.position;
                Vector3 down = -t.up;
                // (Reach is to the very end of the skin: the sole of a standing foot is just on the sand, not in it.)
                const float clear = 0.01f;
                if (!StaticHit(joint, down, limb.Reach, out RaycastHit hit)) continue;
                Vector3 tip = joint + down * limb.Reach;
                Vector3 n = hit.normal;
                float under = -Vector3.Dot(tip - hit.point, n);
                if (under <= clear) continue;
                // Swing the limb toward the surface's plane, keeping the way it points along the surface, just far
                // enough that its end clears it. (Moving the end straight out of the surface instead flipped a leg
                // stuck straight down into the sand up into the air.)
                Vector3 flat = down - n * Vector3.Dot(down, n);
                if (flat.sqrMagnitude < 0.01f)
                {
                    Vector3 toward = limb.IsArm ? transform.right * limb.Side : -transform.up; // out to the side / toward the feet
                    flat = toward - n * Vector3.Dot(toward, n);
                    if (flat.sqrMagnitude < 0.01f) flat = Vector3.Cross(n, transform.right);
                }
                flat.Normalize();
                float height = Vector3.Dot(joint - hit.point, n);       // the joint above the surface
                float sin = Mathf.Clamp(-height / limb.Reach, -1f, 1f); // the end on it
                Vector3 to = flat * Mathf.Sqrt(1f - sin * sin) + n * sin;
                if (Vector3.Dot(to, down) > 0.9999f) continue;
                // (A few degrees a step: turning a leg stuck in the sand all at once flipped the whole body over.)
                Quaternion turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(down, to), 8f);
                body.rotation = turn * body.rotation;
                t.rotation = body.rotation;
                Vector3 w = body.angularVelocity;
                body.angularVelocity = w * 0.5f;
                Vector3 lv = body.linearVelocity;
                float inward = Vector3.Dot(lv, hit.normal);
                if (inward < 0f) body.linearVelocity = lv - hit.normal * inward;
            }
        }

        /// <summary>LateUpdate, upright: where the drawn feet are (the knee bend and the stride shorten the legs).</summary>
        private void MeasureFeet()
        {
            float lowest = float.MaxValue;
            foreach (Limb limb in _limbs)
            {
                if (limb.IsArm) continue;
                Transform foot = _avatar[limb.Kind == LimbKind.LegL ? AvatarRig.Bone.FootL : AvatarRig.Bone.FootR];
                if (foot != null) lowest = Mathf.Min(lowest, foot.position.y - limb.SoleBelowEnd);
            }
            _footGap = lowest < float.MaxValue ? lowest - _groundY - 0.015f : 0f; // (1.5 cm up: the toes of a swinging foot)
        }

        /// <summary>Standing and walking: the torso goes down (or up) until the lower sole is on the sand.</summary>
        private float StandHeight()
        {
            _heightTrim = Mathf.Clamp(_heightTrim - _footGap * 6f * Time.fixedDeltaTime, -0.2f, 0.06f);
            return _standHeight + _heightTrim;
        }

        /// <summary>
        /// Standing and walking, the torso is held up and the legs only swing (as they always have): the leg colliders
        /// let the ground under them through, or their feet dragged in the sand and tipped the walker over forward.
        /// Lying down again they collide with it as before (and KeepOutOfGround turns them back out of it).
        /// </summary>
        private void UpdateStanding(bool standing)
        {
            _standing = standing;
            if (standing)
            {
                if (!StaticHit(_rb.position + Vector3.up * 0.2f, Vector3.down, 4f, out RaycastHit hit)) return;
                if (_groundIgnored.Contains(hit.collider)) return;
                _groundIgnored.Add(hit.collider);
                foreach (Limb limb in _limbs)
                    if (!limb.IsArm)
                    {
                        if (limb.Stick != null) Physics.IgnoreCollision(limb.Stick, hit.collider, true);
                        if (limb.Lower != null) Physics.IgnoreCollision(limb.Lower, hit.collider, true);
                        if (limb.End != null) Physics.IgnoreCollision(limb.End, hit.collider, true);
                    }
                return;
            }
            if (_groundIgnored.Count == 0) return;
            foreach (Collider ground in _groundIgnored)
                if (ground != null)
                    foreach (Limb limb in _limbs)
                        if (!limb.IsArm)
                        {
                            if (limb.Stick != null) Physics.IgnoreCollision(limb.Stick, ground, false);
                            if (limb.Lower != null) Physics.IgnoreCollision(limb.Lower, ground, false);
                            if (limb.End != null) Physics.IgnoreCollision(limb.End, ground, false);
                        }
            _groundIgnored.Clear();
            _heightTrim = 0f;
        }
    }
}
