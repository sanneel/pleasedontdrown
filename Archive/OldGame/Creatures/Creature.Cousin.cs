using FishNet.Object.Synchronizing;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// The Kleptopus's big cousin (chapter 4's boss, phase 2: Sandy's pet "Grabby", under her private jetty): the same
    /// octopus, 1.6 times the size and sunset orange, with its own pattern. It doesn't go up the beach for sunbathers:
    /// its arms take tourists who are already in the water (the ones Sandy's helicopter drops) and drag them under;
    /// shoot the arm to free them (the head is open while it shrieks), or they come up out cold for CPR. It squirts
    /// ink from its second phase and throws the jetty's loot in its third; it never steals a pistol. No reward from the
    /// hotel when it's beaten (it isn't the hotel's octopus): the lost things it hoarded spill out.
    /// </summary>
    public partial class Creature
    {
        public const float CousinSize = 1.6f;
        private static readonly Color CousinTint = new(1f, 0.55f, 0.2f);

        private readonly SyncVar<bool> _cousin = new SyncVar<bool>();
        private readonly VictimBrain[] _draggedVictim = new VictimBrain[ArmCount];

        /// <summary>The big orange cousin (every machine).</summary>
        public bool IsCousin => _cousin.Value;
        /// <summary>Host (tests): a tourist in the water an arm is dragging under right now.</summary>
        public VictimBrain DraggedVictim(int arm) => _draggedVictim[arm];

        /// <summary>Host, right after <see cref="ServerSetup"/> as a Kleptopus: become the cousin; <paramref name="edge"/> is
        /// where its loot spills (the jetty's deck).</summary>
        [FishNet.Object.Server]
        public void ServerMakeCousin(Vector3 edge)
        {
            if (_kind.Value != CreatureKind.Kleptopus) return;
            _cousin.Value = true;
            _pierEdge = edge;
            ApplyCousinLook();
        }

        /// <summary>Every machine: bigger and orange (body, its Tripo dress and its drawn tentacles; again for anything made
        /// later: a rebuilt body, tentacles drawn when the arms first show).</summary>
        private void ApplyCousinLook()
        {
            if (!_cousin.Value) return;
            transform.localScale = Vector3.one * CousinSize;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.name.EndsWith("(cousin)")) continue;
                Material[] mats = r.materials; // (this one's own copies)
                foreach (Material m in mats)
                {
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.Lerp(m.GetColor("_BaseColor"), m.GetColor("_BaseColor") * CousinTint * 1.4f, 0.8f));
                    m.name += " (cousin)";
                }
                r.materials = mats;
            }
        }

        /// <summary>Host: an arm goes for a tourist in the water near it (nobody holding them, not already taken).</summary>
        private bool GrabSwimmer(int arm)
        {
            VictimBrain best = null;
            float bestSq = 30f * 30f;
            foreach (VictimBrain v in VictimBrain.All)
            {
                if (v == null || v.Item.IsHeld || v.IsAshore || !v.State.NeedsHelp()) continue;
                if (System.Array.IndexOf(_draggedVictim, v) >= 0) continue;
                float d = (v.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = v; }
            }
            if (best == null) return false;
            _draggedVictim[arm] = best;
            float reach = Mathf.Clamp(Mathf.Sqrt(bestSq) / 14f, 0.6f, 2f);
            SetArm(arm, ArmState.Reach, best.NetworkObject, default, reach);
            ShoutObservers("*a huge orange tentacle rises out of the water*", false);
            Debug.Log($"[Boss] {DisplayName} arm {arm} goes for {best.Name} in the water");
            return true;
        }

        /// <summary>Host: an arm with a tourist in the water: reach, then drag them to the octopus and under.</summary>
        private void VictimArmThink(int i, float dt)
        {
            VictimBrain v = _draggedVictim[i];
            if (v == null || !v.IsSpawned || v.Item.IsHeld || v.IsAshore)
            {
                // Pulled out of its grip by a lifeguard (or gone): it lets go.
                _draggedVictim[i] = null;
                SetArm(i, ArmState.Recoil, null, default, 1.5f);
                return;
            }
            if (Arm(i) == ArmState.Reach)
            {
                if (Time.time < _armUntil[i]) return;
                SetArm(i, ArmState.Drag, v.NetworkObject);
                _dragStarted[i] = Time.time;
                return;
            }
            // Dragged toward the octopus and down, slowly enough to be shot free.
            Rigidbody body = v.Item.Sync.Body;
            Vector3 at = body.position, to = transform.position + (at - transform.position).normalized * 3f * CousinSize;
            Vector3 flat = new(to.x - at.x, 0f, to.z - at.z);
            float sink = Mathf.Clamp01((Time.time - _dragStarted[i]) / 9f);
            float y = (WaterSurface.Exists ? WaterSurface.HeightAt(at) : 0f) - 0.4f - sink * 1.6f;
            Vector3 next = flat.magnitude > 0.2f ? at + flat.normalized * Mathf.Min(flat.magnitude, 1.6f * dt) : at;
            next.y = Mathf.MoveTowards(at.y, y, 1.5f * dt);
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            body.position = next;
            // Held under too long: they come up out cold (CPR on the jetty).
            if (Time.time - _dragStarted[i] > 10f)
            {
                _draggedVictim[i] = null;
                if (v.State != VictimState.Unconscious) v.ServerCheat("state", (float)VictimState.Unconscious);
                SetArm(i, ArmState.Recoil, null, default, 2f);
                ShoutObservers("*it lets go: they float up, limp*", true);
                Debug.Log($"[Boss] {DisplayName} pulled {v.Name} under");
            }
        }

        /// <summary>Host: let go of a tourist an arm holds (shot, the fight over).</summary>
        private bool LetGoOfVictim(int arm)
        {
            if (_draggedVictim[arm] == null) return false;
            Debug.Log($"[Boss] {DisplayName} let go of {_draggedVictim[arm].Name}");
            _draggedVictim[arm] = null;
            return true;
        }
    }
}
