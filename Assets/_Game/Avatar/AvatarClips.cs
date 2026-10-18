using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// Motion-captured animation clips (humanoid clips, e.g. from Mixamo, in Resources/Mixamo) on an
    /// <see cref="AvatarRig"/>. The code-built skeleton is described to Unity as a humanoid (its T-pose is the rest
    /// pose with the arms raised level to the sides), so one clip plays on every body: the tourists, the girls, the
    /// pirates, the players. <see cref="AvatarAnimator"/> samples the clips right after resetting the pose and keeps
    /// its own work on top (the head looking about, hands on items, the face).
    ///
    /// Locomotion for now: idle, walk and run, blended by speed, the walk and run kept in step with each other.
    /// </summary>
    public sealed class AvatarClips
    {
        /// <summary>Clips for everyone (off until they're proven: <c>clips on</c> in the console turns them on).</summary>
        public static bool Enabled;

        // The clips, by what they're for (Resources/Mixamo/<file>.fbx, imported as humanoid by Editor/MixamoImport).
        private const string IdleFile = "Breathing Idle", WalkFile = "Walking", RunFile = "Running";
        // How fast the person in each clip goes (m/s), so the steps match the ground going by.
        private const float WalkSpeed = 1.45f, RunSpeed = 3.9f;
        private static AnimationClip _idle, _walk, _run;
        private static bool _loaded;

        private readonly AvatarRig _rig;
        private Animator _animator;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idlePlay, _walkPlay, _runPlay;
        private int _builtFor = -1;
        private float _phase, _idleTime, _walkWeight, _runWeight;

        public AvatarClips(AvatarRig rig) => _rig = rig;

        /// <summary>The clips are in the build (the Mixamo files have been added).</summary>
        public static bool Available
        {
            get
            {
                LoadClips();
                return _idle != null && _walk != null && _run != null;
            }
        }

        /// <summary>The humanoid made for this rig (null until <see cref="Ready"/>).</summary>
        public UnityEngine.Avatar Avatar { get; private set; }

        public bool Ready => Avatar != null && _graph.IsValid();

        private static void LoadClips()
        {
            if (_loaded) return;
            _loaded = true;
            _idle = Load(IdleFile);
            _walk = Load(WalkFile);
            _run = Load(RunFile);
        }

        private static AnimationClip Load(string file)
        {
            // (An FBX holds its take and a "__preview__" copy: the real one.)
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>("Mixamo/" + file))
                if (clip != null && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        /// <summary>Builds the humanoid and the clip graph for the rig as it is now (again after a rebuild).</summary>
        public bool Prepare()
        {
            if (!_rig.IsBuilt || !Available) return false;
            if (_builtFor == _rig.BuildVersion && Ready) return true;
            Release();
            Avatar = BuildAvatar(_rig);
            if (Avatar == null || !Avatar.isValid || !Avatar.isHuman)
            {
                Debug.LogWarning($"[Clips] {_rig.name}: couldn't make a humanoid of this body");
                Avatar = null;
                return false;
            }
            _animator = _rig.GetComponent<Animator>();
            if (_animator == null) _animator = _rig.gameObject.AddComponent<Animator>();
            _animator.avatar = Avatar;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.runtimeAnimatorController = null;
            _animator.Rebind(); // (bind the new humanoid to the bones)

            _graph = PlayableGraph.Create("AvatarClips " + _rig.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); // (AvatarAnimator evaluates it, in its own order)
            var output = AnimationPlayableOutput.Create(_graph, "Body", _animator);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _idlePlay = Connect(_idle, 0);
            _walkPlay = Connect(_walk, 1);
            _runPlay = Connect(_run, 2);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _builtFor = _rig.BuildVersion;
            return true;
        }

        private AnimationClipPlayable Connect(AnimationClip clip, int input)
        {
            AnimationClipPlayable play = AnimationClipPlayable.Create(_graph, clip);
            play.SetApplyFootIK(true);
            play.Pause(); // (its time is set by hand below)
            _graph.Connect(play, 0, _mixer, input);
            return play;
        }

        /// <summary>
        /// Poses the body from the clips for this moment: standing still breathes (the idle), moving walks or runs
        /// at the pace of <paramref name="speed"/> (m/s over the ground).
        /// </summary>
        public void Evaluate(float speed, float dt)
        {
            if (!Ready) return;
            // How much walk and how much run: walking up to the walk's own pace, running from the run's.
            float moving = Mathf.Clamp01(speed / 0.35f);
            float run = Mathf.Clamp01((speed - WalkSpeed) / (RunSpeed - WalkSpeed));
            _walkWeight = Mathf.MoveTowards(_walkWeight, moving * (1f - run), dt * 6f);
            _runWeight = Mathf.MoveTowards(_runWeight, moving * run, dt * 6f);
            float idleWeight = Mathf.Max(0f, 1f - _walkWeight - _runWeight);
            _mixer.SetInputWeight(0, idleWeight);
            _mixer.SetInputWeight(1, _walkWeight);
            _mixer.SetInputWeight(2, _runWeight);

            // One step phase for the walk and the run (their feet land together whatever the mix), going round as
            // fast as the ground goes by: the walk's cycle at walking pace, the run's at running pace.
            float walkCycle = _walk.length, runCycle = _run.length;
            float cycle = Mathf.Lerp(walkCycle * WalkSpeed, runCycle * RunSpeed, run); // metres per cycle
            float rate = Mathf.Clamp(speed / Mathf.Max(0.1f, cycle), 0.25f / walkCycle, 2.2f / runCycle);
            if (_walkWeight + _runWeight > 0.001f) _phase = Mathf.Repeat(_phase + dt * rate, 1f);
            _idleTime += dt;
            _idlePlay.SetTime(_idleTime % _idle.length);
            _walkPlay.SetTime(_phase * walkCycle);
            _runPlay.SetTime(_phase * runCycle);
            _graph.Evaluate(0f);
        }

        public void Release()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (Avatar != null) Object.Destroy(Avatar);
            Avatar = null;
            _builtFor = -1;
        }

        // ------------------------------------------------------------------ the humanoid

        private static readonly (Bone bone, string human)[] HumanBones =
        {
            (Bone.Hips, "Hips"), (Bone.Spine, "Spine"), (Bone.Chest, "Chest"), (Bone.Neck, "Neck"), (Bone.Head, "Head"),
            (Bone.UpperArmL, "LeftUpperArm"), (Bone.ForearmL, "LeftLowerArm"), (Bone.HandL, "LeftHand"),
            (Bone.UpperArmR, "RightUpperArm"), (Bone.ForearmR, "RightLowerArm"), (Bone.HandR, "RightHand"),
            (Bone.ThighL, "LeftUpperLeg"), (Bone.ShinL, "LeftLowerLeg"), (Bone.FootL, "LeftFoot"),
            (Bone.ThighR, "RightUpperLeg"), (Bone.ShinR, "RightLowerLeg"), (Bone.FootR, "RightFoot"),
        };

        /// <summary>
        /// The rig as a Unity humanoid: its bones named for Unity's, and the T-pose they stand in (the rest pose, every
        /// limb straight; the arms lifted level out to the sides).
        /// </summary>
        public static UnityEngine.Avatar BuildAvatar(AvatarRig rig)
        {
            Transform root = rig.transform;
            Vector3 position = root.position;
            Quaternion rotation = root.rotation;
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.ResetPose();
            foreach (float side in new[] { -1f, 1f })
            {
                bool left = side < 0f;
                Vector3 outward = Vector3.right * side;
                foreach (Bone b in left ? new[] { Bone.UpperArmL, Bone.ForearmL, Bone.HandL } : new[] { Bone.UpperArmR, Bone.ForearmR, Bone.HandR })
                {
                    Transform t = rig[b];
                    if (t != null) t.rotation = Quaternion.FromToRotation(-t.up, outward) * t.rotation; // (limbs point down their -Y)
                }
                // Palms down, thumbs forward, as a humanoid's T-pose has them: each hand turned about the arm until the
                // line across its knuckles (little finger to index) points ahead (left as they hang, the palms came out
                // of every clip turned 80 degrees forward).
                Transform hand = rig[left ? Bone.HandL : Bone.HandR];
                HandBones fingers = rig.Hand(!left);
                if (hand != null && fingers != null)
                {
                    Transform index = fingers.Bones[HandBones.BoneIndex(1, 0)], pinky = fingers.Bones[HandBones.BoneIndex(4, 0)];
                    Vector3 across = Vector3.ProjectOnPlane(index.position - pinky.position, outward);
                    if (across.sqrMagnitude > 1e-8f)
                        hand.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(across, Vector3.forward, outward), outward) * hand.rotation;
                }
            }

            // Every transform from the root down to the body's bones (the ones the humanoid needs, and their parents).
            var skeleton = new List<SkeletonBone> { Skeleton(root) };
            var seen = new HashSet<Transform> { root };
            foreach (var (bone, _) in HumanBones)
                for (Transform t = rig[bone]; t != null && t != root; t = t.parent)
                    if (seen.Add(t)) skeleton.Add(Skeleton(t));

            var human = new List<HumanBone>();
            foreach (var (bone, name) in HumanBones)
            {
                if (rig[bone] == null) continue;
                var hb = new HumanBone { boneName = rig[bone].name, humanName = name };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
            };
            UnityEngine.Avatar avatar = AvatarBuilder.BuildHumanAvatar(root.gameObject, description);
            avatar.name = "Humanoid " + rig.name;
            rig.ResetPose();
            root.SetPositionAndRotation(position, rotation);
            return avatar;
        }

        private static SkeletonBone Skeleton(Transform t) => new()
        {
            name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale
        };
    }
}
