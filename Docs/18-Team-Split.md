# Team split (3 people, each with Claude), from 2026-10-12

> **Claude: read this and `Docs/16-Plan-Islands-2-3.md` before starting.** Work only on your own part; if a task
> needs someone else's files, say so and ask instead of editing them.

## The one big rule: `Assets/_Game/Scenes/Game.unity`
All three islands are in one 15 MB scene file. Two people saving it at the same time means one of them loses their
work (Git can't merge it). So:
- Only **one person at a time** changes `Game.unity`. Say "scene is mine" in the team chat; when you're done, merge to
  `main` and say "scene is free".
- Part A almost never touches the scene (code only). Parts B and C take turns.
- Planned fix: move each island into its own prefab so B and C can work in parallel (one-time job, one person).

## Branches
Each person has their own branch (A: `shako-dev`; B and C: their own `<name>-dev`). Merge `origin/main` in before
every push, push your branch only, and merge to `main` in small pieces (every day or two), never straight to `main`.

## The full list, in order (pick from the top of your part)

Status: **done** = in code on a branch, waiting for a playtest; **open** = nobody has started; **taken (name)** =
someone is on it. When you take an item, write your name next to it here (and in the team chat) so nobody does it twice.
Order rule (the user's): gameplay logic first, then visuals, then money. Quick removals first because they're cheap.

### A: gameplay and code (owner: `shako-dev`)
| # | Task | Status |
|---|---|---|
| A1 | On-screen text: no BOP/WHAP/BONK/OOF..., no "LONG SHOT! X flew 21 m", no stunt or cannonball money | **done** |
| A2 | Cannon: flies much further (about 50 m) | **done** |
| A3 | Cannon: the one inside is hidden in the barrel; a loaded cannon only turns and aims, doesn't roll about | **done** |
| A4 | Throwing a carried lifeguard works exactly like throwing a tourist (hold Drop, same speed, real flight) | **done** |
| A5 | Defibrillator: a paddle in each hand, kneel, paddles on the chest, the shock | **done** (side view still to check) |
| A6 | Bar: fix the orders | open |
| A7 | Casino working (blackjack side quest) | open |
| A8 | Octagon (tavern cage fight): its own punches | open |
| A9 | Golf cart driving (needs C's model; can start on a greybox) | open |
| A10 | More local pirates walking the village | open |
| A11 | Island 3 story: the sewer behind the back waterfall → the palace → beat the Pirate King → he says where to go | open |
| A12 | Character poses: feet in the ground, sitting, lying; nobody inside anybody (people and animals) | open |
| A13 | On-screen text: a cleaner design for what's left | open |

### B: islands 1 and 2, scene work
| # | Task | Status |
|---|---|---|
| B1 | Island 1: remove Milo's building | **done** (Shako) — the "MILO'S JET SKIS" stall; Milo and his pier stay |
| B2 | Remove the travellers | **done** (Shako) — read as the purple TRAVEL pads on islands 1-3; Esc > Travel, `home`/`hotel`/`island3` and the dev island pad remain |
| B3 | Remove the birds | **done** (Shako) — island 1's parrot flock; island 3's parrot sentries untouched |
| B4 | Remove the palms with coconuts | **done** (Shako) — island 1's 9 shakeable coconut palms (islands 1-2 had no others) |
| B5 | Sandy's house: lower, stairs removed | **done** (Shako) — read as the red-roof house on stilts: down 2.4 m, porch one step up, stairs gone |
| B6 | Towels and shells overlapping the dock (islands 1 and 2) | **done** (Shako) — one towel under Milo's pier moved onto the sand; no shell objects exist on islands 1-2 |
| B7 | Sand texture prettier (islands 1 and 2) | **done** (Shako) — island 3's sand scan (ripples, grain) on the shared beach ground, darker wet sand from the waterline down (`TeamBSandBatch`, material `BeachSand`) |
| B8 | Island 2 smaller (more fun) | **done** (Shako) — 360 x 260 m cut to 180 x 110 m (x -95..85, z -330..-220); the hotel, bar, dock and north beach did not move; room behind the hotel for B9, B10, B13 (`TeamBShrinkIsland2Batch`) |
| B9 | Island 2: pool | **done** (Shako) — east of the hotel, 16 x 10 m, 2.2 m deep; the ground is dug out so the sea's own water fills it (swimming, drowning, rescues work as in the sea); tiled walls, steps at the west end, paved deck, 6 loungers + parasols (`TeamBPoolBatch`). The water is 1.85 m below the deck (the island is raised 1.5 m): a sunken pool |
| B10 | Island 2: golf cart road | open |
| B11 | Island 2: bar restyle (look only; orders are A6) | open |
| B12 | Island 2: casino interior (Tripo tables/chandelier already in `Art/Tripo/Island2/`) | open |
| B13 | Island 2: gun shop | open |
| B14 | List what an expensive island still lacks (show the team before building) | open |
| B15 | Island 1 restyle (get a reference picture first) | open |

### C: island 3 and the Tripo models
| # | Task | Status |
|---|---|---|
| C1 | Remove the wooden structure | open |
| C2 | Buildings overlapping each other | open |
| C3 | Octopus overlapping itself (and animals overlapping things) | open |
| C4 | Main waterfall: water reaches the ocean; fix the texture where the water starts | open |
| C5 | Palace: sewer exit, gate shut so you can't walk in | open |
| C6 | Jungle bigger | open |
| C7 | Tavern bar look; octagon arena | open |
| C8 | Gun shop (island 3) as a real building | open |
| C9 | Tripo: Milo (new character) | open |
| C10 | Tripo: jet ski (model + texture; it's blurry now) | open |
| C11 | Tripo: golf cart | open |
| C12 | Tripo: custom characters for island 2 | open |

Island 3 always night, mountain dock, sleepers: see `Docs/16`.

## Notes on what's done (part A, 2026-10-12)
- Text: `Fun/TrickShots.cs` only logs now; the pop-up words are gone from about 15 files; damage numbers, money,
  SAVED/REVIVED stay.
- Cannon and throws: `PlayerMotor.Launch` keeps a flight going until landing (the walking model used to brake it after
  0.8 s). The cannon uses a quarter of the extra gravity, a thrown lifeguard plain gravity like a tourist.
  `HumanCannon` hides the body inside (`PlayerAvatar.HideInCannon`).
- Defibrillator: `Combat/DefibPaddles.cs`; the paddles were split off the model (`ArtSource/Tools/split_defibrillator.py`,
  `Resources/Props/DefibPaddle.glb`).
