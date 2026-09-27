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
| Use (bell, light switch, sign…) | E | X |
| Pause / connection menu | Esc | Start |
| Dev console | ` (backquote) or F2 | |

Useful console commands: `help`, `noclip`, `speed 3`, `tp spawn`, `lookat x y z`, `fov 90`, `sens 0.15`,
`timescale 0.3`, `players`, `ring`, `lights`, `screenshot [delay]` (saved to `Screenshots/`). Cheats work in the editor and dev builds.

## Test multiplayer on one PC
1. Build: menu **PLEASE DON'T DROWN** or batch (below) → `Builds/Win64/PleaseDontDrown.exe`
2. Run the exe twice: window A **Play solo (local host)**, window B **Join local/LAN** (`localhost`).

Online: **Host (Steam)** → **Invite friends** in the menu. The friend needs the same build; they accept the invite in Steam.

## Batch commands
```bash
"F:/Unity/Editors/6000.3.25f1/Editor/Unity.exe" -batchmode -projectPath "F:/GameDev/PleaseDontDrown" -executeMethod PleaseDontDrown.Editor.GameSceneBuilder.BuildPlayerBatch -quit -logFile Logs/build.log
```

Automated 2-instance smoke test (headless, no Steam):
```bash
Builds/Win64/PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-host-offline -pdd-quit-after 25 -logFile Logs/host.log
Builds/Win64/PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-join localhost -pdd-quit-after 15 -logFile Logs/client.log
```

## Launch switches
| Switch | Effect |
|---|---|
| `-pdd-nosteam` | Skip Steam initialization |
| `-pdd-host-offline` | Start a local host at launch |
| `-pdd-join <address>` | Join a local/LAN host at launch |
| `-pdd-quit-after <sec>` | Quit after N seconds |
| `-pdd-exec "<cmd>; <cmd>"` | Run console commands once the local player exists |

## Layout
`Assets/_Game/` game code (asmdef `PleaseDontDrown`) · `Assets/_Game/Editor/` editor tools · `Assets/ThirdParty/FishySteamworks/` vendored transport (BSD-2) · `Docs/` design.
