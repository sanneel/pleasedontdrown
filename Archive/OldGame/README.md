# The old game, archived (2026-10-23)

Code and assets cut from the game by the scope change (`Docs/23-Scope-Change.md`): the story chapters 1-4, island 4,
the creatures and bosses, stealth, the cut guns, the NPC fights, blackjack, basketball, the bar menu, VIPs and the lost
items. They are kept here, outside the Unity project (Unity doesn't load anything outside `Assets/`), so nothing is
lost and the history stays readable.

The folders mirror where each file used to be under `Assets/_Game/` (`Assets/Items/Prefabs` was
`Assets/_Game/Items/Prefabs`, and so on). To bring a file back, move it **and its `.meta`** back to that place (the
`.meta` keeps its GUID, so the scene and prefabs that used it find it again).
