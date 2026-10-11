using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Sleeping pirates (host), island 3's night arrival: they lie snoring ("z z Z") and see nothing, but they hear.
    /// Running past within a few metres, walking right beside them, a fight or a suppressed shot nearby stirs them (the
    /// "?" over the head fills as they stir); woken, they sit up and look about toward the noise for a few seconds with
    /// sleepy eyes. If they see you: up on their feet, "INTRUDER!", the alarm (a stealth mission restarts). If not,
    /// they grumble and go back to sleep. A loud shot anywhere near wakes them straight into the alarm.
    /// Night guards walk their beat with a lantern (a light every machine draws) and see less far than by day.
    /// </summary>
    public partial class StoryNpc
    {
        private enum SleepState : byte { Asleep, Stirred }

        private const float WakeLookSeconds = 7f, DrowsyDecay = 0.25f, RunStir = 0.35f, WalkStir = 0.2f;
        private static readonly string[] WakeLines = { "Hm? Who's there?", "Wha... whassat?", "Eh? Somebody there?" };
        private static readonly string[] BackToSleep = { "...must've dreamt it.", "Bah. Rats.", "*yawn* ...five more minutes." };

        /// <summary>How a pirate who just woke up sees (squinting in the dark).</summary>
        public static SightSettings Sleepy => new()
        {
            Range = 10f, HalfAngle = 60f, CloseRange = 2.5f, FillPerSecond = 0.9f, DecayPerSecond = 0.2f,
            DisguiseFactor = 0.05f, SuspiciousAt = 0.3f
        };

        /// <summary>A guard at night: the lantern lights a few metres; past that only movement close by gives you away.</summary>
        public static SightSettings NightGuard => new()
        {
            Range = 13f, HalfAngle = 50f, CloseRange = 3f, FillPerSecond = 0.75f, DecayPerSecond = 0.2f,
            DisguiseFactor = 0.08f, SuspiciousAt = 0.3f
        };

        private readonly SyncVar<bool> _lantern = new SyncVar<bool>();
        private bool _asleep;
        private SleepState _sleep;
        private float _sleepUntil, _drowsy, _nextSnore, _sleepYaw;
        private Vector3 _sleepSpot;
        private GameObject _lanternView;

        /// <summary>Host and every machine (pose): fast asleep or just stirred, not yet on the alarm.</summary>
        public bool IsSleeper => _asleep;
        /// <summary>Host (tests): fast asleep right now.</summary>
        public bool IsFastAsleep => _asleep && _sleep == SleepState.Asleep;

        // ------------------------------------------------------------------ host API

        /// <summary>
        /// Host: lie down asleep here. Woken and sure of you, they raise the alarm and (with <paramref name="fighter"/>) fight.
        /// </summary>
        [Server]
        public void ServerSleep(FighterProfile? fighter = null)
        {
            ServerGuard(new[] { transform.position }, fighter, Sleepy);
            _armed.Value = false; // the cutlass lies beside him, not in his hand
            _asleep = true;
            _sleepSpot = transform.position;
            _sleepYaw = transform.eulerAngles.y;
            FallAsleep();
        }

        /// <summary>Host: carry a lit lantern (a night guard).</summary>
        [Server]
        public void ServerSetLantern(bool on) => _lantern.Value = on;

        private void FallAsleep()
        {
            _sleep = SleepState.Asleep;
            _drowsy = 0f;
            _eyes?.Reset();
            _alert.Value = 0;
            _pose.Value = AvatarPose.Lie;
            _mood.Value = AvatarMood.Neutral;
            ServerStop();
            transform.SetPositionAndRotation(_sleepSpot, Quaternion.Euler(0f, _sleepYaw, 0f));
            _nextSnore = Time.time + Random.Range(0.5f, 3f);
        }

        /// <summary>Host (from ServerHear): a sleeper hearing something. True if handled (asleep), false to listen as a guard.</summary>
        private bool SleepHear(Vector3 at, NoiseKind kind, PlayerHub by)
        {
            if (!_asleep || _sleep != SleepState.Asleep) return false;
            float distance = Vector3.Distance(at, transform.position);
            switch (kind)
            {
                case NoiseKind.Gunshot when distance > Stealth.SuppressedShot + 0.5f:
                case NoiseKind.Alarm:
                    // Straight up on his feet; the guard's own rule then raises the alarm.
                    _asleep = false;
                    _pose.Value = AvatarPose.Normal;
                    return false;
                case NoiseKind.Gunshot:
                case NoiseKind.Fight:
                    Wake(at, by);
                    _eyes.Notice(at, 0.45f, by);
                    return true;
                default: // footsteps: pile up while someone keeps making noise nearby
                    if (Stealth.IsDisguised(by)) return true;
                    _drowsy += distance > Stealth.Walking + 0.5f ? RunStir : WalkStir;
                    if (_drowsy >= 1f)
                    {
                        Wake(at, by);
                        _eyes.Notice(at, 0.2f, by);
                    }
                    return true;
            }
        }

        private void Wake(Vector3 at, PlayerHub by)
        {
            if (_sleep == SleepState.Stirred) return;
            _sleep = SleepState.Stirred;
            _sleepUntil = Time.time + WakeLookSeconds;
            _pose.Value = AvatarPose.Sit;
            _mood.Value = AvatarMood.Scared;
            _lookAroundYaw = Quaternion.LookRotation(Flat(at - transform.position, transform.forward)).eulerAngles.y;
            ShoutObservers(WakeLines[Random.Range(0, WakeLines.Length)], false);
            Debug.Log($"[Stealth] {Name} woke up{(by != null ? " (" + by.DisplayName + ")" : "")}");
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : fallback;
        }

        // ------------------------------------------------------------------ thinking (host, from BrainUpdate)

        /// <summary>Host: one step of sleeping. False once they're up on the alarm (the guard brain takes over).</summary>
        private bool SleepThink(float dt)
        {
            if (!_asleep) return false;
            if (IsDefeated)
            {
                _asleep = false;
                ServerStopGuarding();
                return true;
            }
            if (_sleep == SleepState.Asleep)
            {
                _drowsy = Mathf.Max(0f, _drowsy - DrowsyDecay * dt);
                // Stirring shows: the "?" ring fills a little as the noise builds.
                byte shown = (byte)Mathf.RoundToInt(_drowsy * 28f);
                if (Mathf.Abs(shown - _alert.Value) >= 3 || (shown == 0) != (_alert.Value == 0)) _alert.Value = shown;
                if (Time.time >= _nextSnore)
                {
                    _nextSnore = Time.time + Random.Range(2.6f, 4.2f);
                    SnoreObservers();
                }
                return true;
            }

            // Sitting up, looking about toward the noise, then round a little each way.
            Vector3 look = Quaternion.Euler(0f, _lookAroundYaw + Mathf.Sin(Time.time * 0.9f) * 55f, 0f) * Vector3.forward;
            if (_eyes.Interest.HasValue && Time.time - _eyes.InterestAt < 2f) look = Flat(_eyes.Interest.Value - transform.position, look);
            TurnToward(look, 90f, dt);
            if (_eyes.Tick(HeadPosition, transform.forward, dt))
            {
                // Sure: on his feet and shouting.
                _asleep = false;
                _pose.Value = AvatarPose.Normal;
                Alarm(_eyes.Suspect, _eyes.Interest ?? transform.position, "spotted you");
                return false;
            }
            byte suspicion = (byte)Mathf.RoundToInt(_eyes.Suspicion * 100f);
            if (Mathf.Abs(suspicion - _alert.Value) >= 3 || (suspicion == 0) != (_alert.Value == 0)) _alert.Value = suspicion;
            if (_eyes.Suspicion >= _eyes.Sight.SuspiciousAt) _sleepUntil = Mathf.Max(_sleepUntil, Time.time + 2f); // something's there: keep looking
            if (Time.time < _sleepUntil) return true;
            ShoutObservers(BackToSleep[Random.Range(0, BackToSleep.Length)], false);
            FallAsleep();
            return true;
        }

        // ------------------------------------------------------------------ every machine

        [ObserversRpc]
        private void SnoreObservers()
        {
            Camera cam = Camera.main;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 22f * 22f) return;
            FloatingText.Spawn(HeadPosition + Vector3.up * 0.95f, Random.value < 0.5f ? "z z Z" : "z Z z", new Color(0.7f, 0.82f, 1f), 0.7f, 1.8f);
        }

        private void LateUpdate() => UpdateLantern();

        /// <summary>Every machine: the lantern in a night guard's hand (a little warm light that walks with him).</summary>
        private void UpdateLantern()
        {
            bool want = _lantern.Value && !IsDefeated;
            if (!want)
            {
                if (_lanternView != null && _lanternView.activeSelf) _lanternView.SetActive(false);
                return;
            }
            if (_lanternView == null)
            {
                _lanternView = new GameObject("NightLantern");
                _lanternView.transform.SetParent(transform, false);
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(_lanternView.transform, false);
                box.transform.localScale = new Vector3(0.18f, 0.26f, 0.18f);
                var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { color = new Color(1f, 0.78f, 0.4f) };
                box.GetComponent<MeshRenderer>().sharedMaterial = glow;
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(_lanternView.transform, false);
                light.type = LightType.Point;
                light.range = 7f;
                light.intensity = 1.6f;
                light.color = new Color(1f, 0.72f, 0.4f);
                light.shadows = LightShadows.None;
            }
            if (!_lanternView.activeSelf) _lanternView.SetActive(true);
            // Held out at the right hip (the hand, when the body is built).
            Transform hand = _rig != null && _rig.IsBuilt ? _rig[AvatarRig.Bone.HandR] : null;
            Vector3 at = hand != null ? hand.position + Vector3.down * 0.2f : transform.position + transform.rotation * new Vector3(0.38f, 0.95f, 0.15f);
            _lanternView.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }
    }
}
