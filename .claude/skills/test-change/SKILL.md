---
name: test-change
description: Give the person a short, exact recipe to check a change in Unity, and check the change as far as possible without Unity. Use after every code, asset or scene change, or when they ask how to test, check or see something.
---

# Test a change

You usually can't run Unity, so you check what you can and the person checks the rest in the editor.

## What you check yourself

1. Re-read the whole diff (`git diff`) as if you were the compiler:
   - every name you use exists (grep for it);
   - `using` lines and namespaces are right;
   - brackets and semicolons balance;
   - nothing you removed is still used elsewhere.
2. Think about both machines: does it work for the host and for a client who joined? Does it work if the player
   reloads the story (`story reset`) or a second player is nearby?
3. If a Windows build exists and you can run commands on the person's PC, the full story test is
   `powershell -File Tools\test-story.ps1` (about 3 minutes, exit code 0 = passed; the host log's last line says
   `[Autoplay] RESULT PASS` or `FAIL` and where it got stuck).

## The recipe you give the person

Keep it to a few numbered steps they can follow without thinking:

1. Switch to Unity and wait for the spinning icon at the bottom right to stop (it's compiling).
2. Open the **Console** (Window > General > Console). If there are **red** errors, copy them and paste them to you.
3. Open `Assets/_Game/Scenes/Game.unity` if it isn't open, press **Play**, then **PLAY** in the game menu.
4. The exact steps to see the change, using the dev console (press the backquote key or F2, type, Enter). Useful
   commands:
   | Command | Does |
   |---|---|
   | `help` / `help <command>` | lists commands / explains one |
   | `story` · `story goto 1.4` · `story reset` | where the story is · jump to a beat · start over |
   | `tourist f` / `tourist m` (`silent`, `flatline`) | a tourist on the sand who needs CPR |
   | `victim 20 panicking` · `drill` | a drowning tourist 20 m out · a rescue drill |
   | `vset state unconscious` · `cpr 40` | knock out the nearest tourist · do CPR on them |
   | `spawn <item>` (`spawn list`) · `bring <item>` | spawn an item / bring one to you |
   | `tp spawn` · `goto <name>` · `devisland` · `home` | teleport places |
   | `noclip` · `speed 3` · `timescale 0.3` | fly · run fast · slow motion |
   | `money 500` · `tsunami` · `shark` · `pirates` · `robber` | story helpers |
   | `screenshot` | saves a picture to `Screenshots/` |
5. What they should see if it works, and what it looked like before.
6. Which log lines to look for in the Console (for example `[Story]`, `[Banana]`), if any.

If the change touches multiplayer, add: make a Windows build (the batch build command is in `README.md`; run it
for them if you can, with their own Unity path), run the exe twice, one **Play** and one **Join** `localhost`
(see `README.md`, "Test multiplayer on one PC").
