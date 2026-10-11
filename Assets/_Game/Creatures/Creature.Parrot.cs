using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Parrot sentry (host), island 3's lookouts: sits on its perch looking about, then circles its patch of sky
    /// looking down, and back. Sharp eyes (<see cref="SightSettings.Parrot"/>); a pirate disguise fools it. If it is
    /// sure it squawks the alarm ("A parrot saw you!": the arrival mission restarts) and circles over you shrieking.
    /// A loud bang sends it up in a flap; one shot brings it down (a loud one is heard by everyone, of course).
    /// </summary>
    public partial class Creature : ISentry
    {
        private Perception _eyes;
        private float _flyAngle, _flyRadius, _flyHeight, _lookYaw;
        private bool _flying, _lookout;

        /// <summary>Host: a lookout stays on its perch, watching (it only takes off when a bang startles it).</summary>
        public void ServerLookout() => _lookout = true;

        public Vector3 SentryPosition => TopPosition;

        private void ParrotSetup()
        {
            _eyes = new Perception(SightSettings.Parrot, c => c.GetComponentInParent<Creature>() == this);
            _flyRadius = Random.Range(7f, 11f);
            _flyHeight = Random.Range(7f, 10f);
            _flyAngle = Random.Range(0f, 360f);
            _flying = false;
            SetAct(CreatureAct.Perch, Random.Range(6f, 12f));
            Stealth.Register(this);
        }

        public void ServerHear(Vector3 at, NoiseKind kind, PlayerHub by)
        {
            if (!IsServerInitialized || IsDead || _eyes == null || _eyes.Alarmed) return;
            if (_kind.Value != CreatureKind.Parrot) return;
            if (kind == NoiseKind.Gunshot && Vector3.Distance(at, TopPosition) > Stealth.SuppressedShot + 0.5f)
            {
                // A bang: up in a clatter of wings (and now it is looking for you).
                if (!_flying) ShoutObservers("BAWK!", false);
                _flying = true;
                SetAct(CreatureAct.Fly, Random.Range(10f, 16f));
                _eyes.Notice(at, 0.3f, by);
                return;
            }
            if (kind is NoiseKind.Gunshot or NoiseKind.Fight) _eyes.Notice(at, 0.4f, by);
            else if (kind == NoiseKind.Footsteps && !Stealth.IsDisguised(by)) _eyes.Notice(at, 0.08f, by);
        }

        private void ParrotThink(float dt)
        {
            Vector3 p = transform.position;
            Vector3 eyeForward;
            if (_eyes.Alarmed)
            {
                // Circle over whoever it caught, shrieking.
                PlayerHub suspect = _eyes.Suspect;
                Vector3 centre = suspect != null ? suspect.transform.position : _home;
                _flyAngle += 70f * dt;
                FlyTo(centre + Quaternion.Euler(0f, _flyAngle, 0f) * Vector3.forward * 4f + Vector3.up * 5f, 8f, dt);
                if (ActDone)
                {
                    SetAct(CreatureAct.Alarm, 2.2f);
                    ShoutObservers(Random.value < 0.5f ? "SQUAWK! INTRUDER!" : "BAWK! LANDLUBBER! LANDLUBBER!", true);
                }
                return;
            }

            if (_flying)
            {
                // Circling its patch, eyes down and ahead.
                _flyAngle += 360f / (2f * Mathf.PI * _flyRadius) * 6f * dt;
                FlyTo(_home + Quaternion.Euler(0f, _flyAngle, 0f) * Vector3.forward * _flyRadius + Vector3.up * _flyHeight, 6f, dt);
                eyeForward = (transform.forward + Vector3.down * 0.9f).normalized;
                if (ActDone)
                {
                    _flying = false;
                    SetAct(CreatureAct.Fly, 8f); // gliding home to the perch
                }
            }
            else
            {
                bool home = (p - _home).sqrMagnitude < 0.04f;
                if (!home)
                {
                    FlyTo(_home, 5f, dt);
                    if ((transform.position - _home).sqrMagnitude < 0.04f) { transform.position = _home; SetAct(CreatureAct.Perch, Random.Range(8f, 15f)); }
                }
                else
                {
                    if (_act.Value != CreatureAct.Perch) SetAct(CreatureAct.Perch, Random.Range(8f, 15f));
                    // Look about on the perch, a quick turn now and then.
                    if (Random.value < 0.6f * dt) _lookYaw = Random.Range(0f, 360f);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, _lookYaw, 0f), 140f * dt);
                    if (ActDone && !_lookout)
                    {
                        _flying = true;
                        SetAct(CreatureAct.Fly, Random.Range(12f, 20f));
                    }
                }
                eyeForward = (transform.forward + Vector3.down * 0.35f).normalized;
            }

            // Something half seen: it turns to stare at it, wherever it is.
            if (_eyes.Suspicion > _eyes.Sight.SuspiciousAt && _eyes.Interest.HasValue && !_flying)
            {
                Vector3 to = _eyes.Interest.Value - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 220f * dt);
                eyeForward = (_eyes.Interest.Value + Vector3.up - TopPosition).normalized;
            }

            if (_eyes.Tick(TopPosition, eyeForward, dt))
            {
                _alert.Value = StoryNpc.AlertAlarmed;
                SetAct(CreatureAct.Alarm, 2.2f);
                ShoutObservers("SQUAWK! INTRUDER! INTRUDER!", true);
                Stealth.RaiseSpotted("A parrot", _eyes.Suspect, "saw you");
                Stealth.ServerNoise(TopPosition, Stealth.AlarmShout, _eyes.Suspect, NoiseKind.Alarm);
                return;
            }
            byte shown = (byte)Mathf.RoundToInt(_eyes.Suspicion * 100f);
            if (Mathf.Abs(shown - _alert.Value) >= 3 || (shown == 0) != (_alert.Value == 0)) _alert.Value = shown;
        }

        private void FlyTo(Vector3 target, float speed, float dt)
        {
            Vector3 p = transform.position;
            Vector3 to = target - p;
            float distance = to.magnitude;
            if (distance < 0.01f) return;
            Vector3 step = to / distance * Mathf.Min(distance, speed * dt);
            transform.position = p + step;
            Vector3 flat = new Vector3(step.x, 0f, step.z);
            if (flat.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-6f * dt));
            if (_act.Value != CreatureAct.Fly && _act.Value != CreatureAct.Alarm && distance > 0.3f) _act.Value = CreatureAct.Fly;
        }

        private void ParrotHit(PlayerHub attacker) { }

        private void ParrotDied()
        {
            // Down it drops, onto the ground below.
            Vector3 p = transform.position;
            float ground = Shore.GroundHeightAt(p);
            if (!float.IsNaN(ground)) transform.position = new Vector3(p.x, ground + 0.05f, p.z);
        }
    }
}
