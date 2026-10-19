using PleaseDontDrown.Audio;
using PleaseDontDrown.Player;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// The face (2026-10-19): blinking, the mouth with the words, the moods, the brows and the eyes looking about,
    /// worked out every frame from what every copy of the game already knows (the mood, talking, the pose and
    /// gestures, where the players are), so nothing about faces goes over the network. Shown by
    /// <see cref="AvatarRig.SetFace"/> (a generated body's living face, the goofy lifeguard's googly lids).
    /// </summary>
    public partial class AvatarAnimator
    {
        private FaceState _face;
        private float _blinkStart = -10f, _nextBlinkAt;
        private Vector2 _gaze, _gazeAim;
        private float _nextSaccade;
        private float _syllableStart, _syllableEnd, _syllableOpen;
        private int _syllablesLeft;
        private SpeechVoice _voice;
        private float _voiceLookedFor = -10f;
        private bool _voiceHeard;
        private float _emphasis, _nextEmphasis;

        /// <summary>The face shown last frame, blink and gaze included (tests read it).</summary>
        public FaceState Face { get; private set; }

        /// <summary>Times this face has blinked (tests: blinking happens on every copy of the game).</summary>
        public int Blinks { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterFaceCommand()
        {
            Core.DevCommands.Register("faces", "[metres]", "Print the faces of everyone near (lids, jaw, mood, blinks, gaze).", args =>
            {
                float reach = args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 8f;
                Camera view = Camera.main;
                Vector3 from = view != null ? view.transform.position : Vector3.zero;
                foreach (AvatarAnimator a in FindObjectsByType<AvatarAnimator>(FindObjectsSortMode.None))
                {
                    if ((a.transform.position - from).sqrMagnitude > reach * reach) continue;
                    FaceState f = a.Face;
                    var rr = a._rig != null ? a._rig.Renderer : null;
                    Core.DevCommands.Print($"[Face] {a.transform.root.name}/{a.name} body {(a._rig != null && a._rig.GeneratedBody != null ? a._rig.GeneratedBody.name : "code")} renderer {(rr != null ? $"{rr.enabled} vis {rr.isVisible} {rr.bounds.center} mesh {(rr.sharedMesh != null ? rr.sharedMesh.name : "none")}" : "none")}");
                    Core.DevCommands.Print($"[Face] {a.name}: mood {a.Motion.Mood} talking {a.Motion.Talking} lids {f.LidL:0.00}/{f.LidR:0.00} low {f.LowL:0.00} " +
                                           $"jaw {f.Jaw:0.00} smile {f.Smile:0.00} brows {f.BrowUp:0.00}/{f.BrowAngry:0.00}/{f.BrowSad:0.00} gaze {f.Gaze} blinks {a.Blinks}");
                }
            });
        }

        private void PoseFace()
        {
            float t = Now, dt = Mathf.Clamp(_dt, 1e-4f, 0.1f);
            AvatarMood mood = Motion.Mood;
            var want = new FaceState { LidL = 0.06f, LidR = 0.06f };

            // Moods, and what the body is doing.
            switch (mood)
            {
                case AvatarMood.Happy:
                    want.Smile = 1f; want.LowL = want.LowR = 0.25f; want.LidL = want.LidR = 0.06f; want.BrowUp = 0.25f;
                    break;
                case AvatarMood.Scared:
                    want.LidL = want.LidR = 0f; want.BrowUp = 1f; want.BrowSad = 0.7f; want.Jaw = 0.35f; want.Frown = 0.3f;
                    break;
                case AvatarMood.Angry:
                    want.LidL = want.LidR = 0.35f; want.LowL = want.LowR = 0.18f; want.BrowAngry = 1f; want.Frown = 0.5f;
                    break;
                case AvatarMood.Hurt:
                    want.LidL = want.LidR = 0.5f; want.LowL = want.LowR = 0.25f; want.BrowSad = 1f; want.Frown = 0.7f; want.Jaw = 0.3f;
                    break;
            }
            want.BrowAngry = Mathf.Max(want.BrowAngry, _charge * 0.8f);
            want.BrowSad = Mathf.Max(want.BrowSad, _cpr * 0.45f);
            want.Jaw = Mathf.Max(want.Jaw, _run * 0.25f * _move); // out of breath
            if (_eat > 0.5f) want.Jaw = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.55f; // chewing
            if (Motion.Underwater)
            {
                want.LidL = want.LidR = Mathf.Max(want.LidL, 0.45f); // squinting
                want.Jaw = 0f;                                        // holding the breath
            }
            if (Motion.Talking) Talk(t, ref want, mood);
            else { _voiceHeard = false; _syllablesLeft = 0; }

            bool shut = false;
            if (_down > 0.5f) { want = new FaceState { Jaw = 0.3f }; shut = true; }                             // out cold
            else if (_lie > 0.5f && !Motion.Talking) { want = new FaceState { Smile = 0.4f }; shut = true; }       // soaking up the sun
            if (GestureActive(AvatarGesture.Breath, 1.1f)) { want = new FaceState { BrowUp = 0.3f }; shut = true; } // a kiss of life: eyes shut, lips together
            else if (GestureActive(AvatarGesture.Burp, BurpSeconds))
            {
                float b = GestureT(BurpSeconds) * BurpSeconds;
                if (b < 0.25f) { want.Jaw = 0f; want.BrowUp = 0.5f; }                                     // holding it in
                else if (b < 1.05f) { want.Jaw = 0.75f + 0.12f * Mathf.Sin(t * 37f); want.LidL = want.LidR = 0.6f; want.BrowSad = 0.4f; }
                else { want.Jaw = 0.1f; want.BrowUp = 0.6f; want.LidL = want.LidR = 0f; }                  // ...pardon me
            }
            if (shut) want.LidL = want.LidR = want.LowL = want.LowR = 1f;

            // Everything eases over (a tenth of a second); the jaw is quicker, with the words.
            float k = 1f - Mathf.Exp(-12f * dt), kJaw = 1f - Mathf.Exp((want.Jaw > _face.Jaw ? -32f : -20f) * dt);
            _face.LidL = Mathf.Lerp(_face.LidL, want.LidL, k);
            _face.LidR = Mathf.Lerp(_face.LidR, want.LidR, k);
            _face.LowL = Mathf.Lerp(_face.LowL, want.LowL, k);
            _face.LowR = Mathf.Lerp(_face.LowR, want.LowR, k);
            _face.Jaw = Mathf.Lerp(_face.Jaw, want.Jaw, kJaw);
            _face.Smile = Mathf.Lerp(_face.Smile, want.Smile, k);
            _face.Frown = Mathf.Lerp(_face.Frown, want.Frown, k);
            _face.BrowUp = Mathf.Lerp(_face.BrowUp, want.BrowUp, k);
            _face.BrowAngry = Mathf.Lerp(_face.BrowAngry, want.BrowAngry, k);
            _face.BrowSad = Mathf.Lerp(_face.BrowSad, want.BrowSad, k);

            // Blinking: about every 2-5 s (scared: every second or so), now and then twice; 0.11 s, quicker shut than open.
            if (t >= _nextBlinkAt || _nextBlinkAt - t > 8f)
            {
                _blinkStart = t;
                Blinks++;
                _nextBlinkAt = t + (mood == AvatarMood.Scared ? Random.Range(0.6f, 1.8f) : Random.Range(2f, 5.5f));
                if (Random.value < 0.12f) _nextBlinkAt = t + 0.25f; // a double blink
            }
            float since = t - _blinkStart;
            float blink = since < 0.035f ? since / 0.035f : since < 0.05f ? 1f : since < 0.11f ? 1f - (since - 0.05f) / 0.06f : 0f;
            FaceState shown = _face;
            shown.LidL = Mathf.Max(shown.LidL, blink);
            shown.LidR = Mathf.Max(shown.LidR, blink);
            shown.LowL = Mathf.Max(shown.LowL, blink);
            shown.LowR = Mathf.Max(shown.LowR, blink);
            shown.Gaze = Look(t, dt, mood, shut || Motion.Underwater);
            Face = shown;
            _rig.SetFace(shown);
        }

        /// <summary>
        /// The mouth with the words: with the voice when it is heard here (how loud it is right now), else a babble of
        /// syllables and little pauses. Now and then the brows go up on a word.
        /// </summary>
        private void Talk(float t, ref FaceState want, AvatarMood mood)
        {
            if (_voice == null && t - _voiceLookedFor > 1f)
            {
                _voiceLookedFor = t;
                _voice = GetComponentInParent<SpeechVoice>();
            }
            float loud = _voice != null ? _voice.Loudness : -1f;
            float open;
            if (loud >= 0f)
            {
                _voiceHeard = true;
                open = 0.05f + 0.6f * Mathf.Pow(loud, 0.75f);
            }
            else if (_voiceHeard)
            {
                open = 0f; // the line has been said
            }
            else
            {
                if (t >= _syllableEnd || _syllableEnd - t > 1f)
                {
                    _syllableStart = t;
                    if (_syllablesLeft <= 0)
                    {
                        _syllablesLeft = Random.Range(3, 9);
                        _syllableEnd = t + Random.Range(0.12f, 0.32f); // a breath between phrases
                        _syllableOpen = 0.03f;
                    }
                    else
                    {
                        _syllablesLeft--;
                        _syllableEnd = t + Random.Range(0.1f, 0.19f);
                        _syllableOpen = Random.Range(0.28f, 0.7f);
                    }
                }
                float u = Mathf.Clamp01((t - _syllableStart) / Mathf.Max(0.01f, _syllableEnd - _syllableStart));
                open = 0.04f + _syllableOpen * Mathf.Sin(u * Mathf.PI);
            }
            want.Jaw = Mathf.Max(want.Jaw * 0.5f, open);
            if (mood == AvatarMood.Neutral) want.Smile = Mathf.Max(want.Smile, 0.2f);
            if (t >= _nextEmphasis)
            {
                _nextEmphasis = t + Random.Range(1.2f, 3.5f);
                _emphasis = t;
            }
            float lift = Mathf.Clamp01(1f - Mathf.Abs(t - _emphasis - 0.25f) / 0.25f);
            want.BrowUp = Mathf.Max(want.BrowUp, lift * 0.55f);
        }

        /// <summary>
        /// Where the eyes look: at the nearest player close by in front (whoever they're talking to, most of the time),
        /// else little glances about (darting when scared), leading the head's own glances.
        /// </summary>
        private Vector2 Look(float t, float dt, AvatarMood mood, bool closed)
        {
            Transform head = _rig[Bone.Head];
            Vector3 eyes = (_rig[Bone.EyeL].position + _rig[Bone.EyeR].position) * 0.5f;
            Vector2 aim = _gazeAim;
            bool found = false;
            if (!closed && Application.isPlaying)
            {
                float best = 6f;
                foreach (PlayerHub player in PlayerHub.All)
                {
                    if (player == null || player.Head == null) continue;
                    Vector3 to = player.Head.position - eyes;
                    float distance = to.magnitude;
                    if (distance < 0.35f || distance >= best) continue; // (its own head)
                    Vector3 local = head.InverseTransformDirection(to / distance);
                    if (local.z < 0.3f) continue; // behind, or too far round
                    best = distance;
                    aim = new Vector2(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg / 30f, Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg / 35f, -0.4f, 0.4f)); // (up and down only a little: an iris up under the lid read as sleepy)
                    found = true;
                }
            }
            if (!found)
            {
                if (t >= _nextSaccade || _nextSaccade - t > 4f)
                {
                    bool darting = mood == AvatarMood.Scared;
                    _nextSaccade = t + (darting ? Random.Range(0.18f, 0.5f) : Random.Range(0.7f, 2.8f));
                    _gazeAim = Random.value < 0.4f && !darting ? Vector2.zero
                        : new Vector2(Random.Range(-1f, 1f) * (darting ? 0.9f : 0.5f), Random.Range(-0.35f, 0.25f));
                }
                aim = _gazeAim + new Vector2(Mathf.Clamp(_glance.x / 25f, -0.6f, 0.6f), 0f); // the eyes lead the head
            }
            if (closed) aim = Vector2.zero;
            aim = Vector2.ClampMagnitude(aim, 1.1f);
            _gaze = Vector2.Lerp(_gaze, aim, 1f - Mathf.Exp(-28f * dt)); // a quick flick
            return _gaze;
        }
    }
}
