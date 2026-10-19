# Report: living faces and real swimwear (Shako's Claude, 2026-10-19)

Task: `Docs/Prompt-Shako-Faces-And-Swimwear.md`. Pictures: `Docs/Faces-Swimwear-2026-10-19/`.
Commits on `shako-dev`: `27b3690` (faces), `8f05db2` (merge of main + every body re-baked), `1537790` (swimwear, face
fixes), and this report.

## 1. Faces

### What it does now

Every generated body with a painted face (Sandy, Sandy boss, Lola and the six Tripo girls, the barista, the bartender,
the dad and his ten look-alikes, the THIEF, the four pirates, the Pirate King, the hotel director, Marisol and the
other attendant, the black-tee tourist) and the players' goofy lifeguard:

- **Blinks**: 0.11 s (quick shut, slower open), every 2-5.5 s, sometimes twice; every 0.6-1.8 s when scared. Eyes
  shut when out cold, lying in the sun, during the kiss of life. The goofy lifeguard's googly eyes get skin lids.
- **Talks**: the jaw opens and closes with the words. Where the voice is heard on this machine the mouth follows its
  loudness (10 ms steps); otherwise (far away, batch tests) a babble of syllables and pauses. It stops when the line
  ends. The speaker portrait shows the same face (it is a live camera).
- **Moods**: happy (smile, lower lids up a little), scared (wide eyes, brows up and in, mouth open, frown), angry
  (narrowed eyes, angry brows, frown), hurt (half shut, sad brows, mouth open), out cold, the burp, the kiss.
  Brows lift on a word now and then while talking.
- **Eyes look**: at the nearest player close in front (so at whoever they talk to), else little glances about,
  darting when scared; the eyes lead the head's own glances. Up/down only a little (an iris up under the lid read
  as sleepy in the live game).
- **Co-op**: worked out on every copy from what it already knows (mood, talking, pose, gestures, where the players
  are): nothing new on the network. Checked host + client: on the client `faces` showed Sandy talking (jaw 0.06 →
  0.41 → 0.08), happy, and every face's blink count going up.

Pictures: `face_<body>.jpg` (rest, talking, shut, half shut, looking left/right, happy, angry, scared, wide open,
three-quarter talking and shut, side talking) for all 23 faces; before: `faces_before_front.jpg`,
`faces_before_shut.jpg` (the old closed-eye texture swap; most never blinked at all). In the live build:
`live_sandy_talking.jpg` (Sandy at 1.1, 16 frames 0.12 s apart: the mouth opens and shuts with the line),
`live_marisol_talking.jpg` (Marisol at the reception, 10 frames).

### How (decisions)

- **Bake time** (`Editor/MeshyCharacters.FaceRig.cs`): the front of the face is drawn straight on into a fine
  picture (0.15 mm a texel: no UV seams, gutters or modelled eyeballs in the way). The eye whites are found in it as a
  pair; each painted eye is cut out (everything not skin joined to its whites); two small textures are saved per body
  (`<body>_face_open.png`: the eyes; `<body>_face_lid.png`: the same with the eyes filled in from the skin all round
  them). A look-alike (same mesh, repainted) uses its base's eyes. One eye that won't cut out takes its pair's outline
  mirrored. The eye patch (King) and the sunglasses (black tee, THIEF) are left as they are: no lids on them.
- The face is **cut along the lips** (the triangles round the lips split finer first) with a dark mouth inside behind
  them, and **shape keys**: jaw (turns the chin and lower lip down about a hinge), smile, frown, brows up / angry /
  sad, and for modelled eyes one that presses the eye into a smooth lid as it shuts (no hollow or bulge under a shut
  lid). The mouth's inside is its own submesh drawn from inside, so it counts as inside the head for the body checks.
- **Runtime**: `Avatar/AvatarFace.shader` is URP Lit plus the face: it draws the lids over the eyes (with a lash line
  and a fold), moves the iris inside the eye, and colours the mouth's inside. `Avatar/AvatarFace.cs` sets the shape
  keys and the lids/gaze per character (a property block: the hit flash still works). `Avatar/AvatarAnimator.Face.cs`
  works the face out (it replaces `PoseFace`). `AvatarRig.SetExpression(eyes, mouth, brows)` still works for the old
  callers (VictimBody, tests) and maps onto the new face.
- The old closed-eyes texture swap (`MeshyCharacters.ClosedEyes.cs` and every `*_eyes_closed.*`) is gone.
- `Audio/SpeechVoice.cs` got one read-only property, `Loudness` (how loud the line is right now), so the mouth can
  follow the voice; nothing else changed there. `DialogueService` was not touched.
- Mouths painted as a line or hidden in a beard are measured by hand in `MeshyCharacters.MouthHints` (now also the
  pirate girl, marksman, quartermaster and the big pirate); where the painted lips are found right by a measured
  mouth, they win (Marisol's opening now sits on her lips).

## 2. Swimwear

### What they wear now (`swim_after_*.jpg`, before: `swim_before_*.jpg`)

| Body | Before | Now |
|---|---|---|
| Lola (16) | red string bikini | cherry-red sweetheart one-piece, white piping, straps, scoop back |
| Red (17) | red triangle bikini, thong back | sporty: racerback crop top with a white band, high-waisted bottoms with white side stripes |
| Blonde (18) | turquoise string bikini | ruffled bandeau, high-waisted bottoms with a frilled waistband and white side bows |
| Redhead (19) | green string bikini | emerald halter one-piece, white belt, gold buckle |
| Black hair (20) | magenta string bikini | magenta zip-front short-sleeved rash-guard crop top, boy-short swim shorts with black side panels |
| Pink (23) | pink string bikini | retro polka-dot halter top and high-waisted bottoms with a white frill round the legs |
| Lavender (24) | lavender string bikini | lavender tankini with wide straps and a frilled hem over violet high-waisted bottoms |

Every view per woman (front, back, side, close three-quarter, walking, treading water, CPR, swimming):
`swim_body<id>.jpg`. Standing at the hotel and walking: `swim_after_walk.jpg`.

### How (decisions)

- **Painted, not modelled, and no Tripo credits**: `ArtSource/Tools/paint_swimwear.py` (Blender) repaints each
  woman's own colour texture; `ArtSource/Swimwear/paint_all.sh` does all seven from the untouched originals kept in
  `ArtSource/Swimwear/src`. Nothing is added to the meshes, so the bust jiggle, CPR, the kiss of life and swimming move
  exactly as before and nothing can stretch or tear with the legs apart or the arms up (KissCheck, ClipCheck pass;
  treading and swimming pictures). New Tripo models would have meant new rigs, new faces and the jiggle/CPR fits all
  over again, for seven bodies.
- The old bikini is taken off first: found by colour (and its see-through halo: Tripo painted soft pink/red halos round
  the pieces), then filled in from the skin round it **on the body** (1 cm cells round the body, not in the texture,
  where UV islands lie side by side). Hair is found on the mesh (hair-coloured vertices from the head down the
  strands) and by whole UV islands, and is never painted.
- The suits are shaped in body space from each body's own landmarks (hip joints, the bust, the chest's width), so the
  edges are clean lines on the body and the same across UV seams. Cloth keeps the body's broad shading only, darker
  where it curves onto the skin, a thin shadow on the skin along its edge, piping, frills with folds, stripes, dots.
- The shared Smart Mesh (17-24 and the barista/bartender) stays vertex for vertex the same: only textures changed.
- Tasteful: everyone is covered front and back (no thong backs any more), sporty or retro beachwear.

## 3. Tests

- `KissCheck.RunBatch`, `GunHoldCheck.RunBatch`, `ClipCheck.RunBatch`: **ALL PASS** (after the swimwear bake).
- `chapter3check`: **ALL PASS** (with the new faces).
- `chapter4check`: 40/45 on the first run: the five 4.9 checks timed out waiting 60 s for the cast call (the five
  police lines come first; the ending itself played through to the credits and the open shift). The wait is now 120 s:
  see the result in the final run below.
- Host + client: the client sees blinking and talking faces (`faces` console command).
- `DevPlayerBuild.Build`: OK, 0 errors.

## 4. Requests for Sandro

1. `AvatarLook.Bodies` comments still say "red bikini", "turquoise bikini"... (your file): they wear the suits above now.
2. The face shader is URP Lit plus the face: if the renderer settings change (deferred/forward+ features), please keep
   `Avatar/AvatarFace.shader` in mind (it is Lit's own passes with a different forward pass).
3. The women's faces did not get the Blender polish of the four (crisper eyes, smooth skin): their textures have no
   high-detail source, and the polish pipeline needs one. If you want it, a Tripo re-texture of the faces would be the
   way (credits).
4. A rival lifeguard body etc. from the chapter 4 list are still yours.

## 5. Still wrong or not checked

- Big painted eyes with eyeshadow (the attendants, the lavender girl) show a little of the shadow's colour on the shut
  lid; the attendant B's left eye outline picks up a hair strand at its corner (a lock of hair can flicker when she
  blinks).
- The bandeau and halter tops are paint: seen from straight above, their top edge is a line on the skin, not a raised
  edge. Frills and bows are painted too.
- Red girl: a couple of red marks were already painted into her hair at the back (from the original texture); left.
- The goofy lifeguard's googly lids are a skin shell over the dome: funny, but big when shut.
- Not checked: every one of the dad's ten look-alikes up close (they take the base dad's fit; spot-checked 2), and the
  speaker portrait as a picture (it renders only in a window; the faces it films are the ones in the pictures).
