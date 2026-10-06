using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The pink-and-white beach hut on island 1 and its gag: a tourist you brought back with the kiss of life takes
    /// you by the hand and walks you in there. The door shuts; for you the screen goes dark, for everyone outside the
    /// hut wobbles and out come squeaky bed springs, "ooh"s, a boing and a cuckoo clock. Nothing is shown. The host
    /// runs the scene (StoryDirector.LoveHutScene); this is its stage: the leading by the hand, the door, putting the
    /// lifeguard inside and back out, the dark screen, the wobble and the sounds.
    /// </summary>
    public class LoveHut : NetworkBehaviour
    {
        [SerializeField] private Door _door;
        [SerializeField] private Transform _outside;   // on the sand in front of the door, facing the door
        [SerializeField] private Transform _inside;
        [SerializeField] private Transform _wobble;    // the hut model
        [SerializeField] private AudioSource _audio;

        public static LoveHut Instance { get; private set; }
        public Transform Outside => _outside;
        public Transform Inside => _inside;

        // Local: being led by the hand (this machine's lifeguard), and the dark screen.
        private static Transform _leader;
        private static float _dark, _darkTarget;
        private Quaternion _wobbleRest;
        private Vector3 _wobbleScale;
        private float _wobbleUntil;

        // Everyone: who is leading whom by the hand right now (both bodies show it).
        private static Transform _handLeader;
        private static PlayerHub _handHero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _leader = null;
            _handLeader = null;
            _handHero = null;
            _dark = _darkTarget = 0f;
        }

        private void Awake()
        {
            Instance = this;
            useGUILayout = false;
            if (_wobble != null)
            {
                _wobbleRest = _wobble.localRotation;
                _wobbleScale = _wobble.localScale;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ host side (the scene calls these)

        [Server] public void ServerDoor(bool open) => _door.ServerSet(open, _outside.position);

        [Server]
        public void ServerLead(PlayerHub who, StoryNpc by)
        {
            LeadTarget(who.Owner, by != null ? by.NetworkObject : null);
            HandsObservers(who != null ? who.NetworkObject : null, by != null ? by.NetworkObject : null);
        }

        [ObserversRpc(BufferLast = true)]
        private void HandsObservers(NetworkObject hero, NetworkObject leader)
        {
            _handHero = hero != null && leader != null ? hero.GetComponent<PlayerHub>() : null;
            _handLeader = _handHero != null ? leader.transform : null;
        }

        /// <summary>
        /// Hand in hand: where the two hands meet, for the leader (her hand reaching back) and the lifeguard being led
        /// (reaching forward to it). False when nobody is being led, or for anyone else.
        /// </summary>
        public static bool HandHold(Transform body, out HandGripPoint grip)
        {
            grip = default;
            if (_handLeader == null || _handHero == null || body == null || (body != _handLeader && body != _handHero.transform)) return false;
            Transform her = _handLeader, him = _handHero.transform;
            Vector3 herHand = her.position + her.right * 0.32f - her.forward * 0.28f + Vector3.up * 0.92f; // reaching back, low
            Vector3 hisHand = him.position + Vector3.up * 0.95f + Vector3.ProjectOnPlane(her.position - him.position, Vector3.up).normalized * 0.4f;
            Vector3 meet = Vector3.Lerp(herHand, hisHand, 0.5f);
            if ((meet - body.position).sqrMagnitude > 1.6f * 1.6f) return false; // too far apart to hold hands (catching up)
            Vector3 toOther = Vector3.ProjectOnPlane((body == her ? him.position : her.position) - body.position, Vector3.up);
            grip = new HandGripPoint { Point = meet, Toward = toOther.sqrMagnitude > 1e-4f ? toOther.normalized : body.forward };
            return true;
        }

        public struct HandGripPoint
        {
            public Vector3 Point;
            public Vector3 Toward;
        }

        [Server] public void ServerPut(PlayerHub who, Vector3 position, Vector3 lookAt) => PutTarget(who.Owner, position, lookAt);

        [Server] public void ServerDark(PlayerHub who, bool dark) => DarkTarget(who.Owner, dark);

        [Server] public void ServerRockAndRoll(float seconds) => RockObservers(seconds);

        // ------------------------------------------------------------------ the lifeguard's own machine

        [TargetRpc]
        private void LeadTarget(NetworkConnection target, NetworkObject leader)
        {
            _leader = leader != null ? leader.transform : null;
            if (_leader == null) return;
            PlayerHud.ShowToast("She's holding your hand... and she isn't letting go.", 3f);
            PlayerHub.Local?.Look?.LookAt(_leader.position + Vector3.up * 1.4f); // once, to see who's got you; then look about freely
        }

        [TargetRpc]
        private void PutTarget(NetworkConnection target, Vector3 position, Vector3 lookAt)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || me.Motor == null || me.Motor.Seat != null) return;
            me.Motor.Teleport(position);
            me.Look.LookAt(lookAt);
        }

        [TargetRpc]
        private void DarkTarget(NetworkConnection target, bool dark) => _darkTarget = dark ? 1f : 0f;

        private void FixedUpdate()
        {
            PlayerHub me = PlayerHub.Local;
            if (_leader == null || me == null || me.Motor == null || me.Motor.Seat != null) return;
            // Dragged along by the hand: a step behind her and to her right, no say in where the feet go. Pulled by
            // velocity (smooth, the camera doesn't judder) and the head is free: look wherever you like.
            Vector3 target = _leader.position - _leader.forward * 0.95f + _leader.right * 0.35f;
            Vector3 at = me.transform.position;
            Vector3 to = target - at;
            to.y = 0f;
            if (to.magnitude > 4f)
            {
                me.Motor.Teleport(new Vector3(target.x, Mathf.Max(at.y, _leader.position.y), target.z)); // fell far behind (a snag)
                return;
            }
            Vector3 pull = Vector3.ClampMagnitude(to * 4f, 3.4f);
            float climb = _leader.position.y - at.y; // up her steps
            if (climb > 0.04f) pull.y = Mathf.Min(climb * 8f, 3f);
            me.Motor.Drag(pull);
        }

        // ------------------------------------------------------------------ everyone: the wobble and the noises

        [ObserversRpc]
        private void RockObservers(float seconds)
        {
            _wobbleUntil = Time.time + seconds;
            StopAllCoroutines();
            StartCoroutine(Noises(seconds));
        }

        private IEnumerator Noises(float seconds)
        {
            if (_audio == null) yield break;
            float end = Time.time + seconds;
            yield return new WaitForSeconds(0.6f);
            int beat = 0;
            while (Time.time < end - 0.9f)
            {
                _audio.pitch = Random.Range(0.94f, 1.06f);
                _audio.PlayOneShot(FunSounds.Squeak, 0.9f);
                if (beat % 5 == 2) _audio.PlayOneShot(FunSounds.Ooh(true), 0.8f);
                if (beat % 7 == 4) _audio.PlayOneShot(FunSounds.Ooh(false), 0.8f);
                if (beat % 11 == 8) _audio.PlayOneShot(FunSounds.Boing, 0.7f);
                beat++;
                yield return new WaitForSeconds(Mathf.Lerp(0.5f, 0.28f, beat / 20f)); // faster and faster...
            }
            _audio.pitch = 1f;
            _audio.PlayOneShot(FunSounds.Cuckoo, 1f);
        }

        private void Update()
        {
            // The dark screen eases in and out.
            _dark = Mathf.MoveTowards(_dark, _darkTarget, Time.deltaTime * 1.5f);
            if (_wobble == null) return;
            if (Time.time < _wobbleUntil)
            {
                float t = Time.time * 9f;
                _wobble.localRotation = _wobbleRest * Quaternion.Euler(Mathf.Sin(t) * 1.6f, 0f, Mathf.Sin(t * 1.3f + 1f) * 2.2f);
                float bounce = 1f + Mathf.Abs(Mathf.Sin(t * 0.5f)) * 0.03f;
                _wobble.localScale = new Vector3(_wobbleScale.x / Mathf.Sqrt(bounce), _wobbleScale.y * bounce, _wobbleScale.z / Mathf.Sqrt(bounce));
            }
            else if (_wobble.localRotation != _wobbleRest)
            {
                _wobble.localRotation = _wobbleRest;
                _wobble.localScale = _wobbleScale;
            }
        }

        private void OnGUI()
        {
            if (_dark <= 0.001f) return;
            Hud.Fill(new Rect(0f, 0f, Hud.Width, Hud.Height), new Color(0f, 0f, 0f, _dark));
            if (_dark > 0.9f)
                Hud.Label(new Rect(0f, Hud.Height * 0.45f, Hud.Width, 80f), "*the door is shut. what happens in the hut stays in the hut.*", 30f,
                    new Color(1f, 0.85f, 0.9f, (_dark - 0.9f) * 10f));
        }
    }
}
