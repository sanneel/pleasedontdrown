---
name: fix-bug
description: Find and fix a bug in the game: something stuck, frozen, glitching, falling through, looking wrong, too easy or too hard. Use when the person reports a problem, even vaguely ("she gets stuck", "it's weird", "doesn't work"), in any language.
---

# Fix a bug

## 1. Understand the report

Non-programmers describe what they saw, not the cause. Pin down, asking at most one or two short questions if the
report doesn't say:
- where (island 1, island 2, dev island), and which story beat or attraction (`story` in the console says the beat);
- what they did just before, what they expected, what happened instead;
- solo or with friends, host or joined;
- every time, or sometimes.

Restate it in one sentence before changing code ("When the first woman you revive should lead you to the hut, she
stops and you can't move").

## 2. Find the code

Use the project map and the doc table in `CLAUDE.md`, then grep for words from the game (dialogue lines, item
names, console command names, log tags). Read the code path from the trigger to the symptom. Don't stop at the
first plausible cause: list the ways it can go wrong and rank them.

## 3. Edge cases this game keeps hitting

Check each one that applies:
- **Waiting forever.** Story coroutines that `while (...) yield` on a distance or condition need a timeout and a
  fallback (teleport, skip, retry). Check `IsMoving`, route-ended events and `null` (despawned) objects.
- **Something in the way.** `StoryNpc.ObstacleAhead` treats any rigidbody as a wall, including players, items and
  boats. The navmesh doesn't know about rigidbodies.
- **Water.** Depth includes waves, so thresholds flicker near the shoreline. `StoryNpc.ServerMoveTo(..., water: true)`
  is needed for walks that may start in the shallows.
- **Physics bodies.** Tourists (`VictimBody`) are forces and joints: posture bugs are torque, height targets and joint
  limits. Players are rigidbodies driven by `PlayerMotor`.
- **Host and client.** Story, victims, NPCs and money run on the host only. A bug only a joined friend sees is
  usually a missing sync (SyncVar, ObserversRpc) or code that runs only `IsServerInitialized`.
- **Restart and reload.** `story reset`, `story goto`, leaving to the menu and hosting again: statics need
  `ResetStatics`, coroutines need to stop, spawned things need cleaning up.
- **Several at once.** Two players, two tourists, two items in the same spot, a second press while the first is
  still running.
- **Frame rate.** Anything per frame should use `Time.deltaTime` (or `fixedDeltaTime` in `FixedUpdate`), so it
  behaves the same at 30 and 240 fps.

## 4. Fix

- Fix the cause, with the smallest change that does it. Add a safety net only where waiting on the world can
  never be guaranteed (and log a line when the safety net kicks in, with a tag like `[Story]`).
- Don't change unrelated behaviour, tuning or art on the way.
- If the honest fix is big or changes how something plays, explain the options in plain words and let them choose.

## 5. Hand it over

Explain the cause in one or two plain sentences, what you changed, and give the `test-change` recipe that
reproduces the original problem. Mention if you couldn't verify it in Unity. Offer to save it with `save-work`.
