# Overnight prompt for Shako's Claude: finish island 3 (2026-10-18)

Paste everything below the line into a new Claude Code session on Shako's PC (or tell it: "read
`Docs/Overnight-Prompt-Shako-Island-3.md` on origin/main and do it").

---

You are working alone overnight on **PLEASE DON'T DROWN** (Unity 6000.3.25f1, URP, FishNet), GitHub
`sanneel/pleasedontdrown`. Sandro (owner) asked for this; Shako is your user. Nobody will answer questions until
morning, so **never stop to ask**: decide, write the decision down in the report, and keep going.

The owners are strict and judge by pictures and by playing. **A task is only done when you have seen it working in
the real game build, in screenshots you looked at closely yourself** (crop and zoom at full resolution; a contact
sheet is not enough). They get furious about: hands inside bodies, people standing inside furniture or walls, floating
props, things clipping, big heads or big hats, dark boxes behind on-screen text, and "done" claims the pictures don't
back up.

Island 1 and island 2 are in good shape now. **Island 3 (Skull Cove) is the weakest part of the game**: the story
works in pieces, but the Pirate King, the sewer and the palace were never built, and nobody has looked at it closely
from a player's eyes. Tonight you make island 3 as finished as island 2.

## 0. Before you start

1. `git fetch origin`, then on `shako-dev`: `git merge origin/main`. Main has everything as of `0e348064`: Sandro's
   new island 1 village, the new Tripo models (Pirate King body 9, black-tee tourist 26, hotel attendants 27-28,
   blue monkeys) and your island 2 overnight work. If `Game.unity` conflicts, **take main's**
   (`git checkout --theirs` from your side's point of view: main's version) and re-run any of your own scene batches
   that are not on main yet. Make sure LFS files are real files after the merge.
2. **Your hotel texture fix.** Sandro says you fixed the island 2 hotel's bad textures, but it is not on GitHub: no
   commit on any branch touches the hotel's GLBs or textures. Find it in your working copy (`git status`, look for
   `resort_hotel_*`, `Art/Props`, `HotelReal_*` materials or textures), check it in the game (outside and in the
   lobby), commit it on `shako-dev` with a clear message and push. Do this first.
3. Read and follow: `.claude/skills/shako-helper/SKILL.md`, the `pdd-visual-review` skill (if your PC has it; if not,
   its rules are in this file), `.claude/skills/pdd-tripo/SKILL.md` (lessons at the end), `Docs/15-Island-3-Skull-Cove.md`,
   `Docs/16-Plan-Islands-2-3.md` (island 3 part), `Docs/17-Flow-Islands-1-3.md`, `Docs/18-Team-Split.md` (C1-C12, A11).
4. Make sure no Unity editor or batch run is open on the project before a batch run.

## 1. Hard rules

- **Git:** work on `shako-dev` only. Commit in small commits with clear messages (end each with your Co-Authored-By
  line) and push `shako-dev` at the end, and once in the middle of the night. **Never push `main` or `sancho-dev`.**
- **Files Sandro's Claude is changing tonight (don't edit them):** character animation, CPR and fighting:
  `Avatar/AvatarAnimator.cs`, `Avatar/AvatarHands.cs`, `Avatar/BodySpace.cs`, `Rescue/VictimBody.cs`,
  `Rescue/VictimBrain.cs`, everything in `Combat/`, `Player/PlayerCarry.cs`, `Player/PlayerAvatar.cs`,
  `Story/StoryNpc.Combat.cs`, `Story/StoryNpc.Rivals.cs`, `Editor/ReviewCapture.cs`. If island 3 needs a change there
  (for example the Pirate King's fight), write exactly what and why in the report instead.
- **Scene file:** never run `GameSceneBuilder.BuildPlayerBatch` or anything that regenerates `Game.unity` from
  scratch. Change the scene only with small batch methods that open it, add/move named objects and save, and that are
  idempotent (running twice replaces, never duplicates), like `GameSceneBuilder.Island1TownBatch`. After each scene
  patch, `git diff --stat Assets/_Game/Scenes/Game.unity` must be the size you expect. Builds: only
  `DevPlayerBuild.Build`. Unity batch runs leave float noise in `Data/Materials/Prop_*.mat` and `Brass.mat`:
  `git checkout --` those before committing.
- **Islands 1 and 2:** don't change them (except to fix something you broke).
- **Art:** no Tripo credits unless Shako is awake and says yes. Use what exists: `Art/Tripo/Island3/` (castle keep,
  gatehouse, towers, walls, gold hoard, gold bars, coins, treasure table, gun shop, alligator...), the Pirate King body
  (`AvatarLook.Bodies.PirateKing = 9`), the pirate bodies, Meshy props, and clean primitives. Run `GlbTextureSlim.Run`
  on any new GLB.
- **Characters:** no big heads or hats. The players' lifeguard has very short arms (0.56 m reach). Nobody stands inside
  furniture; check with the `overlaps` console command near people.
- **Multiplayer:** the host is the authority; everything must also work for a joined client. Test with two copies.
- **Performance:** island 3 is at night. Measure `perf` on `-pdd-quality low` before and after; no careless real-time
  lights (use the emissive glow approach). The island 3 arrival has an ~80 ms spike; if you can find and fix it, do.
- **Stuck more than ~45 minutes on one thing:** write down what fails and why, leave the code working, move on.

## 2. Tasks, in this order

### 2.1 The sewer and the palace throne room (A11 / C5: the Pirate King is never seen)

Beat 3.7b "The Pirate King" (`Story/StoryDirector.Chapter3.cs`, look for `PirateKingBeat`) is **skipped** because two
points don't exist: `Environment/Island3/Story/SewerEntrance` and `Environment/Island3/Story/PalaceThrone`. Sandro just
made the Pirate King's own Tripo model (red coat, peg leg), and nobody has ever seen him in the story.

- Read the beat's code first and do what it expects. Build, with a batch (`GameSceneBuilder.Island3PalaceBatch` or
  similar, idempotent):
  - **The sewer entrance** behind the back waterfall (C5 put the drain and grate `SewerExitDrain` /
    `SewerGrateHinge` / `SewerExit` in the castle yard): a dark stone tunnel mouth, an iron grate, a short walkable
    tunnel that comes out at `SewerExit`. Simple stone primitives are fine; it must be walkable (navmesh) and lit just
    enough to see (glow, not shadows).
  - **The throne room** inside the castle keep: a room you can walk into from the yard, a throne on a raised step,
    banners, gold (gold hoard, bar stacks, coin piles), torches (glow). `PalaceThrone` in front of the throne, facing
    the room. Big enough for a fight with 2-4 players (at least 10 x 12 m clear floor).
- Then play 3.7b in the build: the king in his own model, his lines, the fight, him saying where to go. Screenshots:
  the sewer from outside, inside, the exit; the throne room empty and with the king; the king up close (face, hands,
  peg leg on the floor, not floating, not in the throne).

### 2.2 Chapter 3 end to end

- `-pdd-autoplay 3.8 -pdd-timescale 2` (headless) and `chapter3check` / `threatcheck` / `killcheck` must pass. Fix
  every failure (story code, scene points, navmesh), not by skipping beats.
- Then play it with pictures (`-pdd-autoshots` on a windowed run): every beat 3.1-3.8 from the player's eyes. List
  each defect you see, fix, re-check.

### 2.3 Island 3 from a player's eyes (strict review)

Walk the whole island at night like a player: the mountain dock arrival, the village and the THIEF's house, the
tavern (octagon cage, the new treasure tables with stools), the gun shop, the jungle (monkeys: capuchin, blue monkey,
blue ape; film from above the canopy), the river, Old Snapper's lair, the castle, the gold vault. For each place:

- People or animals inside walls, furniture, trees, rocks or each other (C3's "other animals" check was never done).
- Floating or sunk props, gaps in walls, see-through faces, z-fighting, stretched textures.
- Too dark to see where to go at night? Add a few glowing lanterns or torches along the paths (no real lights).
- Navmesh holes: NPCs stuck or walking through walls.

Fix what you find with idempotent batches. Before/after pictures in the report.

### 2.4 Island 3 "looks finished"

Like island 2's "look expensive" pass, but pirate: signposts (Tavern, Guns, Jungle, Castle, Dock), rope fences,
barrels and crates at the dock, nets, lanterns on posts along the village road, a few campfires with sleeping or
drinking pirates (use the existing sleepers / regulars code). Sparse and tasteful; nothing on roads or blocking doors.

### 2.5 Leftovers from island 2 (only if time is left)

- The waitress (Paola, now in the flight-attendant uniform, body 28) carries a tray (a held prop in both hands, within
  0.56 m of the shoulders is the lifeguard rule; she is a tourist body, so measure her arms).
- The "GRAND CORAL" arch letters bigger so they read from the beach.

## 3. Testing (paths on your PC: see shako-helper)

- Dev build: `-executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build -pdd-out Builds/Play/PleaseDontDrown.exe`.
- Windowed test run: `Builds/Play/PleaseDontDrown.exe -pdd-nosteam -pdd-port <new port each run> -pdd-host-offline
  -pdd-nosave [-pdd-nostory] -pdd-quality high -screen-width 1280 -screen-height 720 -pdd-exec "..."`.
- Commands: `island3`, `goto <name>`, `tp x y z`, `lookat x y z`, `meet [dist] [head] [name]`, `story goto 3.7b`,
  `npcs`, `overlaps 10`, `perf 6 label`, `cam fixed x y z tx ty tz fov` + `record name 1280 720 2` / `record stop`
  (aerials: `frameshot` captures the player's camera, so film aerials with `record`), `frameshot name` (put a `wait`
  after it), `quit`.

## 4. The morning report

Write `Docs/Overnight-Report-Island-3.md` (short version at the top; then each task: done / partly / not, with the
pictures' paths; decisions you made; numbers: perf before/after, autoplay result; still wrong or not checked). Commit
it with the pictures you reference (small JPGs, under 300 KB each) and push `shako-dev`.
