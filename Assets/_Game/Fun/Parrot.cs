using System.Collections.Generic;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// One of island 1's parrots (instead of seagulls). It sits on a palm crown or a roof, bobs, looks about and now
    /// and then squawks or says something cheeky over the beach. Walk up close and it takes off in a clatter of wings,
    /// circles high over the island for a while, then glides down to a free perch. Only for looks and laughs, so each
    /// machine flies its own parrots (no network traffic).
    /// </summary>
    public class Parrot : MonoBehaviour
    {
        [SerializeField] private ParrotFlock _flock;
        [SerializeField] private Transform _body, _wingL, _wingR;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private bool _startFlying;

        private static readonly string[] Lines =
        {
            "SQUAWK!", "PLEASE DON'T DROWN!", "Pretty lifeguard! Pretty lifeguard!", "BAWK! SHARK! SHARK!",
            "Who's a good lifeguard?", "Polly wants a sandwich!", "Mouth to beak? No thanks!", "Sandy's coming! Look busy!",
            "Ten bucks says he drowns.", "HELP! HELP! ...just kidding. SQUAWK!", "Nice shorts!", "Swim between the flags!"
        };

        private enum State { Perched, Flying, Landing }
        private State _state;
        private Transform _perch;
        private float _nextSay, _flyUntil, _angle, _radius, _height, _speed, _dir = 1f, _lookYaw, _lookTarget, _nextLook, _bobUntil;
        private Vector3 _center, _velocity;

        private const float ScareDistance = 3.5f, TalkHearing = 22f;

        private void Start()
        {
            _nextSay = Time.time + Random.Range(4f, 20f);
            _perch = _startFlying || _flock == null ? null : _flock.TakePerchAt(transform.position);
            if (_perch != null)
            {
                transform.SetPositionAndRotation(_perch.position, _perch.rotation);
                _state = State.Perched;
            }
            else TakeOff(false);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (_state)
            {
                case State.Perched: Perched(dt); break;
                case State.Flying: Flying(dt); break;
                case State.Landing: Landing(dt); break;
            }
            if (Time.time >= _nextSay) Say();
        }

        // ------------------------------------------------------------------ sitting

        private void Perched(float dt)
        {
            if (_perch == null) { TakeOff(false); return; }
            transform.position = _perch.position;
            Fold(dt);
            // Look about now and then; a quick head-bob dance once in a while.
            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + Random.Range(1.2f, 4f);
                _lookTarget = Random.Range(-70f, 70f);
                if (Random.value < 0.15f) _bobUntil = Time.time + 1.6f;
            }
            _lookYaw = Mathf.MoveTowardsAngle(_lookYaw, _lookTarget, 160f * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _perch.eulerAngles.y + _lookYaw, 0f), 1f - Mathf.Exp(-6f * dt));
            float bob = Time.time < _bobUntil ? Mathf.Abs(Mathf.Sin(Time.time * 14f)) * 0.06f : Mathf.Sin(Time.time * 2f) * 0.008f;
            if (_body != null) _body.localPosition = new Vector3(0f, bob, 0f);
            if (SomebodyNear(ScareDistance)) TakeOff(true);
        }

        // ------------------------------------------------------------------ flying

        private void TakeOff(bool scared)
        {
            if (_flock != null) _flock.Release(_perch);
            _perch = null;
            _state = State.Flying;
            _center = _flock != null ? _flock.SkyCentre : transform.position;
            _radius = Random.Range(9f, 22f);
            _height = Random.Range(10f, 15f);
            _speed = Random.Range(6.5f, 9f);
            _dir = Random.value < 0.5f ? -1f : 1f;
            Vector3 from = transform.position - _center;
            _angle = Mathf.Atan2(from.z, from.x);
            _flyUntil = Time.time + Random.Range(12f, 30f);
            _velocity = Vector3.up * 3f;
            if (scared)
            {
                Play(ParrotSounds.Flap, 0.9f);
                Play(ParrotSounds.Squawk, 1f);
                if (Random.value < 0.4f) Shout("BAWK!");
            }
        }

        private void Flying(float dt)
        {
            _angle += _dir * _speed / _radius * dt;
            Vector3 target = _center + new Vector3(Mathf.Cos(_angle) * _radius, _height + Mathf.Sin(Time.time * 0.6f + _radius) * 1.2f, Mathf.Sin(_angle) * _radius);
            Steer(target, _speed, dt);
            Flap(dt, 13f);
            if (Time.time >= _flyUntil && _flock != null)
            {
                _perch = _flock.TakePerch(null, transform.position);
                if (_perch != null) _state = State.Landing;
                else _flyUntil = Time.time + 8f;
            }
        }

        private void Landing(float dt)
        {
            if (_perch == null) { TakeOff(false); return; }
            Vector3 to = _perch.position - transform.position;
            float distance = to.magnitude;
            if (distance < 0.08f)
            {
                transform.position = _perch.position;
                _state = State.Perched;
                _lookYaw = _lookTarget = 0f;
                _nextLook = Time.time + 1f;
                return;
            }
            // Glide in: slow down on the final few metres, flap less.
            Steer(_perch.position, Mathf.Lerp(1.5f, 8f, Mathf.Clamp01(distance / 6f)), dt);
            Flap(dt, distance < 3f ? 18f : 9f);
            if (SomebodyNear(ScareDistance) && distance < 6f) TakeOff(true);
        }

        private void Steer(Vector3 target, float speed, float dt)
        {
            Vector3 want = target - transform.position;
            want = want.sqrMagnitude > 1e-4f ? want.normalized * Mathf.Min(speed, want.magnitude / Mathf.Max(dt, 1e-3f)) : Vector3.zero;
            _velocity = Vector3.MoveTowards(_velocity, want, 14f * dt);
            transform.position += _velocity * dt;
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            if (flat.sqrMagnitude > 0.05f)
            {
                float turn = Vector3.SignedAngle(transform.forward, flat, Vector3.up);
                float bank = Mathf.Clamp(-turn * 2f, -35f, 35f);
                Quaternion face = Quaternion.LookRotation(flat) * Quaternion.Euler(-Mathf.Clamp(_velocity.y * 6f, -30f, 30f), 0f, bank);
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 1f - Mathf.Exp(-5f * dt));
            }
        }

        private void Flap(float dt, float rate)
        {
            float a = Mathf.Sin(Time.time * rate) * 55f + 20f;
            if (_wingL != null) _wingL.localRotation = Quaternion.Euler(0f, 0f, -a - 60f);
            if (_wingR != null) _wingR.localRotation = Quaternion.Euler(0f, 0f, a + 60f);
            if (_body != null) _body.localPosition = Vector3.zero;
        }

        private void Fold(float dt)
        {
            float k = 1f - Mathf.Exp(-10f * dt);
            if (_wingL != null) _wingL.localRotation = Quaternion.Slerp(_wingL.localRotation, Quaternion.identity, k);
            if (_wingR != null) _wingR.localRotation = Quaternion.Slerp(_wingR.localRotation, Quaternion.identity, k);
        }

        // ------------------------------------------------------------------ chatter

        private void Say()
        {
            _nextSay = Time.time + Random.Range(10f, 28f);
            PlayerHub me = PlayerHub.Local;
            if (me == null || (me.transform.position - transform.position).sqrMagnitude > TalkHearing * TalkHearing) return;
            bool talk = _state == State.Perched && Random.value < 0.55f;
            Play(talk ? ParrotSounds.Talk : ParrotSounds.Squawk, talk ? 0.8f : 0.9f);
            Shout(talk ? Lines[Random.Range(1, Lines.Length)] : Lines[0]);
        }

        private void Shout(string line) =>
            FloatingText.Spawn(transform.position + Vector3.up * 0.8f, line, new Color(0.55f, 1f, 0.45f), 0.8f, 2.4f);

        private void Play(AudioClip clip, float volume)
        {
            if (_audio == null || clip == null) return;
            _audio.pitch = Random.Range(0.92f, 1.1f);
            _audio.PlayOneShot(clip, volume);
        }

        private bool SomebodyNear(float distance)
        {
            IReadOnlyList<PlayerHub> players = PlayerHub.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerHub p = players[i];
                if (p == null) continue;
                Vector3 d = p.transform.position + Vector3.up - transform.position;
                if (new Vector2(d.x, d.z).sqrMagnitude < distance * distance && Mathf.Abs(d.y) < distance + 1f) return true;
            }
            return false;
        }
    }
}
