# Night review 2026-10-18: fights, CPR, characters (sancho-dev)

Sandro asked: "better animation and overall game fixed; manual check of everything with screenshots of frames; mostly
characters, CPR; fight too: now fight animation is too crowded and too easy." Every picture below is from the real
dev build (Builds/Play), filmed with the movie camera (`cam orbit/rel` + `record`) or `frameshot`, and looked at in
full-size crops before it went in here. Pictures: `Docs/Review-2026-10-18/`.

## Short version

- **Fights were far too easy, now they aren't.** Measured with a bot that plays like a person (`brawlbot`): before,
  someone mashing the punch button beat 4 pirates in 5 s and the cage fighter in 4 s, and was never knocked down.
  Now the masher loses to 2+ pirates and to the cage fighter; a player who watches the pirate's wind-up, steps
  back from it and hits him right after his swing wins every time without going down.
- **Fights are less crowded.** One pirate swings at a time, the rest wait 4.5 m back and spread out; one pirate
  shouts every few seconds instead of all of them; no "OW!"/"OOF!" and no "-25" over a head on every punch.
- **Fighters look like fighters.** Fists up in a guard while they close in, the punching fist drawn back past the
  cheek before the blow (you can see it coming), guard down for a moment after they swing (your chance).
- **CPR, carry, swimming, walking:** checked, they look right. One fix: the rescue breath stopped 5 cm short of
  a big-bellied tourist's lips; fixed (KissCheck ALL PASS).
- **The red "hurt" frame** was four flat bands (a hard-edged red box round the screen). Now it's a soft glow from the
  edges.

## Fights: the numbers

`brawlbot <seconds> <style>` (Dev/BrawlBot.cs) fights for you: `mash` = clicking as fast as possible, `hooks` =
mashing the hook button, `smart` = steps back from a wind-up, hits the opening after the swing.

| Fight | Before (mash) | Now: mash | Now: smart |
|---|---|---|---|
| 1 pirate (fists) | (not measured) | wins in 6-8 s, takes hits | wins in 3-6 s |
| 2 pirates | (not measured) | loses 2 of 3 | wins 3 of 3 (11-20 s), never down |
| 4 pirates | won in 5 s, never down | loses | wins (11-27 s), never down |
| Cage fighter (Iron Jaw Jack) | won in 4 s | loses | wins (5-20 s) |
| 4 drunks (bar fight) | won in 7 s | wins in 4 s (still easy, they're comic relief) | - |

Pirates landed 0-2 blows per fight before; now they land plenty on a masher. `killcheck` ALL PASS, `GunHoldCheck`
ALL PASS.

### What made it easy, and what changed (StoryNpc.Combat.cs, the comment at the top has all the rules)

- Two jabs (50) were enough to floor a pirate (knockdown at 45): mashing kept everyone on the ground. Now a knockdown
  takes a combo, or a hook/uppercut that lands right after their swing (counts double).
- Every jab staggered them and cancelled their swing. Now jabs into a guard don't stagger, and only a heavy blow early
  in a wind-up stops it.
- New: **the guard.** Fists up = blows from the front are often blocked (pirate 75%, cage fighter 85%, drunk 20%;
  hooks/uppercuts a bit less often). A blocked punch does nothing, your arm bounces off (no punch for 0.45 s) and they
  counter at once. After three blocks you get a one-time hint: "Jabs bounce off a raised guard. Step back from their
  swing and hit them right after it, or throw a hook [RMB]."
- A blow that lands on you rocks you 0.4 s (no punching straight back through it).
- Punches aim at the side of your head facing them (aimed at the eyes, a blow from the side went through the goofy
  lifeguard's big head).

**If it's too hard or too easy:** the guard values are in `Combat/NpcWeapon.cs` (`FighterProfile.Pirate ... Guard =
0.75f`; 0.6 is noticeably easier). Try it: dev island, `brawltest 2 fists` (or `4 mix`, `1 brawler`).

### Pictures

- `01_fight_before_crowded.jpg`: before: four pirates on top of you, speech bubbles stacked.
- `02_fight_after_two_pirates.jpg`, `03_fight_after_four_pirates.jpg`: now: fists up, one at a time, the others back.
- `04_pirate_windup_tell.jpg`: the tell: right fist drawn back by the cheek, left fist forward.
- `05_trading_punches.jpg`: the lifeguard and a pirate trading blows (third person).

## CPR, carry, characters

- `06_cpr_compress_breath_revive.jpg`, `07_rescue_breath_closeup.jpg`: kneel, hands on the chest, the breath (one hand
  on the forehead, one under the chin, knees stay on the sand), she gets up and walks off. Looks right.
- `09_kiss_big_belly_fixed.jpg`: the big-bellied tourist: the breath now reaches his lips (the lean was capped at
  30 cm, now 40). KissCheck had started failing for him after the black-tee tourist changed which bodies its test
  seeds pick.
- `08_carry_tourist.jpg`: carrying a tourist in both arms (forearms under the back and the knees), walking and turning.
  Looks right.
- Swimming and towing (autoplay pictures, 1.2): fine.
- Island 2 staff back in the flight-attendant uniform (body 28): `11_hotel_staff_bella.jpg`,
  `12_hotel_staff_ines.jpg`; Marisol at the desk: `13_marisol_reception.jpg`.
- `10_hurt_box_before_glow_after.jpg`: left the old hard red box (with the new hint text top right), right the new
  soft glow.

## Story still plays through

With the harder fights: story autoplay 1.1 to 3.8 reaches the end (gala raid, cage fight, all of chapter 3), no
exceptions (it reports FAIL only for the usual headless rendering errors); `chapter3check` ALL PASS, `killcheck` ALL
PASS.

## Still not perfect (not fixed tonight)

- One picture at gala night showed Marisol's subtitle doubled (two lines on top of each other). Tried to make it happen
  again (day, night, underwater, a new line over the last one): always clean. Tell me if you see it in the game.

- Up close (first person, nose to nose) a pirate's guard fists look loosely closed; from normal distance they read as
  fists.
- The goofy lifeguard's hands hang with the fingers fanned (the cartoon hand as modelled) when not holding or punching.
- Hook spam still beats a single plain pirate sometimes; it loses to the cage fighter and to groups.
- The hit flash turns the whole body bright red for a moment (old feedback, unchanged).

## Other things done tonight

- Merged Shako's island 2 overnight work and bikini fix into sancho-dev; main = sancho-dev on GitHub.
- Re-ran the island 1 village and tavern table batches on his scene; checked island 1 and island 2 in pictures; story
  autoplay 1.1-2.7 all beats pass.
- `Docs/Overnight-Prompt-Shako-Island-3.md` for Shako's Claude: sewer + throne room so the Pirate King (beat 3.7b)
  is finally seen, chapter 3 end to end, a strict island 3 review, island 3 decor. Shako's hotel texture fix is still
  not on GitHub: the prompt tells his Claude to push it first.
