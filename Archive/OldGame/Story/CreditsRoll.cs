using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The credits at the end of chapter 4 (every machine): the title, the team, how it was made and where the models
    /// came from, rolling up the screen over the beach (the cast is still waving behind it). Drawn only with the HUD's
    /// own pieces (<see cref="Hud.Title"/> outlined titles, <see cref="Hud.Label"/> shadowed text): no boxes behind
    /// the text. <see cref="Show"/> starts it; it ends by itself.
    /// </summary>
    public sealed class CreditsRoll : MonoBehaviour
    {
        public const float Seconds = 34f;

        private static CreditsRoll _instance;
        private float _started;

        /// <summary>Every machine: the credits are rolling.</summary>
        public static bool Rolling => _instance != null;

        private enum Kind : byte { Title, Heading, Name, Small, Gap }

        private static readonly (Kind kind, string text)[] Lines =
        {
            (Kind.Title, "PLEASE DON'T DROWN"),
            (Kind.Small, "Thanks for playing to the end. Everybody got saved. Even Sandy."),
            (Kind.Gap, ""),
            (Kind.Heading, "THE TEAM"),
            (Kind.Name, "Sandro"),
            (Kind.Name, "Shako"),
            (Kind.Gap, ""),
            (Kind.Heading, "MADE WITH"),
            (Kind.Name, "Claude"),
            (Kind.Small, "code, story and the islands, built with Claude Code"),
            (Kind.Name, "Unity 6 and Fish-Networking"),
            (Kind.Gap, ""),
            (Kind.Heading, "THE MODELS"),
            (Kind.Name, "Tripo"),
            (Kind.Small, "the characters, the pirates, the hotel, the yacht, the creatures, the island 1-3 props"),
            (Kind.Name, "Meshy"),
            (Kind.Small, "Sandy, the palms, the jet ski, the pirate boat, the shark, the towers"),
            (Kind.Name, "Blender"),
            (Kind.Small, "the hand-made props (stools, tables, khinkali...) and the clean-up"),
            (Kind.Small, "Islands, docks, Finders Keepers: built in code"),
            (Kind.Gap, ""),
            (Kind.Heading, "FONTS"),
            (Kind.Small, "Lilita One and Roboto (Open Font License / Apache)"),
            (Kind.Gap, ""),
            (Kind.Heading, "STARRING"),
            (Kind.Small, "Sandy, Marisol, Milo, the THIEF, Iron Jaw Jack, the Pirate King, Old Snapper, the Kleptopus, Grabby"),
            (Kind.Small, "and every tourist who ever went out of their depth"),
            (Kind.Gap, ""),
            (Kind.Title, "THE OPEN SHIFT STARTS NOW"),
            (Kind.Small, "Four islands. Endless tourists. Please don't drown."),
        };

        /// <summary>Every machine: roll the credits (once at a time).</summary>
        public static void Show()
        {
            if (_instance != null) return;
            var go = new GameObject("CreditsRoll");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CreditsRoll>();
            _instance._started = Time.unscaledTime;
            Debug.Log("[Credits] rolling");
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private static float HeightOf(Kind k) => k switch { Kind.Title => 110f, Kind.Heading => 70f, Kind.Name => 58f, Kind.Small => 40f, _ => 54f };

        private void OnGUI()
        {
            float age = Time.unscaledTime - _started;
            if (age > Seconds) { Destroy(gameObject); return; }
            GUI.depth = -200;
            float total = 0f;
            foreach (var l in Lines) total += HeightOf(l.kind);
            // From just below the screen upward; the last line (the open shift) stops in the middle.
            float y0 = Hud.Height - Mathf.Min(1f, age / (Seconds - 5f)) * (Hud.Height * 0.5f + total - 70f);
            float fade = Mathf.Clamp01(age / 0.8f) * Mathf.Clamp01((Seconds - age) / 1.5f);
            float y = y0, w = Mathf.Min(1100f, Hud.Width - 80f), x = (Hud.Width - w) * 0.5f;
            foreach (var (kind, text) in Lines)
            {
                float h = HeightOf(kind);
                if (y > -h && y < Hud.Height + h && kind != Kind.Gap)
                {
                    var r = new Rect(x, y, w, h);
                    switch (kind)
                    {
                        case Kind.Title: Hud.Title(r, text, 78f, Fade(Hud.Sand, fade)); break;
                        case Kind.Heading: Hud.Title(r, text, 40f, Fade(Hud.Teal, fade)); break;
                        case Kind.Name: Hud.Label(r, text, 36f, Fade(Hud.Paper, fade), heavy: true); break;
                        default: Hud.Label(r, text, 24f, Fade(Hud.Muted, fade)); break;
                    }
                }
                y += h;
            }
        }

        private static Color Fade(Color c, float a)
        {
            c.a *= a;
            return c;
        }
    }
}
