using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>One spoken line, as shown in the subtitles.</summary>
    public struct DialogueLine
    {
        public string Speaker;
        public string Text;
        public Color Color;
        public float Start;
        public float Duration;
    }

    /// <summary>
    /// Conversations: the host says a line (with who's speaking), every player sees it as a subtitle, the NPC's
    /// mouth moves and they babble it out loud (<see cref="Audio.SpeechVoice"/>). Lines advance on their own after a reading time, so co-op players never wait on each other.
    /// </summary>
    public class DialogueService : NetworkBehaviour
    {
        [SerializeField] private float _secondsPerCharacter = 0.045f;
        [SerializeField] private float _minSeconds = 2.2f;

        private static readonly List<DialogueLine> _history = new();

        public static DialogueService Instance { get; private set; }
        /// <summary>The line on screen now (local).</summary>
        public static DialogueLine? Current { get; private set; }

        /// <summary>The player's own lines use this speaker name and show as "You".</summary>
        public const string PlayerSpeaker = "@player";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _history.Clear();
            Current = null;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>How long a line stays up.</summary>
        public float DurationOf(string text) => Mathf.Max(_minSeconds, 1.2f + (text?.Length ?? 0) * _secondsPerCharacter);

        /// <summary>Host: say a line. Returns how long it stays on screen (story beats wait that long).</summary>
        [Server]
        public float ServerSay(string speaker, string text, StoryNpc npc = null, Color? color = null, float seconds = 0f)
        {
            float duration = seconds > 0f ? seconds : DurationOf(text);
            Color c = color ?? (npc != null ? npc.SpeechColor : new Color(0.75f, 0.9f, 1f));
            SayObservers(speaker ?? string.Empty, text ?? string.Empty, npc, c, duration);
            Debug.Log($"[Dialogue] {speaker}: {text}");
            return duration;
        }

        [ObserversRpc]
        private void SayObservers(string speaker, string text, StoryNpc npc, Color color, float duration)
        {
            var line = new DialogueLine { Speaker = speaker, Text = text, Color = color, Start = Time.time, Duration = duration };
            Current = line;
            _history.Add(line);
            if (_history.Count > 30) _history.RemoveAt(0);
            if (npc != null)
            {
                FloatingText.SpawnSpeech(npc, text, duration);
                npc.OnSpeak(duration, text);
            }
            else
            {
                // Nobody in the world to say it: the player's own line (in their own kind of voice), or a voice from nowhere.
                bool mine = speaker == PlayerSpeaker;
                bool feminine = mine && UI.AvatarCustomizer.LocalLook.Feminine;
                Audio.SpeechVoice.Local.Speak(text, mine ? (feminine ? 2 : 1) : 0, mine ? 1f : 0.92f, 0.8f);
            }
        }

        private void Update()
        {
            if (Current.HasValue && Time.time > Current.Value.Start + Current.Value.Duration + 0.3f)
                Current = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("dialogue", "", "Print the last lines of dialogue.", _ =>
            {
                foreach (DialogueLine l in _history) DevCommands.Print($"  {(l.Speaker == PlayerSpeaker ? "You" : l.Speaker)}: {l.Text}");
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("dialogue", this);
        }
    }
}
