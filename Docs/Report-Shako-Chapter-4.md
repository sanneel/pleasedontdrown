# Report: chapter 4, the last island and the finale (Shako's Claude, 2026-10-18/19)

> Task: `Docs/Prompt-Shako-Chapter-4-Finale.md`. Branch `shako-dev`, pushed after every phase. Pictures:
> `Docs/Chapter-4/`, all from the real dev build, through the player's eyes, 1280x720.

## Short version

| # | Phase | State |
|---|---|---|
| 1 | Chapter 3 ending (3.9, 3.10) | **Done**: four money sacks to the boat, sail home, the THIEF slips off with one, Marisol counts three, Sandy's tag |
| 2 | Island 4 greybox, travel, sailing | **Done**: "Finders Keepers" east of the hotel island, every place the task lists, travel pad, `island4`, Esc > Travel, orange route buoys (the autoplay sails them) |
| 3 | Beats 4.1-4.5 | **Done**: landing fee, rescue race, warehouse stealth, Blackout Berry bar fight, the THIEF talks |
| 4 | Sandy and the boss 4.6-4.8 | **Done**: Sandy's reveal (her boss body), the three-phase fight (airlift, Grabby, the claw), Sandy can't swim + CPR |
| 5 | Ending and credits 4.9 | **Done**: police launch, the lost things go home, cast call, CHAPTER 4 COMPLETE, credits, open shift on all four islands |
| 6 | Art pass (Tripo) | **Waiting for Shako's yes** (list and cost at the end). Everything is greybox and existing models now |
| 7 | Tests and this report | **Done**: results below |

Try it: `story goto 3.9` (or any of 3.10, 4.1 ... 4.9), `island4`, `chapter4check`, `chapter3check`, `finalboss 2` /
`finalboss 3` (skip boss phases), `-pdd-autoplay 4.9 -pdd-timescale 2` (the whole game).

## Chapter 3's end (3.9, 3.10)

- **3.9 Load the gold**: four **money sacks** (a new held item: burlap, a gold $, two hands, fits a pocket slot so one
  lifeguard can take several) lie in the vault. The THIEF waits by the pirate boat at the night dock ("throw them on
  the deck, I'll stack them"). A sack lying on the deck is stowed: the boat's cargo shows it (copies of the sack's
  look, stacked on the bow, on every machine). The marker shows the next sack, or the boat while you carry one. The
  boat is tied up while you load (it drifted off before: `Vehicles/Mooring.Tie`). Pictures `01`-`02`.
- **3.10 Back to the hotel**: drive the boat home round the hotel island's west end (or take a travel pad: the boat
  comes too). On the way the THIEF slips over the side with one sack (cargo 4 to 3). Marisol comes down to the
  beach, counts three ("The casino says FOUR"), the team gets a **$750 finder's fee**, and where the fourth sack was
  lies a new item, the orange **Lost & Found tag**: title card "FINDERS KEEPERS. / Deliver to the island." in Sandy's
  handwriting, then the sea chart with an island circled in orange. Then CHAPTER 3 COMPLETE ("To be continued" is
  gone). Pictures `03`-`05` (the boat waits off the beach in `04`).

## Island 4: Finders Keepers

`Editor/GameSceneBuilder.Island4.cs`, batch `Island4Batch` (re-runnable: it replaces `Environment/Island4`; the scene
only gains objects, and old objects keep their network ids: `AssignSceneIds(force: false)`).

- **Where**: centre (560, -400), 200 x 200 m, 250 m east of the hotel island, 330 m from Skull Cove. Its own ground
  mesh (sand, lawn, cliffs; island 3's ground material) and seabed out to the main grid's edge, deepening to 40 m so
  the patch's edge doesn't show from the air. Daylight. Picture `06`.
- **Layout** (pictures `07`-`16`): the west **jetty** with a toll booth ("PRIVATE BEACH / LANDING FEE $50") and the
  company's orange-and-white **FK RESCUE tower**; the **lagoon** on the south-west (knee-deep for 34 m, then a sudden
  drop: the race happens just past it); rows of orange sunbeds under white parasols; the **warehouse hangar**
  (FINDERS KEEPERS on the roof and over the door; inside, 14 labelled towers of sunglasses, phones, wallets, watches
  and the casino's chips); the **Blackout Berry** smoothie bar; the plaza with an orange fountain; the **helipad** and
  the company **helicopter** (networked: `World/FkHelicopter`, it flies in the boss fight); a **cliff path** up to the
  plateau with **Sandy's villa** and an 11 m **gold statue of Sandy** (her own body mesh, in gold); **Sandy's private
  jetty** at the foot of the east cliff over deep water, with stairs down, a wide head and two swim-up ramps; the
  **police dock** on the north shore with a police launch waiting out at sea (`World/ScriptedBoat`). Pale paving
  with orange kerbs, 54 palms, orange route buoys from the hotel island.
- **Travel**: the travel pad by the jetty, `island4` in the console, Esc > Travel "Finders Keepers (island 4)"
  (`Dev/DevIsland.cs`: the menu reads the list, no UI file touched), and the **sea route**: orange buoys from off the
  hotel beach, east, then to the jetty (the autoplay sails it in about 85 game seconds).
- **Rescue rules**: island 2's tourist profile. Pay: the Economy's hotel rate already covers z -150..-580, where
  island 4 lies, so rescues pay as island 2.
- **Navmesh**: `Data/Navigation/Island4.asset`, added to the loader.

## Chapter 4

| Beat | What happens | Code |
|---|---|---|
| 4.1 | "CHAPTER 4 / Finders Keepers"; Marisol on the radio: follow the orange buoys. The toll guard FK Chad: "PRIVATE BEACH! That'll be $50." (paid, or "we'll put it on your bill") | `Story/StoryDirector.Chapter4.cs` |
| 4.2 | **Finders Keepers vs you**: tourists get into trouble past the drop-off and three company lifeguards swim for them. One they reach first is LOST TO COMPETITION (and billed $30). Save 3 before they grab 3, else the mission restarts. Mission bar "FINDERS KEEPERS vs YOU" | same |
| 4.3 | The warehouse: five Lost & Found tags among the towers, four security guards walking beats (stealth: seen = restart). Each tag is read out ("Wallet. Lost by: Bernie, hotel beach. Sold back to: Bernie, $200.") | same |
| 4.4 | Blackout Berry: "One sip and you forget where you put your wallet!" The barman and three regulars fight (fists) | same |
| 4.5 | The THIEF, cornered at the helipad, spills everything (the task's summary, in his voice) | same |
| 4.6 | Sandy in her villa thanks you, sold everything back twice, changes into `Bodies.SandyBoss`: boss intro **SANDY / Owner, Finders Keepers Rescue Co.**, runs for her jetty | `Story/StoryDirector.Chapter4Boss.cs` |
| 4.7 | One mission, one **SANDY** bar, three phases: **(1) the airlift**: her helicopter hovers off the jetty dropping tourists while her lifeguards swim for them; save 4 in 100 s; **(2) Grabby**, the Kleptopus's big orange cousin (1.6x; its arms take tourists already in the water and drag them under: shoot the arm, head shots when the eyes go wide); **(3) Sandy with the giant grabber claw** from the Lost & Found: a lunge with a tell, caught = flung off the jetty into the sea (two swim-up ramps), until she's knocked down. A failed try restarts the phase it failed in | same, `Creatures/Creature.Cousin.cs`, `World/HeldProp.cs` |
| 4.8 | "SANDY CAN'T SWIM / Please don't drown": a drowning Sandy (out cold in seconds) to bring in and revive with CPR; the team argues about the kiss of life; she wakes: "Did one of you just KISS me?" ... "Finders keepers... losers weepers." | same |
| 4.9 | The police launch ties up at the north dock and takes Sandy and the THIEF away; the warehouse empties (+$1000, 120 things returned); eight characters from islands 1-3 wave on the beach (Marisol, Milo, Iron Jaw Jack, the Pirate King, Bernie, Lola, Mr. Castellane, Rosa); CHAPTER 4 COMPLETE; the **credits**; then the **open shift** on all four islands (and Sandy, reformed, back at her kiosk: "Community service, dear. I hand things BACK now. It's horrible.") | `Story/StoryDirector.Chapter4End.cs`, `Story/CreditsRoll.cs` |

The credits (`Story/CreditsRoll.cs`, a new file that draws only with `Hud.Title` and `Hud.Label`: outlined and
shadowed text, no boxes): PLEASE DON'T DROWN; the team: Sandro, Shako; made with Claude (Claude Code), Unity 6 and
Fish-Networking; the models: Tripo, Meshy, Blender, the islands built in code; the fonts; starring...; THE OPEN SHIFT
STARTS NOW. Pictures `17`-`33`.

## Decisions I made

- **Island 4's place**: east of the hotel island, outside the main terrain grid (the grid ends at x 300 and had no
  room for 220 m plus 250 m of clearance). It brings its own seabed mesh, like island 3.
- **The money sacks fit a pocket slot**: four trips from the vault (through the mountain) to the night dock were
  4 x 120 m. One lifeguard can take all four; a team can split them.
- **The THIEF takes the fourth sack during the sail** (he slips off 260 m from the hotel), so "a sack is short" lands
  when Marisol counts.
- **The rival lifeguards** are the players' own goofy lifeguard in orange and white with an orange visor
  (`RivalLook`); security wears black. A new `NpcRole.Rival`, caption "FINDERS KEEPERS" (the allowed one-line
  `StoryHud.RoleOf` change), orange speech colour (`StoryNpc.SpeechColor`).
- **The race**: a rival's grab is billed $30 like the airlift bill; three grabs restart the race. Rivals swim at
  2.6 m/s, a little slower than a sprinting lifeguard.
- **The warehouse** is stealth with its own sight settings (12 m, 42 degrees: a guard's usual 22 m saw the whole
  hangar at once), and the two middle guards turn before the door (they used to see the start point and fail every
  retry at once).
- **The bar fight** uses the existing fighters (the barman a brawler, the regulars drunk fighters), as a mission.
- **Grabby** is the Kleptopus with a cousin switch (`Creature.Cousin.cs` plus small hooks in `Creature.Kleptopus.cs`,
  `Creature.cs`, `Creature.Body.cs`), not a new creature kind: the same body and arms, scaled and orange, its own grab
  pattern, no pistol theft, no hotel reward.
- **The claw** is a prop that stands in the villa; for the fight a copy goes into Sandy's hand on every machine
  (`World/HeldProp`). No new NPC weapon (that would be `Combat/`, Sandro's).
- **Sandy in the water is a tourist** (a `VictimBrain` with her boss look), so the rescue and the CPR are the game's
  own; the real Sandy waits out of sight and then sits up where she was laid.
- **The boss checkpoints**: everyone down used to restart the whole fight from the airlift; now it restarts the phase
  it failed in.
- **The officer** in the ending speaks as a name only (no new role, no new body).
- **People under roofs**: `SpawnNpc` grounds characters from 3 m above, which put the bar's people on its canopy and
  the tower's lookout on its roof. Island 4 places its people exactly on their points (`SpawnAtPoint`).
- **The pirate boat at the hotel** ties up 32 m out after 3.10 (13 m out it sat on the sand and 4.1's trip couldn't
  start).
- **Test-only command** `finalboss 2|3` skips boss phases (for tests and pictures).

## Tests

All on the real dev build, windowed 1280x720 (headless only adds rendering errors), one copy at a time (two copies
on one PC join each other: `-pdd-port` doesn't keep them apart).

| Test | Result |
|---|---|
| Story autoplay **1.1 to the credits**: `-pdd-autoplay 4.9 -pdd-timescale 2` | **PASS, 0 errors, 0 warnings**, 1449 game seconds, every beat from 1.1 to 4.9, then the open shift |
| `chapter4check` (new) | **ALL PASS, 45/45**: every point of island 4 exists, its navmesh, 4.1 toll and fee, 4.2 the race (losing it restarts, winning moves on), 4.3 tags and guards, 4.4 the bar fight, 4.5 the THIEF, 4.6 Sandy's boss look, 4.7 all three phases (helicopter drops, clock, Grabby drags a tourist under, the claw flings you), 4.8 Sandy in the water and the CPR, 4.9 police, warehouse empties, cast 8/8, report, credits, open shift, tourists on all four islands |
| `chapter3check` (now with 3.9 and 3.10) | **ALL PASS, 38/38** |
| `threatcheck` | **ALL PASS** (21) |
| `killcheck` | **ALL PASS** (9) |
| `kleptocheck` (the Kleptopus code got the cousin switch) | **ALL PASS** (18). Note: run it without `god on`, one check is the lounger hurting you |
| Two copies (host + a client joined to `localhost`), host autoplay 4.2 to the end | **PASS**, 0 errors on either. The client saw the objectives, the dialogue, the boss bars and intros, the title cards, the money and the credits (pictures `34`, `35`) |
| `overlaps` at the bar fight and during the claw phase | **0** hands in bodies on island 4 (one 3 cm pair on island 2's beach, not new) |
| `npcwatch` 80 s with island 4 populated | nothing stuck or walking through walls on island 4 (one island 2 swimmer flagged "sideways", not new) |
| Checkpoints: fail in boss phase 2 / phase 3 (`fail`) | restarts at Grabby / at Sandy, not at the airlift |

## Numbers

Frame time (`perf`, 1280x720, average):

| Where | High | Low |
|---|---|---|
| Island 4 jetty | 6.0 ms | 5.0 ms |
| Warehouse | 7.3 ms | 5.2 ms |
| Villa | 5.2 ms | 4.9 ms |
| Rescue race (4.2) | 11.0 ms | 6.4 ms |
| Boss: the airlift | 5.3 ms | 5.1 ms |
| Boss: Grabby | 5.7 ms | 5.6 ms |
| Boss: the claw | 6.3 ms | 6.0 ms |

For comparison: island 3's castle yard is 8.8 ms on High. The airlift was **243 ms (4 fps)** before the fix in
`e1b2d63` (rivals' path searches over the whole deep seabed).

## Requests for Sandro

| File | What | Why |
|---|---|---|
| `Avatar/AvatarHands.cs`, `Avatar/AvatarAnimator.cs` | A grip pose for an NPC holding a long prop in one or two hands (and a lunge gesture) | Sandy's claw is a prop stuck to her right hand bone (`World/HeldProp`): her fingers don't close round it and the "grab" is the prop swinging, not her arms |
| `Combat/NpcWeapon.cs` | Optionally a proper NPC weapon kind for the claw (long reach, a grab instead of damage) | Then phase 3 could use the fighting system instead of my `ClawGrab` lunge in the story code |
| `Rescue/VictimBrain.cs` | A public way to say "taken by a rival lifeguard" (a `VictimEvent` the HUD and the report count as "lost to rivals") | Today the race and the airlift set the state, despawn the tourist and count/bill it in the story code; the HUD feed doesn't say "LOST TO COMPETITION" itself |
| `Story/StoryHud.cs` | Hide the objective and the team funds while the credits roll (`CreditsRoll.Rolling`) | The credits roll over the live HUD; I can't touch StoryHud beyond the role line |
| `Story/StoryHud.cs` `RoleOf` | A "POLICE" caption (or tell me to add the role) | The ending's officer speaks by name only, so his plate has an initial and no portrait |
| `Avatar/` bodies | A rival lifeguard body (if Tripo is a yes) | They're the players' goofy lifeguard recoloured: orange top, white shorts, orange visor |

## Still wrong or not checked

- **Art**: everything new is greybox or recoloured existing models (phase 6 waits for Shako's yes).
- **Grabby's head stays purple-red**: its Tripo texture is purple, so a tint can't make it really orange (the arms
  are orange-red). It needs its own texture or model.
- **The race is the heaviest place** (11 ms on High, still about 90 fps).
- **The warehouse stealth** was tuned with the autoplay (it sneaks with 0 alarms now), not by a person playing.
- **The client in the two-copy test watched**; the host's autoplay did the racing, fighting and CPR. A client pulling
  tourists or fighting Sandy wasn't tried.
- Tourists dropped in the boss fight and not saved stay in the sea after it (still rescuable; out cold ones get
  airlifted as usual).
- The kiss-of-life jokes are "You" lines, not each player's own voice.
- The villa's inside is bare (a desk, a sofa, the claw on its stand).
- The pictures taken with the movie camera (`cam`) show ghosted HUD text, so the report uses first-person pictures.
- I also edited `UI/FloatingText.cs` in the island 3 task (before the UI rule existed); nothing in `UI/` this time.

## Art pass (phase 6): what I'd ask Tripo for

Tripo costs 30 credits a generation (`.claude/skills/pdd-tripo`). One generation each:

| # | Model | Replaces |
|---|---|---|
| 1 | FK rival lifeguard (orange-and-white uniform, visor, A-pose, rigged by `prepare_character.py`) | the recoloured goofy lifeguard |
| 2 | FK RESCUE helicopter | the primitive helicopter |
| 3 | Warehouse hangar | the steel box |
| 4 | Blackout Berry smoothie bar | the white counter and canopy |
| 5 | Sandy's cliff villa | the white boxes |
| 6 | Giant grabber claw | the pole-and-fingers prop |
| 7 | Police launch | the white hull |
| 8 | FK RESCUE lifeguard tower | the box tower |
| 9 | Grabby (an orange octopus in a sun hat) | the tinted Kleptopus |

**9 x 30 = 270 credits** (a bad result redone at most twice: worst case 810). Waiting for Shako's yes.

## Files

New: `Editor/GameSceneBuilder.Island4.cs`, `Editor/GameSceneBuilder.Chapter4Items.cs`,
`Story/StoryDirector.Chapter3End.cs`, `Story/StoryDirector.Island4.cs`, `Story/StoryDirector.Chapter4.cs`,
`Story/StoryDirector.Chapter4Boss.cs`, `Story/StoryDirector.Chapter4End.cs`, `Story/CreditsRoll.cs`,
`Creatures/Creature.Cousin.cs`, `World/FkHelicopter.cs`, `World/ScriptedBoat.cs`, `World/HeldProp.cs`, items
`MoneySack` and `LostTag`, `Data/Navigation/Island4.asset`, `Data/Meshes/Island4/Island4Ground.asset`, `Fk*` materials.
Changed: `Story/StoryDirector.cs` (beats, chapter 4 title, Sandy's walk home, reformed Sandy lines, `chapter4check`,
`finalboss`), `Story/StoryDirector.Beats.cs`, `Story/StoryDirector.Chapter3.cs` (3.8's end, chapter3check),
`Story/StoryDirector.FreePlay.cs` (four islands), `Story/StoryNpc.cs` (`NpcRole.Rival`, its speech colour),
`Story/StoryHud.cs` (the allowed `RoleOf` line only), `Dev/DevIsland.cs` (island 4 travel), `Dev/StoryAutoplay.cs`
(3.9-4.9), `Vehicles/Mooring.cs` (`Tie`), `Creatures/Creature.Kleptopus.cs`, `Creature.cs`, `Creature.Body.cs`
(cousin hooks), `Editor/GameSceneBuilder.cs` (items, `AssignSceneIds(force)`), `Editor/GameSceneBuilder.Story.cs`
(island 4 navmesh), `Scenes/Game.unity` (island 4 only gained objects), `Data/ItemCatalog.asset`,
`DefaultPrefabObjects.asset`.

## Pictures (`Docs/Chapter-4/`)

`01`-`05` chapter 3's end; `06`-`16` island 4; `17`-`22` beats 4.2-4.6; `23`-`27` the boss and Sandy's rescue;
`28`-`33` the ending, report, credits, open shift; `34`-`35` what the joined client saw.
