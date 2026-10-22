using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// How a hit on a character feels (every machine, from HitObservers): the body flashes red for a moment, a ball
    /// throws a red puff off where it struck, the damage pops up over the head (a headshot bigger and gold, with its
    /// own ding), and whoever landed it gets the host's confirmation: a hit marker (gold for a headshot, red for a
    /// kill) and a tick, ding or thunk. The health bar (<see cref="NpcHealthBar"/>) only shows once they're hurt.
    /// Headshots (a ball above the shoulders of someone standing) do double damage (host, ServerTakeHit).
    /// </summary>
    public partial class StoryNpc
    {
        private const float FlashSeconds = 0.16f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color FlashColour = new(2.2f, 0.55f, 0.5f);

        private Renderer[] _flashRenderers;
        private MaterialPropertyBlock _flashBlock;
        private float _flashAt = -10f;
        private bool _flashing;
        private NpcHealthBar _healthBar;

        /// <summary>Host: a ball above the shoulders of someone standing (the head is the top ~30 cm).</summary>
        private bool IsHeadshot(DamageKind kind, Vector3 point) =>
            kind == DamageKind.Bullet && _pose.Value is AvatarPose.Normal or AvatarPose.Scared or AvatarPose.HandsUp &&
            point.y - transform.position.y > 1.5f;

        private void HitFeel(Vector3 point, Vector3 push, int damage, bool bullet, bool head, bool killed, int healthLeft, PlayerHub attacker)
        {
            // The body flashes.
            _flashAt = Time.time;
            _flashing = true;

            // A ball throws a red puff back out of the wound.
            if (bullet)
            {
                Vector3 normal = push.sqrMagnitude > 0.01f ? -push : Vector3.up;
                ProjectileSystem.Instance.Puff(point, (normal + Vector3.up * 0.3f).normalized, new Color(0.62f, 0.06f, 0.05f), head ? 14 : 9, head ? 1.3f : 1f);
                AudioSource.PlayClipAtPoint(ProceduralAudio.BulletFlesh, point, 1f);
            }

            // The damage pops up over the head (a little to the side each time, so a burst doesn't stack), about the same
            // size on screen near or far (world-sized, a number at arm's length filled the view).
            // (No numbers, no HEADSHOT over the head and no health bar: the user, 2026-10-22, "make shooting people or
            // punching them more realistic and remove unnecessary things".)

            // Ours? The host says it landed.
            if (attacker != null && attacker == PlayerHub.Local)
            {
                bool kill = killed || healthLeft <= 0;
                // The crosshair marks it, and the thud comes close (no ding, no chime).
                WeaponHud.ShowHitMarker(head, kill);
                Camera cam = Camera.main;
                Vector3 ear = cam != null ? cam.transform.position : transform.position;
                AudioSource.PlayClipAtPoint(bullet ? ProceduralAudio.BulletFlesh : ProceduralAudio.HitTick, ear + (point - ear).normalized * 2f, bullet ? 0.55f : 0.35f);
            }

            if (_healthBar != null) _healthBar.Hit(damage);
        }

        /// <summary>How big a popup over someone's head is drawn: about the same size on screen near or far.</summary>
        private static float PopupSize(Vector3 at)
        {
            Camera view = Camera.main;
            return view != null ? Mathf.Clamp(Vector3.Distance(view.transform.position, at) / 9f, 0.2f, 2.4f) : 1f;
        }

        /// <summary>Every frame: the red flash fading back out.</summary>
        private void UpdateHitFlash()
        {
            if (!_flashing) return;
            if (_flashRenderers == null)
            {
                var found = new System.Collections.Generic.List<Renderer>();
                foreach (Renderer r in (_rig != null ? _rig.transform : transform).GetComponentsInChildren<Renderer>(true))
                    if (r is SkinnedMeshRenderer || (r is MeshRenderer && r.GetComponent<TextMesh>() == null)) found.Add(r);
                _flashRenderers = found.ToArray();
                _flashBlock = new MaterialPropertyBlock();
            }
            float t = (Time.time - _flashAt) / FlashSeconds;
            if (t >= 1f)
            {
                _flashing = false;
                foreach (Renderer r in _flashRenderers)
                    if (r != null) r.SetPropertyBlock(null);
                return;
            }
            _flashBlock.SetColor(BaseColorId, Color.Lerp(FlashColour, Color.white, t * t));
            foreach (Renderer r in _flashRenderers)
                if (r != null) r.SetPropertyBlock(_flashBlock);
        }
    }
}
