# UI restyle handoff

## Sol pass

The UI now shares a coastal rescue palette: deep navy surfaces, seafoam interaction accents, coral urgency, warm sand rewards, and off-white text. `Assets/_Game/UI/Hud.cs` is the source for colors, panels, buttons, bars, sliders, and vitals. Changing that file affects every menu and most in-game overlays.

The Sol pass also updated the objective and wallet, subtitles, chapter cards and report, boss bars, hotbar, notifications, console, speech bubbles, and wallet text in the shop, bar menu, and blackjack. The project compiles with `dotnet build PleaseDontDrown.csproj --no-restore` (0 errors; existing Unity serialization warnings).

## UI map

| Area | Source | Contents |
| --- | --- | --- |
| Shared UI primitives | `Assets/_Game/UI/Hud.cs` | Scale, type, palette, panels, buttons, sliders, bars, icons, vitals |
| Main and pause menus | `Assets/_Game/UI/DevConnectMenu.cs` | Start, join, options, keys, invitations, travel, guide, confirmations |
| Character screen | `Assets/_Game/UI/AvatarCustomizer.cs` | Character preview and options |
| Player HUD | `Assets/_Game/UI/PlayerHud.cs` | Prompts, hotbar, held item, vitals, injury, downed state, toast messages |
| Rescue HUD | `Assets/_Game/UI/RescueHud.cs` | Victim markers, CPR sequence and progress, carrying hint |
| Combat HUD | `Assets/_Game/UI/ThreatHud.cs`, `Assets/_Game/UI/WeaponHud.cs` | Alert indicators, disguise, boss/mission bars, timer, crosshair, scope, hit feedback, weapon inspection |
| Story HUD | `Assets/_Game/Story/StoryHud.cs` | Chapter, objective, waypoint, shared wallet, subtitles, chapter report and titles |
| Dialogue and world text | `Assets/_Game/Story/DialogueService.cs`, `Assets/_Game/UI/FloatingText.cs` | Dialogue history/data, NPC speech bubbles, rewards and damage text |
| Purchases and activities | `Assets/_Game/Story/ShopCounter.cs`, `Assets/_Game/Fun/BarSeat.cs`, `Assets/_Game/Fun/BlackjackTable.cs` | Equipment shop, food menu, casino cards and bets |
| Other overlays | `Assets/_Game/Story/LoveHut.cs`, `Assets/_Game/UI/DevConsole.cs`, `Assets/_Game/World/Water/UnderwaterFx.cs`, `Assets/_Game/Rescue/BloodFx.cs` | Story fade, developer logs, underwater and blood effects |
| Supporting assets | `Assets/_Game/UI/ItemIcons.cs`, `Assets/_Game/Resources/Fonts/` | Item thumbnails and HUD fonts |

There is no separate player-to-player chat window or quest log in the current UI. NPC conversations are subtitles and speech bubbles; log output is the developer console and Unity logs. `DialogueService.History` stores recent lines but has no player-facing history panel.

## Astra polish targets

1. Review all screens in Unity at 16:9 and a narrower window. The Sol pass was compiled but not visually run in the Unity editor.
2. Refine hard-coded colors in the individual HUD and menu files so every state follows the palette, especially alert states, blackjack felt, shop rows, and item popups.
3. Check contrast, long localized or dynamic text, subtitle overlap with boss/CPR prompts, and interaction affordances with mouse and controller.
4. Consider replacing the current Roboto-based type hierarchy and procedural icons with a small, consistent art set if the game art direction calls for it.

Existing unrelated working-tree changes were left in place.
