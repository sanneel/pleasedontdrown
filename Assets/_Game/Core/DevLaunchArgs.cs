using System;
using System.Collections;
using PleaseDontDrown.Net;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Command-line switches for automated and multi-instance testing:
    ///   -pdd-nosteam            skip Steam init (read by SteamBootstrap)
    ///   -pdd-host-offline       start a local host immediately
    ///   -pdd-join &lt;address&gt;    join a local/LAN host immediately
    ///   -pdd-quit-after &lt;sec&gt;  quit after N seconds (smoke tests)
    ///   -pdd-noinput            ignore real input devices (automated windowed tests)
    /// </summary>
    public class DevLaunchArgs : MonoBehaviour
    {
        [SerializeField] private ConnectionService _connection;

        public static bool Has(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

        public static string Value(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private IEnumerator Start()
        {
            if (Has("-pdd-noinput"))
            {
                GameInput.IgnoreDevices = true; // tests: real mouse/keyboard can't steer this window
                GameInput.Apply();
            }
            if (float.TryParse(Value("-pdd-quit-after"), out float quitAfter))
                StartCoroutine(QuitAfter(quitAfter));

            yield return null; // let every service finish Start()

            if (Has("-pdd-host-offline"))
            {
                Debug.Log("[Dev] Auto-hosting offline");
                _connection.HostOffline();
            }
            else if (Value("-pdd-join") is { } address)
            {
                Debug.Log($"[Dev] Auto-joining {address}");
                _connection.JoinOffline(address);
            }

            // Console commands to run once our player exists, e.g. -pdd-exec "ring; lights".
            if (Value("-pdd-exec") is { } commands)
            {
                float deadline = Time.realtimeSinceStartup + 15f;
                while (Player.PlayerHub.Local == null && Time.realtimeSinceStartup < deadline)
                    yield return null;
                yield return new WaitForSecondsRealtime(1f);
                Debug.Log($"[Dev] Executing: {commands}");
                DevCommands.Output += line => Debug.Log($"[Console] {line}");
                // "wait <seconds>" pauses the script between commands.
                foreach (string raw in commands.Split(';'))
                {
                    string command = raw.Trim();
                    if (command.StartsWith("wait ") && float.TryParse(command.Substring(5), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float seconds))
                    {
                        yield return new WaitForSecondsRealtime(seconds);
                        continue;
                    }
                    Debug.Log($"[Dev] > {command}");
                    DevCommands.Execute(command);
                }
            }
        }

        private static IEnumerator QuitAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.Log("[Dev] Quit timer elapsed");
            Application.Quit();
        }
    }
}
