using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Registry for console commands. Systems register their own commands (usually when they become active)
    /// and unregister on teardown, so the console only offers what currently makes sense.
    /// </summary>
    public static class DevCommands
    {
        public sealed class Command
        {
            public string Name;
            public string Usage;
            public string Help;
            public bool IsCheat;
            public Action<string[]> Run;
            public object Owner;
        }

        private static readonly Dictionary<string, Command> _commands = new(StringComparer.OrdinalIgnoreCase);

        public static event Action<string> Output;

        /// <summary>Cheats work in the editor and development builds; release builds decide later (host-only).</summary>
        public static bool CheatsAllowed => Application.isEditor || Debug.isDebugBuild;

        public static IEnumerable<Command> All => _commands.Values.OrderBy(c => c.Name);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _commands.Clear();
            Output = null;
            RegisterBuiltIns();
        }

        /// <param name="owner">Who registered it; only the same owner can unregister it (null = permanent/global).</param>
        public static void Register(string name, string usage, string help, Action<string[]> run, bool cheat = false, object owner = null) =>
            _commands[name] = new Command { Name = name, Usage = usage, Help = help, Run = run, IsCheat = cheat, Owner = owner };

        public static void Unregister(string name, object owner = null)
        {
            if (_commands.TryGetValue(name, out Command c) && c.Owner == owner)
                _commands.Remove(name);
        }

        public static void Print(string line) => Output?.Invoke(line);

        /// <summary>Runs one or more commands separated by ';'.</summary>
        public static void Execute(string input)
        {
            foreach (string part in input.Split(';'))
            {
                string[] tokens = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                    continue;

                if (!_commands.TryGetValue(tokens[0], out Command cmd))
                {
                    Print($"<color=#ff8080>Unknown command '{tokens[0]}'. Type 'help'.</color>");
                    continue;
                }
                if (cmd.IsCheat && !CheatsAllowed)
                {
                    Print($"<color=#ff8080>'{cmd.Name}' is a cheat and cheats are disabled.</color>");
                    continue;
                }
                try
                {
                    cmd.Run(tokens.Skip(1).ToArray());
                }
                catch (Exception e)
                {
                    Print($"<color=#ff8080>{cmd.Name} failed: {e.Message}</color>");
                    if (!string.IsNullOrEmpty(cmd.Usage))
                        Print($"usage: {cmd.Name} {cmd.Usage}");
                }
            }
        }

        public static string Complete(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains(' '))
                return prefix;
            var matches = _commands.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
            if (matches.Count == 1)
                return matches[0] + " ";
            if (matches.Count > 1)
                Print(string.Join("  ", matches));
            return prefix;
        }

        public static float ParseFloat(string[] args, int index)
        {
            if (index >= args.Length || !float.TryParse(args[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                throw new ArgumentException($"expected a number at argument {index + 1}");
            return value;
        }

        private static void RegisterBuiltIns()
        {
            Register("help", "[command]", "List commands, or show help for one.", args =>
            {
                if (args.Length > 0 && _commands.TryGetValue(args[0], out Command c))
                {
                    Print($"{c.Name} {c.Usage} - {c.Help}{(c.IsCheat ? " (cheat)" : "")}");
                    return;
                }
                foreach (Command command in All)
                    Print($"  <b>{command.Name}</b> {command.Usage}  <color=#aaaaaa>{command.Help}</color>{(command.IsCheat ? " <color=#ffcc66>(cheat)</color>" : "")}");
            });
            Register("timescale", "<x>", "Slow motion / fast forward (1 = normal).", args =>
            {
                Time.timeScale = Mathf.Clamp(ParseFloat(args, 0), 0.05f, 4f);
                Print($"timescale = {Time.timeScale}");
            }, cheat: true);
            Register("quit", "", "Quit the game.", _ =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }
    }
}
