using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Old Snapper (host), the waterfall boss (plan, island 3 step 7): a giant alligator snapping turtle guarding the
    /// money cave. The shell takes next to nothing from any side; the head is tucked in until it attacks.
    ///  * Snap: it rears up, jaws wide (the cue), and bites ahead; after a bite it gasps with its head out: the stun
    ///    window, the head takes 2.5x.
    ///  * Phase 2 (60%): the slam: it heaves its front up and crashes down, knocking everyone near flat; on entering
    ///    the phase it knocks a pirate guard into the water (<see cref="ServerBossPhase"/>: the story drops someone
    ///    in trouble there to rescue mid-fight).
    ///  * Phase 3 (30%): faster cues, shorter openings, it comes after you harder.
    /// Health scales with the lifeguards (<see cref="BossFight.Scaled"/>).
    /// </summary>
    public partial class Creature
    {
        private readonly SyncVar<byte> _bossPhase = new SyncVar<byte>();
        private readonly SyncVar<bool> _bossOpen = new SyncVar<bool>();

        private BossFight _boss;
        private PlayerHub _bossTarget;
        private float _nextBossThink, _nextSlamAllowed;

        /// <summary>Host: a boss moved to a new phase (the story reacts: a pirate in the water...).</summary>
        public static event System.Action<Creature, int> ServerBossPhase;

        private readonly SyncVar<bool> _dormant = new SyncVar<bool>();
        private float _sink;

        public bool IsBoss => _kind.Value is CreatureKind.OldSnapper or CreatureKind.Kleptopus;
        /// <summary>Every machine: a boss asleep in its lair (low in the water, no bar, it ignores everyone) until woken.</summary>
        public bool IsDormant => _dormant.Value;

        /// <summary>Host: sleep in the lair (it lives here before its fight: it doesn't pop up out of nowhere).</summary>
        [Server]
        public void ServerSleep()
        {
            _dormant.Value = true;
            _boss?.Close();
            SetAct(CreatureAct.Idle, 1f);
        }

        /// <summary>Host: the fight starts: it rises out of the water and comes for them.</summary>
        [Server]
        public void ServerWake()
        {
            if (!_dormant.Value) return;
            _dormant.Value = false;
            _nextSlamAllowed = Time.time + 8f;
            SetAct(CreatureAct.Idle, 2.5f); // a moment to get out of the water before the first move
            RoarObservers(null);
        }
        /// <summary>Every machine: the boss phase (1..) and whether its weak spot is open right now.</summary>
        public int BossPhase => Mathf.Max(1, _bossPhase.Value);
        public int BossPhases => 3;
        public bool BossOpen => _bossOpen.Value;

        /// <summary>Every machine: the boss whose bar to show (alive, someone within 80 m), or null.</summary>
        public static Creature ActiveBoss
        {
            get
            {
                PlayerHub local = PlayerHub.Local;
                foreach (Creature c in _all)
                {
                    if (c == null || !c.IsBoss || c._maxHealth.Value <= 0 || c.IsDormant) continue;
                    if (local != null && (local.transform.position - c.transform.position).sqrMagnitude > 80f * 80f) continue;
                    return c;
                }
                return null;
            }
        }

        private void SnapperSetup()
        {
            _boss = new BossFight(1f, 0.6f, 0.3f);
            _boss.PhaseChanged += phase =>
            {
                _bossPhase.Value = (byte)phase;
                RoarObservers(phase == 2 ? "Old Snapper is furious!" : "Old Snapper goes berserk!");
                ServerBossPhase?.Invoke(this, phase);
            };
            _bossPhase.Value = 1;
            _bossOpen.Value = false;
            _nextSlamAllowed = Time.time + 8f;
            SetAct(CreatureAct.Idle, 2f);
            RoarObservers(null);
        }

        private void SnapperThink(float dt)
        {
            Vector3 p = transform.position;
            if (_dormant.Value)
            {
                if (Flat(p, _home) > 2f) MoveToward(_home, 1.2f, dt, 40f, water: true);
                if (_act.Value != CreatureAct.Idle) SetAct(CreatureAct.Idle, 1f);
                return;
            }
            bool open = _boss.Vulnerable;
            if (_bossOpen.Value != open) _bossOpen.Value = open;
            if (Time.time >= _nextBossThink)
            {
                _nextBossThink = Time.time + 0.4f;
                _bossTarget = NearestStanding(p, 45f);
            }
            PlayerHub t = _bossTarget;
            switch (_act.Value)
            {
                case CreatureAct.Idle:
                case CreatureAct.Move:
                {
                    if (t == null)
                    {
                        if (Flat(p, _home) > 2f) MoveToward(_home, 1.2f, dt, 40f, water: true);
                        break;
                    }
                    Vector3 to = t.transform.position - p;
                    to.y = 0f;
                    float distance = to.magnitude;
                    float angle = Vector3.Angle(transform.forward, to);
                    // Slam when crowded or when they're round its sides/back (phase 2 on).
                    if (_boss.Phase >= 2 && Time.time >= _nextSlamAllowed && (Crowd(p, 7.5f) >= 2 || (distance < 6.5f && angle > 70f)))
                    {
                        SetAct(CreatureAct.Rear, _boss.ByPhase(1f, 0.85f, 0.65f));
                        _slamNext = true;
                        ShoutObservers("*the ground shakes*", false);
                        break;
                    }
                    if (distance < 6f && angle < 28f)
                    {
                        SetAct(CreatureAct.Rear, _boss.ByPhase(0.95f, 0.75f, 0.55f));
                        _slamNext = false;
                        break;
                    }
                    // Lumber after them, turning slowly: flanks are the safe side.
                    SetAct(CreatureAct.Move, 0.3f);
                    MoveToward(t.transform.position, _boss.ByPhase(1.3f, 1.9f, 2.6f), dt, _boss.ByPhase(40f, 55f, 75f), water: true);
                    break;
                }
                case CreatureAct.Rear:
                    if (!_slamNext && t != null) Turn(t.transform.position - p, 30f, dt); // a little tracking, not much
                    if (!ActDone) break;
                    if (_slamNext) Slam(p);
                    else Bite(p);
                    break;
                case CreatureAct.Bite:
                    if (!ActDone) break;
                    // Head out, gasping: hit it now.
                    _boss.OpenFor(_boss.ByPhase(2.4f, 1.9f, 1.4f));
                    SetAct(CreatureAct.Gasp, _boss.ByPhase(2.4f, 1.9f, 1.4f));
                    break;
                case CreatureAct.Slam:
                case CreatureAct.Gasp:
                    if (!ActDone) break;
                    _boss.Close();
                    SetAct(CreatureAct.Idle, _boss.ByPhase(1.2f, 0.8f, 0.4f));
                    break;
                case CreatureAct.Retreat:
                    if (ActDone) SetAct(CreatureAct.Idle, 0.5f);
                    break;
            }
        }

        private bool _slamNext;

        private int Crowd(Vector3 p, float radius)
        {
            int n = 0;
            foreach (PlayerHub h in PlayerHub.All)
                if (h != null && Flat(h.transform.position, p) < radius && h.TryGetComponent(out PlayerCombat c) && !c.IsDowned) n++;
            return n;
        }

        private void Bite(Vector3 p)
        {
            SetAct(CreatureAct.Bite, 0.35f);
            Vector3 jaws = p + transform.forward * 4.2f;
            foreach (PlayerHub h in PlayerHub.All)
            {
                if (h == null || !h.TryGetComponent(out PlayerCombat c) || c.IsDowned) continue;
                Vector3 to = h.transform.position - p;
                to.y = 0f;
                if (Flat(h.transform.position, jaws) > 2.2f || Vector3.Angle(transform.forward, to) > 40f) continue;
                c.ServerDamage(_boss.ByPhase(30, 35, 40), DisplayName, p);
                c.ServerKnockback(to.normalized * 9f + Vector3.up * 4f, DisplayName);
            }
            BiteNpcs(p, jaws, 2.2f, 40f, _boss.ByPhase(30, 35, 40) * 2); // a pirate in the way too (Creature.Prey)
            BiteObservers(jaws);
        }

        private void Slam(Vector3 p)
        {
            SetAct(CreatureAct.Slam, 0.9f);
            _nextSlamAllowed = Time.time + _boss.ByPhase(12f, 9f, 6f);
            foreach (PlayerHub h in PlayerHub.All)
            {
                if (h == null || !h.TryGetComponent(out PlayerCombat c) || c.IsDowned) continue;
                Vector3 to = h.transform.position - p;
                to.y = 0f;
                if (to.magnitude > 8f) continue;
                c.ServerDamage(15, DisplayName, p);
                c.ServerKnockback(to.normalized * 10f + Vector3.up * 5f, DisplayName);
            }
            SlamObservers(p);
            // A slam hard enough to stun it a moment: it can't bite straight after.
            _boss.OpenFor(0.9f);
        }

        private float SnapperZoneMultiplier(CreatureZone zone, DamageKind kind, Vector3 direction)
        {
            if (zone == null) return 0.05f;
            return zone.Part switch
            {
                "Head" => _boss != null && _boss.Vulnerable ? 2.5f : 0.6f,
                "Leg" => 0.3f,
                _ => 0.05f // the shell
            };
        }

        private void SnapperHit(PlayerHub attacker, CreatureZone zone, int dealt)
        {
            if (_maxHealth.Value > 0) _boss?.UpdatePhase((_health.Value - dealt) / (float)_maxHealth.Value);
            // Stung in the open mouth: it snaps the head back in.
            if (zone != null && zone.Part == "Head" && _act.Value == CreatureAct.Gasp && dealt >= 40)
            {
                _boss.Close();
                SetAct(CreatureAct.Retreat, 0.6f);
            }
        }

        private void SnapperDied(PlayerHub by)
        {
            _bossOpen.Value = false;
            RoarObservers("OLD SNAPPER IS BEATEN!");
        }

        [ObserversRpc]
        private void RoarObservers(string title)
        {
            if (_audio != null) _audio.PlayOneShot(Core.ProceduralAudio.Thump, 1f);
            if (!string.IsNullOrEmpty(title)) Story.StoryHud.ShowTitle(title, null);
        }

        [ObserversRpc]
        private void BiteObservers(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(Core.ProceduralAudio.Crunch, 1f);
            FloatingText.Spawn(at + Vector3.up * 1.2f, "SNAP!", new Color(1f, 0.35f, 0.25f), 1.4f, 1f);
        }

        [ObserversRpc]
        private void SlamObservers(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(Core.ProceduralAudio.Thump, 1f);
            if (WaterSurface.Exists && Rescue.Shore.WaterDepthAt(at + Vector3.up) > 0.3f) SplashFx.Spawn(new Vector3(at.x, WaterSurface.HeightAt(at), at.z), 2.5f);
            FloatingText.Spawn(at + Vector3.up * 3f, "SLAM!", new Color(1f, 0.55f, 0.2f), 1.5f, 1f);
            PlayerHub local = PlayerHub.Local;
            if (local != null && local.Look != null && (local.transform.position - at).sqrMagnitude < 20f * 20f) local.Look.Shake(1.6f, 1f);
        }
    }
}
