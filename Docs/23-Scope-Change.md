# Scope change: from the old game to the new plan (2026-10-23)

> Read with `20-New-Game-Plan.md`. This lists what in today's game (`sancho-dev` at 67487d38) the new plan no longer
> needs, what gets reused for something else, what stays, and the order to do it in.

## 0. Status: the cut is done (2026-10-23)

Sandro approved the list in section 3 ("რა იჭრება"). Done in one go (steps 0 and 1 of section 8 together):

- **The game starts straight in the open shift** (`StoryDirector.FreePlay.cs`): tourists get into trouble on whichever
  island the lifeguards are at (1, 2 or 3), each with a gag (`StoryDirector.Gags.cs`); Sandy shouts the rescue tips
  the first time on island 1; the knee-deep false alarm and the love hut stay. Milo's boats are open from the start.
  Only the team's money is saved now (`story.json` version 3).
- **Cut**: chapters 1-4, island 4, the bosses and animals (the shark stays), stealth, the SMG / rifle / sniper / knife,
  suppressor shopping, all three gun shops and the dev armory counter, NPCs fighting and shooting, blackjack,
  basketball, the bar menu and seated eating (bartenders gone), VIPs and their tips, lost items and the Lost & Found
  counter, the rival company's bill. Tests that only checked those went too (`StoryAutoplay`, `FightCheck`, `BrawlBot`,
  `chapter3check`, `kleptocheck`, `threatcheck`, `killcheck`, `brawltest`...).
- **Kept although near the cut** (not on the list): bullet wounds and blood (decision A still open), the pistol and the
  shotgun (decision B: no shop, the dev island table has them), the love hut and the zipline, hunger and coconuts,
  drinks in hand (beer, cola, cocktail, fries), the infirmary bed, the missions framework (for Sandy's jobs).
- **Nothing is deleted**: every cut file is in `Archive/OldGame/` (same folders as before, with its `.meta`), outside
  the Unity project. To bring one back, move it (and its `.meta`) back under `Assets/_Game/`.
- **The scene** was cleaned by a script: *PLEASE DON'T DROWN → Scope cut: clean the scene and items*
  (`GameSceneBuilder.ScopeCut.cs`, batch `ScopeCutBatch`). It removes island 4, the hoop and court, the octagon, the
  rope swing and the bartenders, and takes the cut components (shops, bar seats, blackjack, sewer ladders, tsunami...)
  off the objects that carried them; the buildings stay as scenery (the gun shops' buildings too: Shako may remove
  them). **After merging anyone's `Game.unity`, run it again.**

## 1. In one paragraph

Today's game is a 4-island **story-action game**: a scripted story (beats 1.1-4.9), guns and gun shops, stealth with
disguises, six creature/boss fights, side games (blackjack, basketball, the bar meal) and a credits ending. The new
plan is a **throw-and-rescue sandbox**: pick anyone up, throw, catch, trickshots, rescue pay, bodies, Dr. Sandy's
clinic, carts and launchers, then the Sandy twist on island 2 and the takeover. About **15,000 of the 60,000 lines** of
game code (not counting editor tools) and **all of island 4** serve only the old game. They cost us every day: the
autoplay test, `chapter3check`, `kleptocheck`, scene batches and merges all still have to keep them working.

## 2. One clash to settle first

Plan section 2 says: **no blood, no wounds, never gore**. The last three commits (7cd617b2, 67487d38) added the
opposite: bullet wounds that stay on bodies (`Combat/Weapons/BulletWounds.cs`, 790 lines), blood (`Rescue/BloodFx.cs`),
realistic gunshots. Either the tone rule changes, or wounds and blood go (casings can stay). Decision A in section 6.

## 3. CUT (remove from the game)

| What | Where (main files) | Why it goes | Replaced by |
|---|---|---|---|
| The old story, chapters 1-4 (tsunami, robber chase and the drug "vitamins", Milo's keys, buying the pistol, the shark guest, Kleptopus, pirate raid, Skull Cove, Iron Jaw Jack, Pirate King, the vault, chapter 4 Finders Keepers, the Sandy boss, credits) | `Story/StoryDirector.Beats.cs`, `.Chapter3*.cs`, `.Chapter4*.cs`, `.Kleptopus.cs`, `.Gala.cs`, `.Tsunami.cs`, `.Island4.cs`, `.BossBar.cs`, `.Residents.cs`, `CreditsRoll.cs`, `World/Water/TsunamiState.cs` (~5,000 lines) | Plan phase 8: the new loop replaces it | A short intro (Sandy hires you), then the free shift with deliveries; later phases 7 and 9 |
| Island 4 "Finders Keepers" | `Editor/GameSceneBuilder.Island4.cs`, `.Chapter4Items.cs`, `World/FkHelicopter.cs`, `Creatures/Creature.Cousin.cs`, its navmesh and scene objects | Its only job was chapter 4 | Open question 2: recommend **drop it**; the ending (phase 9) happens in Sandy's clinic / the hotel back room |
| Bosses and creatures | `Creatures/Creature.Kleptopus.cs`, `.Snapper.cs`, `.Alligator.cs`, `.Monkey.cs`, `.Parrot.cs`, `.Tentacles.cs`, `BossFight.cs`, `UI/ThreatHud.cs` (boss bar, clock ring) | Old story's bosses; nothing in the plan fights a monster | The **shark stays** (`Shark.cs`, a cause of death in phase 3) |
| Stealth | `Story/Stealth.cs`, `Combat/PlayerCombat.Stealth.cs`, `Story/StoryNpc.Perception.cs`, `Story/StoryNpc.Sleep.cs`, `Story/RobberDisguise.cs`, the parrot sentries, monkeys stealing the hat, "MISSION FAILED: a parrot saw you" | Island 3's old approach; the plan's island 3 is pirates + the pirate cannon | — |
| Most of the arsenal | SMG, Rifle, Sniper (+ `ScopeLens.cs`), suppressors, the Knife, the two gun shops on island 2 (reception + "Coral Arms") and the island 3 gun shop (`ShopCounter`, `ShopFollowsAnother`), `NpcWeapon` claw/pirate guns | Guns were the old story's tool; the plan's verb is **throw** | Keep one or two guns for the "shoot them in the air" gag (decision B) |
| NPC fight systems | the octagon cage fight (Iron Jaw Jack), the bar bouncer brawl, drunks brawling, NPCs shooting back (`Story/StoryNpc.Combat.cs`, `.HitFeel.cs`, `NpcHealthBar.cs`, `NpcWeaponView.cs`, guard/poise of "Fights v2") | Not in the plan | The lifeguard's own punches stay (knock out → carry → deliver) |
| Side games | `Fun/BlackjackTable.cs`, `BlackjackCards.cs`, `BasketballHoop.cs`, `BasketballDribble.cs`, `BallSounds.cs`, the bar menu and eating (`Barista.cs`, `BarSeat.cs`, `BarCustomer.cs`, `TableMeal.cs`, khinkali, `BurpSound.cs`) | Not in the plan; each one needs poses, seats and tests kept alive | The casino, bar and court stay as **scenery** (no code) |
| The gala and VIPs | `StoryDirector.Gala.cs`, the top-left gala bar, `Avatar/AvatarVipHat.cs`, VIP tips | Old island 2 economy | Tips by how the tourist feels (phase 3) |
| Lost & Found items | `Story/LostItem.cs`, `RobberBag.cs`, items Wallet, Phone, Watch, Sunglasses, LostTag, Baggie, MoneySack, FleetKeyCase, JetSkiKeys | Sandy isn't the lost-and-found lady any more | She takes **people** (phase 4) |
| The FK rival lifeguard and "airlifted" | `VictimState.Lost`, `VictimEvent.TakenByRival`, the rival's body | Plan phase 3 | Death and bodies |
| Island 3 old-story places | the stealth night approach and sleepers, the palace / sewer / Pirate King, the gold vault, the rope swing over the river, the octagon (`Editor/GameSceneBuilder.Island3Palace.cs`, `.Island3Vault.cs`, parts of `.Island3Story.cs`, `World/RopeSwing.cs`, `SewerLadder.cs`) | Old chapter 3 | Island 3 stays as a place: night, pirates, tavern, village, the new pirate cannon |
| Tests and dev tools of the above | `Dev/StoryAutoplay.cs` (1,144 lines, sticks at 2.1 anyway), `FightCheck.cs`, `BrawlBot.cs`, `chapter3check`, `kleptocheck`, `threatcheck`, `killcheck`, `stealthtest`, `bosstest` | Nothing left to test | New checks per phase (`-pdd-exec` scripts) |
| Old plan docs | `16-Plan-Islands-2-3.md`, `17-Flow-Islands-1-3.md`, `Prompt-Shako-Chapter-4-Finale.md`, `Chapter-4/`, the island 4 Tripo prompt list | Describe the old game | Move to `Docs/Archive/` with a one-line "replaced by 20" note |

## 4. REUSE (keep the code, give it a new job)

| Today | New job (plan phase) |
|---|---|
| `Story/LostAndFound.cs` (hand things in at Sandy's window, get paid) | **The clinic counter**: hand in people, receipt, pay (4) |
| `Story/HospitalBed.cs` (carry a victim to the infirmary bed, E) | Folded into the clinic delivery (4) |
| `Fun/BananaBoat.cs`, `Fun/CushionRide.cs` (rope hitch to a jet ski, riders flung off on hard turns) | **Towing** a tourist or a body behind a jet ski or boat (5) |
| `Fun/TrickShots.cs` (already tracks every flight: distance, height, splash; only logs) | The **trickshot scorer** and the name pop-ups (2) |
| `Fun/BouncePad.cs` (trampolines, diving board) | Trampolines for tourists and bodies, a bounce counts (6) |
| `Fun/HumanCannon.cs` | Loads tourists and bodies too (6) |
| `Fun/ThrownImpact.cs` (a thrown thing bonks who it hits) | Tourist bowling: bystanders hit go down (1) |
| `Story/StoryDirector.Missions.cs` (fail and retry a mission) | Sandy's jobs after the reveal (7) |
| `Story/StoryDirector.FreePlay.cs` (the endless shift) | **Becomes the main game** right after the intro |
| `Story/StoryDirector.Gags.cs` (silly reasons people are in the water, lines after a rescue) | Stays; feeds tips and Sandy's "they pulled through" lines (3, 4) |
| `Rescue/DrillBoard.cs` (throw a tourist into the sea for practice) | The tutorial throw in the intro |
| `Story/Economy.cs` | Rescue pay, tips, delivery pay, receipts (3, 4) |
| The police officer | The raid threat in phase 7 |
| `Avatar/AvatarFunny.cs`, `AvatarFunnyWear.cs`, `UI/AvatarCustomizer.cs` | Rewritten for the variant system on the two bases (0) |

## 5. KEEP (the core the plan builds on)

Picking up and carrying (`PlayerHands`, `PlayerCarry`), throwing high (G), knock-down without sliding, bodies fitted
to the sand (`VictimBody*`), the victim brain and rescue flow (`VictimBrain`, `RescueService`, `Shore`), CPR, the
defibrillator, swimming and water, vehicles (jet ski, boats, golf cart: the throw takes their speed), fuel pumps,
the beach crowd, the living faces (`AvatarAnimator.Face.cs`), motion clips, dialogue and speaker portraits, the
lifeguard's punches, networking and Steam, the islands 1-3 as places, all the perf work, `ClipCheck` and `CprBatch`.

## 6. Decisions for Sandro (my recommendation first)

- **A. Wounds and blood** (section 2): **turn them off** to match the cartoon rule (keep the code behind a switch for
  now, delete later); or change the tone rule in `20` section 2.
- **B. Guns**: keep **the pistol only** (shoot a tourist in the air, scare panickers); or pistol + shotgun; or no guns.
  Either way: one place to get it (the lifeguard station locker), no shops.
- **C. Island 4**: **drop it**; or keep it empty as Sandy's warehouse for phase 9.
- **D. Side gags not in the plan**: the love hut (CPR gag), the zipline, the basketball. Recommend: keep the love hut
  and the zipline as they are (cheap, no more work on them), drop the basketball.
- **E. Jet skis**: free to use from the start (recommend), instead of Milo selling the fleet.

## 7. Freeze (keep, but no more work)

- Fixing each generated character on its own (face cleaning, the bikini smear, the goofy hand bake, per-body CPR
  fits): the two shared bases replace this (phase 0). Only fix what blocks a test.
- Scenery polish on islands 2-3 (tennis court, casino interior, bungalows, island 3 jungle): it stays, nothing new
  until phases 1-6 are playable.
- New Tripo props except the ones the phases list (cart, trampolines, catapult, slide, pirate cannon, clinic booth,
  freezers, van, back room).

## 8. New order of work

The plan puts "the old story goes" last (phase 8). It should be **first**: every new mechanic otherwise has to dodge
old beats, and every merge has to keep chapters 3-4 alive.

1. **Step 0, switch off (1 day, Sandro):** the old beats are not run (today `-pdd-nostory` only stops the story;
   this makes the game go straight into the free shift instead); decisions A-E applied as switches. Build, play island 1-3 in co-op, make sure nothing
   breaks.
2. **Step 1, delete (1-2 days, Sandro code, Shako scene):** remove section 3's code and scene objects in two
   commits (code, then `Game.unity` via a scene batch, one person on the scene at a time). Compile, `ClipCheck`,
   `CprBatch`, `GunHoldCheck` (if a gun stays), perf on islands 1-3.
3. **Phases 1 → 2 → 3 → 4** (throw/catch, trickshots, pay/bodies, the clinic): this is the game. Playable and filmed
   in co-op before anything after it.
4. **Phases 5, 6** (cart and towing, launchers), with phase 0's bases dropped in when ready.
5. **Phase 7, then 9** (the reveal, Sandy's jobs, the takeover ending).

## 9. Rules while cutting

- Switch off first, delete after one playtest: a cut is easy to undo while it's only a switch.
- Scene objects go by a batch script, never by hand (they must survive the other person's `Game.unity`).
- One person on `Game.unity` at a time (`18-Team-Split.md`).
- After each step: compile, the tests in plan section 6, and a co-op build run.
