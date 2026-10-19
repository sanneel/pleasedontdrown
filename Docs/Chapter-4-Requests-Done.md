# Chapter 4: the "Requests for Sandro", done (Sandro's Claude, 2026-10-19)

Answers to the table at the end of `Docs/Report-Shako-Chapter-4.md`. Branch `sancho-dev`.

| # | Request | What it is now | Where |
|---|---|---|---|
| 1 | NPC grip for a long prop, and a lunge | `AvatarMotion.LongProp`: a pole held low in front in one or two hands, each palm exactly on its grip (the wrist aimed so the turned hand lands there), elbows down; with one hand the free arm balances. `HandPose.PoleGrip` for a hand round a pole. `AvatarMotion.Brace`: rocked back onto the rear foot (a long weapon's tell; the free hand points ahead). `AvatarGesture.Lunge`: the front foot steps in, hips forward and down, back leaning in; with a long prop in one hand the free arm is flung back like a fencer's; with empty hands it's a two-handed grab | `Avatar/AvatarAnimator.cs`, `Avatar/AvatarHands.cs` |
| 2 | The claw as an NPC weapon | `NpcWeaponKind.Claw` + `FighterProfile.Claw()`: reach 4.2 m, a 0.8 s tell (drawn back along her right side, rocked back, the free hand pointing at you, "MINE!" half the time), a lunge, and it **grabs**: whoever it catches is flung off to the side they stand on (off the jetty), 10 damage, "FINDERS KEEPERS!". It turns slowly while winding up (step aside to dodge) and stays out over its front foot after the lunge (the opening). `NpcWeaponView` draws the villa's claw in her right hand (or `Resources/NpcWeapons/grabber_claw` once the Tripo one exists). Phase 3 uses it; `ClawGrab`, `ClawObservers` and `ClawSwingObservers` are gone, and `World/HeldProp.cs` is no longer used (left in place) | `Combat/NpcWeapon.cs`, `Story/StoryNpc.Combat.cs`, `Story/NpcWeaponView.cs`, `Story/StoryDirector.Chapter4Boss.cs` |
| 3 | "Taken by a rival" | `VictimEvent.TakenByRival` and `VictimBrain.ServerTakenByRival(rivalName)`: lost, "LOST TO COMPETITION" over them, the toast "FK Kyle grabbed Linda: lost to the competition.", the report's LOST TO RIVALS count, the company's $30 bill (Economy, as for the airlift: "rescue bill"), towed off in 1.2 s. The race and the airlift call it; their own counting, billing and despawning are gone (the race's feed line is just the score now: "THEIRS! You 0 - 1 Finders Keepers") | `Rescue/VictimBrain.cs`, `Story/Economy.cs`, `Story/StoryDirector.cs`, `Story/StoryDirector.Chapter4.cs` |
| 4 | StoryHud under the credits | While `CreditsRoll.Rolling`: no objective, no team funds, no marker (they come back with the open shift) | `Story/StoryHud.cs` |
| 5 | A POLICE caption | `NpcRole.Police` (caption POLICE, blue speech colour). Officer Gill is now a character: the goofy lifeguard in navy with a cap and a moustache, out on the launch's bow by the dock once it ties up, so his plate has his face; he rides out behind Sandy and the THIEF | `Story/StoryNpc.cs`, `Story/StoryHud.cs`, `Story/StoryDirector.Chapter4End.cs` |
| 6 | A rival lifeguard body | `Bodies.FkRival` (29): one Tripo generation, approved by Sandro (25 credits): a cartoon lifeguard in an orange shirt with a white chest stripe, white shorts and an orange visor, the tourists' style. All the company's lifeguards wear it (`RivalLook`); warehouse security stays the goofy lifeguard in black. Tripo gave him a T-pose: `ArtSource/Tools/apose_from_tpose.py` lowered the arms and turned the palms in before `prepare_character.py` (new `--ankle 1`) rigged him | `ArtSource/Tripo/Latest/raw/fk_rival_meshopt.glb`, `Art/Characters/fk_rival.glb`, `Avatar/Bodies/fk_rival*`, `Editor/MeshyCharacters.cs`, `Avatar/AvatarLook.cs`, `Story/StoryDirector.Chapter4.cs` |

## Found on the way, fixed

- **Riders on a sailing boat ran on the spot** (Sandy and the THIEF on the police launch): their walk came from the
  boat's speed, and the animator pinned planted feet in the world, so the legs stretched out behind the boat and
  snapped back. `StoryNpc.DeckVelocity` takes the deck's speed off their walk and `AvatarMotion.GroundVelocity` carries
  planted feet along with the deck.
- **The name plate's portrait of a goofy body showed only its nose** (the FK rivals, the officer): the portrait camera
  stood a person's distance from the face; it now stands off by the head's own size (`UI/SpeakerPortrait.cs`; people
  look as before).

## The rival body, notes

- His head is about a quarter of his height, between a tourist's and the players' goofy lifeguard (the prompt asked for
  a seventh; Tripo drew it bigger). Snug visor, no big hat.
- Running, his swinging foot stays flat and hooks forward under the shin. The goofy players' run does exactly the same
  (the game keeps feet flat through the swing); flip-flops hide it on the black-tee tourist. Not changed here.

## Why the claw is in one hand

Sandy's arms reach 47 cm from the shoulder (a pirate's 63). Every two-handed hold either put the pole through her chest
or slid the claw out of both hands, and a left hand that grips only when it can reach snaps on and off. A reach-grabber
is a one-handed tool anyway (the index finger on the trigger). The animator still holds a long prop in two hands when
the weapon gives it a second grip.

## Checked in the dev build (1280x720, offline host)

- Sandy's claw, recorded from three sides at 20 fps and looked at frame by frame: the claw stays in her hand at rest,
  in the tell and in the lunge; nothing through her body; 15 grabs out of 15 swings on a lifeguard standing still.
- The race: grabs come through `ServerTakenByRival` (toast, LOST TO COMPETITION, $30 "rescue bill" each, three restart
  the race); the officer's plate (POLICE, his face); the launch sailing off with nobody running on the spot; the
  credits over a HUD without the objective or the team funds.
- `chapter4check` **ALL PASS 45/45**, 0 exceptions. (A first run stalled at 4.9: the ending waited its 120 s for a
  lifeguard at the police dock while the check had put one there. It didn't happen again with the position logged
  every 10 s; the stall is before anything changed here runs.)
- `GunHoldCheck` ALL PASS.

## Test commands

`brawltest 1 claw` (a claw fighter in front of you, anywhere), `story goto 4.7` then `finalboss 2`, `finalboss 3`
(Sandy's claw phase), `story goto 4.2` (the race), `story goto 4.9` (the police and the credits).
