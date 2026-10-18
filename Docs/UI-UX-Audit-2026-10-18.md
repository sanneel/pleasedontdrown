# UI/UX audit (2026-10-18): start menu to HUD, texts and motion

Goal from Sandro: "think about the UI/UX of this game, from the starting menu to texts, HUD, animations and
everything; keep going until it's a 9.9/10". Rules that stay: no dark boxes, bands or pills behind HUD text (text is
outlined); no giant world text over faces; minimal down screen; the gala bar plain, no numbers.

Scores are mine, judged from real screenshots of the dev build (1280x720). Each area lists what drags it down; the fix
list at the bottom is the order of work. The "after" column is filled in as the work lands.

| Area | Before | After | Main problems |
|---|---|---|---|
| Main menu | 4.5 | 8 | Empty sky/sea behind it (the menu camera looks at nothing), PLAY's dark text with a black outline on coral is mud, FULLSCREEN is a main-menu button, "Development Build" sits on the version number, plain stacked boxes |
| Options | 6 | 8.5 | No graphics quality (Low/Medium/High exists only in the console), no fullscreen toggle here, FOV slider shows 60 at its very left with no units |
| Character screen | 5.5 | 8 | Flat light-blue box behind the lifeguard, long flat list with tiny `<` `>` buttons, DONE button mud like PLAY |
| Pause menu | 5 | 8.5 | RESUME unreadable (dark on coral with a black outline), cheats (GOD MODE, FLY, TRAVEL, RESTART STORY) shown to players, 10 buttons in one column |
| Story dialogue | 3.5 | 8 | NPC speech bubbles are world-sized: next to you they cover half the screen, cut off at the edges, over the objective; the same line is also a subtitle at the bottom; the name tag sits on the bubble text |
| Objective and chapter | 6 | 8.5 | Small, no entrance when the objective changes, the chapter title is a small label in the middle |
| Hotbar and vitals | 5.5 | 7.5 | Six dark boxes always on screen even when empty; the air ring (looks like a magnifier) always shown on land; TEAM FUNDS label tiny |
| Prompts and CPR | 5.5 | 9 | `[RMB]` key chips are mud (same outline bug); CPR steps shown twice (middle and right panel); long one-line instructions |
| Toasts and alerts | 6.5 | 7.5 | Fine but small, no motion, top-right alerts and the victim list crowd the same corner |
| Report card | 6 | 7.5 | Plain list, no panel, no build-up |
| UI motion | 4 | 7.5 | Almost nothing moves: no page transitions, no HUD entrances, no money tick-up feel |
| **Overall** | **5.2** | **8.2** | |

## Fix list (in this order)

1. **Shared drawing (Hud.cs):** text on light fills without a black outline (fixes PLAY, RESUME, DONE, key chips at
   once); a type scale (title / heading / body / caption) used everywhere; buttons with a cleaner look and a hover
   animation; panels for menus only.
2. **Speech bubbles:** a bubble never gets bigger on screen than a set size, fades when the speaker is right next to
   you (the subtitle carries the line), never covers the HUD corners; the name tag never sits on top of it.
3. **Main menu:** a real shot of island 1 behind it (the village, the station, the lighthouse) drifting slowly; a
   proper logo treatment; menu items as big clean text; FULLSCREEN moves into Options; the version out of the corner.
4. **Pause menu:** short (RESUME, CHARACTER, OPTIONS, MAIN MENU, QUIT); the cheats only in development builds, in their
   own small "DEV" group.
5. **Options:** graphics quality, fullscreen, field of view in degrees.
6. **HUD:** hotbar slots light and thin, empty ones faint, the whole bar fading back when idle; vitals only when not
   full; money with a tick-up and a small "+$35" that flies into it; objective card slightly bigger with an entrance
   when it changes; chapter title as a proper title card.
7. **CPR/prompt:** one place for the CPR steps, clean key chips; shorter instruction lines.
8. **Character screen:** a lit backdrop with a floor shadow, a bigger lifeguard, options in groups, bigger arrows.
9. **Motion:** fades/slides for menu pages, toasts in and out, report card rows building up.
10. Re-shoot every screen, re-score, repeat until every row is 9.5+.

## Pass 1 (2026-10-18): what changed

Pictures: `Docs/UI-2026-10-18/` (before/after).

- **Main menu:** behind it a picture of island 1's village filmed in the game, slowly drifting (the island only exists
  once a session runs, so the old menu camera saw empty sea); the title as PLEASE DON'T over a big coral DROWN with a
  bobbing lifebuoy; the buttons slide in one after another; five buttons (FULLSCREEN moved to Options); the picture
  dims when Options, Keys or Join is open; Steam status with a dot and the version small in the top right.
- **All buttons:** dark letters on the coral button no longer get a black outline (PLAY, RESUME, DONE, key chips were
  blobs); hover eases in (teal bar up the left edge, words slide and turn sand); a picked choice is framed teal.
- **Options:** GRAPHICS (Low / Medium / High) and DISPLAY (Windowed / Fullscreen) rows; field of view in degrees.
- **Pause:** RESUME, CHARACTER, OPTIONS, MAIN MENU, QUIT; the testing switches (god mode, fly, travel, restart story) only
  in development builds, small, in a DEV row under it; the dim fades in.
- **Character screen:** the island picture behind it from the main menu; the lifeguard on a sand disc in a deep-sea
  studio, framed; options grouped (SHAPE, COLOURS, THINGS TO WEAR); a line on who sees the look.
- **Speech bubbles:** a set size on screen whatever the distance (between 4% and 10% of the screen's height); a
  story line's bubble steps aside when the speaker is close (the subtitle carries it); bubbles fade toward the screen
  edges and over the objective; the name tag hides while someone speaks.
- **Story HUD:** objective slightly bigger with a teal line down its left, sliding in and flashing sand when it changes;
  the wallet pops and shows the difference (+$35) rising beside it; chapter title cards big, growing in with a teal
  line opening under them; subtitles bigger and fading in and out; the report's numbers count up.
- **Player HUD:** the prompts under the crosshair as keycaps (E, RMB) with the action beside them; CPR's centre prompt
  just names the step (the panel on the right counts); lighter hotbar (empty slots faint, numbers without dark badges);
  food shown as an apple (the drumstick read as a magnifying glass); the game fades up from dark when you arrive; the
  hurt glow soft (earlier tonight).
- **Boss bar:** the boss's name big over a thick bar, the phase small under it (the mission clock moved beside it).

## Pass 2

- The keys under the name of what you hold are keycaps too (LMB shoot, RMB aim, R reload, F inspect, G drop / hold to
  throw), one shared keycap drawing (Hud.KeyCap / Hud.KeyHints) for every prompt.
- Messages in the top right carry a thin teal bar on their right edge (they read as messages, not words in the sky).
- PLAY says STARTING... with "Starting the island..." beside it until the game is up (hosting through Steam takes a
  moment).
- Weapons: no ammo counter on purpose (How to Fish shows none; the gun tells you), left as it is.

## Still between here and 9.9

1. A display font for titles and the logo (Roboto everywhere is clean but generic): needs a free (OFL) font downloaded.
2. The guide (Tab) and the chapter report card laid out as cards with a little more structure.
3. The alerts in the top right sorted against the victim list when many tourists are in trouble at once.
