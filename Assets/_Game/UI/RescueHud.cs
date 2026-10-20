using System;
using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Markers over tourists in trouble you can see (name, state, distance, air or time left), the IN TROUBLE list in
    /// the top left (everyone who needs help, with which way they are), the CPR card, and a hint while you carry
    /// someone. Drawn with <see cref="Hud"/> on its 1080-high sheet.
    /// </summary>
    public class RescueHud : MonoBehaviour
    {
        // Drawn on the HUD canvas (no IMGUI): see HudCanvas.
        private void OnEnable() => HudCanvas.Add(this, HudCanvas.OrderWorldMarkers, DrawHud);

        private void OnDisable() => HudCanvas.Remove(this);

        private void DrawHud()
        {
            PlayerHub local = PlayerHub.Local;
            Camera cam = local != null && local.Look != null ? local.Look.Camera : null;
            if (cam == null || !GameInput.GameplayActive)
                return;
            var victims = VictimBrain.All;
            _placed.Clear();
            _trouble.Clear();
            for (int i = 0; i < victims.Count; i++)
            {
                VictimBrain v = victims[i];
                VictimState state = v.State;
                if (!state.NeedsHelp()) continue;
                if (v.Item.IsHeld && v.Item.Holder == local) continue; // it's in your arms
                // Silent drowners give no sign: only a marker once you're close (or they're out cold).
                if (v.IsSilent && state.IsStruggling() && Vector3.Distance(local.transform.position, v.transform.position) > 12f) continue;
                _trouble.Add(v);
                DrawMarker(cam, local, v, state);
            }

            DrawTroubleList(cam, local);
            DrawCarryHint(local);
            DrawCpr(local);
        }

        // The CPR panel's words, made again only when the patient, the step or a key changes.
        private VictimBrain _cprPatient;
        private int _cprStamp = -1;
        private readonly string[] _cprKeys = new string[2], _cprWhat = new string[2], _cprCount = new string[2];
        private string _cprPercent;
        private int _cprPercentShown = -1;

        /// <summary>
        /// Looking at someone who needs CPR: the steps, each with its own button (pump the chest; for a man, then a
        /// slap), the one to do now lit up, and how far along they are.
        /// </summary>
        private void DrawCpr(PlayerHub local)
        {
            Interactable target = local.Interactor != null ? local.Interactor.Current : null;
            if (target == null || local.Interactor.CurrentSecondaryPrompt == null || !target.TryGetComponent(out VictimBrain v)) return;
            if (v.State != VictimState.Unconscious || v.IsFlatlined) return;

            CprStep now = v.NextCprStep;
            int steps = v.IsFemale ? 1 : 2; // (a woman: compressions only)
            int stamp = System.HashCode.Combine((int)now, v.CprCount, GameInput.BindingsVersion);
            if (v != _cprPatient || stamp != _cprStamp)
            {
                _cprPatient = v;
                _cprStamp = stamp;
                _cprKeys[0] = GameInput.KeyLabel(VictimBrain.KeyOf(CprStep.Compress));
                _cprKeys[1] = GameInput.KeyLabel(VictimBrain.KeyOf(CprStep.Punch));
                _cprWhat[0] = "PUMP THE CHEST";
                _cprWhat[1] = "SLAP AWAKE";
                _cprCount[0] = $"{(now == CprStep.Compress ? v.CprCount : v.CompressionsPerSet)} / {v.CompressionsPerSet}";
                _cprCount[1] = $"{(now == CprStep.Compress ? 0 : v.CprCount)} / 1";
            }
            int percent = Mathf.RoundToInt(v.Cpr01 * 100f);
            if (percent != _cprPercentShown)
            {
                _cprPercentShown = percent;
                _cprPercent = $"REVIVING  {percent}%";
            }

            // On the right, half way down: clear of the dialogue box and the hotbar (bottom middle), the objective (top
            // left) and the patient in the middle of the view.
            const float chip = 330f, gap = 12f, height = 80f;
            float left = Hud.Width - chip - 48f, top = Hud.Height * 0.5f - height - gap * 0.5f;
            Hud.Surface(new Rect(left - 18f, top - 54f, chip + 36f, 300f), 0.94f);
            Hud.Label(new Rect(left, top - 42f, chip, 28f), "RESCUE  /  CPR", 18f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            for (int i = 0; i < steps; i++)
            {
                bool current = (i == 0) == (now == CprStep.Compress);
                var box = new Rect(left, top + i * (height + gap), chip, height);
                // (No dark boxes: only the current step is framed; the words are outlined straight on the game.)
                if (current) Hud.Frame(box, Hud.Teal, 2f, 10f);
                Color words = current ? Hud.Paper : Hud.Muted;
                var key = new Rect(box.x + 12f, box.y + 11f, 84f, 58f);
                if (current) Hud.Fill(key, Hud.Teal, 8f);
                else Hud.Frame(key, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, 0.5f), 1.5f, 8f);
                Hud.FitLabel(key, _cprKeys[i], 26f, current ? Hud.Ink : words, TextAnchor.MiddleCenter, heavy: true);
                Hud.FitLabel(new Rect(box.x + 108f, box.y + 8f, chip - 120f, 36f), _cprWhat[i], 21f, words, TextAnchor.MiddleLeft, heavy: true);
                Hud.Label(new Rect(box.x + 108f, box.y + 40f, chip - 116f, 30f), _cprCount[i], 21f, words, TextAnchor.MiddleLeft, shadow: false);
            }
            var bar = new Rect(left, top + steps * height + (steps - 1) * gap + 14f, chip, 14f);
            Hud.Bar(bar, v.Cpr01, Hud.Teal);
            Hud.Label(new Rect(bar.x, bar.yMax + 4f, bar.width, 30f), _cprPercent, 17f, Hud.Teal, heavy: true, shadow: false);
        }

        // The labels drawn so far this frame: the next one steps aside rather than land on them.
        private readonly List<Rect> _placed = new();

        private bool Crowded(Rect label)
        {
            if (label.Overlaps(PlayerHud.ToastArea)) return true;
            foreach (Rect r in _placed)
                if (label.Overlaps(r)) return true;
            return false;
        }

        private void DrawMarker(Camera cam, PlayerHub local, VictimBrain v, VictimState state)
        {
            Vector3 world = v.Body.HeadPosition + Vector3.up * 0.7f;
            float distance = Vector3.Distance(local.transform.position, v.transform.position);
            Vector3 screen = cam.WorldToScreenPoint(world);
            float margin = 70f * Hud.Scale;
            // Off the screen: the IN TROUBLE list in the top left points their way (pins on the screen's edges piled
            // up in the corners). Right in front of you: the prompts under the crosshair and the CPR card say it all.
            if (screen.z < 0f || screen.x < margin || screen.x > Screen.width - margin || screen.y < margin || screen.y > Screen.height - margin) return;
            if (distance < 2.6f) return;

            // From here on: sheet units.
            float x = screen.x / Hud.Scale;
            float y = (Screen.height - screen.y) / Hud.Scale;
            Color color = ColorOf(state);
            if (state == VictimState.Unconscious)
                color = Color.Lerp(color, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 8f)));

            const float size = 14f;
            Hud.Fill(new Rect(x - size * 0.5f - 3f, y - size * 0.5f - 3f, size + 6f, size + 6f), new Color(0f, 0f, 0f, 0.6f), size * 0.5f + 3f);
            Hud.Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), color, size * 0.5f);

            string info = state switch
            {
                VictimState.Unconscious when v.IsFlatlined => $"NO PULSE: DEFIBRILLATOR!  {v.ConditionSecondsLeft:F0}s",
                VictimState.Unconscious => $"UNCONSCIOUS  {v.ConditionSecondsLeft:F0}s",
                VictimState.Saved => "SAVED",
                VictimState.Injured when v.IsKnockedOut => "KNOCKED OUT: TO THE INFIRMARY",
                _ => state.Label()
            };
            if (v.HasLostLeg && state != VictimState.Saved && state != VictimState.Lost) info += $"  BLEEDING {v.BleedSecondsLeft:F0}s";
            if (v.Item.IsHeld) info += $"  (with {v.Item.Holder.DisplayName})";
            string text = $"{v.Name}  ·  {distance:F0} m";
            // Label and bar above the dot (clear of the tourist), or below it when pinned to the top edge.
            bool below = y < 100f;
            float infoHeight = Hud.TextHeight(info, 280f, 17f, heavy: true);
            float labelHeight = 40f + infoHeight;
            var rect = new Rect(Mathf.Clamp(x - 156f, 12f, Hud.Width - 324f), below ? y + 18f : y - labelHeight - 30f, 312f, labelHeight);
            // Several tourists the same way (or a message in the top right): stack the labels clear of each other.
            for (int tries = 0; tries < 6 && Crowded(rect); tries++) rect.y += below ? rect.height + 16f : -(rect.height + 16f);
            _placed.Add(new Rect(rect.x, rect.y, rect.width, rect.height + 16f));
            Hud.Surface(rect, 0.8f, color);
            Hud.FitLabel(new Rect(rect.x + 16f, rect.y + 6f, 280f, 27f), text, 21f, Hud.Paper, TextAnchor.MiddleCenter, heavy: true);
            Hud.Label(new Rect(rect.x + 16f, rect.y + 34f, 280f, infoHeight), info, 17f, color, TextAnchor.UpperCenter, heavy: true, wrap: true, shadow: false);

            // Air (awake) or CPR progress (unconscious on land).
            float bar = state == VictimState.Unconscious ? v.Cpr01 : v.Air01;
            if (state.NeedsHelp() && (state != VictimState.Unconscious || bar > 0f))
                Hud.Bar(new Rect(rect.center.x - 45f, rect.yMax + 6f, 90f, 8f), bar,
                    state == VictimState.Unconscious ? new Color(0.5f, 1f, 0.5f) : bar < 0.35f ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.8f, 1f));
        }

        // ------------------------------------------------------------------ in trouble (the list)

        private readonly List<VictimBrain> _trouble = new();
        private const int TroubleRows = 4;
        private static readonly Comparison<VictimBrain> ByUrgency = (a, b) => Urgency(a).CompareTo(Urgency(b));

        /// <summary>
        /// Everyone who needs help, in the top left under the objective, the most urgent first: a little radar (you
        /// the white dot in the middle, them the blip on the ring: turn until it's at the top and they're straight
        /// ahead), the name, what's wrong and how far, and the air they have left. It replaced the pins on the
        /// screen's edges.
        /// </summary>
        private void DrawTroubleList(Camera cam, PlayerHub local)
        {
            if (_trouble.Count == 0) return;
            _trouble.Sort(ByUrgency);
            float x = Hud.Margin, y = Story.StoryHud.TopLeftBottom + 12f;
            Hud.Label(new Rect(x + 2f, y, 320f, 22f), _trouble.Count == 1 ? "IN TROUBLE" : "IN TROUBLE  ·  " + _trouble.Count, 15f, Hud.Coral, TextAnchor.MiddleLeft, heavy: true);
            y += 30f;
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            for (int i = 0; i < _trouble.Count && i < TroubleRows; i++)
            {
                VictimBrain v = _trouble[i];
                VictimState state = v.State;
                Color color = ColorOf(state);
                if (state == VictimState.Unconscious)
                    color = Color.Lerp(color, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 8f)));
                Vector3 to = v.transform.position - cam.transform.position;
                to.y = 0f;
                float turn = Mathf.Repeat(Vector3.SignedAngle(forward, to, Vector3.up) / 360f, 1f);
                var ring = new Rect(x, y + 3f, 42f, 42f);
                Hud.Fill(ring, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.35f), 21f);
                Hud.Arc(ring, 0f, 1f, 0.06f, new Color(1f, 1f, 1f, 0.5f));
                Hud.Fill(new Rect(ring.center.x - 3f, ring.center.y - 3f, 6f, 6f), Hud.Paper, 3f);
                float angle = turn * Mathf.PI * 2f;
                var blip = new Vector2(ring.center.x + Mathf.Sin(angle) * 17f, ring.center.y - Mathf.Cos(angle) * 17f);
                Hud.Fill(new Rect(blip.x - 8f, blip.y - 8f, 16f, 16f), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.8f), 8f);
                Hud.Fill(new Rect(blip.x - 6f, blip.y - 6f, 12f, 12f), color, 6f);
                float distance = Vector3.Distance(local.transform.position, v.transform.position);
                Hud.FitLabel(new Rect(x + 56f, y, 260f, 26f), v.Name, 21f, Hud.Paper, heavy: true);
                Hud.Label(new Rect(x + 56f, y + 25f, 320f, 22f), Status(v, state) + "  ·  " + Mathf.RoundToInt(distance) + " m", 16f, color, TextAnchor.MiddleLeft, heavy: true);
                if (state.IsStruggling())
                {
                    float air = v.Air01;
                    Hud.Bar(new Rect(x + 56f, y + 51f, 150f, 6f), air, air < 0.35f ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.8f, 1f));
                }
                y += 66f;
            }
            if (_trouble.Count > TroubleRows)
                Hud.Label(new Rect(x + 56f, y - 6f, 300f, 22f), "+" + (_trouble.Count - TroubleRows) + " more", 16f, Hud.Muted, TextAnchor.MiddleLeft, heavy: true);
        }

        /// <summary>Lower comes first: out cold by the seconds left, then drowning by air left, then the rest.</summary>
        private static float Urgency(VictimBrain v)
        {
            VictimState s = v.State;
            float urgency = s switch
            {
                VictimState.Unconscious => v.ConditionSecondsLeft,
                VictimState.Drowning => 200f + v.Air01 * 100f,
                VictimState.Injured => v.HasLostLeg ? v.BleedSecondsLeft : 600f,
                VictimState.Panicking => 400f + v.Air01 * 100f,
                _ => 700f + v.Air01 * 100f
            };
            return v.Item.IsHeld ? urgency + 2000f : urgency; // (someone has them already)
        }

        private static string Status(VictimBrain v, VictimState state)
        {
            if (v.Item.IsHeld) return "WITH " + v.Item.Holder.DisplayName.ToUpperInvariant();
            if (v.HasLostLeg) return "BLEEDING " + Mathf.CeilToInt(v.BleedSecondsLeft) + "s";
            return state switch
            {
                VictimState.Unconscious when v.IsFlatlined => "NO PULSE " + Mathf.CeilToInt(v.ConditionSecondsLeft) + "s",
                VictimState.Unconscious => "OUT COLD " + Mathf.CeilToInt(v.ConditionSecondsLeft) + "s",
                VictimState.Injured when v.IsKnockedOut => "KNOCKED OUT",
                _ => state.Label().ToUpperInvariant()
            };
        }

        private void DrawCarryHint(PlayerHub local)
        {
            var held = local.Hands != null ? local.Hands.HeldItem : null;
            if (held == null || !held.TryGetComponent(out VictimBrain v)) return;
            string drop = GameInput.KeyLabel(GameInput.Drop);
            string cpr = GameInput.KeyLabel(GameInput.Secondary);
            string text = v.HasLostLeg && v.State != VictimState.Saved
                ? $"Rush <b>{v.Name}</b> to the hotel infirmary and put them on the bed (<b>[{GameInput.KeyLabel(GameInput.Interact)}]</b>)  -  {v.BleedSecondsLeft:F0}s left"
                : v.State == VictimState.Unconscious
                ? $"Get <b>{v.Name}</b> onto the sand, put them down (tap <b>[{drop}]</b>) and do CPR (<b>[{cpr}]</b>)  -  {v.ConditionSecondsLeft:F0}s left"
                : v.State.NeedsHelp()
                    ? $"Bring <b>{v.Name}</b> back to the shallows!"
                    : $"Carrying <b>{v.Name}</b>";
            // Where the held item's name and keys would be.
            float width = Mathf.Min(960f, Hud.Width - 160f);
            float height = Hud.TextHeight(text, width - 40f, 22f) + 24f;
            var card = new Rect((Hud.Width - width) * 0.5f, Hud.HotbarTop - height - 18f, width, height);
            Hud.Surface(card, 0.9f, Hud.Sand);
            Hud.Label(new Rect(card.x + 20f, card.y + 12f, width - 40f, height - 24f), text, 22f, Hud.Sand, wrap: true, shadow: false);
        }

        private static Color ColorOf(VictimState s) => s switch
        {
            VictimState.Distressed => new Color(1f, 0.9f, 0.35f),
            VictimState.Panicking => new Color(1f, 0.6f, 0.2f),
            VictimState.Drowning => new Color(1f, 0.4f, 0.25f),
            VictimState.Unconscious => new Color(1f, 0.2f, 0.2f),
            VictimState.Saved => new Color(0.45f, 1f, 0.5f),
            VictimState.Injured => new Color(1f, 0.35f, 0.35f),
            _ => Color.white
        };
    }
}
