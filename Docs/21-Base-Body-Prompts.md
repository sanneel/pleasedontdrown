# Base bodies: the GPT prompts (2026-10-20)

Everyone in the game, the goofy lifeguard included, moves to one style: chunky clay-toy cartoon people (see
`New-Game-2026-10-20/style_reference_clay_tourists.webp` and Sandro's bikini girls sheet, saved next to it as
`style_reference_bikini_girls.png`). Built on **two bodies only**, one man and one woman, sharing one skeleton,
one height and one UV layout. Every character after that is the same body with a different texture, hair, hat,
glasses and body sliders. **No new mesh per character, ever.** Why: [20-New-Game-Plan.md](20-New-Game-Plan.md), "Art".

## How to use these

1. Run **P1** and **P2** in GPT (image generation), with both style reference pictures attached each time.
   Generate a few, pick the best of each, and make sure the man and the woman match each other (same height, same
   head size, same hands and feet). If one is off, run it again with the chosen other one attached: "match this one".
2. Those two pictures go to Tripo (image to 3D, one model each) or to a 3D artist, then Blender: symmetry, one
   clean mesh of 15-20k triangles, the two bodies sharing one UV layout, eyes as separate spheres.
3. **P3** (goofy) and **P4** (variants) are concept pictures only: they show what each character looks like. They
   are painted onto the base (texture) and hair/hats become small separate pieces. They are never turned into new
   3D bodies.

Rules for every prompt below (they are written into each one): plain light grey background, orthographic-looking
straight views, even soft light, no floor shadow, no text, nothing in the hands, whole body in frame, the four views
at the same scale and the same height line.

## P1: the woman's base body

```
Using the two attached pictures as the exact art style (chunky clay-toy cartoon people, soft matte clay shading,
big round head about 1/3 of the body height, big friendly eyes, small nose, short stubby arms and legs, round soft
belly, big simple hands with a thumb and four fingers together, big bare feet):

A neutral BASE BODY for a 3D game character, a woman. Character turnaround sheet: front view, three-quarter view,
side view, back view, side by side, same scale, all four standing on the same line.

Pose: A-pose. Arms straight, held out about 45 degrees from the body, palms facing down and slightly back,
fingers together, thumb apart. Legs straight, feet apart about shoulder width, toes pointing forward.
Head looking straight ahead, neutral calm face, mouth closed in a small smile, eyes open looking forward.

Bald (no hair at all, a smooth round head: hair will be added separately). Plain simple one-colour skin
(medium warm tan), no makeup, no freckles, no tattoos. A plain simple grey two-piece swimsuit (triangle top,
simple bottoms) tight to the body with no strings, bows, frills or loose parts.

Plain light grey background, even soft studio light, no floor shadow, no text, no props, whole body in frame
with space around it.
```

## P2: the man's base body

```
Using the two attached pictures as the exact art style, and the attached woman base body as the size reference
(same height, same head size, same hands and feet, same A-pose, same camera):

A neutral BASE BODY for a 3D game character, a man, made to match the woman exactly. Character turnaround sheet:
front view, three-quarter view, side view, back view, side by side, same scale, all four standing on the same line.

Pose: A-pose. Arms straight, held out about 45 degrees from the body, palms facing down and slightly back,
fingers together, thumb apart. Legs straight, feet apart about shoulder width, toes pointing forward.
Head looking straight ahead, neutral calm face, mouth closed in a small smile, eyes open looking forward.

A slightly wider chest and shoulders than the woman, a soft round belly, no muscles showing. Bald (smooth round
head), no beard, no body hair, plain one-colour skin (medium warm tan). Plain simple grey swim shorts tight to the
body, ending above the knee, no drawstring, no pattern.

Plain light grey background, even soft studio light, no floor shadow, no text, no props, whole body in frame
with space around it.
```

## P3: goofy, the players' lifeguard, in the new style

Attach the style pictures, the chosen man base (P2) and `New-Game-2026-10-20/goofy_current_turnaround.png`.

```
Redraw the attached goofy lifeguard character in the attached art style, on the attached man base body (same
height, same proportions, same A-pose, same camera, same four views). Keep what makes him goofy, his gag:
huge round googly eyes (white with small black pupils, bulging out), two big buck teeth in a wide silly grin,
a big nose, sticking-out ears, short brown hair.

Clothes: a white sleeveless lifeguard vest a little too short (his round belly shows under it), red lifeguard
swim shorts, a whistle on a black cord round his neck. Barefoot.

Turnaround sheet: front, three-quarter, side, back, side by side, same scale, on the same line. Plain light grey
background, even soft light, no floor shadow, no text, nothing in the hands.
```

## P4: a character variant (concept only)

One per character. Attach the style pictures and the matching base (P1 or P2). Replace the part in < >.

```
The attached base body, exactly the same body, pose, proportions and camera, dressed and styled as:
<WHO: e.g. "Stacy, a tourist: long wavy black hair, light brown skin, a purple string bikini, white
sunglasses pushed up on her head, pink flip-flops">.

Only change: skin colour, hair, face details (eyebrows, freckles, moustache, makeup), clothes and small
accessories. Do not change the body shape, the hands, the feet, the head size or the pose.

Turnaround sheet: front, three-quarter, side, back, side by side, same scale, on the same line. Plain light grey
background, even soft light, no floor shadow, no text.
```

For a bigger or older body (the sunburnt dad, the Pirate King), add: `a rounder belly and a little wider, same
height` (that becomes a body slider, not a new body). For a kid: `the same character at 3/4 of the height, same
proportions` (a scale, not a new body).

## P5: hair, hats and glasses (separate pieces)

```
A sheet of separate cartoon <hair styles / hats / sunglasses> in the attached art style, each one on its own,
seen from the front and from the side, as if floating in place on an invisible bald head of the attached base
body (same size). Plain light grey background, no head or body visible, no text.
<list: e.g. "short brown messy hair, a blonde bob, a high black curly bun, a brown ponytail with a red hair tie,
a straw sun hat, a blue baseball cap, a pirate tricorn, round black sunglasses, heart-shaped sunglasses">
```

## The current cast as variants (for P4)

Sandy (and Sandy at her Lost & Found booth), the sunburnt dad and his ten look-alikes (one base, ten textures),
Lola and the six beach girls, the barista, the bartender, Marisol and the second hotel attendant, the hotel
director, Bellhop Benny, the black-tee tourist, Milo, the THIEF, the four pirates, the Pirate King, the FK rival
lifeguard. Every one is P4 on P1 or P2; none gets its own 3D model.
