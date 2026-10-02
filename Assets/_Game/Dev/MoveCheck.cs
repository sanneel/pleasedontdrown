using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Checks that the body moves as designed, for automated tests: <c>-pdd-movecheck</c> on a host (use
    /// <c>-pdd-nostory</c> with it). On the station's dock it walks, sprints and jumps on the same autopilot the
    /// console's <c>walk</c> command uses (the real motor, no teleporting), runs off the end into the sea, swims,
    /// dives and climbs back out, measuring each against the numbers the motor is set to. The log gets a line per
    /// step and a last line <c>[MoveCheck] RESULT PASS</c> or <c>FAIL</c>, then the game quits.
    /// It tests that the mechanics work, not how they feel: that still takes a person.
    /// </summary>
    public class MoveCheck : MonoBehaviour
    {
        // The station dock: deck 0.3 m above the sand, from z 6 (land) out to z -19 (deep water), at x -8.
        private static readonly Vector3 DockStart = new(-8f, 0.4f, 4.5f), DockEnd = new(-8f, 0.4f, -19f);

        private readonly List<string> _problems = new();
        private int _errorCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!DevLaunchArgs.Has("-pdd-movecheck")) return;
            var go = new GameObject("MoveCheck");
            DontDestroyOnLoad(go);
            go.AddComponent<MoveCheck>();
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type is LogType.Exception or LogType.Error or LogType.Assert) _errorCount++;
        }

        private static void Say(string text) => Debug.Log($"[MoveCheck] {text}");

        private void Expect(bool ok, string what)
        {
            Say((ok ? "ok    " : "WRONG ") + what);
            if (!ok) _problems.Add(what);
        }

        private IEnumerator Start()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (PlayerHub.Local == null && Time.realtimeSinceStartup < deadline) yield return null;
            PlayerHub me = PlayerHub.Local;
            if (me == null)
            {
                Say("RESULT FAIL: no player (start with -pdd-host-offline)");
                Application.Quit(1);
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            PlayerMotor motor = me.Motor;

            // Walk: along the dock toward the sea.
            motor.Teleport(DockStart);
            me.Look.LookAt(DockEnd + Vector3.up * 1.6f);
            yield return new WaitForSeconds(0.6f);
            Expect(motor.IsGrounded, "standing on the dock");
            Vector3 from = me.transform.position;
            DevCommands.Execute("walk 2");
            yield return new WaitForSeconds(1.9f);
            float walk = motor.HorizontalSpeed;
            Expect(walk > 4.5f && walk < 5.5f, $"walking speed {walk:0.0} m/s (set to 5)");
            Expect(Mathf.Abs(me.transform.position.x - from.x) < 0.4f, "walks straight where it looks");
            yield return new WaitForSeconds(0.6f);
            Expect(motor.HorizontalSpeed < 0.5f, $"stops when the keys are let go ({motor.HorizontalSpeed:0.0} m/s after 0.5 s)");

            // Sprint.
            DevCommands.Execute("walk 1.5 sprint");
            yield return new WaitForSeconds(1.4f);
            float sprint = motor.HorizontalSpeed;
            Expect(sprint > 6.8f && sprint < 8.2f, $"sprinting speed {sprint:0.0} m/s (set to 7.5)");
            yield return new WaitForSeconds(0.8f);

            // Jump: how high the feet get.
            float ground = me.transform.position.y, highest = ground;
            DevCommands.Execute("jump");
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                highest = Mathf.Max(highest, me.transform.position.y);
                yield return null;
            }
            Expect(highest - ground > 0.9f && highest - ground < 1.8f, $"jump height {highest - ground:0.00} m");
            Expect(motor.IsGrounded, "lands again");

            // Off the end of the dock into the sea, and swim on.
            DevCommands.Execute("walk 8 sprint");
            float until = Time.time + 6f;
            while (!motor.IsSwimming && Time.time < until) yield return null;
            Expect(motor.IsSwimming, $"swimming after running off the dock (at {me.transform.position:F1})");
            yield return new WaitForSeconds(1.8f);
            float swim = motor.HorizontalSpeed;
            Expect(swim > 1.2f && swim < 6f, $"swimming speed {swim:0.0} m/s");
            float stamina = motor.Stamina01;
            Expect(stamina < 0.999f, $"swimming fast uses stamina ({stamina:P0} left)");
            yield return new WaitForSeconds(0.4f);

            // Dive, then come back up for air.
            DevCommands.Execute("dive 2.5");
            yield return new WaitForSeconds(2.2f);
            Expect(motor.IsHeadUnderwater, "head under water while diving");
            float air = motor.Air01;
            Expect(air < 0.999f, $"holding breath uses air ({air:P0} left)");
            yield return new WaitForSeconds(4f);
            Expect(!motor.IsHeadUnderwater, "floats back up to the surface");
            yield return new WaitForSeconds(2.5f);
            Expect(motor.Air01 > air, $"air comes back at the surface ({motor.Air01:P0})");

            // Climb out: beside the dock, facing it.
            motor.Teleport(new Vector3(-10.1f, -1.2f, -8f));
            me.Look.LookAt(new Vector3(-8f, 0.6f, -8f));
            yield return new WaitForSeconds(1.2f);
            DevCommands.Execute("climb");
            yield return new WaitForSeconds(2.5f);
            Expect(!motor.IsSwimming && me.transform.position.y > 0.2f, $"climbs out onto the dock (at {me.transform.position:F1})");

            bool passed = _problems.Count == 0 && _errorCount == 0;
            Say($"RESULT {(passed ? "PASS" : "FAIL")}: {_problems.Count} wrong, {_errorCount} errors logged");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(passed ? 0 : 1);
        }
    }
}
