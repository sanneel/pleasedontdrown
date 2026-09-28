# PLEASE DON'T DROWN

Co-op (1–4 players) lifeguard chaos game. Unity 6.3 LTS (6000.3.25f1) · URP · FishNet 4.7.3 · Steam (FishySteamworks).

* Design: [Docs/01-Game-Design.md](Docs/01-Game-Design.md)
* **Dev island** (every gun and part free, shooting range, rescue test buttons, model gallery; pad on island 1 or `devisland`): [Docs/09-Dev-Island.md](Docs/09-Dev-Island.md)
* **Guns** (pistol, SMG, shotgun, rifle, sniper; parts, recoil, projectiles): [Docs/08-Weapons.md](Docs/08-Weapons.md)
* **Story mode** (chapters 1-2: Sandy, the robber, the hotel island, sharks, pirates): [Docs/05-Story-Mode.md](Docs/05-Story-Mode.md)
* Architecture and milestones: [Docs/02-Technical-Architecture.md](Docs/02-Technical-Architecture.md)
* Art direction + Meshy prompts: [Docs/03-Art-Direction-Meshy.md](Docs/03-Art-Direction-Meshy.md) (exports go in `ArtSource/Meshy/`)
* **Everything to generate in Meshy** (checklist, prompts, order): [Docs/06-Meshy-Asset-List.md](Docs/06-Meshy-Asset-List.md);
  island 1 only, Free plan (characters via an A-pose picture): [Docs/07-Meshy-Island-1.md](Docs/07-Meshy-Island-1.md)

## Art

* **Characters** (lifeguards and tourists) are cartoon avatars built in code (`Assets/_Game/Avatar/`): one skinned
  mesh per character from rounded shapes, animated procedurally (walk/run with planted feet, crouch, jump, crawl,
  breaststroke, treading water, carrying, throwing, CPR, eating, waving, looking around, blinking). Players pick their
  look in **Customize your lifeguard** (menu); tourists get a random look from their seed. Your own view shows
  first-person hands only, no arms (How to Fish style): two big, chunky, faceted hands with a thumb and jointed
  fingers that float at the bottom of the view, trail your turns, and blend into each item's grip (palms on a box's sides, fingers spread over a ball,
  a coconut cupped in the palm, a fist round the life ring's tube; items can carry hand-placed `GripRight/GripLeft`
  transforms), stroke when you swim, press flat during CPR, point at what you use and follow a throw out.
* **Meshy GLBs**: shack, watchtower and palms (decimated). The shack (x1.45) and tower (x1.2, and x1.5 wider) are
  scaled so you can walk in: their painted doors are cut out and replaced with real doors. Sign lettering is painted
  into textures by `ArtSource/Tools/make_signs.py` (Roboto, Apache 2.0).
* **Characters**: Sandy is a rigged Meshy model on the game's own skeleton (`ArtSource/Tools/prepare_character.py`
  + `Editor/MeshyCharacters.cs`, see 04 "Rigged characters"). The older Meshy characters were static poses
  (no skeleton), so they are no longer used; see [Meshy integration](Docs/04-Meshy-Integration.md).

## Open the project
Unity Hub → **Add → Add project from disk** → `F:\GameDev\PleaseDontDrown`, then open `Assets/_Game/Scenes/Game.unity` and press Play.
Keep the Steam client running for online features (dev app id **480** / Spacewar, see `steam_appid.txt`).

## Controls (prototype)
| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move / look | WASD / mouse | Left / right stick |
| Sprint · Crouch · Jump | Shift · Ctrl or C · Space | L3 · B · A |
| Use / pick up (bell, switch, sign, doors, palms, items, tourists) | E | X |
| Inventory slots (small things go in your pockets) | 1-4 / mouse wheel | D-pad left / right |
| Eat the food in your hands (coconut) | Hold right mouse | Hold LT |
| Wave | V | D-pad up |
| CPR on an unconscious tourist on land (keep tapping: compressions, then mouth-to-mouth or a punch) | Right mouse | LT |
| Punch (empty hands) | Left mouse | RT |
| Use the held tool (pistol: shoot, defibrillator: shock) | Left mouse | RT |
| Throw held item (hold to charge) | Left mouse, or hold G | RT, or hold Y |
| Drive a jet ski / boat: throttle, steer · get off | W/S, A/D · E | Left stick · X |
| Drop held item | Tap G | Tap Y |
| Swim: dive · rise | Ctrl · Space (underwater) | B · A |
| Climb out onto dock / rock | Space in the water, facing the ledge | A |
| Pause / connection menu | Esc | Start |
| Dev console | ` (backquote) or F2 | |

**Food:** your food meter empties over a shift (faster when sprinting or swimming). Hungry lifeguards get their breath
back slowly. Shake a palm (E on the trunk) and a coconut drops; hold right mouse with it in hand to eat it.

**Rescue drill:** press E on the red *RESCUE DRILL* board by the spawn. A tourist appears 25-40 m out and the bell rings.
Swim out, grab them (E) and tow them to the shallows. If they go unconscious, carry them onto the sand, put them down (tap G)
and tap the right mouse button for CPR before their time runs out. Tourists near a floating life ring grab it and calm down.

Useful console commands: `help`, `noclip`, `speed 3`, `tp spawn`, `lookat x y z`, `fov 90`, `sens 0.15`, `timescale 0.3`,
`players`, `ring`, `lights`, `spawn <item> [n]` / `spawn list`, `items`, `itemdebug`, `grab`, `throw [0-1]`, `drop`,
`waves <scale>` (storm = 3), `water`, `breath`, `knock x y z`, `use`, `use2`, `targetdebug`, `contacts`, `screenshot [delay]` (saved to `Screenshots/`),
`grab [name]`, `drill`, `victims`, `victim [distance] [state]`, `vset <state|air|panic|condition> <value>` (nearest tourist),
`cpr [pumps]`, `clearvictims`, `ragdoll`.
Story: `story` (status), `story list`, `story skip`, `story goto 1.9`, `story reset`, `story off`, `money [n]`, `npcs`,
`goto <name>`, `bring <item>`, `tourist f|m [silent] [flatline]` (on land: collapsed, needs CPR), `robber`, `whack [n]`,
`shark`, `pirates`, `vehicles`, `drive <s> [steer] [throttle]`, `usetool`, `punch`, `buy <n>`, `dialogue`.
Cheats work in the editor and dev builds.

**Story mode** starts by itself when you host: Sandy walks over to meet you from her Lost & Found (the shack by the
bell: hand things in at its window), then follow the objective in the top left.
The beaches are full of tourists sunbathing, wading and swimming (press E to chat); the ones who get into trouble are
swimmers you saw a moment ago. Progress (beat + money) is saved in `%USERPROFILE%/AppData/LocalLow/PleaseDontDrown/PLEASE DON'T DROWN/story.json`;
`story reset` starts over. Test switches: `-pdd-nostory` (sandbox, no story) and `-pdd-nosave` (don't read or write the save).

## Feel (modelled on How to Fish)
* **Physics-body player**: a rigidbody capsule (75 kg) whose velocity is shaped each physics step: eased walk/sprint,
  ground acceleration and braking, air control that keeps momentum, extra gravity, coyote time + jump buffer,
  slides off steep slopes, steps up low ledges (≤ 0.36 m), knockback with a moment of lost control. Shoves crates, kicks balls.
* **Physics-held items**: the item stays a live body steered to a hold point by velocity, so it glides in after pickup,
  lags with its weight, slides along walls instead of clipping, and pulls back while a throw charges. Others see it glued to your head.
* **Grab targeting**: exact crosshair hit first, otherwise a capsule around the view line picks the visible item nearest the crosshair.
* **Camera**: stepped head bob, strafe roll, landing dip, sprint FOV. Surface footsteps (sand, wood, rock, shallow water).

## Test multiplayer on one PC
1. Build: menu **PLEASE DON'T DROWN** or batch (below) → `Builds/Win64/PleaseDontDrown.exe`
2. Run the exe twice: window A **Play solo (local host)**, window B **Join local/LAN** (`localhost`).

Online: **Host (Steam)** → **Invite friends** in the menu. The friend needs the same build; they accept the invite in Steam.

## Batch commands
```bash
"F:/Unity/Editors/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath "F:/GameDev/PleaseDontDrown" -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.BuildPlayerBatch -quit -logFile Logs/build.log
```

Automated 2-instance smoke test (headless, no Steam, on its own port so it never joins a game you're playing):
```bash
Builds/Win64/PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-port 7790 -pdd-host-offline -pdd-quit-after 25 -logFile Logs/host.log
Builds/Win64/PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-port 7790 -pdd-join localhost -pdd-quit-after 15 -logFile Logs/client.log
```

Every connection shakes hands with its build version (`NetVersion`: app version + the stamp the scene builder writes).
A client from a different build is refused with a clear message instead of failing on mismatched scene objects.

## Launch switches
| Switch | Effect |
|---|---|
| `-pdd-nosteam` | Skip Steam initialization |
| `-pdd-host-offline` | Start a local host at launch |
| `-pdd-join <address>` | Join a local/LAN host at launch |
| `-pdd-quit-after <sec>` | Quit after N seconds |
| `-pdd-exec "<cmd>; <cmd>"` | Run console commands once the local player exists (`wait <sec>` pauses) |
| `-pdd-noinput` | Ignore the real mouse/keyboard/gamepad (windowed automated tests; console commands still work) |
| `-pdd-port <port>` | Local/LAN port (default 7770; tests use 7790) |

## Layout
`Assets/_Game/` game code (asmdef `PleaseDontDrown`) · `Assets/_Game/Editor/` editor tools · `Assets/ThirdParty/FishySteamworks/` vendored transport (BSD-2) · `Docs/` design.
