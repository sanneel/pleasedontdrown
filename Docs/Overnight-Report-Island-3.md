# Report: finishing island 3 (Shako's Claude, 2026-10-18)

> Task: `Docs/Overnight-Prompt-Shako-Island-3.md`. Branch `shako-dev`, pushed. Pictures: `Docs/Overnight-Island-3/`,
> all from the real game build (player's eyes; the movie camera doesn't draw inside the keep).

## Short version

| # | Task | State |
|---|---|---|
| 0.2 | Hotel texture fix "not on GitHub" | **Nothing to push**: there is no such change on this PC (see below) |
| 2.1 | Sewer + throne room, beat 3.7b | **Done**: 3.7b plays (sewer, palace, the Pirate King in his own model, fight, his lines) |
| 2.2 | Chapter 3 end to end | **Done**: autoplay 3.1 → 3.8 PASS, 0 errors; chapter3check, threatcheck, killcheck ALL PASS |
| 2.3 | Strict review from a player's eyes | **Partly**: walked every place at night; fixed what I found (list below); not every corner photographed |
| 2.4 | "Looks finished" | **Partly**: signposts, the east dock dressed; no rope fences or nets |
| 2.5 | Island 2 leftovers | **Half**: the arch letters are big now; the waitress's tray not done |

Try it: `story goto 3.7b` (then walk into the sea cave behind the south waterfall, or `goto King`), `island3`,
`chapter3check`, `-pdd-autoplay 3.8 -pdd-timescale 2`.

## 0.2 The hotel texture fix

I looked for it everywhere on Shako's PC: the working copy is clean, no change touches `resort_hotel_*`, `Art/Props`
or `HotelReal_*`, the only stash is a half-finished download from 2026-10-17 (not a fix), and no other Claude session
on this PC has worked on hotel textures. The last hotel change from this PC is already on main (59ce325, 2026-10-12,
"Hotel touch-ups: the entrance doorway lined in one piece, plaster strips over the open seams"). Maybe that's the one
Sandro means. Nothing to push.

## 2.1 The sewer and the throne room

- **The sewer** (`GameSceneBuilder.Island3PalaceBatch`): a doorway in the sea cave's west wall (behind the south
  waterfall; the wall there is rebuilt in four pieces round it, the rocks in the way switched off), an 89 m stone
  tunnel that climbs gently (5°) under the plateau, a dark water channel down the middle with kerbs, stone ribs every
  6 m, 10 caged lanterns (warm light, no shadows), and at the end a shaft with an iron ladder and a lamp. **E on the
  ladder climbs out of the drain in the castle yard; E on the drain climbs back down** (`World/SewerLadder.cs`; the
  grate swings open). The tunnel uses the island's own stone (cave rock walls, the castle's paving).
  Pictures `01`-`04`.
- **The throne room**: the keep's hall made 3 m deeper (17 m inside, 13.6 m wide: room for 2-4 players to fight),
  the throne, dais, gold and banners moved back with it, a proper red carpet to the dais, two more torches halfway,
  the door torches lit. `PalaceThrone` stands in front of the throne. Picture `05`.
- **3.7b in the game**: the objective sends you to the sewer, then into the palace; the Pirate King (Sandro's model:
  red coat, tricorn, peg leg on the floor, not floating) and his two Crown Guards wait at the throne; he talks, the
  fight starts (boss intro), beat him and he says where the gold is → 3.8. Pictures `06`-`08`. `overlaps` during the
  fight: 0 hands inside bodies.

## 2.2 Chapter 3 end to end

- Story autoplay 3.1 → 3.8 (windowed): **PASS, 0 errors, 0 warnings**. (Headless with `-nographics` it logs 43
  rendering errors: those are only the missing graphics card, not the game.)
- `chapter3check` now also checks 3.7b (sewer and throne room exist, the sewer leads to the palace, the king is in his
  throne room in his own model, beating him moves on): **ALL PASS**. `threatcheck` **ALL PASS**, `killcheck` **ALL PASS**.
- The autoplay test learned 3.7b (it was always skipped before).

## 2.3 What I found walking the island, and fixed

| Found | Fixed |
|---|---|
| Up close (talking to the THIEF), a speech bubble covered the **whole screen** | Bubbles shrink as you come closer (from 3.5 m in) and stay over the head |
| "Wobbly Will", "Salty Sven", "Mangy Mo"... had the **pirate girl's body** (the crew is dealt round the 4 models) | A pirate whose name isn't a woman's gets a man's body (same body every time for the same name). Picture `11` |
| The east pirate dock was bare planks in the dark | Dressed (see 2.4) |
| The sewer's first version: plain grey boxes, too dark, the ladder hidden behind its own end wall | Island stone, every lantern lit, ladder lamp, ladder on the inside face (climb tested both ways) |

Checked and fine: the night dock and lookout tower, the village road (lantern posts already there), the tavern and
the octagon cage (the regulars stand outside the bars), the gun shop, the jungle (monkeys steal the hat), the river,
Old Snapper's pond under the crag falls, the castle yard and gate, the gold vault. `npcwatch` 80 s on the island: no
one stuck or walking through walls; `overlaps`: 0 hands inside bodies on island 3.

`npcwatch` reports "head 0.21 m below the hips while upright" for every character on the pirate girl's body (11). They
look normal in the pictures, so it's that body's bones fooling the check (Sandro's animation area: not touched).

## 2.4 Looks finished

- Pirate signposts (driftwood board on a post, cream letters, both sides): **DOCK, GUNS, TAVERN, CASTLE, JUNGLE**.
  Picture `10`.
- The east pirate dock: barrels (one lying), crates, a rope coil, two lantern posts. Picture `09`.
- `GameSceneBuilder.Island3DressBatch` (re-runnable).
- Not done: rope fences, nets, more campfires.

## 2.5 Island 2 leftovers

- The **GRAND CORAL arch**: 7.4 m beam, letters 1.7x bigger (read from the beach and the sea), pillars wider apart
  and thinner than the beam (the same depth flickered through its face). `GameSceneBuilder.Island2ArchBatch` (only
  the arch). Picture `12`.
- **The waitress's tray: not done.** Holding a prop in both hands means NPC hand poses (AvatarHands / AvatarAnimator,
  Sandro's files tonight). What's needed: a tray item parented between her hands at waist height, hands posed palm-up
  under it.

## Decisions I made

- The king's room: deeper keep (moved the back wall 3 m) rather than a new building, so the castle's look stays.
- The sewer runs from the sea cave behind the **south** waterfall (the "back waterfall" next to the castle), not the
  crag falls (that one leads to the gold vault).
- Climbing the shaft is a press of E (a teleport), not a climb: a 6 m ladder climb needs climbing animation (Sandro's).

## Numbers

Performance, castle yard, 1280x720: High **8.57 → 8.77 ms** (the new lanterns); Low 4.4 ms; Low in the sewer 4.7 ms,
in the throne room 4.3 ms. The ~80 ms arrival spike: not looked at.

## Files

New: `Editor/GameSceneBuilder.Island3Palace.cs`, `Editor/GameSceneBuilder.Island3Dress.cs`, `Editor/SceneDump.cs`
(read-only planning dumps), `World/SewerLadder.cs`, materials (`SewerFlame`, `SewerWater`, `ThroneCarpet`,
`PirateTorchWood`, `PirateSignBoard`). Changed: `Story/StoryDirector.cs` (pirate bodies), `Story/StoryDirector.Chapter3.cs`
(chapter3check), `Dev/StoryAutoplay.cs` (3.7b), `UI/FloatingText.cs` (bubbles), `Editor/GameSceneBuilder.Island2Finish.cs`
(arch), `Editor/Island2Survey.cs` (any area), `Scenes/Game.unity` (only new objects; nothing old removed),
`Data/Navigation/Island3.asset`.

## Still wrong or not checked

- Not photographed up close: every sleeper by the campfire, every monkey from above the canopy.
- The arrival lag spike, rope fences/nets, the waitress's tray.
