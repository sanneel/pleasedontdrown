using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Danger on screen: over every guard and parrot that is getting suspicious a "?" whose ring fills (stealth, plan
    /// C5), a red "!" once they raise the alarm; the boss bar at the top (name, health, phase pips, "OPEN!" while its
    /// weak spot is exposed, plan C7); a small "DISGUISED" tag while you wear the pirate disguise. Puts itself in the
    /// game at start (no scene object needed).
    /// </summary>
    public class ThreatHud : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("ThreatHud");
            DontDestroyOnLoad(go);
            go.AddComponent<ThreatHud>();
        }

        private void Awake() => useGUILayout = false;

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive) return;
            Camera cam = local.Look != null ? local.Look.Camera : null;
            if (cam != null)
            {
                foreach (StoryNpc npc in StoryNpc.All)
                    if (npc != null && npc.IsGuarding && (npc.Alert > 0.02f || npc.IsAlarmed) && !npc.IsDefeated)
                        DrawAlert(cam, local, npc.HeadPosition + Vector3.up * 0.75f, npc.Alert, npc.IsAlarmed);
                foreach (Creature c in Creature.All)
                    if (c != null && !c.IsDead && (c.Alert > 0.02f || c.IsAlarmed))
                        DrawAlert(cam, local, c.TopPosition + Vector3.up * 0.45f, c.Alert, c.IsAlarmed);
            }
            DrawBossBar();
            DrawInk();
            if (local.TryGetComponent(out PlayerCombat combat) && combat.IsDisguised)
                Hud.Label(new Rect(30f, 140f, 520f, 26f), "<b>DISGUISED</b>  <color=#c8c8c8>pirates take you for one of theirs</color>", 18f,
                    new Color(0.85f, 0.95f, 0.7f), TextAnchor.MiddleLeft);
        }

        private static void DrawAlert(Camera cam, PlayerHub local, Vector3 world, float alert, bool alarmed)
        {
            if ((world - local.transform.position).sqrMagnitude > 70f * 70f) return;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0.3f) return;
            float sx = screen.x / Hud.Scale, sy = (Screen.height - screen.y) / Hud.Scale;
            float size = alarmed ? 46f : 34f + alert * 10f;
            var r = new Rect(sx - size * 0.5f, sy - size * 0.5f, size, size);
            Hud.Fill(r, new Color(0f, 0f, 0f, 0.55f), size * 0.5f);
            if (alarmed)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 12f);
                Hud.Fill(new Rect(r.x + 3f, r.y + 3f, size - 6f, size - 6f), new Color(0.95f, 0.18f, 0.12f, pulse), size * 0.5f - 3f);
                Hud.Label(r, "!", 34f, Color.white, heavy: true);
                return;
            }
            // The ring fills as they grow sure; it turns from yellow to orange near the top.
            Color ring = Color.Lerp(new Color(1f, 0.85f, 0.25f), new Color(1f, 0.45f, 0.15f), Mathf.InverseLerp(0.5f, 1f, alert));
            Hud.Arc(r, 0f, alert, 5f, ring);
            Hud.Label(r, "?", 24f, ring, heavy: true);
        }

        // Kleptopus ink: big dark blots over the view, clearing over the last second.
        private static readonly Vector4[] Blots =
        {
            new(0.3f, 0.35f, 0.42f, 0f), new(0.62f, 0.3f, 0.5f, 0f), new(0.5f, 0.62f, 0.55f, 0f), new(0.15f, 0.7f, 0.35f, 0f),
            new(0.85f, 0.6f, 0.4f, 0f), new(0.45f, 0.1f, 0.3f, 0f), new(0.8f, 0.15f, 0.3f, 0f), new(0.1f, 0.15f, 0.28f, 0f)
        };

        private static void DrawInk()
        {
            float left = Creature.InkUntil - Time.time;
            if (left <= 0f) return;
            float alpha = Mathf.Clamp01(left / 1.2f) * 0.94f;
            var ink = new Color(0.07f, 0.03f, 0.1f, alpha);
            float w = Hud.Width, h = Hud.Height;
            foreach (Vector4 b in Blots)
            {
                float r = b.z * h * (1f + 0.04f * Mathf.Sin(Time.time * 2f + b.x * 9f));
                Hud.Fill(new Rect(b.x * w - r, b.y * h - r, r * 2f, r * 2f), ink, r);
            }
            Hud.Fill(new Rect(0f, 0f, w, h), new Color(ink.r, ink.g, ink.b, alpha * 0.55f));
        }

        private static void DrawBossBar()
        {
            Creature boss = Creature.ActiveBoss;
            if (boss == null) return;
            // Bottom middle, above the hotbar: the top left is the mission's, the middle is for titles.
            float width = 700f, x = (Hud.Width - width) * 0.5f, y = Hud.HotbarTop - 120f;
            Hud.Label(new Rect(x, y, width, 30f), boss.IsDead ? $"{boss.DisplayName.ToUpperInvariant()}  BEATEN" : boss.DisplayName.ToUpperInvariant(), 24f,
                new Color(1f, 0.88f, 0.55f), heavy: true);
            var bar = new Rect(x, y + 34f, width, 18f);
            Color fill = boss.BossOpen ? Color.Lerp(new Color(1f, 0.85f, 0.2f), Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 14f)) : new Color(0.86f, 0.22f, 0.18f);
            Hud.Bar(bar, boss.Health01, fill);
            Hud.Frame(bar, new Color(1f, 1f, 1f, 0.7f), 2f, 9f);
            // Phase pips under the bar.
            for (int i = 0; i < boss.BossPhases; i++)
            {
                var pip = new Rect(x + width * 0.5f - boss.BossPhases * 14f + i * 28f, y + 58f, 20f, 8f);
                Hud.Fill(pip, i < boss.BossPhase ? new Color(1f, 0.6f, 0.25f) : new Color(1f, 1f, 1f, 0.25f), 4f);
            }
            if (boss.BossOpen && !boss.IsDead)
                Hud.Label(new Rect(x, y - 32f, width, 30f), "WEAK SPOT OPEN! HIT THE HEAD!", 21f, new Color(1f, 0.95f, 0.4f), heavy: true);
        }
    }
}
