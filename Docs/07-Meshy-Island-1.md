# Island 1: everything to make in Meshy (Free plan)

> 2026-09-28. Only what's on the first island: the Lost & Found hut, the tower, the beach, the people, the robber and
> his jet ski. Island 2 (hotel, pirates, shark...) is in [06-Meshy-Asset-List.md](06-Meshy-Asset-List.md).
> Every prompt is complete: copy the whole grey block into Meshy.

## Characters on the Free plan (no Pose button)

Pose Control (A-pose / T-pose) is Pro-only, and typing "A-pose" in a text prompt doesn't work reliably. Image to 3D
copies the pose of the picture, so give Meshy a picture that is already in A-pose:

1. **Make the picture.** Paste the *picture prompt* into an image AI: Meshy's **Text to Image**, or ChatGPT / Gemini /
   Copilot. Keep a picture only if both arms are clearly away from the body and the hands are empty.
2. **Image to 3D** in Meshy with that picture (texture on). The model comes out in the same pose, with the picture's colours.
3. **Auto-Rig** in Meshy: pick *Humanoid*, place the markers (chin, wrists, elbows, knees, groin) and download the
   **rigged GLB**. If auto-rig isn't available, download the plain GLB: Claude rigs it in Blender, as long as the arms
   are away from the body.
4. Save it as `ArtSource/Meshy/<file>.glb` (file name below) and tell Claude.

Arms stuck to the body = a statue that can't move (that's what happened to the old `tourist_dad` and `lifeguard`).
Don't bother with those, make a new picture.

**Free plan:** the credits run out fast, so go in the order below (most visible first). Free models are public and
CC BY (credit Meshy): fine for building the game; before the Steam release, regenerate or re-download the final set on
a paid month (see 03 §10).

## Objects (no pose, no rig)
Text to 3D, paste the prompt. If Meshy asks for a texture prompt separately, paste the same prompt again. Download GLB.

---

## 1. People (11)

**Done:** `sandy`, `sandy_boss`, and four tourists (red bikini, sporty, purple bikini, sunburnt dad), all in the game since 2026-09-28.

**Women: the chest.** The CPR jiggle moves two spring bones inside the chest, so the model needs a real, rounded
bust shape; Sandy came out flat and would barely move. The women's prompts below ask for it. Check the picture from
the front *and* imagine it from the side: if the image AI draws the top flat, make it again. If an image AI refuses
a prompt, take out "full" / "big" and keep "curvy figure". One woman per picture, nothing in front of the chest.

### 1.1 `tourist_bikini_red`: Tourist woman
Picture prompt:
```
Adult woman about 25 years old, beach tourist. Slim hourglass figure: a full, rounded bust clearly shaped in the bikini top (not flat), narrow waist, curvy hips. Long wavy brown hair, bright red triangle bikini top and red bikini bottoms, white sunglasses pushed up on her head, yellow flip-flops, light tanned skin, cheerful smile. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.2 `tourist_swimmer`: Tourist man (most swimmers)
Picture prompt:
```
Skinny young adult man, shirtless, pale skin with sunburnt pink shoulders and nose, green swim trunks with white side stripes, messy red hair, swim goggles pushed up on the forehead, goofy grin. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the body, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
Retexture later for 2-3 more swimmers (other trunks and hair).

### 1.3 `robber`: The beach thief
Picture prompt:
```
Skinny sneaky man, stubble, black bandana on his head, dark sunglasses, black and white horizontally striped long-sleeve shirt, navy cargo trousers, grey sneakers, sly grin. No bag, nothing in his hands. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the body, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
His backpack is a separate model (below). He runs, gets knocked down, kneels and begs.

### 1.4 `tourist_bikini_curvy`: Tourist woman
Picture prompt:
```
Adult woman about 30 years old, curvy plus-size beach tourist: a big, full, rounded bust clearly shaped in the bikini top, soft round belly, wide hips. Dark brown skin, short curly black hair, yellow bikini with a green palm-leaf print, wide straw sun hat, pink flip-flops, friendly laugh. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.5 `tourist_bikini_sporty`: Tourist woman
Picture prompt:
```
Athletic adult woman about 28 years old, beach volleyball player: toned body, a medium, rounded bust clearly shaped in a sporty bikini top, narrow waist. Sun-tanned skin, blonde high ponytail, turquoise sporty bikini (racerback top and bottoms), white sweatband on one wrist, barefoot, confident grin. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.6 `tourist_bikini_redhead`: Tourist woman **NEW**
Picture prompt:
```
Adult woman about 23 years old, slim beach tourist: a medium, rounded bust clearly shaped in the bikini top, slim waist. Pale freckled skin with pink sunburnt shoulders, long straight red hair, emerald green bikini with small white polka dots, green flip-flops, shy smile. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.7 `tourist_selfie`: Tourist woman (was P2)
Picture prompt:
```
Adult woman about 24 years old, curvy beach tourist: a full, rounded bust clearly shaped in the bikini top, narrow waist, wide hips. Neon-pink bikini top and bottoms with white board shorts over them, heart-shaped sunglasses, big high ponytail, tanned skin, excited face. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.8 `tourist_mom`: Tourist woman
Picture prompt:
```
Middle-aged woman about 45 years old, beach tourist: soft curvy figure with a full, heavy, rounded bust clearly shaped in the swimsuit. Tanned skin, brown hair, huge straw sun hat, big round sunglasses, turquoise one-piece swimsuit, orange inflatable arm floaties on both upper arms, cheerful face. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the chest and hips, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
The figure: a clearly shaped, rounded chest in the top (not flat), so the CPR jiggle has something to move. Claude adds the two chest spring bones (`--bust 1`), nothing to do in Meshy.

### 1.9 `tourist_dad`: Tourist man
Picture prompt:
```
Chubby middle-aged male beach tourist, sunburnt pink skin, loud red Hawaiian shirt with white hibiscus flowers worn open, baggy blue swim shorts, flip-flops, bucket hat, bushy mustache, goofy friendly face. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the body, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```
Replaces the old statue `tourist_dad.glb`.

### 1.10 `tourist_grandpa`: Tourist man
Picture prompt:
```
Skinny elderly male tourist, bald head with fluffy white side hair, huge white mustache, round glasses, white tank top, purple striped retro swim trunks, black knee-high socks with sandals, knobbly knees. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the body, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```

### 1.11 `tourist_gym`: Tourist man
Picture prompt:
```
Very muscular tanned bodybuilder tourist, small orange swim trunks, yellow sweatband on the forehead, spiky blond hair, gold chain, overconfident grin. Full-body character design, front view, standing straight in an A-pose: both arms straight and angled 45 degrees down and out to the sides, clearly apart from the body, open hands with fingers together, legs slightly apart, feet flat. Empty hands, nothing held, nothing on the back. Stylized cartoon 3D game character, chunky proportions, big simple round eyes, bright tropical colors, soft hand-painted look. Plain white background, even flat lighting, no shadow, no text.
```

## 2. Lost & Found hut and the station

### 2.1 `lf_sign_frame` **NEW** · 2.5 × 0.8 m
```
Long wooden shop sign board for the roof of a beach hut: a thick chunky wooden frame with rounded corners around a large completely blank flat panel painted plain teal, two short wooden legs under it, a little rope wrapped around each corner. Front view, flat face. No letters, no symbols. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
The Lost & Found roof sign. Claude lays the painted "LOST & FOUND" picture on the blank panel.

### 2.2 `lf_shelf` **NEW** · 1.2 m wide, 1.8 m high
```
Wooden shelf unit with three shelves crammed with lost beach things: colorful flip-flops, straw sun hats, a pink bucket and spade, rolled striped towels, a snorkel mask, a small teddy bear, sunglasses, water bottles and a small cardboard box. Front view. No text. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Inside the hut, behind Sandy: makes it look like a real Lost & Found through the window.

### 2.3 `lost_box` **NEW** · 0.5 m
```
Open cardboard box overflowing with lost beach things: a flip-flop, a straw sun hat, a yellow rubber duck, a snorkel and a rolled towel. Plain brown box, no writing. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
On the counter next to the window.

### 2.4 `bar_stool` · 0.75 m high
```
Tall wooden bar stool with a round red cushion seat and a round footrest ring. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Sandy's stool in the window.

### 2.5 `tower_wide` **NEW** · 4.5 m wide deck, 5.5 m high
```
Wide beach lifeguard watchtower: a wide, low wooden cabin about twice as wide as it is tall, with a red roof and big open windows on three sides, a wide platform deck with a white railing all around, raised on six sturdy white wooden stilts with cross bracing, a wide slatted ramp with handrails leading up to a door in the middle of the front, red and white paint, a red and white life ring hanging on each side railing. Whole structure, front 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Replaces `tower.glb`. **Wide**: room for 4 lifeguards inside. (Until it arrives, the old tower is stretched 1.5x sideways in the game.)

### 2.6 `alarm_bell` · 2.3 m high
```
Brass hand bell hanging from a curved iron bracket on a wooden post, with a short pull rope. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
The station bell next to the shack.

### 2.7 `signboard` · 1.5 m wide
```
Wooden notice board on two posts with a large blank white panel, simple wooden frame and a small slanted roof on top. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture.
```
Drill board and welcome sign: Claude paints the words on, like the Lost & Found sign.

### 2.8 `radio` **NEW** · 0.35 m
```
Old portable beach radio: chunky rounded red plastic body, big round speaker grille, a silver tuning dial and two knobs, a telescopic antenna and a carry handle. Side 3/4 view. No logos, no text. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
The station radio.

## 3. Docks

### 3.1 `dock_section` · 2.4 × 4 m
```
Straight section of a wooden pier: weathered planks across two thick beams on four round wooden posts, a few iron bolts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Repeated along both docks.

### 3.2 `dock_end` · 2.4 × 3 m
```
End section of a wooden pier: square deck of weathered planks on thick round posts, a metal swim ladder going down on one side, two mooring bollards and a coiled rope. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture.
```
End of both docks.

## 4. Beach

### 4.1 `umbrella` · 2.2 m
```
Open beach umbrella with red and white stripes and a scalloped edge on a wooden pole stuck in the ground. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
~12 on the beaches. Retexture 2 more: teal/white, yellow/white.

### 4.2 `beach_towel` · 1.9 m
```
Beach towel lying flat on the sand, slightly rumpled corners, bold horizontal stripes. Top-down 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Sunbathers lie on these. Retexture for 3-4 colour sets. (If Meshy makes it too thick, Claude uses a flat textured quad.)

### 4.3 `rock_boulder` · 3 m
```
Large smooth rounded coastal boulder, grey-beige granite with soft cracks, a few barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture.
```
The two swim-out rocks.

### 4.4 `rock_cluster` · 4 m
```
Cluster of three smooth coastal rocks of different sizes fused together, grey-beige granite, barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture.
```

### 4.5 `swim_buoy` · 0.5 m
```
Floating swim-zone marker buoy: round yellow plastic float with a white band and a small loop on top. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
The swim-zone line.

### 4.6 `palm_short` *(optional)* · 4 m
```
Short leaning palm tree with a thick curved trunk, drooping green fronds and three coconuts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.
```
Variety next to the tall palms.

### 4.7 `sandcastle` *(optional)* · 0.8 m
```
Lumpy sandcastle with four towers, a little moat wall and a seashell flag on top. Stylized cartoon game asset, chunky rounded shapes, warm sand colors, soft hand-painted texture.
```
Just for fun, a few on the beach.

## 5. Things you pick up

### 5.1 `lifering` · 0.7 m
```
Classic life ring buoy: thick red-orange foam ring with four white fabric bands and a grab rope looped around the outside. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```

### 5.2 `crate` · 0.6 m
```
Wooden supply crate made of planks with dark metal corner brackets and a painted blue stripe, lid nailed shut. Stylized cartoon game asset, chunky rounded shapes, warm colors, soft hand-painted texture.
```

### 5.3 `cooler` · 0.55 m
```
Picnic cooler box, blue plastic body, white lid, grey carrying handle, small drain plug. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```

### 5.4 `firstaid_kit` · 0.4 m
```
First aid case, green hard plastic box with a white cross symbol on the lid, two latches and a carry handle. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
On the shack shelf.

### 5.5 `coconut` · 0.22 m
```
Single brown coconut with hairy husk texture and three dark eyes at one end. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Falls from palms, you eat it.

## 6. Lost things and the robber's things

### 6.1 `wallet` · 0.12 m
```
Folded brown leather wallet, slightly bulging with money, stitched edges. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Lost item.

### 6.2 `phone` · 0.16 m
```
Smartphone in a bright pink rubber case, dark screen, small camera bump. No logos. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Lost item (sinks: you dive for it).

### 6.3 `sunglasses` · 0.14 m
```
Pair of chunky cartoon sunglasses with a bright pink plastic frame and dark lenses, arms unfolded. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Lost item.

### 6.4 `watch` · 0.16 m
```
Gold wristwatch with a round white face and a black strap lying open. No logos, no text. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Lost item.

### 6.5 `robber_backpack` · 0.5 m
```
Lumpy worn brown canvas backpack, stuffed full, flap buckled shut, two shoulder straps. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Worn on the robber's back; bursts open when he goes down.

### 6.6 `evidence_bag` · 0.1 m
```
Small clear plastic zip bag with a red seal strip and a few white round tablets inside. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
Falls out of the robber's backpack.

### 6.7 `jetski_keys` · 0.12 m
```
Two keys on a ring with a chunky orange foam floating keychain. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.
```
The robber gives these up.

## 7. Leaving the island

### 7.1 `jetski` · 3 m
```
Sporty jet ski with a glossy black and lime green hull, one long padded seat for two riders, handlebars with rubber grips, a small windscreen, footwells on both sides. Side view. Stylized cartoon game vehicle, chunky rounded proportions, bright colors, soft hand-painted texture.
```
The robber's jet ski: you ride it to island 2 at the end of chapter 1.

## Order (most visible first)
tourist_bikini_red → tourist_swimmer → tourist_bikini_curvy → lf_sign_frame → tower_wide → umbrella → beach_towel → robber → lifering → lf_shelf → wallet → phone → sunglasses → watch → robber_backpack → jetski → the rest.

## Already done, keep
`station_rusty` (the Lost & Found hut itself), `palm_tall`, `sandy`, `sandy_boss`. The old `tower` stays until
`tower_wide` arrives.

## What Claude does with each file
* Shrinks it to the game's budget, fixes size, pivot and facing, makes colliders (never the mesh itself), re-bakes the
  navmesh around buildings.
* People: maps the rig onto the game skeleton so walking, swimming, drowning, CPR, sitting and the ragdoll keep
  working; adds the chest spring bones on women; can lay the game's talking eyes and mouth over the face.
  (`ArtSource/Tools/prepare_character.py`, then the scene build bakes it: see 04.)
* Signs: paints the words in the engine (`ArtSource/Tools/make_signs.py`) and puts them on the blank panels.
