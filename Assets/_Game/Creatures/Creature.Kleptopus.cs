using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// The Kleptopus (host), island 2's boss (Docs/16-Plan-Islands-2-3.md): a giant purple octopus in stolen
    /// jewellery, sunglasses and a panama, lurking under the hotel pier on a heap of loot. Two working arms (shootable,
    /// "Arm0"/"Arm1", each with its own health) do the mischief; four more just wave.
    ///  * Phase 1: an arm snakes up the beach, grabs a sunbather and drags them into the sea. Shoot the arm: it lets go
    ///    (they're in the water now: rescue them) and the octopus shrieks with its eyes wide (open: the head takes 2.5x).
    ///  * Phase 2 (66%): it squirts ink (everyone near sees black for a few seconds), then an arm snatches a pistol out of
    ///    someone's hands and waves it over the pier. Punch the arm to get it back.
    ///  * Phase 3 (33%): it throws stolen sun loungers and phones at you; its eyes glow before each throw (the cue to move
    ///    and the moment to shoot the head).
    ///  * Beaten: the stolen things spill out (return them to their owners for pay) and the hotel pays $300.
    /// </summary>
    public partial class Creature
    {
        private const int ArmCount = 2, ArmHealth = 30;
        public const int KleptopusReward = 300;

        public enum ArmState : byte { Idle, Reach, Drag, Steal, Throw, Recoil }

        private readonly SyncVar<ArmState> _arm0 = new SyncVar<ArmState>(), _arm1 = new SyncVar<ArmState>();
        private readonly SyncVar<NetworkObject> _armTarget0 = new SyncVar<NetworkObject>(), _armTarget1 = new SyncVar<NetworkObject>();
        private readonly SyncVar<Vector3> _armPoint0 = new SyncVar<Vector3>(), _armPoint1 = new SyncVar<Vector3>();
        private readonly SyncVar<bool> _holdsGun = new SyncVar<bool>();

        private readonly int[] _armHealth = { ArmHealth, ArmHealth };
        private readonly float[] _armUntil = new float[ArmCount];
        private readonly StoryNpc[] _dragged = new StoryNpc[ArmCount];
        private readonly float[] _dragStarted = new float[ArmCount];
        private float _nextGrab, _nextInk, _nextSteal, _nextThrow;
        private PlayerHub _throwAt;
        private Vector3 _pierEdge;
        // The stolen pistol: what it was and what was fitted, to hand back the same gun.
        private string _gunName;
        private readonly List<(Weapon.PartKind kind, byte index)> _gunParts = new();
        private PlayerHub _gunOwner;

        /// <summary>Every machine (HUD): ink in the eyes until this time.</summary>
        public static float InkUntil { get; private set; } = -10f;
        /// <summary>Host (tests): someone dragged by an arm right now / the gun it holds.</summary>
        public StoryNpc Dragged(int arm) => _dragged[arm];
        public bool HoldsGun => _holdsGun.Value;
        public ArmState Arm(int i) => i == 0 ? _arm0.Value : _arm1.Value;

        private void SetArm(int i, ArmState state, NetworkObject target = null, Vector3 point = default, float seconds = 0f)
        {
            if (i == 0) { _arm0.Value = state; _armTarget0.Value = target; _armPoint0.Value = point; }
            else { _arm1.Value = state; _armTarget1.Value = target; _armPoint1.Value = point; }
            _armUntil[i] = Time.time + seconds;
        }

        private NetworkObject ArmTarget(int i) => i == 0 ? _armTarget0.Value : _armTarget1.Value;
        private Vector3 ArmPoint(int i) => i == 0 ? _armPoint0.Value : _armPoint1.Value;

        private void KleptopusSetup()
        {
            _boss = new BossFight(1f, 0.66f, 0.33f);
            _boss.PhaseChanged += phase =>
            {
                _bossPhase.Value = (byte)phase;
                RoarObservers(phase == 2 ? "The Kleptopus is fuming!" : "The Kleptopus goes berserk!");
                ServerBossPhase?.Invoke(this, phase);
            };
            _bossPhase.Value = 1;
            _bossOpen.Value = false;
            for (int i = 0; i < ArmCount; i++) SetArm(i, ArmState.Idle);
            _nextGrab = Time.time + 4f;
            _nextInk = _nextSteal = _nextThrow = Time.time + 3f;
            // Where it waves a stolen pistol: just off the pier, at deck height, where a punch can reach.
            _pierEdge = transform.position + Vector3.up * 2.4f;
            GameObject pier = GameObject.Find("HotelDock");
            if (pier != null)
            {
                Vector3 deck = pier.transform.position;
                Vector3 p = transform.position;
                Vector3 nearest = deck + Vector3.Project(p - deck, pier.transform.forward).normalized * Mathf.Min(11f, Vector3.Project(p - deck, pier.transform.forward).magnitude);
                Vector3 away = new Vector3(p.x - nearest.x, 0f, p.z - nearest.z);
                _pierEdge = new Vector3(nearest.x, deck.y + 1.4f, nearest.z) + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right) * 1.9f;
            }
            SetAct(CreatureAct.Idle, 2f);
            RoarObservers(null);
        }

        private void KleptopusThink(float dt)
        {
            bool open = _boss.Vulnerable;
            if (_bossOpen.Value != open) _bossOpen.Value = open;
            Vector3 p = transform.position;
            // Face whoever is nearest (the eyes and the throwing arm with it).
            if (Time.time >= _nextBossThink)
            {
                _nextBossThink = Time.time + 0.5f;
                _bossTarget = NearestStanding(p, 60f);
            }
            if (_bossTarget != null) Turn(_bossTarget.transform.position - p, 45f, dt);

            for (int i = 0; i < ArmCount; i++) ArmThink(i, dt);

            int phase = _boss.Phase;
            switch (_act.Value)
            {
                case CreatureAct.Rear: // eyes glowing, about to throw
                    if (!ActDone) break;
                    Throw();
                    SetAct(CreatureAct.Idle, 0.6f);
                    break;
                case CreatureAct.Gasp: // shrieking after an arm was shot, eyes wide
                case CreatureAct.Slam: // ink
                    if (ActDone) SetAct(CreatureAct.Idle, 0.4f);
                    break;
                default:
                    if (!ActDone) break;
                    // Grab sunbathers in phase 1 (now and then in 2).
                    if ((phase == 1 || (phase == 2 && Random.value < 0.4f)) && Time.time >= _nextGrab && FreeArm(out int a))
                    {
                        _nextGrab = Time.time + _boss.ByPhase(9f, 14f, 99f);
                        GrabSunbather(a);
                    }
                    if (phase >= 2 && Time.time >= _nextInk)
                    {
                        _nextInk = Time.time + _boss.ByPhase(99f, 13f, 16f);
                        Ink(p);
                        SetAct(CreatureAct.Slam, 1f);
                        _nextSteal = Mathf.Min(_nextSteal, Time.time + 1.2f); // the snatch comes out of the cloud
                        break;
                    }
                    if (phase == 2 && !_holdsGun.Value && Time.time >= _nextSteal && FreeArm(out int s))
                    {
                        _nextSteal = Time.time + 12f;
                        TrySteal(s);
                    }
                    if (phase >= 3 && Time.time >= _nextThrow && _bossTarget != null)
                    {
                        _nextThrow = Time.time + Random.Range(3.5f, 5f);
                        _throwAt = _bossTarget;
                        _boss.OpenFor(1.5f); // eyes aglow: hit the head now
                        SetAct(CreatureAct.Rear, 1.4f);
                        ShoutObservers("*its eyes glow*", false);
                    }
                    break;
            }
        }

        private bool FreeArm(out int arm)
        {
            for (arm = 0; arm < ArmCount; arm++)
                if (Arm(arm) == ArmState.Idle && Time.time >= _armUntil[arm]) return true;
            arm = -1;
            return false;
        }

        // ------------------------------------------------------------------ arms

        private void ArmThink(int i, float dt)
        {
            switch (Arm(i))
            {
                case ArmState.Reach:
                {
                    StoryNpc npc = _dragged[i];
                    if (npc == null) { SetArm(i, ArmState.Idle); break; }
                    if (Time.time < _armUntil[i]) break; // still snaking up the beach
                    SetArm(i, ArmState.Drag, npc.NetworkObject);
                    _dragStarted[i] = Time.time;
                    npc.ServerShout("AAAH! SOMETHING'S GOT MY LEG!", true);
                    npc.ServerSetPose(Avatars.AvatarPose.Normal);
                    npc.ServerSetMood(Avatars.AvatarMood.Scared);
                    break;
                }
                case ArmState.Drag:
                {
                    StoryNpc npc = _dragged[i];
                    if (npc == null) { SetArm(i, ArmState.Idle); break; }
                    // Dragged down the beach and out to the octopus, slowly enough to be saved.
                    Vector3 at = npc.transform.position, to = transform.position + (npc.transform.position - transform.position).normalized * 2.6f;
                    Vector3 flat = new Vector3(to.x - at.x, 0f, to.z - at.z);
                    if (flat.magnitude > 0.2f)
                    {
                        Vector3 next = at + flat.normalized * Mathf.Min(flat.magnitude, 2f * dt);
                        float ground = Shore.GroundHeightAt(next + Vector3.up * 2f);
                        float y = float.IsNaN(ground) ? next.y : ground;
                        if (WaterSurface.Exists && WaterSurface.HeightAt(next) - 1.3f > y) y = WaterSurface.HeightAt(next) - 1.3f;
                        npc.ServerTeleport(new Vector3(next.x, y, next.z), Quaternion.LookRotation(-flat).eulerAngles.y, keepExact: true);
                    }
                    if (Random.value < 0.4f * dt) npc.ServerShout(Random.value < 0.5f ? "HELP! SHOOT IT!" : "NOT THE SUNGLASSES!", true);
                    // Held at the octopus too long: pulled under; they come up out cold.
                    if (flat.magnitude <= 0.2f && Time.time - _dragStarted[i] > 14f) LetGoOf(i, unconscious: true);
                    break;
                }
                case ArmState.Steal:
                    // Waving the pistol just off the pier (punch it there).
                    if (ArmPoint(i) != _pierEdge) SetArm(i, ArmState.Steal, null, _pierEdge);
                    break;
                case ArmState.Recoil:
                case ArmState.Throw:
                    if (Time.time >= _armUntil[i]) SetArm(i, ArmState.Idle, null, default, 1.5f);
                    break;
            }
        }

        private void GrabSunbather(int arm)
        {
            BeachCrowd crowd = BeachCrowd.Nearest(transform.position);
            StoryNpc npc = crowd != null ? crowd.Borrow(BeachCrowd.Activity.Sunbathe, transform.position, 55f) : null;
            if (npc == null) return;
            crowd.Release(npc); // theirs no more: the story has them now
            _dragged[arm] = npc;
            float reach = Mathf.Clamp(Flat(npc.transform.position, transform.position) / 12f, 0.8f, 2.5f);
            SetArm(arm, ArmState.Reach, npc.NetworkObject, default, reach);
            ShoutObservers("*a tentacle slithers up the beach*", false);
            Debug.Log($"[Boss] Kleptopus arm {arm} goes for {npc.Name}");
        }

        /// <summary>Let go of whoever the arm drags: in the water they're a rescue now; still on the sand, they run.</summary>
        private void LetGoOf(int arm, bool unconscious = false)
        {
            StoryNpc npc = _dragged[arm];
            _dragged[arm] = null;
            if (npc == null) return;
            Vector3 p = npc.transform.position;
            bool inWater = Shore.WaterDepthAt(p + Vector3.up * 2f) > 1.1f;
            if (!inWater || RescueService.Instance == null)
            {
                npc.ServerSetMood(Avatars.AvatarMood.Scared);
                npc.ServerShout("RUN!", true);
                npc.ServerMoveTo(p + (p - transform.position).normalized * 12f, run: true);
                return;
            }
            TouristProfile profile = TouristProfile.Default;
            profile.Name = npc.Name;
            profile.Look = npc.LookPacked;
            profile.Figure = npc.Look.Feminine ? 1 : 0;
            profile.SecondsToUnconscious = 25f;
            profile.Shouts = "HELP!|IT TOOK MY SUNGLASSES!|I CAN'T SWIM IN THESE SHORTS!";
            Vector3 spot = new Vector3(p.x, WaterSurface.Exists ? WaterSurface.HeightAt(p) - 0.4f : p.y, p.z);
            float yaw = npc.transform.eulerAngles.y;
            Despawn(npc.gameObject);
            RescueService.Instance.SpawnVictim(spot, yaw, unconscious ? VictimState.Unconscious : VictimState.Distressed, unconscious ? 0f : 30f, unconscious ? 0f : 1f, profile);
            Debug.Log($"[Boss] Kleptopus let go of {profile.Name} in the water{(unconscious ? " (out cold)" : "")}");
        }

        /// <summary>Host: let go of everyone and give the pistol back (the mission restarting, the boss going away).</summary>
        public void ServerReleaseAll()
        {
            if (_kind.Value == CreatureKind.Kleptopus && _setUp) ReleaseEverything();
        }

        private void ReleaseEverything()
        {
            for (int i = 0; i < ArmCount; i++)
            {
                if (_dragged[i] != null) LetGoOf(i);
                if (Arm(i) == ArmState.Steal) GiveGunBack(null);
                if (IsServerInitialized && _setUp) SetArm(i, ArmState.Idle);
            }
        }

        // ------------------------------------------------------------------ ink and the pistol

        private void Ink(Vector3 p)
        {
            InkObservers(p, 24f);
            ShoutObservers("*SPLOOSH*", true);
        }

        [ObserversRpc]
        private void InkObservers(Vector3 at, float radius)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 1f);
            if (WaterSurface.Exists) SplashFx.Spawn(new Vector3(at.x, WaterSurface.HeightAt(at), at.z), 3f);
            PlayerHub local = PlayerHub.Local;
            if (local != null && (local.transform.position - at).sqrMagnitude < radius * radius) InkUntil = Time.time + 4.5f;
        }

        private void TrySteal(int arm)
        {
            Item gun = null;
            foreach (Item item in Item.All)
            {
                if (!item.IsHeld || item.Holder == null || !item.TryGetComponent(out Weapon _)) continue;
                if ((item.Holder.transform.position - transform.position).sqrMagnitude > 35f * 35f) continue;
                gun = item;
                break;
            }
            if (gun == null) return;
            StartCoroutine(Steal(arm, gun));
        }

        private System.Collections.IEnumerator Steal(int arm, Item gun)
        {
            PlayerHub owner = gun.Holder;
            SetArm(arm, ArmState.Reach, owner.NetworkObject, default, 1f);
            yield return new WaitForSeconds(0.9f);
            if (gun == null || owner == null) { SetArm(arm, ArmState.Idle); yield break; }
            DropTarget(owner.Owner);
            float until = Time.time + 1.5f;
            while (gun != null && gun.IsHeld && Time.time < until) yield return null;
            if (gun == null || gun.IsHeld) { SetArm(arm, ArmState.Idle, null, default, 2f); yield break; }
            // Note what it was (with its suppressor and sights), and take it.
            _gunName = gun.DisplayName;
            _gunParts.Clear();
            if (gun.TryGetComponent(out Weapon w))
                foreach (Weapon.PartKind kind in System.Enum.GetValues(typeof(Weapon.PartKind)))
                    for (byte index = 1; index < 8; index++)
                        if (w.IsFitted(kind, index) && (kind != Weapon.PartKind.Laser && kind != Weapon.PartKind.Magazine || index == 1))
                            _gunParts.Add((kind, index));
            _gunOwner = owner;
            Despawn(gun.gameObject);
            _holdsGun.Value = true;
            SetArm(arm, ArmState.Steal, null, _pierEdge);
            StolenObservers(owner.NetworkObject);
            Debug.Log($"[Boss] Kleptopus stole {owner.DisplayName}'s {_gunName}");
        }

        [TargetRpc]
        private void DropTarget(NetworkConnection target)
        {
            PlayerHub me = PlayerHub.Local;
            if (me != null && me.Hands != null && me.Hands.HeldItem != null) me.Hands.Drop();
        }

        [ObserversRpc]
        private void StolenObservers(NetworkObject owner)
        {
            if (owner != null && owner.IsOwner) PlayerHud.ShowToast("<b>It snatched your pistol!</b> Punch the tentacle by the pier to get it back.", 4f);
        }

        /// <summary>Host: the pistol drops out of the arm, tossed toward <paramref name="to"/> (or just down).</summary>
        private void GiveGunBack(PlayerHub to)
        {
            if (!_holdsGun.Value || string.IsNullOrEmpty(_gunName)) return;
            _holdsGun.Value = false;
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(_gunName) : null;
            if (prefab == null) return;
            Vector3 from = _pierEdge + Vector3.up * 0.3f;
            Item item = Instantiate(prefab, from, Quaternion.identity);
            Spawn(item.gameObject);
            if (item.TryGetComponent(out Weapon w))
                foreach ((Weapon.PartKind kind, byte index) in _gunParts) w.ServerFit(kind, index);
            Vector3 toward = (to != null ? to : _gunOwner) is { } who ? who.transform.position - from : Vector3.zero;
            toward.y = 0f;
            StartCoroutine(Toss(item, (toward.sqrMagnitude > 0.01f ? toward.normalized * 2.5f : Vector3.zero) + Vector3.up * 2.5f));
            Debug.Log($"[Boss] Kleptopus dropped the {_gunName}");
            _gunName = null;
        }

        private static System.Collections.IEnumerator Toss(Item item, Vector3 velocity)
        {
            yield return new WaitForFixedUpdate();
            if (item != null && !item.Sync.Body.isKinematic) item.Sync.Body.linearVelocity = velocity;
        }

        // ------------------------------------------------------------------ throwing loot

        private void Throw()
        {
            PlayerHub t = _throwAt;
            if (t == null) return;
            int arm = FreeArm(out int a) ? a : 0;
            // Aimed where they're going (a little lead), so standing still or running straight is what gets you hit.
            Vector3 target = t.transform.position;
            if (t.TryGetComponent(out PlayerCombat c)) target += Vector3.ProjectOnPlane(t.transform.position - _lastThrowSeen, Vector3.up).normalized * Mathf.Min(c.ServerSpeed, 6f) * 0.6f;
            _lastThrowSeen = t.transform.position;
            Vector3 from = transform.position + Vector3.up * 4f;
            bool lounger = Random.value < 0.55f;
            float flight = 1.1f;
            SetArm(arm, ArmState.Throw, null, from + Vector3.up * 2f, 0.8f);
            ThrownProp.Launch(from, target, flight, lounger);
            ThrowObservers(from, target, flight, lounger);
            StartCoroutine(Land(target, flight, lounger));
        }

        private Vector3 _lastThrowSeen;

        [ObserversRpc(ExcludeServer = true)]
        private void ThrowObservers(Vector3 from, Vector3 to, float flight, bool lounger) => ThrownProp.Launch(from, to, flight, lounger);

        private System.Collections.IEnumerator Land(Vector3 at, float flight, bool lounger)
        {
            yield return new WaitForSeconds(flight);
            float radius = lounger ? 2.4f : 1.4f;
            foreach (PlayerHub h in PlayerHub.All)
            {
                if (h == null || !h.TryGetComponent(out PlayerCombat c) || c.IsDowned) continue;
                Vector3 d = h.transform.position - at;
                d.y = 0f;
                if (d.magnitude > radius) continue;
                c.ServerDamage(lounger ? 22 : 12, "a flying " + (lounger ? "sun lounger" : "phone"), at);
                c.ServerKnockback((d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward) * 6f + Vector3.up * 3f, "The Kleptopus");
            }
        }

        // ------------------------------------------------------------------ getting hit

        private float KleptopusZoneMultiplier(CreatureZone zone)
        {
            if (zone == null) return 0.4f;
            if (zone.Part.StartsWith("Arm")) return 0.25f; // a shot arm hurts the octopus a little
            return zone.Part == "Head" ? (_boss != null && _boss.Vulnerable ? 2.5f : 1f) : 0.4f;
        }

        private void KleptopusHit(PlayerHub attacker, CreatureZone zone, int damage, DamageKind kind)
        {
            if (zone == null || !zone.Part.StartsWith("Arm")) return;
            int arm = zone.Part == "Arm1" ? 1 : 0;
            ArmState state = Arm(arm);
            if (state == ArmState.Steal)
            {
                // Only a fist gets the pistol back (a bullet near your own gun... no).
                if (kind == DamageKind.Punch)
                {
                    GiveGunBack(attacker);
                    SetArm(arm, ArmState.Recoil, null, default, 1.2f);
                    ShoutObservers("*the tentacle lets go*", false);
                }
                return;
            }
            _armHealth[arm] -= damage;
            if (_armHealth[arm] > 0) return;
            _armHealth[arm] = ArmHealth;
            if (state is ArmState.Reach or ArmState.Drag)
            {
                LetGoOf(arm);
                // It shrieks, eyes wide: the head is open a moment.
                _boss.OpenFor(_boss.ByPhase(2.5f, 2f, 1.5f));
                SetAct(CreatureAct.Gasp, 2f);
                ShoutObservers("SKREEEE!", true);
            }
            SetArm(arm, ArmState.Recoil, null, default, 2.5f);
        }

        private void KleptopusDied(PlayerHub by)
        {
            ReleaseEverything();
            _bossOpen.Value = false;
            RoarObservers("THE KLEPTOPUS IS BEATEN!");
            // The loot spills out onto the sand by the pier: things the guests lost, to give back.
            string[] kinds = { "Wallet", "Phone", "Sunglasses", "Watch", "Wallet", "Sunglasses" };
            Vector3 shore = _pierEdge;
            float ground = Shore.GroundHeightAt(shore + Vector3.up * 3f);
            for (int i = 0; i < kinds.Length; i++)
            {
                Item prefab = GameContent.Items != null ? GameContent.Items.Find(kinds[i]) : null;
                if (prefab == null) continue;
                Vector3 at = transform.position + Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 1.5f + Vector3.up * 3f;
                Item item = Instantiate(prefab, at, Quaternion.identity);
                Spawn(item.gameObject);
                if (item.TryGetComponent(out LostItem lost)) lost.ServerSetup(VictimBrain.RandomName(), true);
                Vector3 toward = shore - at;
                toward.y = 0f;
                StartCoroutine(Toss(item, toward.normalized * Random.Range(3f, 6f) + Vector3.up * 5f));
            }
            if (Economy.Instance != null) Economy.Instance.ServerAdd(KleptopusReward, "the Kleptopus (the hotel's thanks)", transform.position);
        }
    }
}
