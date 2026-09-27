# PLEASE DON'T DROWN

Co-op (1–4 players) lifeguard chaos game. Unity 6.3 LTS (6000.3.25f1) · URP · FishNet 4.7.3 · Steam (FishySteamworks).

* Design: [Docs/01-Game-Design.md](Docs/01-Game-Design.md)
* Architecture and milestones: [Docs/02-Technical-Architecture.md](Docs/02-Technical-Architecture.md)

## Open the project
Unity Hub → **Add → Add project from disk** → `F:\GameDev\PleaseDontDrown`, then open `Assets/_Game/Scenes/Game.unity` and press Play.
Keep the Steam client running for online features (dev app id **480** / Spacewar, see `steam_appid.txt`).

## Controls (prototype)
| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move / look | WASD / mouse | Left / right stick |
| Sprint · Crouch · Jump | Shift · Ctrl or C · Space | L3 · B · A |
| Use / pick up (bell, switch, sign, items) | E | X |
| Throw held item (hold to charge) | Left mouse, or hold G | RT, or hold Y |
| Drop held item | Tap G | Tap Y |
| Swim: dive · rise | Ctrl · Space (underwater) | B · A |
| Climb out onto dock / rock | Space in the water, facing the ledge | A |
| Pause / connection menu | Esc | Start |
| Dev console | ` (backquote) or F2 | |

Useful console commands: `help`, `noclip`, `speed 3`, `tp spawn`, `lookat x y z`, `fov 90`, `sens 0.15`, `timescale 0.3`,
`players`, `ring`, `lights`, `spawn <item> [n]` / `spawn list`, `items`, `itemdebug`, `grab`, `throw [0-1]`, `drop`,
`waves <scale>` (storm = 3), `water`, `breath`, `knock x y z`, `use`, `targetdebug`, `contacts`, `screenshot [delay]` (saved to `Screenshots/`).
Cheats work in the editor and dev builds.

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
| `-pdd-port <port>` | Local/LAN port (default 7770; tests use 7790) |

## Layout
`Assets/_Game/` game code (asmdef `PleaseDontDrown`) · `Assets/_Game/Editor/` editor tools · `Assets/ThirdParty/FishySteamworks/` vendored transport (BSD-2) · `Docs/` design.
