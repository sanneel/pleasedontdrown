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
| C5 | Perception: sight cone, hearing (`ServerNoise`, gunshots: suppressed ~4 m, loud ~60 m), alarm | to do | `Story/Stealth.cs` (planned) |
| C6 | Creatures: base with health + hit zones, parrot sentry, monkey (steals items), alligator (lunge + grab) | to do | `Creatures/` |
| C7 | Boss framework: phases, telegraph → stun window, boss bar, scaling per player | to do (Kleptopus, Old Snapper) | `Story/BossFight.cs` (planned) |

Weapons are **logic only** for now (`NpcWeaponKind`: Fists, Cutlass, Pistol, Musket, synced as `StoryNpc.WeaponKind`);
models get attached later. Shako does not want separate weapon models generated yet.

Measured (headless tests, 2026-10-11): fists vs a still player ~90% land, vs a player dodging left/right 29%; pistols
~40% still, ~21% dodging. A cage fighter no longer gets stun-locked by mashed punches.

## Test commands (dev build: `PLEASE DON'T DROWN > Dev build (scene as is)` or `DevPlayerBuild.Build`)
- `brawltest [count] [fists|cutlass|pistol|musket|mix|drunk|brawler]`: a mission with fighters around you.
- `fightstats`: every fighter's swings/hits/misses/shots/blocks (host log, `[Fight]`).
- `fail [reason]`: fail the running mission. `hurt [n]`: hurt yourself. `cprfriend`: CPR the nearest downed friend.
- `strafe <s> [left|right|back] [sprint]`: autopilot dodge (tests). `whack [n]`: hit the nearest fighter.
- Headless: `PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-host-offline -pdd-nosave -pdd-nostory
  -pdd-exec "wait 6; brawltest 3 mix; wait 40; fightstats" -logFile <log>`.

## Art made with Tripo (not in the game yet)
- `ArtSource/Tripo/Pirates/`: pirate_king (hook + eye patch; a hand is still next to the hook: fix in Blender
  later), pirate_king_nohook, pirate_fat, pirate_teen (T-pose), pirate_girl, pirate_marksman, pirate_quartermaster,
  tavern_brawler. ~28-30k triangles each, PBR. To be rigged with `ArtSource/Tools/prepare_character.py`.
- `ArtSource/Tripo/island3/`: jungle_monkey, alligator, old_snapper (stylised-realistic), gun_shop, ufc_cage (came out
  6-sided, no gate), yacht (in the island-3 low-poly style). The parrot already exists (`Fun/Parrot.cs`).
- Review pictures: `Screenshots/Review/Tripo/` (local only: `Screenshots/` is git-ignored; render them again with
  Blender from the GLBs if needed). Prompts: `.claude/skills/pdd-tripo/prompts/pirates.md`, `island3.md`.

## Known issues
- The story gets stuck at beat 2.1 ("Go to the hotel reception") in the autoplay test, on plain `main` too
  (01c9421): not caused by `shako-dev`. Probably the hotel lobby move.
- `Tools/test-story.ps1` failed in folders with spaces (log paths unquoted): fixed on `shako-dev`.
