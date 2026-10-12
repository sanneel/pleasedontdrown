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
Each person has their own branch (A: `sancho-dev`; Shako: `shako-dev`; the third: their own `<name>-dev`). Merge `origin/main` in before
every push, push your branch only, and merge to `main` in small pieces (every day or two), never straight to `main`.

## Playtest 1 (the user, 2026-10-12, on `main` 2425c265): what's accepted, what's next
**Accepted:** A1, A2, A3, A4, A9 (the user adds a car model), B2, B3, B7, C1, C6. A10: not testable yet. C2: the user
will find the spot.

| # | The user's verdict | Next |
|---|---|---|
| A5 | Works but weird | **Redone** (12a08a88): put it down by the casualty, stand beside them, E on it; the other button picks it up again |
| A6 | Works, bad visuals | Better looks for the bar ordering/eating (card, food, serving) |
| A7 | Needs better visuals | **Redone** (a2683701): cards dealt on the felt (E deal/hit, other button stand or bet, R double), no pop-up |
| A8 / C7 | Octagon is inside the tavern | The octagon stands **next to** the tavern, outside (C7 builds it, A8's fight works wherever it is) |
| A11 / C5 | No sewer anywhere; C5 done very badly | C5's drain/grate in the castle yard **removed**; a real sewer (entrance behind the back waterfall, tunnel, exit in the castle) still to be built, then A11 hooks the Pirate King beat to it |
| A12 | Worse: hands very weird, lying on the stomach worst | **Redone** (5bd265dc): propped on the elbows on the stomach, arms along the body on the back, every body |
| A13 | Changed, not clearer | A cleaner on-screen text design |
| B1 | Removed | — |
| B4 | Wrong island | Coconut palms **back on island 1**; remove them from **island 3** |
| B5 | Fixed, but the white house still has stairs | Remove the white house's stairs (not needed) |
| B6 | Only island 1 | Same on island 2 |
| B8 | Smaller, but bare | Restyle island 2 and place more things |
| B9 | Pool done | Redesign it |
| B10 | Cart road done but bad | Redo it |
| B11 | **Not done** | Bar textures missing; you can walk into the bar's walls; the barista stands inside a wall (only the head shows) |
| B12 | Casino added | Needs changes |
| B13 | Gun shop added | Restyle; **no pop-up** for buying |
| C3 | Kleptopus | Faster; it **grabs** people and pulls them into the sea, picks up and throws (not makes them walk) |
| C4 | **Not done** | The waterfall's water isn't open and doesn't reach the ocean |
| C8 | Gun shop | Restyle; guns (and silencers, upgrades) on display: **click one to buy it**, simply |

## The full list, in order (pick from the top of your part)

Status: **done** = in code on a branch, waiting for a playtest; **open** = nobody has started; **taken (name)** =
someone is on it. When you take an item, write your name next to it here (and in the team chat) so nobody does it twice.
Order rule (the user's): gameplay logic first, then visuals, then money. Quick removals first because they're cheap.

### A: gameplay and code (owner: `sancho-dev`)
| # | Task | Status |
|---|---|---|
| A1 | On-screen text: no BOP/WHAP/BONK/OOF..., no "LONG SHOT! X flew 21 m", no stunt or cannonball money | **done** |
| A2 | Cannon: flies much further (about 50 m) | **done** |
| A3 | Cannon: the one inside is hidden in the barrel; a loaded cannon only turns and aims, doesn't roll about | **done** |
| A4 | Throwing a carried lifeguard works exactly like throwing a tourist (hold Drop, same speed, real flight) | **done** |
| A5 | Defibrillator: a paddle in each hand, kneel, paddles on the chest, the shock | **done** (side view still to check) |
| A6 | Bar: a real menu card (mouse or number keys), khinkali, the tables' armchairs as seats, the order into your hand, eating seated | **done** |
| A7 | Casino working (blackjack side quest) | **done** (`Fun/BlackjackTable.cs`) |
| A8 | Octagon (tavern cage fight): its own punches | **done**: LMB jab, RMB hook, crouch+LMB uppercut, jab-jab-hook combo |
| A9 | Golf cart driving (needs C's model; can start on a greybox) | **done** on a greybox (`Vehicle._land`), C's Tripo model to drop in |
| A10 | More local pirates walking the village | **done**: tavern regulars, the village drunk |
| A11 | Island 3 story: the sewer behind the back waterfall → the palace → beat the Pirate King → he says where to go | **done** (beat 3.7b), waits for C's `SewerEntrance` / `PalaceThrone` points |
| A12 | Character poses: feet in the ground, sitting, lying; nobody inside anybody (people and animals) | **done**: lying arms, chair/stool sitting, CPR at the side, towels in decks skipped, only GLB characters; NpcWatch clean on all islands |
| A13 | On-screen text: a cleaner design for what's left | **done**: messages on dark pills, no timers |

### B: islands 1 and 2, scene work
| # | Task | Status |
|---|---|---|
| B1 | Island 1: remove Milo's building | **done** (Shako) — the "MILO'S JET SKIS" stall; Milo and his pier stay |
| B2 | Remove the travellers | **done** (Shako) — read as the purple TRAVEL pads on islands 1-3; Esc > Travel, `home`/`hotel`/`island3` and the dev island pad remain |
| B3 | Remove the birds | **done** (Shako) — island 1's parrot flock; island 3's parrot sentries untouched |
| B4 | Remove the palms with coconuts | **done** (Shako) — island 1's 9 shakeable coconut palms (islands 1-2 had no others) |
| B5 | Sandy's house: lower, stairs removed | **done** (Shako) — Sandy's house is the Lost & Found shack (Shako's screenshot): its plank base went under the sand (the shack 0.79 m lower, floor 4 cm over the sand), its step gone, the alarm bell kept, Sandy lowered with her stool (`TeamBSandyHouseBatch`). First pass misread it as the red-roof stilt house, which is still lowered (ask Shako whether to put it back) |
| B6 | Towels and shells overlapping the dock (islands 1 and 2) | **done** (Shako) — one towel under Milo's pier moved onto the sand; no shell objects exist on islands 1-2 |
| B7 | Sand texture prettier (islands 1 and 2) | **done** (Shako) — island 3's sand scan (ripples, grain) on the shared beach ground, darker wet sand from the waterline down (`TeamBSandBatch`, material `BeachSand`) |
| B8 | Island 2 smaller (more fun) | **done** (Shako) — 360 x 260 m cut to 180 x 110 m (x -95..85, z -330..-220); the hotel, bar, dock and north beach did not move; room behind the hotel for B9, B10, B13 (`TeamBShrinkIsland2Batch`) |
| B9 | Island 2: pool | **done** (Shako) — east of the hotel, 16 x 10 m; full: its own water 15 cm under the edge, about 1.6 m deep (`World/Water/PoolWater.cs`: WaterSurface answers the pool's level inside it, so swimming, drowning and rescues work as in the sea; tested in the build); tiled walls, steps, paved terrace, 6 loungers + parasols. The lounger model's back now reclines from the cushion's head end with the pillow on it (`polish_resort_beach.py`, all loungers) (`TeamBPoolBatch`) |
| B10 | Island 2: golf cart road | **done** (Shako) — 3.5 m paved road in a U (hotel front-west → past the bar → round the back → the pool), kerbs, dashed line, parking bays `CartParkingHotel` and `CartParkingPool` (markers for A9/C11), no colliders (it lies on the sand) (`TeamBCartRoadBatch`) |
| B11 | Island 2: bar restyle (look only; orders are A6) | **done** (Shako) — dressed in the hotel's teal/cream/gold: a "CORAL BAR" sign, warm string lights under the front and back beams (+3 soft lights), palm planters at the corners; the Tripo bar model, colliders, barista and serving spots untouched (`TeamBBarLookBatch`) |
| B12 | Island 2: casino interior (Tripo tables/chandelier already in `Art/Tripo/Island2/`) | **done** (Shako) — the 6 box slots → Tripo slot machines on the same stands; Tripo roulette table (with a collider) in the back half; the 2 drawn chandeliers → Tripo crystal ones (+ warm lights); velvet ropes funnelling the door; champagne tower on a gold table. The existing blackjack table is left for A7 (`TeamBCasinoBatch`) |
| B13 | Island 2: gun shop | **done** (Shako) — "Coral Arms", a walk-up boutique between the bar and the hotel facing the beach (teal awning, gold sign, guns on a walnut wall). Its counter is a copy of the reception's shop (same guns and prices) and opens/closes with the reception (`Story/ShopFollowsAnother.cs`, a tiny host script): the "buy the pistol" step (2.2) passes when bought here, tested in the build. A6/A: the objective still says "at reception" (`TeamBGunShopBatch`) |
| B14 | List what an expensive island still lacks (show the team before building) | **list written** (Shako) — `Docs/19-Island-2-What-It-Lacks.md` (14 items, suggested order); waiting for the team to pick before building |
| B15 | Island 1 restyle (get a reference picture first) | **waiting for a reference picture** — not started (no picture yet). Note: after B4 island 1 has no palms left; the restyle should bring new trees |

### C: island 3 and the Tripo models
| # | Task | Status |
|---|---|---|
| C1 | Remove the wooden structure | **done** (Shako, from shako-dev) — read as the wooden stair tower + lookout deck against the crags (night arrival); `TowerFoot` kept (sneak start); the story skips the climb (Sandy's tower line only plays if a lookout exists); the builder no longer makes it; `chapter3check` ALL PASS (`TeamCRemoveTowerBatch`) |
| C2 | Buildings overlapping each other | **checked, nothing found** (Shako) — an overlap report over island 3 (`TeamBSceneTools.OverlapReport`) finds no two houses, the tavern, the gun stall or the castle overlapping each other (only the castle walls joining their towers, and tree crowns over house 13); needs a screenshot of the spot meant |
| C3 | Octopus overlapping itself (and animals overlapping things) | **octopus done** (Shako) — the Kleptopus's two working arms now leave its body on the side facing their tip (they used to come out of the front and run back through its body and its own tentacles), `Creature.Tentacles.cs`; `kleptocheck` ALL PASS. Other animals: not checked yet |
| C4 | Main waterfall: water reaches the ocean; fix the texture where the water starts | **done** (Shako) — the sea cave falls: the flat 13 m plate where the roof stream met the edge is now a fan widening smoothly from the stream and curling over the edge (streaks flow toward it); the falling sheets go 1 m under the sea so no gap opens in a wave trough (`TeamCWaterfallBatch`, mesh `FallsLipFan`). Checked in the build |
| C5 | Palace: sewer exit, gate shut so you can't walk in | **done** (Shako) — the castle gate shut (iron-banded doors, barred, a solid blocker `GateShut`); a stone drain with an iron grate in the castle yard (west half): `SewerExitDrain`, the grate on `SewerGrateHinge` (swings up, for A11), the climb-out point `SewerExit`. Island 3 navmesh baked again (`TeamCPalaceBatch`). **Still needed for A11:** two empty GameObjects under `Environment/Island3/Story/`: `SewerEntrance` (the way into the sewer, behind the back waterfall) and `PalaceThrone` (in front of the throne, facing the room); until they exist the Pirate King beat (3.7b) is skipped |
| C6 | Jungle bigger | **done** (Shako) — a 14 m band of jungle all round its old edge: +59 trees, +120 bushes, +150 ferns (723 → 952 plants), clear of roads, river, falls, castle, houses, tavern, gun stall and night arrival; plants settled on the ground; island 3 navmesh baked again; `chapter3check` ALL PASS (`TeamCJungleBatch`). The jungle-floor ground colour was not extended (needs an island 3 ground rebuild) |
| C7 | Tavern bar look; octagon arena | **done** (Shako) — an 8-sided cage (6 m, canvas floor with a red ring, padded posts, padded lower walls with bars above, solid sides) in the middle of the tavern, its open side toward the front door; Iron Jaw Jack already stands in it. The 4 tables and their stools had to go (no room). Bar: "THE RUSTY ANCHOR" sign, a Jolly Roger on the back wall, 3 hanging lanterns with warm light. Navmesh baked; `chapter3check` ALL PASS incl. 3.4 (`TeamCTavernBatch`) |
| C8 | Gun shop (island 3) as a real building | **done** (Shako) — the Tripo `gun_shop` (from `ArtSource/Tripo/island3`, now in `Art/Tripo/Island3`) stands at the stall's spot, 7 m wide, its open front toward the road; the working "Skull Cove guns" counter sits on the model's own counter (its box hidden), bought guns appear there; the stall's awning/posts/sign removed; a body collider; navmesh baked; `chapter3check` ALL PASS incl. 3.3 (`TeamCGunShopBatch`) |
| C9 | Tripo: Milo (new character) | open — needs Shako signed in to Tripo and a "yes" on the credit cost (not started overnight) |
| C10 | Tripo: jet ski (model + texture; it's blurry now) | open — needs Shako signed in to Tripo and a "yes" on the credit cost (not started overnight) |
| C11 | Tripo: golf cart | open — needs Shako signed in to Tripo and a "yes" on the credit cost (not started overnight) |
| C12 | Tripo: custom characters for island 2 | open — needs Shako signed in to Tripo and a "yes" on the credit cost (not started overnight) |

Island 3 always night, mountain dock, sleepers: see `Docs/16`.

## After merging part A's branch (`sancho-dev`)
Part A's scene changes are made by a script, so they survive anyone's scene: after merging, keep the other person's
`Game.unity` and run **PLEASE DON'T DROWN → Part A: scene additions (bar tables, khinkali, blackjack)** once (it also
places the golf cart and the bar's new stools and lounge tables), then save.

## Notes on what's done (part A, 2026-10-12)
- Sitting at the hotel bar (the user, 2026-10-12: sit like a person): the bar model's own twelve stools (1.2 x 0.7 m
  ovals right against the counter) and the lounge's four low tables (5-14 cm from the armchairs) are cut out of the
  coloured bar LODs (`ArtSource/Tools/remove_bar_furniture.py`; B: don't re-colour the LODs from `source.glb` without
  running it again). New `bar_stool.glb` (sized for the goofy lifeguard: cushion 1.07 m, footrest ring 0.29 under it)
  0.55 m back from the counter, and `bar_table.glb` halfway between each pair of armchairs. Seated poses by IK on the
  body's measured shape: on a stool thighs forward, heels on the ring, hands on the knees; in an armchair knees over
  the cushion's edge, legs hanging in front of it. The bar guests sit the same way (`AvatarPose.SitStool`).
  `ReviewCapture` (`seat:<name>`) counts vertices inside the cushions, the counter and the tables: all 0.
- Text: `Fun/TrickShots.cs` only logs now; the pop-up words are gone from about 15 files; damage numbers, money,
  SAVED/REVIVED stay.
- Cannon and throws: `PlayerMotor.Launch` keeps a flight going until landing (the walking model used to brake it after
  0.8 s). The cannon uses a quarter of the extra gravity, a thrown lifeguard plain gravity like a tourist.
  `HumanCannon` hides the body inside (`PlayerAvatar.HideInCannon`).
- Defibrillator: `Combat/DefibPaddles.cs`; the paddles were split off the model (`ArtSource/Tools/split_defibrillator.py`,
  `Resources/Props/DefibPaddle.glb`).
