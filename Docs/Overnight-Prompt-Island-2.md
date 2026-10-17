# Overnight prompt: finish Island 2 (paste everything below the line into a new Claude Code session)

---

You are working alone overnight on **PLEASE DON'T DROWN**, a Unity 6000.3.25f1 multiplayer (FishNet) lifeguard game.
Repo: `F:\GameDev\PleaseDontDrown`, branch `sancho-dev`. The owner is asleep. Nobody will answer questions until
morning, so **never stop to ask**: decide, write the decision down in the report, and keep going. Work through the whole
task list below, in order, until everything is done and verified or you run out of things you can do. Then write the
morning report.

The owner judges by playing and by looking at pictures, is very strict, and gets furious about anything that looks
broken: hands inside bodies, floating props, things clipping through walls, characters standing inside furniture,
"done" claims that the pictures don't back up. **A task is only done when you have seen it working in the real game
build, in screenshots you looked at closely yourself.**

## 0. Before you start (15 minutes)

1. Read these, in this order, and follow them:
   - `.claude/skills/shako-helper/SKILL.md` (team rules; the git rules there apply)
   - `Docs/16-Plan-Islands-2-3.md` (the agreed story and money numbers; island 2 section)
   - `Docs/19-Island-2-What-It-Lacks.md` (the list of what the island lacks)
   - `Docs/18-Team-Split.md` (who did what; B8–B14 are island 2)
   - The memory index `MEMORY.md` in your memory folder, and every memory it links about island 2, the hotel, the
     test harness and visual review.
   - The `pdd-visual-review` skill: load it and follow it before sending or approving ANY picture.
2. Run `git status` and note which files are already modified. **Many uncommitted changes belong to other work
   (a UI restyle, a texture pass, animation fixes).** Never revert, reformat or "clean up" files you didn't change
   for your task. If you must touch one of them, change only the lines you need.
3. Check free disk space on F: (about 21 GB free last time). Every player build is ~760 MB and recordings add up:
   delete your own old recordings (`Builds/Play/Recordings/<your names>`) and screenshots as you go. Never delete
   anything you didn't create. If free space drops under 8 GB, stop making recordings and clean up first.
4. Make sure no Unity editor or batch process is already running on this project before you start a batch run
   (another session may be working). If one is, wait for it to finish; don't kill it.

## 1. Hard rules (break none of these)

- **Git:** do NOT commit, push, merge, rebase or switch branches. Leave all work uncommitted for the owner to review.
- **Scene file:** never run `GameSceneBuilder.BuildPlayerBatch` or anything that regenerates `Assets/_Game/Scenes/Game.unity`
  from scratch: the hotel's look is fragile and other islands were hand-tuned. Change the scene only with small,
  targeted editor batch scripts that open the scene, add/move named objects, and save (example of the pattern:
  `Assets/_Game/Editor/PatchGolfCartReach.cs`). Also put the same change in the matching `GameSceneBuilder.*.cs`
  so a future rebuild keeps it. After each scene patch, run `git diff --stat Assets/_Game/Scenes/Game.unity` and make
  sure the change is the size you expect (a few hundred lines for new objects, not tens of thousands).
- **The hotel building:** do not rebuild, re-texture or reshape the Grand Coral hotel. It's the owner's own model.
  Add things around it and inside rooms; never replace it.
- **Island 2 stays small and hotel-centred** (180 x 110 m). Decor is allowed now (the owner approved the plan), but
  keep it tasteful and sparse: no clutter, nothing on the cart road or blocking doors and paths.
- **Islands 1 and 3 and the dev island:** don't change them, except shared code that island 2 needs; then re-test
  that island 1's story start and the dev island still work.
- **No new art generation tonight** (nobody is signed in to Tripo/Meshy). Use models that already exist:
  `Assets/_Game/Art/Tripo/Island2/` (yacht, inflatable_cushion, champagne_tower, crystal_chandelier,
  velvet_rope_stanchions, roulette/blackjack tables, slot machine), the Meshy props in `Assets/_Game/Art/Meshy/`
  (palm_tall and others), the existing resort models, and simple clean primitives where nothing exists (signposts,
  lamp posts, string lights). Run `GlbTextureSlim.Run` if you add any new GLB.
- **Characters:** never use big heads. The players' lifeguard ("goofy") has very short arms (0.56 m reach): every
  pose that reaches for something must stay within that. Tourists and staff are the existing baked bodies
  (`Assets/_Game/Avatar/Bodies`, hotel director = body 25, rigged, not in the story yet).
- **Multiplayer:** the host is the authority. Money, story state and NPC behaviour run on the server and sync to
  clients. Every feature must work for a client who joined, not just the host. Test with two copies (see 4).
- **Performance:** the game must still run on very low-end PCs. Measure with the `perf` console command before and
  after (see `pdd-low-end-perf` memory). Don't add real-time lights carelessly: use the existing emissive "glow"
  approach and few shadowed lights. Night lighting on the island must not drop the Low preset noticeably.
- **Style of the code:** match the surrounding code: its comment density, naming and plain-English comments.
- **If stuck for more than ~45 minutes on one thing:** write down exactly what fails and why in the report, leave the
  code in a working state (or revert only your own change), and move on to the next task.

## 2. What island 2 is for

Island 2 is **the island where you earn money.** Everything on it should pay you, cost you, or move the story toward
the gala and the yacht. The story beats in code are 2.1 Check in, 2.2 Buy a weapon, 2.3 Harder work, 2.4 Shark!,
2.5 One more guest, 2.5b The Kleptopus, 2.6 Pirates, 2.7 Your boat now (`Assets/_Game/Story/StoryDirector*.cs`).
The money plan (Docs/16): rescues pay 1.5x, VIP tips $100–300, bouncer $50 (−$20 per broken chair), cushion ride
$40 per tourist per tour, Kleptopus $300 + returned items, gala target $1,000 x number of players.

Useful code to read first: `Story/StoryDirector*.cs`, `Story/Economy.cs`, `Story/BeachCrowd.cs`, `Story/StoryNpc*.cs`,
`Story/ShopCounter.cs`, `Fun/BarSeat.cs`, `Fun/Barista.cs`, `Fun/BlackjackTable.cs`, `Vehicles/Vehicle.cs` (jet ski,
banana boat seats), `Rescue/VictimBrain.cs` and `VictimBody.cs`, `Editor/GameSceneBuilder.HotelIsland.cs`,
`GameSceneBuilder.HotelBar.cs`, `GameSceneBuilder.BarDining.cs`.

## 3. The task list (in this order)

For every task: design it in the existing code style → implement → compile → build the game → test it in the build
with scripted console commands → record and look at pictures → fix → write it in the report. Add a console test
command for each new feature (like the existing `bosstest`, `story goto`, `kleptocheck`) so the owner can try it
in one line.

### Task 1: VIP guests and tips
- About 1 in 5 tourists on island 2's beach is a VIP: visibly richer (gold sunglasses or a gold hat from the existing
  wear library, a gold name tag in the HUD prompt, e.g. "VIP Mr. Gold").
- Rescuing a VIP pays the normal rescue (x1.5 on island 2) **plus a $100–300 tip**, shown with the existing money
  popup. VIPs never take money away.
- Test: a command `viptest` that spawns a drowning VIP near the player; rescue them; log the money before/after.
- Done when: the tip lands in the shared money on host and client, the VIP look is clearly different in screenshots.

### Task 2: The gala money bar
- A **plain bar, top left, no numbers and no percent**, that fills with the team's money toward
  **$1,000 x number of players**. Shown only on island 2 during chapter 2 before the gala. Use the HUD system in
  `UI/HudCanvas.cs` (not IMGUI) and match the HUD style; check `Docs/UI-Restyle-Handoff.md` first and don't touch the
  restyle's own files beyond adding your element.
- When full: the director (Task 6) announces the gala.
- Test: `galamoney <amount>` to add money; screenshots at 0%, 50%, 100% with 1 and 2 players.

### Task 3: Bar bouncer job
- Now and then (every few minutes while players are near the Coral Bar) two drunk guests start a fist fight at the
  bar (reuse the existing NPC fight code: `StoryNpc.Combat`, punches, knockdowns).
- Stopping it (knocking out or pulling apart the fighters, use what fits the existing combat) pays **$50**; each
  chair broken during the fight takes **$20** off. Injured fighters can be carried to the infirmary
  (`HospitalBed.cs`).
- Test: `bartest` starts a fight now. Record it from two angles; check with the `overlaps` command that fighters'
  hands don't go into each other more than a few cm (see the test-harness memory).

### Task 4: Inflatable cushion ride
- A little jetty (or the end of the existing dock) with the inflatable cushion (`inflatable_cushion.glb`) and a sign.
  Two tourists sit on the cushion; it is towed by a jet ski the player drives (reuse `Vehicle.cs`: the banana boat
  already has straddle back seats, grips and a tow; the cushion replaces it on island 2).
- **$40 per tourist per completed tour** (a lap round buoys). Hard turns fling the tourists off: they then need
  rescuing (they become normal drowning victims).
- Test: `cushiontest` puts the player on the jet ski with the cushion and two riders. Record a lap and a fling.
- Check the riders sit ON the cushion (not inside it), hands on its handles, and the tow rope connects.

### Task 5: Blackjack side quest (not income)
- The blackjack table exists (`Fun/BlackjackTable.cs`, B12/A7). Make one full hand playable from start to end with the
  existing card UI. Bets come from the team's money; the casino closes after the gala robbery, so money can't be lost
  after that. It's a side quest: small, clean, working.
- Test: `bjtest` sits the player at the table and plays a hand automatically; screenshots of the cards.

### Task 6: The hotel director in the story
- The director (body 25) stands in the lobby (or on the hotel steps), talks like the other story NPCs (`StoryNpc`,
  `npcsay`), and drives the end of chapter 2: when the gala bar is full he announces the gala (dialogue + objective),
  after the pirate robbery he hands over the yacht keys.
- Keep the existing beats working: test `story goto 2.1` through `2.7` in order with the autoplay test
  (`-pdd-autoplay`), and fix anything that blocks progress. Note: the autoplay test was already stuck at beat 2.1
  before; find out why and fix it if you can.

### Task 7: Gala night
- When the gala starts: island 2 turns to night (reuse `World/IslandNight.cs` from island 3), and outside the casino:
  a red carpet, the velvet ropes, string lights, the champagne tower and chandeliers inside, guests in the casino.
- Then the existing beat 2.6 (pirates rob the casino) plays; after it the casino closes (blackjack locked).
- Test: `story goto 2.6` and a dedicated `galatest`. Record the arrival at the casino at night.

### Task 8: Marina and the real yacht
- A second pier (east or west of the main dock, not blocking the beach rescues) with the Tripo **yacht** moored at it,
  replacing the pirate boat that stands in for the yacht. The yacht must still be the thing that sails to island 3 in
  beat 2.7 (find the current stand-in in code and swap the model/vehicle, keeping the travel logic).
- Check the yacht floats at the right height on the water, the players stand on its deck (not inside it), and the
  pier lines up with its side.

### Task 9: Make it look expensive (sparse, tasteful)
1. **Palms and flowers:** palms along the cart road and the beach edge, a few flower beds at the hotel front, hedges
   round the pool. Keep paths, doors, the cart road and rescue routes clear. Check nothing floats or sinks
   (ground-snap every object; look at screenshots at eye level).
2. **Night lights:** lamp posts along the cart road, lights at the pool, lit windows on the hotel (emissive, not new
   shadowed lights). They switch on at night (gala) only.
3. **Signposts:** small gold-on-teal signs at the road: Casino, Coral Bar, Coral Arms, Pool, Marina, Cushion Ride.
4. **Arrival:** a fountain on the hotel forecourt and a "GRAND CORAL" arch at the dock, only if time allows.

### Task 10: People using the place (only if 1–9 are done)
- Staff: a bellboy at the door, a waiter walking between the bar and the pool, a pool attendant. Guests on the pool
  loungers and at the slot machines. Reuse `BeachCrowd.cs` behaviour and the existing seats/loungers; use only
  existing bodies. Check every seated character with the visual review rules (bottom on the seat, feet on the floor,
  hands not inside anything).

## 4. How to build and test (you have done this before; it works)

- Unity: `F:/Unity/Editors/6000.3.25f1/Editor/Unity.exe`.
- Compile + player build (cheats on, the scene as it is):
  `Unity.exe -batchmode -quit -projectPath "F:/GameDev/PleaseDontDrown" -executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build -pdd-out "Builds/Play/PleaseDontDrown.exe" -logFile <log>`
  then grep the log for `error CS` and `[Build] dev player OK`.
- Scripted run: `Builds/Play/PleaseDontDrown.exe -pdd-nosteam -pdd-port <unique port> -pdd-host-offline -pdd-nosave -pdd-quality high -pdd-exec "wait 8; hotel; wait 6; ...; quit" -logFile <log>`.
  Add `-pdd-nostory` for feature tests, leave it out for story tests. Kill the process if it's still running after
  your timeout.
- Two players: start a host as above, wait ~8 s, start a second copy with `-pdd-join localhost -pdd-port <same port>`.
- Useful console commands: `hotel`, `home`, `island3`, `tp x y z | spawn`, `thirdperson`, `meet [dist] [name]`,
  `aimat <name>`, `walk <s>`, `pitch <deg>`, `use`, `grab <name>`, `drop`, `punch`, `fire`, `click`, `victim`,
  `cpr`, `story goto <beat>`, `npcsay <name> <text>`, `overlaps <seconds>` (hands inside other bodies),
  `heldinfo`, `perf <seconds> <tag>`, `items`, `gun`, `targetdebug`.
- Pictures: `cam orbit <target> <distance> <height> <speed> <fov> <angle>` (the 5th number is the field of view, not a
  tilt; use 35–45) then `record <name> 960 540 <fps>` / `record stop` → frames in `Builds/Play/Recordings/<name>/`.
  `frameshot <name>` captures the final screen including HUD, but it fires at the end of the frame: put a `wait`
  after it. `cam chase`, `cam look`, `cam fixed` also exist (Dev/Showreel.cs). Contact sheets with Python + PIL
  (Python: `C:/Users/User/AppData/Local/hermes/hermes-agent/venv/Scripts/python`); always crop and zoom at full
  resolution to check hands, feet and contact points: a thumbnail is not evidence.
- Editor pose renders: `ReviewCapture.CaptureBatch -shots <file>` (see the visual-review skill for the line format;
  `seat:<object name>`, `nearm`, `handcam`).
- Automated checks that must still print ALL PASS at the end of the night:
  `GunHoldCheck.RunBatch`, `KissCheck.RunBatch`, `WalkCheck.RunBatch`, `HandCurlCheck.RunBatch`.
- Shell notes: the Bash tool is Git Bash; write multi-line Python edits to a file in the scratchpad and run it (inline
  heredocs with quotes break); keep each file's line endings (CRLF vs LF) when editing with scripts.

## 5. The strict visual check (do this for every visible change)

For each new or changed thing, look at it from at least 4 sides at normal camera distance and up close, in the
real build, by day and (if it has lights) by night, and go through this list. Any failure → fix and re-shoot:
- Does anything float, sink into the ground, clip through a wall, the hotel, the pier or the water?
- Characters: feet on the ground or seat, hands on what they hold (not inside it or another body), no stretched
  limbs, no big heads, faces readable.
- Can a player walk where they should (doors, paths, pier, yacht deck) and not where they shouldn't (into walls)?
  Test by walking through it with `walk` and checking the position in the log.
- Is it readable on a small screen, and does the HUD text stay clean (no boxes behind it, outlined text)?
- Does it still work for the client in a two-player run?

## 6. The morning report (required)

Write `Docs/Overnight-Report-Island-2.md` and make one overview image (`Builds/Play/Recordings/island2_overnight.png`)
plus one image per task. The report must say, for each task:
- **Done / partly done / not done**, in one line.
- What the player sees and the one console command to try it.
- What you tested (host, client, which angles) and the pictures.
- Anything still wrong, stated plainly ("still bad: the cushion's tow rope goes through the jet ski seat"). Never
  call something fixed that a picture shows otherwise.
- Decisions you made without the owner, so they can overrule them.
Also list every file you changed or added, the perf numbers before/after, and the four automated checks' results.
Write the report in plain, simple English (the owner's second language is English; short sentences, no jargon).

Start now with section 0, then Task 1. Don't stop until the list is done or nothing more can be done tonight.
