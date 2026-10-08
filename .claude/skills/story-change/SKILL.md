---
name: story-change
description: Change the story, dialogue, jokes (gags), rescues, objectives or how story characters behave. Use when the person wants to add, cut, reorder or reword a story beat, a line someone says, a rescue gag, a quest step, or what an NPC like Sandy does.
---

# Change the story

## Where things are

- **Beat list (the source of truth):** `BuildBeats()` at the top of `Assets/_Game/Story/StoryDirector.Beats.cs`.
  Each `Add("1.2", "First rescue", FirstShift)` is one beat: an id, a title, and the coroutine that plays it.
  Chapter 1 ids start with `1.`, chapter 2 with `2.`. Ids are saved in players' save files, so don't renumber
  existing beats; give new ones a new id and add them in order.
- **Beat code:** the coroutines in `StoryDirector.Beats.cs` (most beats), `StoryDirector.Tsunami.cs` (chapter 1
  tsunami and rental fleet), `StoryDirector.Gags.cs` (rescue gags, the hut scene, the false alarm).
  Shared helpers are in `StoryDirector.cs`: `Say(npc, text)`, `SetObjective(text, done, need)`, `SpawnNpc`,
  `SpawnStoryTourist`, `WaitTalk`, `GagRescue`.
- **Rescue gags:** the `Island1Gags` / island 2 arrays in `StoryDirector.Gags.cs`. Each has `Shouts` (lines split
  by `|`), `Spotted` (the guide's line, with `{name}`, `{he}`, `{him}`, `{his}`, `{himself}`), `After`, `Drop`.
- **Characters:** `StoryNpc` (walking, talking, routes), `StoryNpc.Brains.cs` (what each role does on its own),
  `BeachCrowd` (ambient tourists who become rescue victims).
- **Design docs:** `Docs/05-Story-Mode.md` (chapters 1-2) and `Docs/14-Tsunami-Island-1.md` (chapter 1 now: the
  tsunami replaced the old thief and drug beats). Where docs and `BuildBeats()` disagree, the code is right; fix
  the doc.
- **Old help text:** the `story list` console help in `StoryDirector.cs` may list old beats. Update it when you
  change the beat list.

## Rules

- The story runs on the **host**. Everything a beat changes for other players must go through the existing
  Server/Observers calls, SyncVars or `SetObjective`, never local-only code.
- Every wait in a beat needs a way out: a timeout, an `IsMoving` / null check, or a fallback (teleport, skip). A
  wait that can hang leaves players stuck with no objective.
- Spawned characters and items must be cleaned up (`_spawnedActors`, `RemoveLater`) so `story reset` and
  `story goto` start clean.
- Dialogue should sound like the existing lines: short, silly, plain words. Match the voice of the speaker.
- If the person writes lines in Georgian, ask whether the game text should be English (the game is in English now)
  or whether they want it translated.

## Test

Tell them: Play, then `story goto <id>` (or `story reset` for the start), play the beat, and check the objective
text at the top left. For a full run, `powershell -File Tools\test-story.ps1` plays chapter 1 by itself, and
`-Last 2.7` plays both chapters. If a beat was added, check `Dev/StoryAutoplay.cs` knows how to play it, or the
autoplay test will get stuck there.
