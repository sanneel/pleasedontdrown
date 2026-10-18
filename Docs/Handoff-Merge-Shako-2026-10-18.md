# Handoff: merge Shako's work into ours, then carry on (2026-10-18)

You are taking over from another Claude session on **PLEASE DON'T DROWN** (Unity 6000.3.25f1, URP, FishNet), repo
`F:\GameDev\PleaseDontDrown`, GitHub `sanneel/pleasedontdrown`. The user is **Sandro** (sanneel). His teammate is
**Shako**, whose own Claude works on the branch `shako-dev`. Our branch is **`sancho-dev`**. `main` was last set equal
to `sancho-dev` at `48844c0b`.

Your job, in order:

1. Commit our uncommitted work on `sancho-dev`.
2. Bring in everything Shako's Claude has done on `shako-dev`. That includes Shako's **fix for the island 2 hotel's bad
   textures**.
3. Re-apply our scene changes on top of the merged scene, and verify everything in real screenshots.
4. Push, then continue with the open tasks at the end of this file.

Work without asking step by step. Ask Sandro only for things only he can do: signing in to Tripo, or Shako pushing
something. Answer him in plain, short English. He is blunt; when he is angry, fix the thing, don't argue.

---

## 0. Read these first

- Memory index: `C:\Users\User\.claude\projects\F--GameDev-PleaseDontDrown\memory\MEMORY.md`. Read every file it
  lists, especially:
  - `pdd-island1-village.md`
  - `pdd-team-split-part-a.md`
  - `pdd-test-harness.md`
  - `pdd-low-end-perf.md`
  - `hotel-user-tripo-look.md`
  - `pdd-island3-night-arrival.md`
- Skills (in `.claude/skills/`):
  - **`pdd-visual-review`**: mandatory before you send Sandro any picture or say "fixed".
  - **`pdd-tripo`**: read its "Lessons" sections at the end.
  - `shako-helper`: describes Shako's way of working.
- Docs:
  - `Docs/13-Island-1-Fun.md` (section "The beach village")
  - `Docs/18-Team-Split.md`
  - `Docs/16-Plan-Islands-2-3.md`
  - Shako's `Docs/Overnight-Report-Island-2.md` (on `shako-dev`)

## 1. What the previous session did (all on sancho-dev, NOT committed yet)

`git status` shows about 17 modified files and about 150 untracked ones, all ours. Commit them first, in two or three
commits with clear messages. Before committing:

- Run `git checkout -- Assets/_Game/Data/Materials/Prop_*.mat Assets/_Game/Data/Materials/Brass.mat` if they show as
  modified. Unity batch runs leave float noise in them; never commit it.
- **Keep** the new untracked materials `Prop_moss.mat` and `StationDoor*.mat`.

### 1a. Island 1: the beach village (Sandro: "make it bigger buildings, only Sandy's house and the toilet this size, bar bigger")

- **Lifeguard station.** The red-roof house `Environment/Tower` was **deleted** and replaced by
  `Environment/LifeguardStation` on the same spot. It is two storeys: a walk-in room with a real Door, a roof deck
  with a LIFEGUARD board, a lookout cabin with open windows and a stool, and outside stairs.
  - Model: `lifeguard_station` in `ArtSource/Tools/model_props.py`, exported to `Assets/_Game/Art/Props/`.
- **Beach bar.** Same object, new model `beach_bar_big`: an 8.8 m counter, 8 stools, a terrace with 3 tables and
  torches. The barista, his 8 serve spots, the wipe spots and the drink spots were moved to the new counter. Ordering
  a beer was tested and works.
- **New things on the sand:**
  - boardwalks: both docks, a promenade at z 18, and paths to the toilets, the bar and the lighthouse
  - two PLEASE DON'T DROWN arches: at the west dock and at Milo's pier
  - 18 sunbeds with striped umbrellas
  - a volleyball court
  - 4 dunes with beach grass, rocks and a rope fence
- **Lighthouse.** Made with Tripo; the model is `Assets/_Game/Art/Tripo/Island1/lighthouse.glb`. It stands 16 m tall
  at (22, 47.5) with its door facing the beach.
- **Dunes in the terrain.** The dunes are part of `BeachHeight` (`Island1DuneHeight`), so `Data/Meshes/BeachTerrain.asset`
  and `Data/Navigation/Island1.asset` changed.
- **How it's applied.** Everything is placed by **`GameSceneBuilder.Island1TownBatch`**
  (`Editor/GameSceneBuilder.Island1Town.cs`). It is idempotent: run it again and it replaces what it made. It also
  moves palms, towels and loose items out of the way, and logs every move to `Logs/island1-town.txt`.
- **Checks already done:**
  - Perf on Low: +0.2 ms per frame (3.1 ms at the spawn).
  - The island 1 story autoplay passes up to 2.2. It reports FAIL only because of headless render errors.
  - Stair climbing works.

### 1b. Sandro's latest Tripo models (made by him on 2026-10-12, in his Tripo "Assets")

The sources (decoded, about 2 M triangles each) are in `ArtSource/Tripo/Latest/`.

- **Pirate King** (red coat, peg leg). New body id `AvatarLook.Bodies.PirateKing = 9`, file `pirate_king`. Beat 3.7b
  now uses it. Before, he borrowed the quartermaster's body.
- **Black-tee tourist.** Body `TouristTee = 26`, file `tourist_blacktee`. One man in four on the beaches wears it
  (`AvatarLook.RandomTourist`).
- **Hotel attendants (flight-attendant uniforms):**
  - `HotelAttendantA = 27` is now **Marisol**, the receptionist (`StoryDirector.ReceptionistLook`).
  - `HotelAttendantB = 28` is ready for the hotel staff (see task 4).
- **Monkeys.** The blue monkey and the blue ape are `Resources/CreatureModels/monkey_blue.glb` and `monkey_ape.glb`.
  Island 3's jungle monkeys pick the capuchin, blue or ape model by `ObjectId % 3` (`Creature.Tripo.cs`).
  - Monkeys live **only in the island 3 jungle** (Sandro insisted).
  - The `creature monkey` test command now also spawns them at the jungle homes (`StoryDirector.Threats.cs`).
- **Pirate treasure table.** Lives in the island 3 tavern: two tables, one each side of the door, six stools each.
  Placed by `GameSceneBuilder.TavernTreasureTablesBatch` (`Editor/GameSceneBuilder.TavernTables.cs`). The model is
  `Art/Tripo/Island3/treasure_table.glb`.

### 1c. Faces fixed (Sandro: "fix the faces and animations of the characters")

- **The problem.** The four characters first came out crumpled: creased faces, hair like screwed-up paper. Reducing a
  2 M-triangle model with its UV seams protected folds the surface.
- **The fix in `ArtSource/Tools/prepare_character.py`.** Sources larger than 8x the triangle budget are now:
  1. reduced freely, to `--tris 30000`;
  2. given new UVs;
  3. given their colour back by a Cycles bake from a full-detail copy (cage 0.006 H, ray 0.012 H).
- **Arm fix.** Arms held out almost level (the Pirate King) no longer send the shoulder off the body. The fallback
  logs "level arms".
- **Face fixes in `Editor/MeshyCharacters.cs`:**
  - `MouthHints`: mouths measured by hand on orthographic grid renders, for the king, the tee and both attendants.
  - `EyesHidden`: eyes behind sunglasses.
  - One eye under a patch is mirrored to the other side.
  - New `MeshyCharacters.BakeOnlyBatch -pdd-bodies a,b`: bakes just those bodies and keeps the rest of the library.
- **Checked** in ReviewCapture filmstrips: talk, walk at 4 phases, feet in heels, waving hands. All clean.
- **Known limits, the same for every model character:**
  - No body's mouth opens when talking. A mouth patch looked like a hole and was removed long ago.
  - The Tripo women never blink.

### 1d. Tools changed

- `tripo_prepare_creatures.py` and `tripo_prepare_island3.py`: weld UV seams before decimating. Without it, a 2 M
  model stopped at 40–70k triangles.
- `tripo_prepare_island3.py`: island 1 support and a `slim=` option.
- `GlbTextureSlim.Run -pdd-only <path part>` was run on every new GLB. Run it again for any new GLB.

## 2. The merge

```
git fetch origin
git log --oneline HEAD..origin/shako-dev
```

When this was written there were 11 commits, the newest `3e2e43a5` "Island 2 strict review". They cover:

- island 2: VIP guests, gala night, cushion ride, marina yacht, mooring, a fountain, slot-machine guests, lounger
  guests, carrying drunks to the infirmary
- hotel night lights
- `Docs/Overnight-Report-Island-2.md`

Their last commit **removed the hotel staff** because "every staff body is a bikini model". See task 4.

**Shako's hotel texture fix.** Sandro says Shako fixed the island 2 hotel's bad textures. When this was written, no
commit on `origin/shako-dev` touched the hotel GLBs or textures (`resort_hotel_*`, `Art/Props`, `HotelReal_*`
materials). So:

- Fetch again and look for it: `git log origin/shako-dev --stat | grep -i -B5 "hotel\|resort\|texture"`. Check every
  remote branch too: `git branch -r`.
- If it isn't pushed, tell Sandro in one line that Shako must push it, and go on with the rest.
- When it arrives, merge it and **do not undo it**. Our earlier low-end pass (`GlbTextureSlim`, see `pdd-low-end-perf`)
  compressed hotel GLB textures. Don't re-run the slim over the hotel files unless you've checked it keeps Shako's fix.
- Verify the hotel by screenshots, outside and inside the lobby, before and after.
- Rule from Sandro: the hotel is **his** Tripo look. Polish it, never rebuild it (`hotel-user-tripo-look.md`).

Merge steps (`git merge origin/shako-dev` on `sancho-dev`):

- **`Assets/_Game/Scenes/Game.unity` will conflict. Take Shako's** (`git checkout --theirs`). Then re-apply ours by
  running these batches in this order, with Unity closed:
  1. **"Part A: scene additions"**. Find the menu item or method in `Editor/` (`grep -rn "Part A: scene additions"`).
     It adds the bar tables, khinkali, blackjack, golf cart and stools.
  2. `PleaseDontDrown.Editor.PatchGolfCartReach` (the golf cart's wheel/grip fix, 3 lines), if it still exists.
  3. `PleaseDontDrown.Editor.GameSceneBuilder.Island1TownBatch`
  4. `PleaseDontDrown.Editor.GameSceneBuilder.TavernTreasureTablesBatch`
  5. The island 3 night batch, if `pdd-island3-night-arrival.md` says it is needed after merges.

  The command line for each:
  `"F:/Unity/Editors/6000.3.25f1/Editor/Unity.exe" -batchmode -quit -nographics -projectPath . -executeMethod <method> -logFile Logs/<name>.log`
  Then grep for `error CS` and `PASS`.
- **`BeachTerrain.asset`.** If both sides changed it, keep either; `Island1TownBatch` regenerates it from the merged
  `BeachHeight`. It refuses if the saved terrain differs from the generator off the dunes; it logs
  "DUNES SKIPPED". If it refuses, find out why before forcing anything.
- **Navmeshes:** take theirs for `Data/Navigation/Island2.asset` and ours for `Island1.asset`. `Island1TownBatch`
  rebakes island 1.
- **Code conflicts.** Expect them in `StoryDirector.Beats.cs`, `StoryDirector.cs`, `StoryNpc.cs`, `VictimBody.cs` and
  `Vehicle.cs`. Merge by hand and keep both sides' intent. Our side's animation and carry work in `VictimBody.cs` and
  `AvatarAnimator.cs` is important: Sandro spent days on hand overlaps.
- `Resources/AvatarBodies.asset` is ours; Shako didn't touch it. Check that body ids 26–28 aren't used on his side.
- After merging, compile (batch mode, grep `error CS`), then build:
  `-executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build -pdd-out Builds/Play/PleaseDontDrown.exe`.
  **Never** `BuildPlayerBatch`, which regenerates the whole scene.

## 3. Verify (strict, with pictures; follow `pdd-visual-review`)

- **Island 1.** Use the shots in `F:\temp\claude\...\scratchpad\tools\isl1b.sh`, or write your own: aerials via
  `cam fixed x y z tx ty tz fov` plus `record name 1280 720 2`, and ground views via `tp` + `lookat` + `frameshot`.
  Check:
  - the station, its stairs, deck, door and inside
  - the bar (order a beer)
  - the arches, sunbeds, volleyball, dunes and lighthouse
  - no palm, towel or NPC inside anything
- **Island 2.** Check:
  - Shako's new features
  - **the hotel textures, outside and in the lobby**
  - Marisol at reception (`meet 1.5 head Marisol`)
  - the gala and the yacht
- **Island 3:**
  - the tavern tables (`island3`, then `tp 86 21.3 -962.5`, `lookat 84 20.6 -966.5`)
  - the monkeys in the jungle: `goto JungleStart`. It's night there; film from above the canopy.
- **Story autoplay:** `-pdd-autoplay 2.1 -pdd-timescale 2`, headless. Every island 1 beat must pass. Later, 2.7 on
  island 2.
- **Perf:**
  `perf 6 spawn; wait 8; hotel; wait 8; perf 6 hotel; wait 8; island3; wait 8; perf 6 island3`
  on `-pdd-quality low`. Compare with about 3 ms at the spawn.
- Test harness tips: `pdd-test-harness.md`.
  - `frameshot` needs a `wait` after it.
  - The dev build is `Builds/Play/PleaseDontDrown.exe` with flags `-pdd-nosteam -pdd-port <p> -pdd-host-offline
    -pdd-nosave [-pdd-nostory] -pdd-quality high -screen-width 1280 -screen-height 720 -pdd-exec "..."`.
  - Use a fresh port per run.

## 4. Then continue with these tasks

1. **Hotel staff with the new bodies.** Shako removed the staff (bellhop Bella, pool attendant Ines, waitress Paola,
   in `StoryDirector.Gala.cs` `Staff()`) because they used a bikini body. Put them back on
   `AvatarLook.Bodies.HotelAttendantB` (28), the flight-attendant uniform. Marisol is already 27. Check the waitress
   walking her route and that nobody stands inside furniture.
2. **Shako's hotel texture fix:** done and verified (see section 2). If it is still not pushed, remind Sandro.
3. **Pirate King's scene.** Beat 3.7b skips because there is no `Environment/Island3/Story/PalaceThrone` (part C
   never built the palace). Tell Sandro, and offer to build a simple throne room so the new king model is seen in the
   story.
4. **Island 1 village review.** Re-check it after the merge. Sandro may ask for changes, such as more or fewer
   things or other positions. Edit the constants in `GameSceneBuilder.Island1Town.cs` and re-run the batch; never
   hand-edit the scene.
5. **Commit and push.** Commit on `sancho-dev` and push it. Sandro's pattern so far: "push our work to main, merge
   shako-dev first, then push". If he asks, fast-forward `main` to `sancho-dev` and push `main`. Never push to
   `shako-dev`. End commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
6. **Update memory** (`pdd-island1-village.md` and the others) with what you did, and keep `Docs/` current.

## 5. Rules Sandro cares about

- **No dark boxes, bands or pills behind HUD text.** The text is outlined. They came back once through an old
  restyle commit; watch merges for it (`Hud.Surface` must stay empty).
- **No big heads.** Hands must hold things properly; no clipping into bodies or props.
- **Never send a picture you haven't checked** at full resolution for defects.
- **Builds:** `DevPlayerBuild.Build` only. Unity must be closed for batch runs. Commit or push only when asked; doing
  it right after the merge counts as asked for this handoff.
- **Never revert other sessions' or Shako's work.** If something of his looks wrong, tell Sandro.
- **Paths and tools:**
  - Unity: `F:/Unity/Editors/6000.3.25f1/Editor/Unity.exe`
  - Blender: `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe`
  - Python (with PIL): `C:/Users/User/AppData/Local/hermes/hermes-agent/venv/Scripts/python`
  - Bash heredocs with quotes break: write scripts to files and run them.
- **Tripo** (`studio.tripo3d.ai`, built-in browser): Sandro must be signed in. It was signed out at the end of the
  last session. Settings: Ultra Mesh off, 2K, Triangle, polycount 30000. Download through the page's pinia store
  (see the `pdd-tripo` skill).
