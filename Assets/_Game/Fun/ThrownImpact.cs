using FishNet.Object;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// Armed on whatever a lifeguard throws, on the thrower's machine (it simulates the flight): the first person it
    /// hits hard (another lifeguard, a tourist, a robber...) gets BONKed, and a heavy, fast one knocks a lifeguard
    /// flat. One bonk per throw.
    /// </summary>
    public class ThrownImpact : MonoBehaviour
    {
        private const float MinSpeed = 4.5f, Window = 2.5f;

        private PlayerHub _thrower;
        private Rigidbody _body;
        private float _until;

        public static void Arm(Item item, PlayerHub thrower)
        {
            if (item == null || thrower == null || item.GetComponent<VictimBrain>() != null) return; // throwing a person is no bonk
            if (!item.TryGetComponent(out ThrownImpact impact)) impact = item.gameObject.AddComponent<ThrownImpact>();
            impact._thrower = thrower;
            impact._body = item.Sync.Body;
            impact._until = Time.time + Window;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_thrower == null || Time.time > _until) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < MinSpeed) return;
            NetworkObject target = null;
            Collider c = collision.collider;
            PlayerHub player = c.GetComponentInParent<PlayerHub>();
            if (player != null && player != _thrower) target = player.NetworkObject;
            else if (c.GetComponentInParent<StoryNpc>() is { } npc) target = npc.NetworkObject;
            else if (c.GetComponentInParent<VictimBrain>() is { } victim) target = victim.NetworkObject;
            if (target == null) return;
            _until = 0f; // one bonk per throw
            float mass = _body != null ? _body.mass : 1f;
            if (_thrower.TryGetComponent(out PlayerCombat combat))
                combat.ReportBonk(target, collision.GetContact(0).point, speed * Mathf.Sqrt(Mathf.Max(0.1f, mass)));
        }
    }
}
