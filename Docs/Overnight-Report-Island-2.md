# Overnight report: finishing island 2 (2026-10-17)

> Work on `Docs/Overnight-Prompt-Island-2.md`, done on Shako's PC, branch `shako-dev` (pushed as it went, Shako's own
> rule; the prompt was written for sancho's PC and said "don't commit"). This file is updated as the work goes on.

## Short version

| # | Task | State |
|---|---|---|
| 1 | VIP guests and tips | **Done** |
| 2 | Gala money bar | **Done** (1 player checked; 2 players: see below) |
| 3 | Bar bouncer job | **Done** (carrying the hurt drunks to the infirmary: not done) |
| 4 | Cushion ride | **Done** (lap pay: see below) |
| 5 | Blackjack side quest | **Done** (it already worked; now it closes after the robbery, `bjtest`) |
| 6 | Hotel director in the story | **Done** |
| 7 | Gala night | **Done** |
| 8 | Marina and the real yacht | **Done** |
| 9 | Make it look expensive | **Mostly done** (no fountain) |
| 10 | People using the place | **Partly** (staff yes; guests on loungers and slots no) |

(Filled in below as each is checked.)

## Decisions I made without you (overrule any of them)

- **Git:** committed and pushed to `shako-dev` as I went (your rule), not left uncommitted as the prompt said.
- **Story order:** a new beat **2.5c "The gala fund"** comes after the Kleptopus: the director asks for a gala, the
  bar fills, then **2.6 is the gala night** (the pirates' raid now robs the casino), **2.7** the director gives the
  yacht keys. `story goto 2.6` and `story goto 2.7` still work.
- **The pirates beaten but the money gone:** the landing party is knocked out as before, but their boat speeds off
  with the casino's money (it ends up moored off Skull Cove). That's why you go to island 3.
- **Rescues on island 2 pay x1.5** (Docs/16 said so but the code didn't do it yet).
- **VIP look:** a gold panama hat (made in code, sized to each head), "VIP" before the name (e.g. "VIP Mrs. Astor").
  The baked tourist bodies can't wear the lifeguard's hat library, so the hat is new, not from the wear library.
- **Broken chairs:** the bar stools are fixed seats, not loose objects, so a "broken chair" is a stool a fighter
  stumbles into: it tips over on everyone's screen, -$20; the staff stand it back up 20 s after the fight.
- **Yacht:** the Tripo yacht from `ArtSource/Tripo/island3/` (the only one there is), copied into
  `Assets/_Game/Art/Tripo/Island2/`, 10 m long, with a small helm console on the aft deck (the model's own wheel
  is at the front and can't be reached standing).
- **Gala test:** the story test tops the wallet up after a minute of shift (`galamoney`), so it doesn't take an hour.
