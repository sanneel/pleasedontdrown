# Playing with Steam friends

Up to 4 players. Everyone needs Steam running and **the same build** of the game (the host refuses other
versions, and the menu marks a friend on another build).

## Hosting

Press **PLAY** in the start menu. With Steam running this makes a friends-only Steam lobby and the session runs
over Steam; without Steam (or if Steam can't make a lobby) it plays offline and says so.

While you are in a Steam session your Steam status carries a join link (rich presence), so friends see
**Join Game** on you in their Steam friends list.

## Joining a friend

Any of these, while the game is running on your PC:

* Start menu > **FRIENDS PLAYING** > **JOIN** next to their name (lists friends in this game; "other build" means
  their version differs, "in the menu" means they aren't hosting yet).
* Steam friends list > right-click the friend > **Join Game**.
* Accept their invite in Steam chat.

If you are already in a session, it ends first and you move to theirs.

## Inviting

Pause menu (Esc) > **INVITE A FRIEND** lists your online Steam friends; **INVITE** sends a Steam invite (no
overlay needed). With the overlay, **INVITE FRIENDS / STEAM** opens Steam's own invite window.

## Steam overlay (Shift+Tab)

Steam only puts its overlay into games it starts. Started from the .exe directly, the game has no overlay (the
menu says so and the overlay buttons are greyed out). To get it: Steam > **Games > Add a Non-Steam Game to My
Library...** > browse to `Builds/Win64/PleaseDontDrown.exe`, then start it from the Steam library. While the
overlay is open the game ignores its own input.

## Development app id (480)

Until the game has its own Steam App ID it runs as app 480 (Spacewar, `steam_appid.txt`), so:

* Friends see you "playing Spacewar".
* Joining only works when the friend already has **this game** running. If their game is closed, Steam's
  Join Game / invite would start the real Spacewar instead.
* The start menu only lists friends whose status carries this game's version key, so people playing the real
  Spacewar don't show up as joinable.

With a real App ID (Steam Direct), put it in `steam_appid.txt` (and the build's copy). Joining from a closed
game then works as well: Steam starts this game with `+connect_lobby <id>`, which the game already handles.

## Testing

* `-pdd-host-steam` presses PLAY at start (Steam session, or offline if Steam can't).
* `-pdd-host-offline` / `-pdd-join <address>` / `-pdd-nosteam` for local tests without Steam.
* Code: `Net/SteamLobbyService.cs` (lobby, rich presence, join requests, friends list, invites),
  `Net/ConnectionService.cs` (`Play`, `JoinFriend`), `UI/DevConnectMenu.cs` (menus),
  `Core/SteamBootstrap.cs` (overlay).
