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

        // Drawn on the HUD canvas (no IMGUI): see HudCanvas.
        private void OnEnable() => HudCanvas.Add(this, HudCanvas.OrderWorldMarkers, DrawHud);

        private void OnDisable() => HudCanvas.Remove(this);

        private void DrawHud()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive) return;
            DrawInk();
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
            if (local.TryGetComponent(out PlayerCombat combat) && combat.IsDisguised)
            {
                var tag = new Rect(Hud.Margin, 242f, 290f, 54f);
                Hud.Surface(tag, 0.86f, Hud.Teal);
                Hud.Label(new Rect(tag.x + 18f, tag.y + 7f, tag.width - 36f, 20f), "DISGUISED", 16f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                Hud.Label(new Rect(tag.x + 18f, tag.y + 28f, tag.width - 36f, 18f), "Pirates see you as one of their own", 14f, Hud.Muted, TextAnchor.MiddleLeft, shadow: false);
            }
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
            Color ring = Color.Lerp(Hud.Sand, Hud.Coral, Mathf.InverseLerp(0.5f, 1f, alert));
            Hud.Arc(r, 0f, alert, 0.1f, ring);
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
            if (boss == null)
            {
                DrawMissionBossBar();
                return;
            }
            Color fill = boss.IsDead ? Hud.Teal : boss.BossOpen ? Hud.Sand : Hud.Coral;
            Rect card = BossCard(boss.DisplayName, boss.IsDead ? "DEFEATED" : $"PHASE {boss.BossPhase} / {boss.BossPhases}", boss.Health01, fill);
            if (boss.BossOpen && !boss.IsDead)
            {
                var hint = new Rect(card.x + 70f, card.yMax + 8f, card.width - 140f, 34f);
                Hud.Surface(hint, 0.9f);
                Hud.Label(hint, "WEAK SPOT OPEN  ·  AIM FOR THE HEAD", 17f, Hud.Sand, heavy: true, shadow: false);
            }
        }

        private static float _missionBarShown = -1f;

        /// <summary>
        /// A mission run like a boss fight (StoryDirector.ServerBossBar): same place and size as a boss's bar, the bar
        /// emptying as the team closes in, and a clock ring beside it (the last 10 seconds counted down in the middle).
        /// </summary>
        private static void DrawMissionBossBar()
        {
            if (!StoryDirector.BossBarShown)
            {
                _missionBarShown = -1f;
                return;
            }
            float target = StoryDirector.BossBar01;
            _missionBarShown = _missionBarShown < 0f ? target : Mathf.MoveTowards(_missionBarShown, target, Time.unscaledDeltaTime * 0.6f);
            float total = StoryDirector.BossBarTimeTotal, left = StoryDirector.BossBarTimeLeft;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(left));
            bool hurry = total > 0f && left <= 10f;
            string status = total > 0f ? $"{seconds / 60}:{seconds % 60:00} LEFT" : "IN PROGRESS";
            Rect card = BossCard(StoryDirector.BossBarTitle, status, _missionBarShown, hurry ? Hud.Danger : Hud.Coral);
            if (total > 0f)
            {
                var ring = new Rect(card.xMax - 20f - Hud.TextWidth(status, 16f, heavy: true) - 28f, card.y + 73f, 20f, 20f); // (left of the time)
                Hud.Arc(ring, 0f, 1f, 0.12f, new Color(1f, 1f, 1f, 0.15f));
                Hud.Arc(ring, 0f, Mathf.Clamp01(left / total), 0.12f, hurry ? Hud.Danger : Hud.Sand);
            }
        }

        private static Rect BossCard(string title, string status, float fraction, Color accent)
        {
            // The boss's name big over a thick bar, its phase small at the bar's right end (no box: outlined words).
            const float width = 640f;
            var card = new Rect((Hud.Width - width) * 0.5f, Hud.BossTop, width, 96f);
            Hud.FitLabel(new Rect(card.x, card.y + 4f, width, 44f), title.ToUpperInvariant(), 36f, Hud.Paper, TextAnchor.MiddleCenter, heavy: true);
            Hud.Bar(new Rect(card.x + 20f, card.y + 52f, width - 40f, 18f), fraction, accent);
            Hud.Label(new Rect(card.x + 20f, card.y + 72f, width - 40f, 22f), status, 16f, accent, TextAnchor.MiddleRight, heavy: true, shadow: false);
            return card;
        }
    }
}
