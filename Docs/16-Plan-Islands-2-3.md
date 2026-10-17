# Plan: islands 2 and 3 (and the core gameplay under them)

> Agreed with Shako on 2026-10-11 (branch `shako-dev`). This is the source of truth for the story, money and the core
> systems of islands 2 and 3. **Claude on another PC: read this first**, then the "Status" and "Rules" sections.

## Rules for working on this (Claude)

- Shako has no coding background: talks in Georgian, plain words and screenshots. Answer in Georgian, simply. If a
  request is unclear, ask a short concrete question (offer 2-3 options) instead of guessing. Decisions that need an
  expert (architecture, licensing): say so and ask him to bring in a teammate (sanneel).
- **Never push to `main`.** Work on `shako-dev`. Before every push: `git fetch`, look at `git log HEAD..origin/main`,
  merge `origin/main` into `shako-dev`, make sure it compiles, then push `shako-dev` only.
- How to Fish is the licensed reference (mechanics only). Its decompiled code lives outside the repo
  (`HowToFish_Decompiled/` next to it on Shako's PC). Take ideas, never copy code or assets.
- Visual style: **stylised-realistic** like Shako's `THIEFASPIRATE.glb` (detailed worn materials, natural proportions,
  PBR). Not flat cartoon (that was rejected). Tripo rules: `.claude/skills/pdd-tripo/` (prompt lists in `prompts/`).
- Work order Shako asked for: **core gameplay logic first** (fights, shooting, creatures, bosses, retries), visuals
  later (models go in when the logic is solid). Money/economy is lower priority than how fights feel.

## Story

### Island 1 (built; Shako finds its story and gameplay the weakest; not being changed right now)
Rescue one tourist → tsunami (Milo, the thief and tourists swept away) → rescue everyone → return Milo's keys → Milo
lends the jet skis. Sandy (our friend) tells us a new hotel island is hiring lifeguards (better pay).

### Island 2: the hotel (where the money is earned)
1. Buy a **pistol** ($250) at reception. (No shotgun here.)
2. Earn money with:
   - **Beach shift**: rescues pay ×1.5. **VIP guests** (about 1 in 5) leave $100-300 tips. VIPs never take money away.
   - **Kleptopus** (boss, see below).
   - **Bar bouncer**: drunk fist-fights; stopping one pays $50; injured go to the infirmary; -$20 per broken chair.
   - **Inflatable cushion ride**: two tourists on a cushion towed by a jet ski, $40 per tourist per tour; hard
     turns fling them off and they need rescuing (replaces the banana boat).
   - **Blackjack** in the casino: a side quest only, not an income. (Casino slots stay as decoration.)
   - Parasailing was rejected.
3. **Gala**: the director announces a casino gala once the hotel earns enough. A **plain bar, top left, no numbers
   and no percent** fills with the team's money toward **$1,000 × number of players**. Full → gala night →
   **pirates rob the casino** → casino closes (can't lose the money at blackjack after that).
4. The director gives the **yacht keys** → go straight to island 3. Players can always sail back to island 2.

**Kleptopus**: giant purple octopus wearing stolen jewellery, sunglasses and a panama; lair under the pier on a heap of
loot. Guests report thefts → the receptionist walks up to the team → boss fight (pistol).
- Phase 1: tentacles drag sunbathers into the sea; shoot the tentacle to free them, then rescue them.
- Phase 2: ink blinds the screen and a tentacle steals your pistol; punch it to get it back.
- Phase 3: throws stolen loungers and phones; its eyes glow before a throw → shoot the head.
- Death: stolen items scatter (return them for pay); the hotel pays $300.

### Island 3: Skull Cove (pirates) — no income here (you're undercover)
**Always night** (Shako 2026-10-11): moon, stars, lanterns; a dark sea mist hides the hotel island (the island did
not move: the night and the mist make it feel far). You land at a little dock on the **mountain side** (under the
northern crags), climb a pirates' stair tower to a **lookout** on the cliff where you see the whole village, then sneak
in past **sleeping pirates** to the THIEF. Runtime look: `World/IslandNight.cs` (blends by camera position, no asset
changes); places: `GameSceneBuilder.Island3Night.cs` (menu: *Island three: night arrival*); sleepers:
`Story/StoryNpc.Sleep.cs`.
1. **Arrival** = a stealth mission run like a boss fight: if a **parrot** sees you, the mission restarts.
2. **THIEF** (the robber, renamed, new look = `THIEFASPIRATE`) owes you for the tsunami: gives a pirate disguise
   (hat + eye patch) and tells you to buy guns and suppressors or you're dead.
3. **Gun shop** next to the dock: pistol 250, SMG 700, shotgun 500, suppressor 250 (per weapon), binoculars 100.
   The mission does not start until you own a pistol and a shotgun and **every owned gun has a suppressor**.
4. **Tavern**: UFC fights against a cage fighter (needs a good fight AI: aim 9/10).
5. **Jungle**: monkeys steal the disguise hat. You may kill them only with a suppressed gun; an unsuppressed shot is
   heard, a pirate raises the alarm, you're caught → restart.
6. **River**: rope swing across; alligators in the river and the pond.
7. **Waterfall**: boss "Old Snapper", a giant alligator snapping turtle guarding the money cave. The shell stops shots
   from above; hit it when the mouth opens. It knocks a pirate guard into the water mid-fight; rescue him.
8. Not yet confirmed by Shako: rescuing pirates who can't swim (reputation), heavy money sacks in the cave, the Pirate
   King falling off the cliff and being rescued, the return to the hotel.
- Treasure map: removed. Paid animal kills on island 3: removed (would blow your cover).
- After island 3 the yacht goes to **island 4** (not designed yet; a Kraken idea is parked for later).

## Money (agreed numbers)
| | |
|---|---|
| Rescue / revive / defibrillator / hospital (code) | 60 / 80 / 150 / 200, ×1.5 on island 2 |
| VIP tip | 100-300 |
| Kleptopus | 300 + returned items |
| Bar fight stopped | 50 (-20 per broken chair) |
| Cushion ride | 40 per tourist per tour |
| Gala target | 1,000 × players |
| Gun shop (island 3) | pistol 250, SMG 700, shotgun 500, suppressor 250, binoculars 100 |

To remove (Shako): the stunt money rewards (trickshots ORBITAL!/TRICKSHOT!/LONG SHOT!/NICE THROW! in
`Fun/TrickShots.cs`, the cannonball score in `Combat/PlayerCombat.Fun.cs`) and on-screen text clutter (confirm by
screenshot first).

## Core gameplay systems (the part being built now)

| # | System | Status | Where |
|---|---|---|---|
| C1 | Player health, going down, CPR revive by a friend | **done** (tested headless, 2 copies) | `Combat/PlayerCombat.Health.cs` |
| C2 | Missions that restart on failure (everyone down / `ServerFailMission`) | **done** | `Story/StoryDirector.Missions.cs` |
| C8 | Hit feel: red edges, camera jolt, punch hit marker, quiet "down" line | **done** (Shako asked for a quieter down screen: done) | `UI/PlayerHud.cs`, `Player/PlayerLook.cs` |
| C3 | Melee fight AI: turns (max 2 on one player), wind-up then a real hit check, back-off, knockdown + get up, cage fighter guard/combos, no stun-lock | **logic done, needs Shako's playtest** | `Story/StoryNpc.Combat.cs`, `Combat/NpcWeapon.cs` |
| C4 | Gun AI (pirates): keep range, line of sight, aim lag, bursts, reload, reposition, don't shoot through allies | **logic done, needs playtest** | same |
| C5 | Perception: sight cone, hearing (`ServerNoise`, gunshots: suppressed ~4 m, loud ~60 m), alarm | **done** (21 automated checks pass, 2-player smoke test), needs Shako's playtest | `Story/Stealth.cs`, `Story/StoryNpc.Perception.cs`, `Combat/PlayerCombat.Stealth.cs`, `UI/ThreatHud.cs` |
| C6 | Creatures: base with health + hit zones, parrot sentry, monkey (steals items), alligator (lunge + grab) | **logic done** (greybox bodies; Tripo models later), needs playtest | `Creatures/Creature*.cs`, prefab `Resources/Creature` |
| C7 | Boss framework: phases, telegraph → stun window, boss bar, scaling per player | **done**; Old Snapper and the Kleptopus built on it (logic, greybox bodies) | `Creatures/BossFight.cs`, `Creatures/Creature.Snapper.cs` |
| C9 | Killing vs knocking out; characters fighting each other: fists/blades knock people out, a bullet or an animal's bite that finishes them kills them (story people the plot needs only get K.O.'d); animals die, small ones are cleared away after ~30 s; stray NPC blows/shots hit other NPCs and start grudges; drunks brawl among themselves; the alligator and Old Snapper go for pirates too; a friend's bullet hurts a lifeguard (down = CPR, never dead) | **done** (`killcheck` ALL PASS), needs Shako's playtest | `Story/StoryNpc.Rivals.cs`, `Creatures/Creature.Prey.cs`, `Story/StoryDirector.Kills.cs` |

Weapons (`NpcWeaponKind`: Fists, Cutlass, Pistol, Musket, synced as `StoryNpc.WeaponKind`) are **in the pirates' hands**
since 2026-10-11 (the user asked for them): Tripo flintlock pistol, flintlock musket and cutlass
(`Resources/NpcWeapons`, made by `ArtSource/Tools/tripo_prepare_weapons.py`, drawn by `Story/NpcWeaponView.cs`).
- **What you see:** held exactly as the players' guns are seen from outside (`PlayerHands.ThirdPersonHold`: the
  pistol held out in front at chest height, the musket's butt in the shoulder; the players' grip nodes and finger
  poses from `WeaponGripAuthoring`; the hands reach the grips by arm IK). Armed guards carry their gun on watch, pointing
  at the ground ahead. In a fight a gunman points it at you (a hammer click warns you), fires (flash, a cloud of black
  powder smoke, the ball's tracer and its puff where it lands), then lowers it to reload. The cutlass is held forward
  at the side (the players' knife pose, tipped less: it's long), raised over the shoulder for the wind-up and slashed
  down. Up close a gunman hits you with the gun. Knocked down, the weapon stays in the hand.
- **Logic:** the pistol is a flintlock too now: **one shot, then a 2.6 s reload** (was a 6-round gun with bursts), 22
  damage; the musket one shot, 3.2 s. The reload is the moment to rush them. Shots and the "is an ally in the line"
  check start from the drawn gun's muzzle. The gun state and aim point are synced (`StoryNpc.GunState`, `AimPoint`).
- The weapons are drawn bigger than life (pistol 1.5x, musket 1.1x, cutlass 1.15x): the Tripo pirates have big hands.
- Dev command `god [on|off]` (no damage, for tests and filming); `aimat <name> head` aims at the head.
- **Hitting them** (`Story/StoryNpc.HitFeel.cs`): the body flashes red for a moment, a ball throws a red puff and a
  heavy smack sound, the damage pops up over the head (sized to read the same near or far). **Headshots** (a ball above
  ~1.5 m on someone standing) do **double damage**: "HEADSHOT -50" in gold. The shooter gets the host's confirmation:
  a hit marker (white, gold for a headshot, red and bigger for a kill) with a tick / ding / thunk.
- **Health:** the row of pips after the name is gone. `Story/NpcHealthBar.cs`: a slim bar over the head shows only
  once they're hurt (green to yellow to red, a white chunk for the damage just done draining after it) and fades out
  ~4 s after the last hit; gone when they're down.

Measured (headless tests, 2026-10-11): fists vs a still player ~90% land, vs a player dodging left/right 29%; pistols
~40% still, ~21% dodging. A cage fighter no longer gets stun-locked by mashed punches.

### How C5-C7 work (2026-10-11)
- **Stealth:** guards (`StoryNpc.ServerGuard`) and parrots look with a cone of sight; what they see fills a meter (faster
  close up and when you run, slower when you creep). Over their head: a "?" ring filling, then a red "!" (the alarm).
  A loud shot is heard ~60 m off (instant alarm), a suppressed one ~4 m, running feet ~7 m. In the pirate disguise
  guards and parrots ignore you (only standing right in a guard's face for a while makes him wonder). During a
  stealth mission the alarm fails it ("Lookout Lou spotted you!") and it restarts.
- **Creatures** (one prefab, `Resources/Creature`, greybox bodies): parrot lookout (perch, circle, squawk the alarm;
  one shot downs it); monkey (creeps up on a disguised lifeguard, steals the hat = disguise gone, runs off and jeers;
  hit it and it drops the hat, walk over the hat to put it back on; its screech is a little noise); alligator (stalks
  people in or by the water, hisses with jaws open = the warning, lunges, grabs and drags with a death roll; five
  punches break free, or friends hurt it enough).
- **Boss framework + Old Snapper:** phases by health (100/60/30%), every attack cued first, a stun window after the
  bite (head 2.5x, shell 5%, legs 30%), a slam from phase 2 when crowded or flanked, faster from phase 3; health x1.6
  per extra lifeguard; boss bar bottom middle with phase pips and "WEAK SPOT OPEN!". Phase 2 knocks a pirate who can't
  swim into the water (rescue him mid-fight).

### Story built on them (2026-10-11)
- **2.5b The Kleptopus** (between the last guest rescue and the pirates): guests shout that their things are gone, the
  receptionist runs out to the team (wet sucker marks lead under the pier, the hotel pays $300), then the fight at
  the pier's far end. Phase 1: an arm drags a sunbather into the sea; shoot the arm (they're then in the water to
  rescue; left too long they come up out cold) and it shrieks with its eyes wide (head 2.5x). Phase 2 (66%): ink on
  everyone's screen, then an arm snatches a pistol and waves it by the pier: punch it to get the same gun back (parts
  and all). Phase 3 (33%): its eyes glow, then it throws a sun lounger or phone at you. Beaten: 6 stolen things spill
  out (hand them in) and the hotel pays $300.
- **Chapter 3, Skull Cove** (after 2.7 the objective is to sail south, to the dock under the mountain): 3.1 at night:
  climb the lookout tower, then sneak to the THIEF's shack at the far end of the village past 8 sleeping pirates (they
  hear running and footsteps right beside them, sit up and look about; seen = restart) and 2 guards with lanterns · 3.2 the THIEF: everyone gets the disguise, "buy suppressed
  guns" · 3.3 gun stall by the dock (pistol 250, shotgun 500, SMG 700): done when the team has a pistol and a shotgun
  and every gun is suppressed (the THIEF tops the wallet up: no pay on this island) · 3.4 the tavern: cage fight with
  Iron Jaw Jack (brawler AI) · 3.5 the jungle: 3 guards and 3 monkeys between the west road and the river; a loud shot
  or a pirate seeing your face = caught, restart · 3.6 the rope swing over the river, 2 alligators · 3.7 Old Snapper
  in the pond under the crag falls (the plan's "waterfall"); phase 2 knocks a pirate into the water · 3.8 the gold
  vault (Shako 2026-10-11: "the gold inside the mountain, in the centre, where the waterfall is"): swim in behind the
  crag falls, up a ramp, along a tunnel to a hall in the heart of the crags (a mound of gold, bars, chests, sacks and
  the casino's chips): chapter 3 complete. `GameSceneBuilder.Island3Vault`. The south sea cave is now just a cave.
  Places: `Island3/Story/...` (GameSceneBuilder.Island3Story).
- **Island 2 finished (2026-10-17, see `Docs/Overnight-Report-Island-2.md`):** VIP guests (gold hat, $100-300 tip,
  rescues x1.5 on island 2), beat **2.5c the gala fund** (the director Mr. Castellane in the lobby, the plain gala bar
  top left, beach shift, bar fights $50 / -$20 a knocked-over stool, the cushion ride $40 a guest), **2.6 gala night**
  (island 2 at night, red carpet, ropes, string lights, champagne tower, chandeliers, guests; the pirates rob the casino
  and their boat escapes with the money; the casino closes), **2.7** the director gives the keys of the hotel's yacht
  (Tripo, at the new marina pier east of the beach). Palms, flower beds, pool hedges, lamp posts, signposts, the GRAND
  CORAL arch. Scene: `GameSceneBuilder.Island2FinishBatch` (re-runnable). Tests: `viptest`, `galamoney`, `bartest`,
  `galatest`, `cushiontest [fling|lap]`, `bjtest`.
- Still open: suppressors cost 150 in the gun prefabs (plan says 250); pirates walking the village; guests on the
  pool loungers and at the slots; Shako's playtest verdicts.

## On another PC (after `git pull` of `shako-dev`)
`git lfs pull` (the models and textures are in LFS), open the project in Unity 6000.3.25f1 and let it import. Nothing
has to be regenerated: the Game scene, the baked bodies (Avatar/Bodies) and Resources/CreatureModels are committed.
Build: `PLEASE DON'T DROWN > Dev build (scene as is)` (or `-executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build`).
Then try `story goto 3.8`, `bosstest kleptopus`, `creature alligator`, `chapter3check`.

## Test commands (dev build: `PLEASE DON'T DROWN > Dev build (scene as is)` or `DevPlayerBuild.Build`)
- `bosstest kleptopus` (at the hotel pier), `kleptocheck` (automated), `story goto 2.5b` / `story goto 3.1`..`3.8`,
  `chapter3check` (plays chapter 3 through automatically), `swing` (nearest rope swing).
- `stealthtest [guards] [parrots]`: sneak to the flag 45 m ahead past patrolling guards and parrots; seen = restart.
- `creature <parrot|monkey|alligator|snapper> [count]`, `creatures` (list), `disguise [on|off]`, `bosstest` (Old Snapper).
- Sleepers: running within ~7 m (or walking right past, ~2.5 m) stirs them; woken they sit up and look ~7 s with sleepy
  eyes (10 m), then sleep again; a loud shot wakes them into the alarm. Night guards see 13 m (22 by day).
- `killcheck`: automated checks of killing/K.O., drunk brawls, stray shots, the alligator killing a pirate (host log `[Check]`). `preytest`: an alligator by the nearest shore with two pirates on the bank.
- `threatcheck`: automated checks of all of the above (host log `[Check]`, ends `[Check] DONE: ALL PASS`).
- `brawltest [count] [fists|cutlass|pistol|musket|mix|drunk|brawler]`: a mission with fighters around you.
- `fightstats`: every fighter's swings/hits/misses/shots/blocks (host log, `[Fight]`).
- `fail [reason]`: fail the running mission. `hurt [n]`: hurt yourself. `cprfriend`: CPR the nearest downed friend.
- `strafe <s> [left|right|back] [sprint]`: autopilot dodge (tests). `whack [n]`: hit the nearest fighter.
- Headless: `PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-host-offline -pdd-nosave -pdd-nostory
  -pdd-exec "wait 6; brawltest 3 mix; wait 40; fightstats" -logFile <log>`.

## Art made with Tripo
In the game (2026-10-11):
- **Pirates**: pirate_fat (Big Sal, Iron Jaw Jack), pirate_girl (One-Eyed Olga), pirate_marksman (Pirate Pete),
  pirate_quartermaster (Salty Bill): avatar bodies 10-13 (`AvatarLook.Bodies.Pirate*`), dealt out by
  `StoryDirector.PirateLook`. Rigged with `prepare_character.py --turn -90 --symlegs 1` (+ `--shoulderx 0.1` for the
  long coats). Hotel director: body 25, rigged, not in the story yet.
- **Creatures**: monkey, alligator, Old Snapper, Kleptopus: `Resources/CreatureModels/*.glb` from
  `ArtSource/Tools/tripo_prepare_creatures.py` (faced forward, sized, split into Body/Head/Jaw/Tail on hinges);
  `Creature.Tripo.cs` hangs them on the greybox pivots (the greybox keeps the hit zones). Jaws open, tails sway. The
  Kleptopus keeps its two working (shootable) arms as before; its own 8 tentacles are the idle arms. The parrot is
  still greybox.
- **Gold vault**: gold_hoard, gold_bar_stack (+ the island 3 chests, coin piles, sacks, chips).
- **Casino**: `BuildCasino` puts the Tripo slot_machine on the 6 black stands (`GameSceneBuilder.CasinoTripo`), but the
  saved scene still has the box machines (merged into the lobby's combined mesh): they change at the next hotel
  rebuild, which wasn't run (Shako's hotel look is fragile).
- Ready for the gala, not placed yet (`Assets/_Game/Art/Tripo/Island2/`): roulette_table, blackjack_table,
  champagne_tower (no table under it: stand it on one), crystal_chandelier, velvet_rope_stanchions,
  inflatable_cushion (the cushion ride). Prompts: `.claude/skills/pdd-tripo/prompts/island2-gala-gold.md`.

Not in the game:
- pirate_king and pirate_king_nohook: the automatic rig fails (arms wide in the coat: the arm joints land on the wrong
  side). pirate_teen (T-pose) and tavern_brawler (arms against the body) can't be rigged automatically either.
- `ArtSource/Tripo/island3/`: gun_shop, ufc_cage (came out 6-sided, no gate), yacht (in the island-3 low-poly style).
- Known look issue: the marksman's and Olga's hands touch the holster at their hip when standing still.
- Review pictures: `Screenshots/Review/Tripo/` (local only: `Screenshots/` is git-ignored; render them again with
  Blender from the GLBs if needed). Prompts: `.claude/skills/pdd-tripo/prompts/pirates.md`, `island3.md`.

## Known issues
- (Fixed by 2026-10-17: the autoplay test plays 2.1 to 2.7 through; it used to stick at 2.1.)
- `Tools/test-story.ps1` failed in folders with spaces (log paths unquoted): fixed on `shako-dev`.
