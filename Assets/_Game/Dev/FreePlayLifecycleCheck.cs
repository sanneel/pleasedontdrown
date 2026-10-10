using System;
using System.Collections;
using System.Reflection;
using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Isolated development player check: -pdd-freeplaycheck -pdd-nosave -pdd-host-offline -pdd-nosteam
    /// -pdd-noinput -pdd-port 7794. Exercises the menu's actual leave/rehost path without saving a scene or game.
    /// </summary>
    public class FreePlayLifecycleCheck : MonoBehaviour
    {
        private int _errors, _tourists;
        private bool _watchStopped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!DevLaunchArgs.Has("-pdd-freeplaycheck")) return;
            var root = new GameObject("FreePlayLifecycleCheck");
            DontDestroyOnLoad(root);
            root.AddComponent<FreePlayLifecycleCheck>();
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error or LogType.Assert) _errors++;
            if (!message.StartsWith("[Story] tourist ", StringComparison.Ordinal)) return;
            _tourists++;
            if (_watchStopped) _errors++;
        }

        private static bool HostReady() => ConnectionService.Instance != null &&
            ConnectionService.Instance.NetworkManager.IsServerStarted && PlayerHub.Local != null &&
            StoryDirector.Instance != null && StoryDirector.Instance.IsServerInitialized &&
            !string.IsNullOrEmpty(StoryDirector.Instance.BeatId);

        private IEnumerator WaitFor(Func<bool> ready, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < end) yield return null;
        }

        // Fixture setup only: bypass the completed story's boat ride. All leave/rehost operations use the game API.
        private static void ArrangeOpenShift()
        {
            StoryDirector story = StoryDirector.Instance;
            story.ServerCheat("story:off", 0, PlayerHub.Local, true);
            PlayerHub.Local.Motor.Teleport(new Vector3(0f, 2f, 10f));
            typeof(StoryDirector).GetMethod("BeginFreePlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(story, null);
        }

        private IEnumerator Start()
        {
            if (!DevLaunchArgs.Has("-pdd-nosave"))
            {
                Finish(false, "-pdd-nosave required to protect the real story save");
                yield break;
            }
            Time.timeScale = 2f;
            yield return WaitFor(HostReady, 45f);
            if (!HostReady()) { Finish(false, "first host never became ready"); yield break; }
            yield return new WaitForSeconds(2f); // Boot has finished before arranging the open shift.
            ArrangeOpenShift();
            yield return new WaitForSeconds(1f); // Stop during the free-play spawner's initial wait.
            ConnectionService session = ConnectionService.Instance;
            StoryDirector first = StoryDirector.Instance;
            session.Leave();
            yield return WaitFor(() => !session.IsActive && !first.IsServerInitialized, 10f);
            if (session.IsActive || first.IsServerInitialized || first.IsFreePlay)
            {
                Finish(false, "server or open shift remained active after Leave");
                yield break;
            }
            _watchStopped = true;
            _tourists = 0;
            yield return new WaitForSeconds(45f); // Longer than a full first-spawn interval.
            if (_tourists != 0) { Finish(false, "tourists spawned while host stopped"); yield break; }
            _watchStopped = false;
            if (!session.HostOffline()) { Finish(false, "rehost rejected"); yield break; }
            yield return WaitFor(HostReady, 45f);
            if (!HostReady()) { Finish(false, "second host never became ready"); yield break; }
            yield return new WaitForSeconds(2f);
            ArrangeOpenShift();
            _tourists = 0;
            yield return new WaitForSeconds(43f);
            // One fresh loop first spawns after 26–42 s and can spawn at most twice by 43 s.
            bool valid = StoryDirector.Instance.IsFreePlay && _tourists >= 1 && _tourists <= 2;
            Finish(valid, $"rehost open shift spawned {_tourists} tourists; {_errors} logged errors");
        }

        private void Finish(bool passed, string detail)
        {
            bool clean = passed && _errors == 0;
            Debug.Log($"[FreePlayLifecycle] RESULT {(clean ? "PASS" : "FAIL")}: {detail}");
            Application.Quit(clean ? 0 : 1);
        }
    }
}
