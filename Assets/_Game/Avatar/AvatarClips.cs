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
    /// Three layers, mixed by weight:
    /// <list type="bullet">
    /// <item>Getting about: an idle, a walk and a run, blended by speed, the walk and run kept in step with each other.
    /// Which idle, walk and run is the character's own (<see cref="Style"/>): the women walk like women, the drunk
    /// staggers, tourists each stand about their own way.</item>
    /// <item>Talking: a talking clip in place of the idle while standing and talking (<see cref="Talk"/>).</item>
    /// <item>An action for the whole body (<see cref="Action"/>): kneeling for CPR, knocked out and getting up, treading
    /// water, terrified. The animator says which clip, how far into it, and how much of it shows; when the action
    /// changes the old one fades out under the new one.</item>
    /// </list>
    /// </summary>
    public sealed class AvatarClips
    {
        /// <summary>Clips for everyone in the game (<c>clips off</c> in the console turns them off). Editor tools keep
        /// the hand-made animation unless they ask for clips (<see cref="AvatarAnimator.UseClips"/>): outside play the
        /// graph isn't evaluated and the skin isn't redrawn.</summary>
        public static bool Enabled = true;

        // The clips, by what they're for (Resources/Mixamo/<file>.fbx, imported as humanoid by Editor/MixamoImport).
        public const string Idle = "Breathing Idle", Walk = "Walking", Run = "Running";

        // How fast the person in each walk and run goes (m/s), so the steps match the ground going by. (Measured on
        // the clips' own travel before they were made in place: Mixamo's walks cover 1.3-1.5 m a second.)
        private static readonly Dictionary<string, float> Pace = new()
        {
            { Walk, 1.45f }, { Run, 3.9f }, { "Female Walk", 1.3f }, { "Feminine Walk", 1.25f }, { "Drunk Walk", 0.9f },
        };

        private static readonly Dictionary<string, AnimationClip> _cache = new();

        /// <summary>A clip by its file name in Resources/Mixamo (null if it isn't there).</summary>
        public static AnimationClip Get(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (_cache.TryGetValue(file, out AnimationClip clip)) return clip;
            clip = null;
            // (An FBX holds its take and a "__preview__" copy: the real one. "<file> Mirror": its mirrored copy, the
            // other hand, made by Editor.MixamoImport for the punches.)
            bool mirror = file.EndsWith(" Mirror");
            string path = mirror ? file.Substring(0, file.Length - " Mirror".Length) : file;
            foreach (AnimationClip c in Resources.LoadAll<AnimationClip>("Mixamo/" + path))
                if (c != null && !c.name.StartsWith("__preview__") && c.name.EndsWith(" Mirror") == mirror) { clip = c; break; }
            _cache[file] = clip;
            return clip;
        }

        public static float Length(string file) => Get(file) is { } c ? c.length : 0f;

        /// <summary>The basic clips are in the build (the Mixamo files have been added).</summary>
        public static bool Available => Get(Idle) != null && Get(Walk) != null && Get(Run) != null;

        /// <summary>One character's own way of standing and getting about (clip files; a missing one falls back to the
        /// basic idle, walk and run).</summary>
        public struct Style
        {
            public string Idle, Walk, Run;
            public static readonly Style Basic = new() { Idle = AvatarClips.Idle, Walk = AvatarClips.Walk, Run = AvatarClips.Run };
        }

        private readonly AvatarRig _rig;
        private Animator _animator;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private int _builtFor = -1;
        private float _phase, _idleTime, _talkTime, _walkWeight, _runWeight;

        // Mixer inputs.
        private const int SlotIdle = 0, SlotWalk = 1, SlotRun = 2, SlotTalk = 3, SlotAct = 4, SlotOut = 5, SlotProbe = 6, Slots = 7;
        private readonly AnimationClipPlayable[] _play = new AnimationClipPlayable[Slots];
        private readonly AnimationClip[] _clipOf = new AnimationClip[Slots];

        private Style _style = Style.Basic;
        private string _talkFile;
        private float _talkWeight;
        private string _actFile;
        private float _actTime, _actWeight;
        private Vector3 _actOffset;
        private float _actYaw;
        private float _outTime, _outWeight;
        private Vector3 _outOffset;
        private float _outYaw;
        private readonly Dictionary<(string, int), (Vector3, Quaternion)> _hipsCache = new();

        public AvatarClips(AvatarRig rig) => _rig = rig;

        /// <summary>The humanoid made for this rig (null until <see cref="Ready"/>).</summary>
        public UnityEngine.Avatar Avatar { get; private set; }

        public bool Ready => Avatar != null && _graph.IsValid();

        /// <summary>The action playing now (null: none), for tests.</summary>
        public string CurrentAction => _actWeight > 0.01f ? _actFile : null;

        /// <summary>This character's idle, walk and run from now on.</summary>
        public void SetStyle(Style style)
        {
            style.Idle = Get(style.Idle) != null ? style.Idle : Idle;
            style.Walk = Get(style.Walk) != null ? style.Walk : Walk;
            style.Run = Get(style.Run) != null ? style.Run : Run;
            _style = style;
            if (!Ready) return;
            SetSlot(SlotIdle, Get(_style.Idle));
            SetSlot(SlotWalk, Get(_style.Walk));
            SetSlot(SlotRun, Get(_style.Run));
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
            _mixer = AnimationMixerPlayable.Create(_graph, Slots);
            output.SetSourcePlayable(_mixer);
            for (int i = 0; i < Slots; i++) _clipOf[i] = null;
            SetSlot(SlotIdle, Get(_style.Idle));
            SetSlot(SlotWalk, Get(_style.Walk));
            SetSlot(SlotRun, Get(_style.Run));
            SetSlot(SlotTalk, Get(_talkFile));
            SetSlot(SlotAct, Get(_actFile));
            _outWeight = 0f;
            _hipsCache.Clear();
            _graph.Play();
            _builtFor = _rig.BuildVersion;
            return true;
        }

        /// <summary>Puts a clip on a mixer input (or clears it), its time to be set by hand.</summary>
        private void SetSlot(int slot, AnimationClip clip)
        {
            if (!_graph.IsValid()) return;
            if (_clipOf[slot] == clip && (clip == null || _play[slot].IsValid())) return;
            if (_play[slot].IsValid())
            {
                _graph.Disconnect(_mixer, slot);
                _play[slot].Destroy();
            }
            _play[slot] = default;
            _clipOf[slot] = clip;
            if (clip == null) return;
            AnimationClipPlayable play = AnimationClipPlayable.Create(_graph, clip);
            play.SetApplyFootIK(true);
            play.Pause(); // (its time is set by hand)
            _graph.Connect(play, 0, _mixer, slot);
            _mixer.SetInputWeight(slot, 0f);
            _play[slot] = play;
        }

        private void SetTime(int slot, float time)
        {
            if (_play[slot].IsValid()) _play[slot].SetTime(time);
        }

        /// <summary>Talking while standing: this clip (looped on its own clock) in place of the idle, this much.</summary>
        public void Talk(string file, float weight)
        {
            if (file != _talkFile)
            {
                _talkFile = file;
                _talkTime = 0f;
                SetSlot(SlotTalk, Get(file));
            }
            _talkWeight = Get(file) != null ? Mathf.Clamp01(weight) : 0f;
        }

        /// <summary>
        /// The whole body in an action this frame: <paramref name="file"/> at <paramref name="time"/> seconds (looping
        /// clips wrap), showing <paramref name="weight"/> of it over getting about; <paramref name="offset"/> moves its
        /// body (rig space) so it lines up with the one before. Null or weight 0: no action.
        /// </summary>
        public void Action(string file, float time, float weight, Vector3 offset = default, float yaw = 0f)
        {
            AnimationClip clip = Get(file);
            if (clip == null) { file = null; weight = 0f; }
            if (file != _actFile)
            {
                // The old action fades out under the new one, frozen where it was.
                if (_actWeight > 0.02f && _actFile != null)
                {
                    SetSlot(SlotOut, Get(_actFile));
                    _outTime = _actTime;
                    _outWeight = _actWeight;
                    _outOffset = _actOffset;
                    _outYaw = _actYaw;
                }
                _actFile = file;
                SetSlot(SlotAct, clip);
            }
            _actTime = time;
            _actWeight = Mathf.Clamp01(weight);
            _actOffset = offset;
            _actYaw = yaw;
        }

        /// <summary>
        /// Where the hips are (rig space: metres, +z ahead) at a moment of a clip played alone on this body. For lining
        /// up actions (a fall ends lying where the get-up starts). Measured once per clip and moment.
        /// </summary>
        public Vector3 HipsAt(string file, float time) => HipsPoseAt(file, time).position;

        /// <summary>As <see cref="HipsAt"/>, with the way the hips are turned (rig space).</summary>
        public (Vector3 position, Quaternion rotation) HipsPoseAt(string file, float time)
        {
            if (!Ready || Get(file) == null) return (Vector3.zero, Quaternion.identity);
            var key = (file, Mathf.RoundToInt(time * 100f));
            if (_hipsCache.TryGetValue(key, out var hips)) return hips;
            SetSlot(SlotProbe, Get(file));
            for (int i = 0; i < Slots; i++) _mixer.SetInputWeight(i, i == SlotProbe ? 1f : 0f);
            SetTime(SlotProbe, ClipTime(Get(file), time));
            _graph.Evaluate(0f);
            Transform bone = _rig[Bone.Hips];
            hips = (_rig.transform.InverseTransformPoint(bone.position), Quaternion.Inverse(_rig.transform.rotation) * bone.rotation);
            _mixer.SetInputWeight(SlotProbe, 0f);
            _hipsCache[key] = hips;
            return hips;
        }

        /// <summary>
        /// Which way a body with its hips turned so faces (degrees about up, 0 = +z): standing, the way the hips face;
        /// lying on the back, away from the head (the way it faces once it sits up).
        /// </summary>
        public static float FacingYaw(Quaternion hips)
        {
            Vector3 f = hips * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.25f)
            {
                f = -(hips * Vector3.up);
                f.y = 0f;
            }
            return f.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Poses the body from the clips for this moment: standing still breathes (the idle) or talks, moving walks or
        /// runs at the pace of <paramref name="speed"/> (m/s over the ground); an action on top takes its share.
        /// </summary>
        public void Evaluate(float speed, float dt)
        {
            if (!Ready) return;
            AnimationClip walk = _clipOf[SlotWalk], run = _clipOf[SlotRun], idle = _clipOf[SlotIdle];
            if (walk == null || run == null || idle == null) return;
            float walkPace = Pace.TryGetValue(_style.Walk, out float wp) ? wp : 1.4f;
            float runPace = Pace.TryGetValue(_style.Run, out float rp) ? rp : 3.9f;

            // How much walk and how much run: walking up to the walk's own pace, running from the run's.
            float moving = Mathf.Clamp01(speed / 0.35f);
            float run01 = Mathf.Clamp01((speed - walkPace) / Mathf.Max(0.1f, runPace - walkPace));
            _walkWeight = Mathf.MoveTowards(_walkWeight, moving * (1f - run01), dt * 6f);
            _runWeight = Mathf.MoveTowards(_runWeight, moving * run01, dt * 6f);
            float still = Mathf.Max(0f, 1f - _walkWeight - _runWeight);

            // The action takes its share first, then the one fading out, then getting about has the rest.
            _outWeight = Mathf.MoveTowards(_outWeight, 0f, dt * 4f);
            float act = _actWeight;
            float outW = _outWeight * (1f - act);
            float rest = Mathf.Max(0f, 1f - act - outW);
            float talk = _talkWeight * (_clipOf[SlotTalk] != null ? 1f : 0f);
            _mixer.SetInputWeight(SlotIdle, still * (1f - talk) * rest);
            _mixer.SetInputWeight(SlotTalk, still * talk * rest);
            _mixer.SetInputWeight(SlotWalk, _walkWeight * rest);
            _mixer.SetInputWeight(SlotRun, _runWeight * rest);
            _mixer.SetInputWeight(SlotAct, _clipOf[SlotAct] != null ? act : 0f);
            _mixer.SetInputWeight(SlotOut, _clipOf[SlotOut] != null ? outW : 0f);
            _mixer.SetInputWeight(SlotProbe, 0f);

            // One step phase for the walk and the run (their feet land together whatever the mix), going round as
            // fast as the ground goes by: the walk's cycle at walking pace, the run's at running pace.
            float walkCycle = walk.length, runCycle = run.length;
            float cycle = Mathf.Lerp(walkCycle * walkPace, runCycle * runPace, run01); // metres per cycle
            float rate = Mathf.Clamp(speed / Mathf.Max(0.1f, cycle), 0.25f / walkCycle, 2.2f / runCycle);
            if (_walkWeight + _runWeight > 0.001f) _phase = Mathf.Repeat(_phase + dt * rate, 1f);
            _idleTime += dt;
            _talkTime += dt;
            SetTime(SlotIdle, _idleTime % idle.length);
            if (_clipOf[SlotTalk] != null) SetTime(SlotTalk, _talkTime % _clipOf[SlotTalk].length);
            SetTime(SlotWalk, _phase * walkCycle);
            SetTime(SlotRun, _phase * runCycle);
            if (_clipOf[SlotAct] != null) SetTime(SlotAct, ClipTime(_clipOf[SlotAct], _actTime));
            if (_clipOf[SlotOut] != null) SetTime(SlotOut, ClipTime(_clipOf[SlotOut], _outTime));
            _graph.Evaluate(0f);

            // Line the action's body up (rig space), as much as it shows: turned about the root, then moved.
            float yaw = _actYaw * act + _outYaw * outW;
            Vector3 offset = _actOffset * act + _outOffset * outW;
            Transform hips = _rig[Bone.Hips];
            if (Mathf.Abs(yaw) > 0.01f)
            {
                Quaternion turn = Quaternion.AngleAxis(yaw, _rig.transform.up);
                Vector3 root = _rig.transform.position;
                hips.SetPositionAndRotation(root + turn * (hips.position - root), turn * hips.rotation);
            }
            if (offset.sqrMagnitude > 1e-8f) hips.position += _rig.transform.TransformVector(offset);
        }

        /// <summary>A looping clip wraps; one played once holds its last frame.</summary>
        private static float ClipTime(AnimationClip clip, float time) =>
            clip.isLooping ? Mathf.Repeat(time, clip.length) : Mathf.Clamp(time, 0f, clip.length);

        public void Release()
        {
            if (_graph.IsValid()) _graph.Destroy();
            for (int i = 0; i < Slots; i++)
            {
                _play[i] = default;
                _clipOf[i] = null;
            }
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

        /// <summary>The bones a clip poses (for blending a clip pose with the hand-made one).</summary>
        public static readonly Bone[] PosedBones =
        {
            Bone.Hips, Bone.Spine, Bone.Chest, Bone.Neck, Bone.Head, Bone.UpperArmL, Bone.ForearmL, Bone.HandL,
            Bone.UpperArmR, Bone.ForearmR, Bone.HandR, Bone.ThighL, Bone.ShinL, Bone.FootL, Bone.ThighR, Bone.ShinR, Bone.FootR,
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
            // (The root at its own origin: its local position is relative to its parent, and a character standing on a
            // raised floor, the hotel porch 1.8 m up, came out with the clips' body that far under its feet.)
            var skeleton = new List<SkeletonBone> { new() { name = root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one } };
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
