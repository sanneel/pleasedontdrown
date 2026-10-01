# Island 1: what's left before it's "finished"

> 2026-10-02. Island 1 = story chapter 1 (meet Sandy → leave on the jet ski), playable solo and with friends.
> "Finished" here means: someone who has never seen the game can play it start to end, with a friend, and nothing
> on screen is a grey box. Ordered by how much each item blocks that.

## 1. Play it (blocks everything else)

Almost nothing below has been played by a person: it was built and compiled, and tested headless at most.

| # | What | Why it's open |
|---|---|---|
| 1.1 | **One full run of chapter 1, solo**: beats 1.1 → 1.10 without the console | The beats were tested headless on 2026-09-28; the movement rebuild, guns, hands, new HUD and menu all came after and were never played |
| 1.2 | **The same run with a friend over Steam** | The Steam friends path (PLAY, invite, join) has never been tried with a second real account |
| 1.3 | **Feel tuning from that run**: walk / sprint / jump, throw, swim speed and stamina, mouse sensitivity, CPR timing, 20 s drowning timer, pay | All numbers are first guesses or How to Fish's; none were tuned by playing |
| 1.4 | **Jet ski**: get on, ride to island 2, get off | Sit glitch and "can't move after getting off" were fixed on 2026-09-30 but not re-tested |
| 1.5 | **Black bars**: confirm they're gone | Fixed blind (display mode is now set at every start) |

## 2. Art still grey (Docs/07 has every prompt)

In the game already: Sandy, 4 tourist bases + 40 look-alikes, the Lost & Found hut, the tower (stretched), tall palm,
the lifeguard jet ski, the painted signs.

| # | Still a placeholder | Notes |
|---|---|---|
| 2.1 | **Robber** (07 §1.3) and his **backpack** (§6.5) | Uses a tourist look-alike today; he is the chapter's only villain |
| 2.2 | **Player lifeguard** in third person | Players are still code-built avatars (what your friend sees) |
| 2.3 | **Wide tower** `tower_wide` (§2.5) | The current one is the old model stretched 1.5x |
| 2.4 | **Beach dressing**: umbrella, towel, rocks, swim buoys, dock sections, bell, signboard (§2-4) | All primitives |
| 2.5 | **Things you carry**: life ring, crate, cooler, first-aid kit, coconut (§5) | Primitives |
| 2.6 | **Lost things**: wallet, phone, sunglasses, watch, evidence bag, jet ski keys (§6) | Primitives; these are what the hotbar pictures show |
| 2.7 | **Inside the hut**: shelf, lost box, bar stool, radio (§2.2-2.8) | Optional, but it's the first place a player looks |
| 2.8 | More tourist bases (swimmer, curvy, redhead, mom, grandpa, gym: §1.2-1.11) | Optional: 4 bases + recolours carry the island |
| 2.9 | **Faces**: NPCs don't blink or move their mouths when they talk | Meshy's painted face; the game's eyes/mouth overlay isn't on them yet |
| 2.10 | Fingers of NPCs don't bend; first-person hands were restyled by another session | Check both in game |

## 3. Game pieces missing on the island

| # | What | Notes |
|---|---|---|
| 3.1 | **Voices and music**: nobody speaks (Sandy, the thief, tourists are subtitles only), no music | Every other sound is in (procedural), including a synthesized "heeelp" cry |
| 3.2 | **Options**: sensitivity + field of view are in (2026-10-02); still missing **volume**, key rebinding | |
| 3.3 | **Failure**: a tourist you lose is simply replaced by another one; it costs nothing | Decide: lose pay, a count on the chapter card, or leave it light |
| 3.4 | **Chapter end**: 1.10 ends when the jet ski reaches island 2, with no summary of the chapter (rescued / lost / money) | The design's "Rescue Report" card doesn't exist yet |
| 3.5 | **Late joiner**: a friend joining mid-chapter isn't told the story so far and spawns at the start | Fine for island 1, noted |
| 3.6 | **Guns on island 1**: guns exist (dev island armory) but chapter 1 gives none | Intended: the first weapon is bought on island 2. Decide if island 1 stays fists-only |
| 3.7 | Menu/customizer polish: the character customizer and the shop / teleport panels still use the old small grey look | Not on the new HUD kit yet |

## 4. Not needed for island 1

From the original design, deliberately outside chapter 1: panic grab (M5), CPR timing minigame, buoy launcher,
the emergency director and shifts, station upgrades, and all of island 2 (hotel, shark, pirates).

## Shortest path to "island 1 is finished"

1. Play 1.1-1.5 once and write down what feels wrong.
2. Generate robber + backpack, the six lost things, life ring, umbrella, towel (2.1, 2.4-2.6): about 12 Meshy models.
3. Voices for the help shouts and Sandy's lines (3.1), a volume slider (3.2).
4. Decide 3.3 and add a small chapter-end card (3.4).
