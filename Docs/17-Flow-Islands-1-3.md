# How the story flows through islands 1-3 (and what How to Fish taught us)

> Written 11 October 2026 (branch `shako-dev`) after Shako asked for the whole flow to work "like How to Fish":
> logical spawns and missions, and island 3 starting like a boss fight where the pirates and parrots mustn't see you.

## What How to Fish does (decompiled reference, mechanics only)

- **Islands are unlocked by NPC quests** (`NPCQuest`, `QuestType.UnlockIsland`) and reached by **boat**; each island
  is its own scene.
- **A boss is summoned, not met by accident**: you prepare, then start it (a boss bait). It is **introduced**: boss
  music intro, a **boss bar** (name, health that shakes when hit, a lagging "damage" bar), health **scaled by the number
  of players**, a **time limit** (a ring, the last 10 seconds counted; out of time = it leaves), everyone dead = it
  goes away (try again), a trophy when it dies (`BossManager`, `BossUI`).
- Players who wander away from the group get an "island warning" (`IslandWarning`).

## Our flow now

| Step | What happens | Logic |
|---|---|---|
| 1.1-1.10 | Island 1 as before; jet skis south | unchanged |
| 2.1-2.5 | Hotel: check in, pistol, three guests | 2.4 the shark: saving the guest **before** the bite now ends the beat (it used to wait for a bite forever) |
| 2.5b | Kleptopus | rises from under the pier when you get there; **boss intro** (title card + boss sting + drums) |
| 2.6-2.7 | Pirate raid, take their boat | Skull Cove **comes alive as you set sail** (out of anyone's sight) |
| 3.1 | **Arrival = boss fight** | starts when you come in to the dock: card "SKULL COVE", sting, a boss bar "Skull Cove's lookouts" that empties as you near the THIEF's shack, a 5-minute clock ring. Seen / too slow / everyone down = back to the landing, lookouts reset |
| 3.2 | The THIEF | lives on his porch; the disguises go on **the moment you reach his door** (a lookout perches by it) |
| 3.3 | Gun stall | unchanged |
| 3.4 | Iron Jaw Jack | has been in the tavern all along; steps up with a boss intro; stays there afterwards |
| 3.5-3.6 | Jungle, river | their guards, monkeys and alligators live there; a failed try resets only them |
| 3.7 | Old Snapper | **asleep low in its pond** (no bar, no attacks, bullets splash) until you come; then it rises with a boss intro |
| 3.8 | The gold vault | inside the mountain behind the crag falls (the THIEF and Jack now both say so) |

Code: `StoryDirector.Residents.cs` (who lives on Skull Cove, reset per group), `StoryDirector.BossBar.cs` (boss intro,
the mission boss bar and clock), `Creature.Snapper.cs` (`ServerSleep` / `ServerWake`), `ThreatHud.DrawMissionBossBar`,
`MusicSynth.RenderBoss`.

## Tests

- `chapter3check`, `kleptocheck`, `threatcheck` (console, host): all pass.
- `-pdd-autoplay 3.8` plays the **whole story by itself**, 1.1 to the end of chapter 3 (638 game seconds at
  `-pdd-timescale 2`): PASS, 0 errors.

## Open (needs Shako)

- The arrival's 5-minute clock: keep, lengthen or drop?
- Tourists still appear straight in the water on islands 1-2; they could walk in from the beach crowd first.
- Free play's line still says "trickshot them back in" (stunt rewards are on the removal list: confirm by screenshot).
