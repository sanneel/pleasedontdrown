# TikTok 1: "your friends are the lifeguards" (shot list)

> Written 2026-10-12 from a reference TikTok (a sponsored ad for the co-op game *We Are So Cooked*, 31 s), broken
> down shot by shot with Gemini. This is the same recipe with our game in it.

## The recipe (what makes the reference work)

| Part | Reference | Ours |
|---|---|---|
| Length | 27 s of gameplay + 4 s end card | same, 25-28 s |
| Cuts | 19 shots, a new one every **1-1.8 s**, never a shot without a person in it | ~18 shots |
| Banner | white pill pinned at the top for the whole video: the premise in 6 words + 3 emoji | `Save Drowning Tourists In This Co-Op Game 🛟😭🎮` |
| Captions | 1-3 words at a time, bold white, the key word **green**, low-middle (clear of the TikTok buttons) | same |
| Voice | one fast, excited narrator (~2.8 words/s), no pauses | same |
| Story | hook (new co-op game, you + friends, trouble) → what went wrong → the goal → 3 escalating threats → the twist → a funny gag → name + where to get it | below |
| Camera | **third person**, chest height, close to the characters, mostly static or slow follow; one ground-level prop close-up; one "enemies point guns at the camera" shot | our `cam` modes |
| Music | bouncy, comedic synth-pop, low under the voice; a ragdoll whoosh/thud on the slapstick | same |

## The voiceover (draft, ~27 s)

> **(0-4)** Here's a brand new co-op game where you and your friends are lifeguards... and you're really bad at it.
> **(4-9)** A regular day at the beach has gone totally wrong, because the tourists keep drowning.
> **(9-14)** You have to work together to drag them back to shore and bring them back with CPR,
> **(14-19)** but watch out for sharks, a giant octopus that steals your stuff, and even pirates.
> **(19-23)** And when it's quiet? You can fire your friends out of a cannon.
> **(23-27)** It's called Please Don't Drown. *[wishlist on Steam / coming soon: whatever is true when we post]*

Don't say "coming to console" or anything else that isn't true yet.

## What to record

Record every shot **4-6 s long** (we use 1-2 s of it) and, where it says so, from a second angle. Set up the actors
first, then roll; a bad take costs nothing.

| # | Line (caption) | What's on screen | Camera | Where / how |
|---|---|---|---|---|
| 1 | "brand new / co-op game" | Two lifeguards carry a limp tourist out of the surf up the beach, toward the camera | low, walking backwards ahead of them | island 1, `carry`; `cam rel` / `cam chase` |
| 2 | "you and / your friends" | Same carry from the side, crowd and palms behind | side follow | 2nd angle of shot 1 |
| 3 | "really bad / at it" | A sharp banana boat turn flings the rider into the sea | from the beach, static | existing clip `banana_ride` |
| 4 | "regular day / at the beach" | Wide sunny beach: tower, crowd, parrots on palms | slow orbit round the tower | `cam orbit` |
| 5 | "gone / totally wrong" | Close-up: a tourist flailing, arms up, going under | water level, static | `victim`; `cam look victim.head` |
| 6 | "tourists / keep drowning" | Three or four tourists in trouble at once, lifeguards running in | wide from the tower deck | spawn 3-4 `victim`s |
| 7 | "work together" | Two lifeguards swim one tourist in, or tow her behind the jet ski | follow | island 1 |
| 8 | "back to shore" | They drop the body on the sand, it flops | low, close | `drop` |
| 9 | "bring them back / with CPR" | Mouth-to-mouth, she coughs and sits up | across the body | existing clip `cpr_f_across` |
| 10 | (same line) | The defibrillator: paddles on the chest, the shock jolts the body | side | A5 (`defib`) |
| 11 | "sharks" | Fin circling a guest, then the bite / the save | water level | island 2, beat 2.4 (`shark`) |
| 12 | "giant octopus / steals your stuff" | The Kleptopus rising under the pier with its title card | low from the pier, looking up | beat 2.5b (`kleptocheck`) |
| 13 | "and even / pirates" | Pirates turn and **point their guns at the camera** (our version of the security-agents shot) | eye level, static, close | Skull Cove at night, `pirates` |
| 14 | (same line) | A parrot lookout squawks and spots you | close on the parrot | island 3 arrival |
| 15 | "fire your friends" | A friend gets loaded into the human cannon | static, side | existing clip `cannon_load` |
| 16 | "out of / a cannon" | BOOM, the friend flies over the sea and splashes | follow | existing clips `cannon_fly`, `cannon` |
| 17 | "please don't" | A lifeguard knocked flat on the sand, stars spinning, googly eyes | ground level, close on the face | friendly fire: 3 punches |
| 18 | "DROWN" + title | The game's name on screen over a final gag (the love hut wobbling with the door shut works well) | static, wide | `LoveHut`; title added in the edit |

Optional fillers (record if there's time, swap in if a shot above looks weak): basketball swish (`ball`), the
slap-awake CPR (`cpr_m_slap`), blackjack cards on the felt, eating khinkali at the bar, the trampolines, the tsunami.

The reference's ground-level "rubber duck" shot (a prop big in the foreground, a character walking up behind) is
worth copying with a rescue ring or a coconut on the sand.

## How to record

- **Filming tools already exist.** In the game console: `cam fixed|look|chase|rel|follow|orbit|drift|off` frames the
  shot, `record <name> 1080 1920 30` / `record stop` writes it to `Recordings/<name>` (vertical 1080x1920). `costar <n>`
  gives each player a different face and colours so the friends don't look alike. Details: `Assets/_Game/Dev/Showreel.cs`.
- **Scripted scenes:** `python Tools/showreel.py <scene>` films a scene with background copies acting it out (banana,
  cannon, kiss, slap, basketball are there; add new ones in its `SCENES`). Clips land in `Builds/Showreel/clips/`.
  These copies have **no sound**; film windowed if you want the game's audio, otherwise add sfx in the edit.
- **By hand with friends** is better for the chaotic shots (6, 7, 13): one person is the cameraman (`noclip`, `cam`),
  the others act.
- Sunny daytime for islands 1-2, night for island 3 (it always is). Check the first clip has no HUD in it.
- 60 fps (`record name 1080 1920 60`) for the cannon and the banana fling if we want slow motion.

## The edit

- Order the clips by the table, cut on the voice: every new caption phrase is a new shot.
- `Tools/showreel_cut.py` already cuts clips into a vertical video with captions, but in a different style (Impact,
  top third). For this TikTok it needs: the pinned banner at the top, word-by-word captions with the key word in green
  at the low-middle, the voiceover track and the music.
- Voice: record it on a phone in a quiet room, fast and excited. Get the word timings for the captions afterwards.
- End: 2-3 s on the title + the call to action. The `#ad` in the reference is only there because it's a paid
  sponsorship; we don't need it on our own account.
