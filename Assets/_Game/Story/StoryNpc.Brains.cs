using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Host-side behaviour per role:
    ///  * Robber: when told to flee, keeps running away from the nearest lifeguard inside an area (land only),
    ///    picking open routes, taunting now and then. Hits stagger him; the story decides what happens when he's down.
    ///  * Pirate: goes for the nearest lifeguard and punches (knockback, nobody dies); otherwise marches on the hotel.
    ///  * Bystander: stands at the water's edge pointing and yelling for help.
    /// Guides and receptionists just stand, face you and talk (the story drives them).
    /// </summary>
    public partial class StoryNpc
    {
        private static readonly string[] Taunts = { "Can't catch me!", "Too slow, lifeguard!", "Hah!", "Nope!", "Catch me if you can!" };
        private static readonly string[] PirateShouts = { "ARRR!", "Hand over the loot!", "Yarr!", "Get 'em, lads!" };

        private readonly RaycastHit[] _brainHits = new RaycastHit[8];
        private float _nextThink;
        private Vector3 _home;
        private Rect _area = new Rect(-40f, 5f, 80f, 40f);
        private bool _fleeing;
        private bool _aggressive;
        private float _nextAttack;
        private float _nextShout;
        private string _shoutText;
        private Vector3 _shoutAt;
        private float _shoutInterval = 2.6f;

        private void ServerSetupBrain()
        {
            _home = transform.position;
            _fleeing = _aggressive = false;
            _shoutText = null;
        }

        /// <summary>Host (robber): run from the lifeguards inside this x/z area.</summary>
        [Server]
        public void ServerFlee(bool flee, Rect area)
        {
            _fleeing = flee;
            _area = area;
            _nextThink = 0f;
            if (!flee) _moveTarget = null;
        }

        /// <summary>Host (pirate): attack lifeguards; with nobody around, head for <paramref name="home"/>.</summary>
        [Server]
        public void ServerAggressive(bool aggressive, Vector3 home)
        {
            _aggressive = aggressive;
            _home = home;
        }

        /// <summary>Host (bystander): keep shouting this, facing <paramref name="at"/>. Null text stops.</summary>
        [Server]
        public void ServerKeepShouting(string text, Vector3 at, float interval = 2.6f)
        {
            _shoutText = text;
            _shoutAt = at;
            _shoutInterval = interval;
            _nextShout = 0f;
            _facePoint = at;
        }

        private void OnServerHit(PlayerHub attacker)
        {
            _nextThink = 0f; // react right away (robber: run; pirate: turn on whoever hit us)
            if (_role.Value == NpcRole.Robber && _health.Value > 0 && Random.value < 0.4f)
                ShoutObservers(_health.Value == 1 ? "OK OK, ENOUGH!" : "Hey, ow!", false);
        }

        private void BrainUpdate(float dt)
        {
            if (!string.IsNullOrEmpty(_shoutText) && Time.time >= _nextShout)
            {
                _nextShout = Time.time + _shoutInterval;
                ShoutObservers(_shoutText, true);
                GestureObservers(AvatarGesture.Wave, _shoutAt);
            }
            if (IsDefeated || _pose.Value != AvatarPose.Normal || Time.time < _nextThink) return;

            switch (_role.Value)
            {
                case NpcRole.Robber when _fleeing:
                    _nextThink = Time.time + 0.35f;
                    RobberThink();
                    break;
                case NpcRole.Pirate when _aggressive:
                    _nextThink = Time.time + 0.15f;
                    PirateThink();
                    break;
            }
        }

        private void RobberThink()
        {
            Vector3 p = transform.position;
            PlayerHub chaser = NearestPlayer(30f);
            if (chaser == null)
            {
                // Nobody after him: jog about the area.
                if (!_moveTarget.HasValue && TryOpenPoint(p, Random.insideUnitCircle.normalized, 8f, out Vector3 wander))
                    ServerMoveTo(wander, run: false);
                return;
            }

            Vector3 away = p - chaser.transform.position;
            away.y = 0f;
            float close = away.magnitude;
            if (close < 7f && Random.value < 0.06f) ShoutObservers(Taunts[Random.Range(0, Taunts.Length)], false);
            // Only re-plan when the current leg is nearly done or the chaser is closing in.
            if (_moveTarget.HasValue && (_moveTarget.Value - p).sqrMagnitude > 4f && close > 5f) return;

            Vector3 best = p;
            float bestScore = float.MinValue;
            Vector3 dirAway = close > 0.01f ? away / close : Vector3.forward;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f + Random.Range(-10f, 10f);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                if (!TryOpenPoint(p, new Vector2(dir.x, dir.z), 7f, out Vector3 candidate)) continue;
                float fromChaser = Vector3.Distance(candidate, chaser.transform.position);
                // Run away, but prefer carrying on roughly the same way (no silly zig-zags into the chaser's arms).
                float score = fromChaser + Vector3.Dot(dir, dirAway) * 2f + Random.value * 1.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (bestScore > float.MinValue) ServerMoveTo(best, run: true);
        }

        /// <summary>A point <paramref name="distance"/> m away that is on land, inside the area and not behind a wall.</summary>
        private bool TryOpenPoint(Vector3 from, Vector2 direction, float distance, out Vector3 point)
        {
            point = from + new Vector3(direction.x, 0f, direction.y) * distance;
            if (!_area.Contains(new Vector2(point.x, point.z))) return false;
            if (Shore.WaterDepthAt(point + Vector3.up * 0.2f) > 0.2f) return false;
            float ground = Shore.GroundHeightAt(point + Vector3.up * 3f);
            if (float.IsNaN(ground) || Mathf.Abs(ground - from.y) > 2f) return false;
            point.y = ground;
            Vector3 a = from + Vector3.up * 0.9f, b = point + Vector3.up * 0.9f;
            int count = Physics.RaycastNonAlloc(a, (b - a).normalized, _brainHits, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (_brainHits[i].collider.attachedRigidbody == null) return false; // a wall, a palm, the shack
            return true;
        }

        private void PirateThink()
        {
            Vector3 p = transform.position;
            PlayerHub target = NearestPlayer(35f);
            if (target == null)
            {
                if ((p - _home).sqrMagnitude > 4f && !_moveTarget.HasValue) ServerMoveTo(_home, run: false);
                return;
            }
            Vector3 to = target.transform.position - p;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > 1.35f)
            {
                ServerMoveTo(target.transform.position - to.normalized * 1.1f, run: distance > 4f);
                if (Random.value < 0.01f) ShoutObservers(PirateShouts[Random.Range(0, PirateShouts.Length)], false);
                return;
            }
            _moveTarget = null;
            _facePoint = target.transform.position;
            if (Time.time < _nextAttack) return;
            _nextAttack = Time.time + Random.Range(1.1f, 1.6f);
            GestureObservers(AvatarGesture.Punch, target.Head.position);
            if (target.TryGetComponent(out PlayerCombat combat))
                combat.ServerKnockback(to.normalized * 6.5f + Vector3.up * 3f, Name);
        }
    }
}
