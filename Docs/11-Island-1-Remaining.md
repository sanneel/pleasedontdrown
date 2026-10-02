# Island 1: what's left before it's "finished"

> 2026-10-02 (evening). Island 1 = story chapter 1 (meet Sandy → leave on the jet ski), playable solo and with friends.
> "Finished" here means: someone who has never seen the game can play it start to end, with a friend, and nothing
> on screen is a grey box. Everything that code could close is closed; what is left needs a person or a new download.

## 1. Play it (the only thing that still blocks "finished")

The game tests itself now (`powershell -File Tools\test-story.ps1 -Client`, about three minutes):

- **The body**: walking 5.0 m/s, sprinting 7.5 m/s, jumping, running off the dock, swimming, diving and coming up for
  air, climbing out onto the dock. Done on the real motor with the keys held by the test, no teleporting.
- **The story**: all ten beats of chapter 1 to the hotel island, as host with a second copy joined as a client,
  no errors in either log. It uses the real interactions but teleports where a player would walk.

So the mechanics work. What no test can say is how it *feels*:

| # | What | Why it's open |
|---|---|---|
| 1.1 | **One full run of chapter 1 by a person** | Feel: movement, swimming, aiming a throw, how long things take, whether the story is clear |
| 1.2 | **The same run with a friend over Steam** | The Steam friends path (PLAY, invite, join) has never been tried with a second real account; the local client join is tested |
| 1.3 | **Feel tuning from that run**: throw, swim speed and stamina, CPR timing, 20 s drowning timer, pay, the $30 airlift bill, music and voice loudness | All numbers are first guesses or How to Fish's |
| 1.4 | **Black bars**: confirm they're gone | Fixed blind (display mode is set at every start); tests run windowed |

## 2. Art

Modelled or generated and in the game: Sandy, 4 tourist bases + 40 look-alikes, the Lost & Found hut, the tower,
palms, the lifeguard jet ski, umbrellas, the thief's backpack, and since 2026-10-02 everything that was a grey box:

- **Things you carry**: crate, cooler, beach ball, life ring, coconut, defibrillator.
- **Lost things**: wallet, phone, sunglasses, watch, baggie, jet ski keys (these are also the hotbar pictures).
- **The beach**: the dock (planks, beams, roped posts), rocks, swim buoys, towels.
- **The station**: alarm bell on its post, welcome sign, rescue-drill board and the hut's roof sign (painted, no more
  engine text), and in the hut a shelf with a radio, a first-aid box and a box of lost things, and Sandy's bar stool.

These are modelled in code (`ArtSource/Tools/model_props.py`, Blender) in the same size and place as the grey boxes
they replace, so physics, hold poses and the story are unchanged. `GameSceneBuilder.Props.cs` puts them in.

| # | Still not final | Notes |
|---|---|---|
| 2.1 | **The thief's own body** | He wears a black beanie and a red bandana now (`Story/RobberDisguise.cs`, fitted to the head of whatever body he has) on a tourist look-alike. His own Meshy model needs a new download: the one in `ArtSource/Meshy/raw/robber_raw.glb` has its hands in its pockets and can't be rigged (generate from an A-pose picture) |
| 2.2 | **Player lifeguard** in third person | Players are code-built avatars on purpose: that's what the CHARACTER screen changes. A generated lifeguard would lose the customizer |
| 2.3 | **Wide tower** `tower_wide` (07 §2.5) | The current one is the Meshy tower stretched 1.5x sideways; needs a new download |
| 2.4 | More tourist bases (swimmer, curvy, redhead, mom, grandpa, gym: 07 §1.2-1.11) | Optional: 4 bases + 40 recolours carry the island |

## 3. Game pieces

| # | What | State |
|---|---|---|
| 3.1 | **Voices and music** | Done 2026-10-02, all synthesized in code like the other sounds. Music (`Audio/MusicSynth.cs`, `GameMusic.cs`): a ukulele-and-marimba beach loop (in the menu too), a drum layer that comes in while a tourist is in trouble, jingles for saved / lost / chapter done. Voices (`Audio/SpeechSynth.cs`, `SpeechVoice.cs`): every dialogue line is babbled out loud in the speaker's own voice (four kinds, pitched per person; no real words, the subtitles carry the meaning). `Tools/PDD/Export music and voice preview` writes WAVs to listen to. **Not heard by a person yet**: only checked for pitch, level and timing by measuring the output |
| 3.2 | **Options** | Done: mouse sensitivity, field of view, master volume, music, voices, and KEYS (every keyboard/mouse key can be changed; prompts and the guide follow). Gamepad buttons and the 1-6 slots are fixed |
| 3.3 | **Failure** | Done: a tourist lost to the rival company costs the team $30 and shows on the report card |
| 3.4 | **Chapter end** | Done: report card when the jet ski reaches island 2 |
| 3.5 | **Late joiner** | Done 2026-10-02: a friend who joins mid-story gets a title card ("You joined at: The thief"), a one-line recap (part n of 10, rescued, lost, wallet) and is put next to the team if they're far away |
| 3.6 | **Guns on island 1** | Decided: island 1 stays fists-only. The first weapon is bought on island 2 (that's chapter 2's first goal), the dev island armory is for testing |
| 3.7 | **Menus** | Done 2026-10-02: the character customizer (colour chips instead of "3 / 16"), the shop and the travel pad are on the HUD kit, same look and size as the menus at any resolution |

## 4. Not needed for island 1

From the original design, deliberately outside chapter 1: panic grab (M5), CPR timing minigame, buoy launcher,
the emergency director and shifts, station upgrades, and all of island 2 (hotel, shark, pirates; its signs are
still engine text).

## Shortest path to "island 1 is finished"

1. Play 1.1 once (and 1.2 with a friend) and write down what feels wrong.
2. Listen to the music and voices; turn them down or say what's wrong.
3. Regenerate the thief from an A-pose picture if he should have his own body.
