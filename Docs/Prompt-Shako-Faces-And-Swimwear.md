# Task for Shako's Claude: faces that come alive, and swimwear that isn't underwear (2026-10-19)

Paste everything below the line into a new Claude Code session on Shako's PC (or tell it: "read
`Docs/Prompt-Shako-Faces-And-Swimwear.md` on origin/main and do it"). Plan it in phases, commit and push after each
phase, and keep going across sessions until the list is done.

---

You are working on **PLEASE DON'T DROWN** (Unity 6000.3.25f1, URP, FishNet), GitHub `sanneel/pleasedontdrown`, for
Shako. Sandro (owner) asked for this task. Your chapter 4 work is merged into `main` (thank you: the whole story now
plays from 1.1 to the credits). Work on your own: when something is unclear, **decide, write the decision in your
report, and keep going**. Ask Shako only for things only he can give (Tripo credits, a yes on cost).

Sandro, playing it, about the people in the game:

1. **Faces:** they don't animate. Most characters never blink, mouths don't move when they talk, faces don't show
   what they feel. The dialogue name plate shows a live portrait of whoever talks, so a dead face is right in front of
   the player in every conversation.
2. **The girls' swimwear:** *"girls are wearing very bad underwear, it is too bad."* The beach women's bikinis are
   thin painted two-pieces that read as underwear, not swimwear (see `Docs/Characters-2026-10-18/01_everyone_standing.jpg`,
   top rows).

Your task: **make every face alive, and dress the women in swimwear that looks like real, good beachwear.**

The owners judge by pictures and by playing. **A task is only done when you have seen it working in the real game
build, in screenshots you looked at closely yourself** (crop and zoom at full resolution; a contact sheet is not
enough). They get furious about: holes or dark lines on faces, lids or mouths floating off the face, hands inside
bodies, clothes stretched or torn in poses, big heads or big hats, blurry faces, "strap" brows (raised strips with a
lit rim), and "done" claims the pictures don't back up.

## 0. Before you start

1. `git fetch origin`, then on `shako-dev`: `git merge origin/main`, `git lfs pull`. Main has your chapter 4, Sandro's
   motion clips (`Avatar/AvatarClips.cs`, off by default: console `clips on`), and the four newest characters
   polished in Blender with normal maps (pirate king, black-tee tourist, both hotel attendants). Build once
   (`DevPlayerBuild.Build`) and look at people talking: `story goto 1.1`, the hotel reception (Marisol), the pirates.
2. Read: `ArtSource/Polish/README.md` (how the four were polished, and the dead ends), `.claude/skills/pdd-tripo/SKILL.md`,
   `Docs/Characters-2026-10-18/` (everyone standing, walking, treading water) and `Docs/Characters-2026-10-19/`
   (before/after of the polish).
3. Read the code: `Avatar/AvatarRig.cs` (`SetExpression`, the face bones `EyeL/EyeR/Mouth/Brows`, the lid patches),
   `Avatar/AvatarAnimator.cs` (`PoseFace`: blink timing, moods `Neutral/Happy/Scared/Angry/Hurt`, talking, down, the
   kiss, the burp), `Avatar/AvatarBody.cs` (`HasFace`, `ClosedEyesMaterial`), `Avatar/AvatarFunny.cs` (the players'
   googly-eyed lifeguard), `Editor/MeshyCharacters.cs` (`BuildFace`: finds eyes and mouth in the texture, makes the
   lid and mouth patches; `OpenEyesOnly`, `MouthHints`, `EyesHidden`, `SameMeshAs`) and
   `Editor/MeshyCharacters.ClosedEyes.cs` (shut eyes painted into a copy of the texture), `Editor/ReviewCapture.cs`
   (face close-ups; poses `talk`, `happy`, `scared`, `blink`, `down`, `kiss`, `cpr`), `UI/SpeakerPortrait.cs` (the
   live portrait; don't edit it, just look through it).
4. No Unity editor or batch run may be open on the project during a batch run.

## 1. Hard rules

- **Git:** work on `shako-dev` only. Small commits (end each with your Co-Authored-By line); push `shako-dev` after
  each phase. **Never push `main` or `sancho-dev`.**
- **This time you own** (you may edit):
  - the face: the face parts of `Avatar/AvatarRig.cs` (`SetExpression`, the face bones and patches) and of
    `Avatar/AvatarAnimator.cs` (`PoseFace` and the mood/talk/blink timing it uses), `Avatar/AvatarFunny.cs`'s face,
    new files for the face (e.g. `Avatar/AvatarFace.cs`), the face fit in `Editor/MeshyCharacters.cs` (`BuildFace` and
    what it calls) and `Editor/MeshyCharacters.ClosedEyes.cs`;
  - the women's bodies: their GLBs and textures in `Art/Characters/` and their assets in `Avatar/Bodies/`: Sandy (1),
    Sandy boss (2), the pirate girl (11), Lola and the tourist girls (16-20, 23, 24), the barista (21), the
    bartender (22); and new swimwear art or tools in `ArtSource/`.
- **At the same time, Sandro's Claude (`sancho-dev`) is:**
  - polishing the men's bodies in Blender: the dad (6), the thief (7), the hotel director (25), the pirates (10, 12, 13)
    (their files in `Art/Characters/` and `Avatar/Bodies/`): don't edit those;
  - fixing the islands (island 1's routes, island 2's bar and buildings, people stuck in walls...): everything in the
    scene, `Editor/GameSceneBuilder*.cs`, `Game.unity`, the props: don't edit those.
  - The pirate king (9), the black-tee tourist (26) and the attendants (27, 28) were just polished: keep their textures;
    their faces get your animation like everyone's.
- **Still Sandro's** (don't edit; write what you need in the report): the rest of `Avatar/` (body animation, hands,
  clips, BodySpace, jiggle), `Combat/`, `UI/`, `Story/StoryHud.cs`, `Story/DialogueService.cs`, the rescue/carry files.
  Exception: if the mouth must follow the dialogue and `DialogueService` doesn't say who is talking yet, a small
  public event or flag there is fine: say it in the commit.
- **Bakes:** `MeshyCharacters.BakeOnlyBatch -pdd-bodies <files>` for the bodies you change (needs graphics: no
  `-nographics`). If the face animation needs every body baked again, `git merge origin/sancho-dev` first (the men's
  polish) and bake after it. After a bake: `git checkout --` the float noise in `Data/Materials/*.mat`, and revert
  `Resources/AvatarBodies.asset` when it only changed order. The attendants need their leg fix again after any bake of
  them: `LegWeightFix.FixWebBatch`, then `FixStretchBatch`, then `FixStretchBatch -ratio 2 -min 0.03`
  (`-bodies hotel_attendant_a,hotel_attendant_b`).
- **Faces stay smooth:** no normal-map shape on faces (the sculpted brows came out as straps, nostrils as rings), no
  blurring the skin (Sandro: "too blurry"), no big heads or hats.
- Builds: only `DevPlayerBuild.Build`. Scene: don't regenerate or patch `Game.unity` (it's Sandro's this time).

## 2. Faces

What's there now: a generated body's eyes are either open or shut (a second texture with shut eyes, or lid patches),
and the bodies in `OpenEyesOnly` (Lola and the Tripo girls, the pirates, the hotel director, the attendants, the
black-tee tourist) **never blink**. The mouth is a dark patch that grows over the lips, or nothing. Brows don't move.

What Sandro wants, on **every** character with a face (tourists, staff, pirates, Sandy, the players' goofy
lifeguard):

- **Blinks**: both eyes, quick (about 0.1 s), a natural rhythm, more when scared; eyes shut when out cold, in the kiss
  of life, sunbathing.
- **Talking**: the mouth opens and closes while the character speaks (in step with the voice/dialogue line), and
  stops when they stop. The speaker portrait shows it.
- **Expressions** for the moods the animator already has: happy (smile, soft eyes), scared (wide eyes, raised brows,
  open mouth), angry (frown, narrowed eyes), hurt, out cold, and the burp/kiss gestures.
- **Eyes that look**: at whoever they talk to, at the player near them (if the eyes are painted, move the iris in the
  texture or with a small eye patch; decide).
- Works for every player in co-op: drive it from what each copy already knows (`AvatarMotion`, mood, talking), no
  new network traffic unless you must.

The bodies are single meshes with painted faces (no separate lids or jaw). One way (your call): **blend shapes made at
bake time** in `MeshyCharacters` from the face it already finds: lids swept over the eye whites in the skin colour
round them, a jaw/lip shape that drops the lower lip and chin, brow raise/frown on the brow vertices, a smile at the
mouth corners; then `SetExpression` sets blend weights. Where geometry can't do it, painted variants as today.

Must not look worse than now: no holes, no dark line across shut lips, no lids standing off the face or showing at
the sides, no skin over the lashes, no mouth inside the beard, works with sunglasses (black-tee tourist) and the eye
patch (pirate king).

## 3. Swimwear

The beach women: Lola (16), the girls 17-20, 23, 24 (one shared Smart Mesh: `SameMeshAs`, they must stay vertex for
vertex the same, or split them on purpose), and anyone else in a bikini. Today their bikinis are thin, flat, painted
on, with cheap colours and edges bleeding into the skin: they read as underwear.

What Sandro wants: **beachwear that reads as swimwear** in the game, from the player's distance and in the
portrait: real bikinis and swimsuits with shape and trims, and variety (one-pieces, sporty tops, high-waisted
bottoms, board shorts, sarongs or wraps, ruffles...), colours that look good on the sand and in the water. It is
a beach game for everyone: tasteful, fun, not underwear.

Ways (your call, say why in the report): repaint and reshape in Blender (`ArtSource/Tools/paint_outfit.py`,
`fix_bikini_bleed.py`, `clean_character_textures.py`; `polish_tripo_character.py` when there is a high-detail source),
or new Tripo models (costs credits: give Shako a list with the cost first, like last time). While you are on the
women's textures, give their faces the same polish as the four (crisper eyes, smooth skin, no seams, no strap brows).

Must keep working: the bust jiggle (`BustL/R`), CPR, the kiss of life (`KissCheck`), swimming and treading water
(no swimwear stretched or torn with the legs apart or the arms up), the motion clips (`ClipCheck`).

## 4. Tests (all must pass before you call it done)

- `DevPlayerBuild.Build`, then look in the live build: people talking at the hotel, on the beach, the pirates,
  the players in co-op (two copies, host + client: the client sees blinking and talking faces).
- `ReviewCapture` close-ups of every face: front, three-quarter, side, in `talk`, `happy`, `scared`, `blink`,
  `down`; crops at full resolution.
- Swimwear: everyone standing, walking, treading water like `Docs/Characters-2026-10-18`, plus front/back/side
  close-ups and CPR and swimming poses.
- `KissCheck.RunBatch`, `GunHoldCheck.RunBatch`, `ClipCheck.RunBatch`: ALL PASS. Your `chapter4check` and
  `chapter3check` still pass.

## 5. Report

`Docs/Report-Shako-Faces-Swimwear.md` with before/after pictures (each face talking and blinking, each woman's
swimwear), what you decided and why, requests for Sandro, and what is still wrong or not checked.

Your chapter 4 requests (an NPC grip for long props, the claw as an NPC weapon, "taken by a rival lifeguard" as a
victim event, the HUD hidden under the credits, a POLICE caption, a rival lifeguard body) are on Sandro's list.
The chapter 4 Tripo art pass (9 models, 270 credits) still waits for Shako's yes: if he says yes, do it after
this task.
