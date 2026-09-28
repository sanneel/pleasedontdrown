# PLEASE DON'T DROWN: everything to generate in Meshy

> Written 2026-09-28, after story mode chapters 1-2, the beach crowd and Sandy's Lost & Found hut.
> This is the complete checklist. Meshy settings, the style rules and the texture prompt template are in
> [03-Art-Direction-Meshy.md](03-Art-Direction-Meshy.md) §2 and §4.1; import notes in [04](04-Meshy-Integration.md).

> **On the Free plan, start with [07-Meshy-Island-1.md](07-Meshy-Island-1.md):** island 1 only, and characters made
> from an A-pose picture (Pose Control is Pro-only). It also adds `tower_wide`, `lf_sign_frame`, `lf_shelf`, `lost_box`
> and `radio`.

**Already done (keep):** `station_rusty` (the shack, now Sandy's Lost & Found), `tower`, `palm_tall`.
**Must be redone:** `tourist_dad` and `lifeguard` came out as statues (no A-pose, no skeleton), so they can't move.

Everything in the game today is a stand-in built in code. Each model below replaces one of them. The code only needs
the named points (seat, grips, muzzle...), which Claude adds after import.

## How to use this list
1. Work top to bottom: **P1** is what the game shows right now, **P2** adds variety, **P3** is for later milestones.
2. For each row: Text to 3D (or Image to 3D where it says so), paste the **Prompt** (complete, style included),
   pick the best shape, then the texture step with the **Texture** prompt if there is one (otherwise the template in
   03 §4.1 with the colours from the prompt), PBR on, 2K.
3. Characters: **A-pose**, then **auto-rig (humanoid)**. Everything else: no pose, no rig. (No Pose button on the Free
   plan: make an A-pose picture first and use Image to 3D, see 07.)
4. Download **GLB** named exactly like the **File** column into `F:\GameDev\PleaseDontDrown\ArtSource\Meshy\`.
5. Tell Claude which files arrived. Claude decimates, fixes scale and pivots, adds colliders and swaps them in.

**Credits:** P1 is ~45 models, which is one month of Meshy Pro (1,000 credits, up to 100 assets) with room for
retries. Generate the final set on Pro: Free-plan output is public and CC BY (see 03 §10).

## Rules for every model
* Cartoon, chunky, rounded, bright tropical colours, soft hand-painted texture. Never realistic, never gory.
* **No text or logos** in textures: every sign, name tag and label is written in the engine.
* **No Red Cross emblem.** First aid uses a white cross on **green**, or a heart.
* One object per model: nothing held in hands, no bags on characters (bags are separate props).
* Characters: full body, A-pose, arms away from the body, hands open and empty, legs apart, facing forward,
  symmetrical, no long loose cloth (skirts, capes, sarongs flap badly on a rig). ~1.75 m, **10k triangles**.
* Faces: big simple round eyes and a small mouth. Claude may lay the game's animated eyes, brows and mouth over the
  head so characters still talk, blink and look scared.

---

## P1: needed for what's in the game now

### Characters (A-pose, auto-rig)
Every character prompt below already ends with the character style: *Full body, A-pose, arms angled down and away from the body, legs
slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical
colors, soft hand-painted texture.*

| # | File | Who | Prompt | Texture prompt | Notes |
|---|---|---|---|---|---|
| C-01 | `sandy` | Sandy, the guide (Lost & Found) | Friendly woman about 50 years old, slim, blonde hair tied in a bun, no glasses, faded coral short-sleeve collared shirt with a few big cream flower shapes, dark teal apron with one square pocket, beige cropped trousers, flat brown sandals, a small ring of keys hanging at the waist, kind face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Light skin, golden-blonde hair, faded coral shirt with big cream flowers, dark teal apron, beige trousers, brown sandals, brass keys. | **Image to 3D** from `ArtSource/Concepts/Sandy/sandy-game-style-v1.png` (crop the left, GUIDE figure) gives the best likeness; add this prompt. She sits on a stool most of the time: check the rig bends at the hips and knees. |
| C-02 | `receptionist` | Marisol, hotel receptionist | Young adult hotel receptionist woman, long dark hair in a low ponytail, warm brown skin, neat navy blue hotel uniform blouse with a small blank gold badge, navy trousers, black flat shoes, polite professional smile. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Warm brown skin, dark brown hair, navy uniform, plain gold badge, black shoes. | Stands behind the reception desk. |
| C-03 | `robber` | The beach thief | Skinny sneaky man, stubble, black bandana on his head, dark sunglasses, black and white horizontally striped long-sleeve shirt, navy cargo trousers, grey sneakers, sly grin. No bag, nothing in his hands. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Tanned skin, black bandana, black sunglasses, black and white striped shirt, navy trousers, grey sneakers. | His backpack is a separate prop (I-16). Needs to run, get knocked down, kneel and beg. |
| C-04 | `pirate_captain` | Pirate Pete | Big-bellied cartoon pirate man, red bandana, bushy black beard, gold hoop earring, open brown leather vest over a torn white shirt, wide black belt with a big buckle, baggy brown trousers, black boots, grinning. Empty hands, no weapons. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Tanned skin, red bandana, black beard, brown vest, off-white shirt, brown trousers, black boots, gold earring and buckle. | Retexture it for **Salty Bill** (grey beard, blue bandana). |
| C-05 | `pirate_olga` | One-Eyed Olga | Tough cartoon pirate woman, black eyepatch, red-orange hair in two thick braids, blue and white striped shirt, brown leather corset belt, baggy dark trousers tucked into boots, fierce grin. Empty hands, no weapons. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Freckled light skin, red-orange hair, black eyepatch, blue and white stripes, brown belt, dark grey trousers, brown boots. |  |
| C-06 | `pirate_brute` | Big Sal | Huge muscular bald cartoon pirate, thick black mustache, sleeveless red and black striped shirt, rolled-up baggy trousers, bare feet, angry eyebrows. Empty hands, no weapons. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Tanned skin, black mustache, red and black stripes, khaki trousers. |  |
| C-07 | `tourist_bikini_red` | Tourist (woman) | Young adult woman beach tourist, slim, long wavy brown hair, bright red two-piece bikini, white sunglasses pushed up on her head, flip-flops, cheerful face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Light tanned skin, brown hair, bright red bikini, white sunglasses, yellow flip-flops. | **Women need two extra chest bones** for the CPR jiggle: Claude adds `BustL`/`BustR` in Blender after the auto-rig (don't worry about it in Meshy). |
| C-08 | `tourist_bikini_curvy` | Tourist (woman) | Curvy adult woman beach tourist, short curly black hair, yellow two-piece bikini with a green palm-leaf print, wide straw sun hat, flip-flops, friendly face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Dark brown skin, black curly hair, yellow bikini with green leaf print, straw hat, pink flip-flops. | Same chest bones. |
| C-09 | `tourist_bikini_sporty` | Tourist (woman) | Athletic adult woman beach tourist, blonde high ponytail, turquoise sporty two-piece bikini, white sweatband on the wrist, barefoot, confident face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Sun-tanned skin, blonde hair, turquoise bikini, white sweatband. | Same chest bones. |
| C-10 | `tourist_mom` | Tourist (woman) | Middle-aged female beach tourist, tanned skin, huge straw sun hat, big round sunglasses, turquoise one-piece swimsuit, orange inflatable arm floaties on both upper arms, cheerful face. No sarong. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text. Warm tanned skin, brown hair, straw-yellow woven sun hat with a pink ribbon, black round sunglasses, turquoise swimsuit, bright orange arm floaties. | Same chest bones. |
| C-11 | `tourist_dad` | Tourist (man) | Chubby middle-aged male beach tourist, sunburnt pink skin, loud red Hawaiian shirt with white hibiscus flowers worn open, baggy blue swim shorts, flip-flops, bucket hat, bushy mustache, goofy friendly face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text. Sunburnt pink skin, dark brown mustache, red Hawaiian shirt with big white hibiscus flowers, royal blue swim shorts, khaki bucket hat, yellow flip-flops. | **Redo** in A-pose (the current one is a statue). |
| C-12 | `tourist_grandpa` | Tourist (man) | Skinny elderly male tourist, bald head with fluffy white side hair, huge white mustache, round glasses, white tank top, purple striped retro swim trunks, black knee-high socks with sandals, knobbly knees. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Pale wrinkly skin, white hair and mustache, round glasses, white tank top, purple and white striped trunks, black socks, brown sandals. |  |
| C-13 | `tourist_gym` | Tourist (man) | Very muscular tanned bodybuilder tourist, small orange swim trunks, yellow sweatband on the forehead, spiky blond hair, gold chain, overconfident grin. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky exaggerated proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Deeply tanned skin, spiky blond hair, yellow sweatband, orange swim trunks, gold chain. |  |
| C-14 | `tourist_swimmer` | Tourist (man) | Skinny young adult man, shirtless, pale skin with sunburnt pink shoulders and nose, green swim trunks with white side stripes, messy red hair, swim goggles pushed up on the forehead, goofy grin. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Hand-painted stylized cartoon texture, soft shading, no text. Pale skin, pink sunburn, red hair, green trunks with white stripes, blue goggles. | Most of the swimmers and drowning men. Retexture it for 2-3 more (different trunks, hair). |

### Buildings and furniture (no rig)
Every prompt below already ends with the style: *Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture.*

| # | File | Size | Prompt | Notes |
|---|---|---|---|---|
| B-01 | `hotel` | 24 × 12 m, 7 m high | Two-storey cartoon beach hotel, cream stucco walls, teal window shutters and trim, terracotta tiled roof, a wide open arched entrance in the middle of the front, big windows on both floors, a blank sign board above the entrance, two potted palms by the door. Whole building, front 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | The Grand Coral Hotel. Outside shell only: Claude cuts the entrance open and builds the lobby inside (walls, floor) from the props below. |
| B-02 | `reception_desk` | 3.2 m wide | Curved hotel reception desk, warm wood top, teal front panel with a gold stripe, a small silver service bell and a blank name plate on top. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | The shop counter. |
| B-03 | `hospital_bed` | 2.1 m long | Hospital bed on small wheels, white metal frame, thick white mattress, light blue sheet, a pillow, a folded blanket at the foot. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | The infirmary. The shark victim is laid on it. |
| B-04 | `firstaid_cabinet` | 0.9 m wide | Wall-mounted first aid cabinet, white box with a green door showing a white cross, a small wooden shelf underneath. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | Hotel first-aid station (the defibrillator sits on the shelf). White cross on **green**. |
| B-05 | `bar_stool` | 0.75 m high | Tall wooden bar stool with a round red cushion seat and a round footrest ring. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | Sandy's stool in the Lost & Found window. |
| B-06 | `alarm_bell` | 2.3 m high | Brass hand bell hanging from a curved iron bracket on a wooden post, with a short pull rope. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | The station bell next to the shack. |
| B-07 | `signboard` | 1.5 m wide | Wooden notice board on two posts with a large blank white panel, simple wooden frame and a small slanted roof on top. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture. | Drill board, welcome sign, Lost & Found roof sign: text is added in the engine. |
| B-08 | `dock_section` | 2.4 × 4 m | Straight section of a wooden pier: weathered planks across two thick beams on four round wooden posts, a few iron bolts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | Repeated along both docks. |
| B-09 | `dock_end` | 2.4 × 3 m | End section of a wooden pier: square deck of weathered planks on thick round posts, a metal swim ladder going down on one side, two mooring bollards and a coiled rope. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture. | End of both docks. |

### Beach
| # | File | Size | Prompt | Notes |
|---|---|---|---|---|
| N-03 | `rock_boulder` | 3 m | Large smooth rounded coastal boulder, grey-beige granite with soft cracks, a few barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. | The two swim-out rocks. |
| N-04 | `rock_cluster` | 4 m | Cluster of three smooth coastal rocks of different sizes fused together, grey-beige granite, barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. |  |
| N-05 | `umbrella` | 2.2 m | Open beach umbrella with red and white stripes and a scalloped edge on a wooden pole stuck in the ground. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | ~12 on the beaches. Retexture 2 more: teal/white, yellow/white. |
| N-07 | `swim_buoy` | 0.5 m | Floating swim-zone marker buoy: round yellow plastic float with a white band and a small loop on top. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | The swim-zone line. |
| N-10 | `beach_towel` | 1.9 m | Beach towel lying flat on the sand, slightly rumpled corners, bold horizontal stripes. Top-down 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | Sunbathers lie on these. Retexture for 3-4 colour sets. (If Meshy makes it too thick, Claude uses a flat textured quad.) |

### Items (hold / pocket / throw)
Every prompt below already ends with the style: *Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture.*
Small items **0.8-2k triangles**, 1024 texture. Side view for anything with a front.

| # | File | Size | Prompt | Notes |
|---|---|---|---|---|
| I-01 | `lifering` | 0.7 m | Classic life ring buoy: thick red-orange foam ring with four white fabric bands and a grab rope looped around the outside. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |  |
| I-03 | `crate` | 0.6 m | Wooden supply crate made of planks with dark metal corner brackets and a painted blue stripe, lid nailed shut. Stylized cartoon game asset, chunky rounded shapes, warm colors, soft hand-painted texture. |  |
| I-04 | `cooler` | 0.55 m | Picnic cooler box, blue plastic body, white lid, grey carrying handle, small drain plug. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |  |
| I-05 | `firstaid_kit` | 0.4 m | First aid case, green hard plastic box with a white cross symbol on the lid, two latches and a carry handle. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | On the shack shelf. |
| I-08 | `coconut` | 0.22 m | Single brown coconut with hairy husk texture and three dark eyes at one end. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Falls from palms, you eat it. |
| I-10 | `wallet` | 0.12 m | Folded brown leather wallet, slightly bulging with money, stitched edges. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Lost item. |
| I-11 | `phone` | 0.16 m | Smartphone in a bright pink rubber case, dark screen, small camera bump. No logos. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Lost item (sinks: you dive for it). |
| I-12 | `sunglasses` | 0.14 m | Pair of chunky cartoon sunglasses with a bright pink plastic frame and dark lenses, arms unfolded. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Lost item. |
| I-13 | `watch` | 0.16 m | Gold wristwatch with a round white face and a black strap lying open. No logos, no text. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Lost item. |
| I-14 | `evidence_bag` | 0.1 m | Small clear plastic zip bag with a red seal strip and a few white round tablets inside. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Falls out of the robber's backpack. |
| I-15 | `jetski_keys` | 0.12 m | Two keys on a ring with a chunky orange foam floating keychain. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | The robber gives these up. |
| I-16 | `robber_backpack` | 0.5 m | Lumpy worn brown canvas backpack, stuffed full, flap buckled shut, two shoulder straps. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Worn on the robber's back; bursts open when he goes down. |
| I-18 | `pistol` | 0.25 m | Chunky cartoon pistol with toy-like proportions, dark grey metal and black grip, side view, barrel pointing right. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Bought at reception. Claude adds the grip point and muzzle. |
| I-19 | `defibrillator` | 0.35 m | Portable defibrillator: bright yellow plastic case with a carry handle and a white heart with a lightning bolt on the lid, two black paddles with coiled cables resting on top. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. | Hotel first aid. |

### Vehicles and creatures (no rig)
Vehicles **8-12k triangles**, 2048 texture.

| # | File | Size | Prompt | Notes |
|---|---|---|---|---|
| V-01 | `jetski` | 3 m | Sporty jet ski with a glossy black and lime green hull, one long padded seat for two riders, handlebars with rubber grips, a small windscreen, footwells on both sides. Side view. Stylized cartoon game vehicle, chunky rounded proportions, bright colors, soft hand-painted texture. | The robber's jet ski (the rescue one in 03 V-01 comes later). Claude adds the seat and handlebar grip points. |
| V-04 | `pirate_boat` | 7 m | Small wooden pirate motorboat, dark brown planks, a short mast with a black flag showing a white skull, a little cabin at the back with a ship's steering wheel in front of it, rope coils and a barrel on the deck, an outboard motor. Side view. Stylized cartoon game vehicle, chunky rounded proportions, soft hand-painted texture. | Four pirates stand on the front deck; later you drive it. |
| A-01 | `shark` | 3 m | Cartoon shark, blue-grey body, white belly, big toothy grin with goofy eyes, dorsal fin and tail fin clearly separate, mouth slightly open. Side view. Stylized cartoon game creature, chunky rounded shapes, soft hand-painted texture. | No rig: Claude cuts the tail off at its base in Blender so it can wag. |

---

## P2: variety (after P1 looks right in game)
| # | File | Prompt | Notes |
|---|---|---|---|
| C-15 | `tourist_grandma` | Elderly woman tourist, white curly hair under a pink flowered swim cap, big round sunglasses, purple one-piece swimsuit with a small frill, flip-flops, sweet smile. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Chest bones. |
| C-16 | `tourist_suit` | Middle-aged office worker who went swimming in his suit: soaked grey business suit, loosened red tie, trousers rolled up to the knees, bare feet, slicked-back hair, worried confused face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright colors, soft hand-painted texture. |  |
| C-17 | `tourist_backpacker` | Lanky young adult backpacker, pale skin, messy brown hair, oversized yellow t-shirt with a pineapple print, green board shorts, white sneakers, earbuds, bored face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |  |
| C-18 | `tourist_selfie` | Young adult female tourist, neon-pink two-piece bikini with white board shorts, heart-shaped sunglasses, big high ponytail, phone in a waterproof pouch on a lanyard around her neck, excited face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | A bikini, not a swimsuit: gets the chest bones too. |
| C-19 | `lifeguard_m` | Athletic cartoon lifeguard, red swim shorts, white tank top with one red horizontal stripe, red cap worn backwards, silver whistle on a cord around the neck, sunglasses pushed up on the forehead, white sunscreen stripe on the nose, confident face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright colors, soft hand-painted texture. | Replaces the statue `lifeguard.glb`. |
| C-20 | `lifeguard_f` | Athletic female lifeguard, red one-piece swimsuit with white side stripes, red cap worn backwards, silver whistle on a cord, sunglasses pushed up on the forehead, white sunscreen on the nose, confident face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. | Chest bones. |
| N-02 | `palm_short` | Short leaning palm tree with a thick curved trunk, drooping green fronds and three coconuts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |  |
| N-06 | `sunbed` | Wooden beach sun lounger with a slatted frame and a thick turquoise cushion. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |  |
| N-08 | `sandcastle` | Lumpy sandcastle with four towers, a little moat wall and a seashell flag on top. Stylized cartoon game asset, chunky rounded shapes, warm sand colors, soft hand-painted texture. |  |
| N-09 | `tiki_bar` | Small tiki beach bar with a thatched straw roof, bamboo counter, three bar stools, a blank chalkboard and hanging string lights. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |  |
| N-11 | `potted_palm` | Small palm in a big round terracotta pot. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | hotel lobby |
| N-12 | `beach_bag` | Straw beach bag with a towel and a bottle sticking out. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. | next to towels |

The lifeguards (C-19/C-20) are only needed if players stop using the customizable code-built characters.
Decide before generating: a Meshy lifeguard means fixed looks with colour tints instead of the customizer.

## P3: later milestones
`station_good`, `station_hq` (03 §6, station upgrades), `rescue_tube`, `rescue_board`, `buoy_launcher` (03 §8, M7),
`rescue_rib` (03 §8, M8), `rival_heli` (03 §8), `octopus` (03 §8 "Later").

---

## Checklist
P1 (45): sandy, receptionist, robber, pirate_captain, pirate_olga, pirate_brute, tourist_bikini_red,
tourist_bikini_curvy, tourist_bikini_sporty, tourist_mom, tourist_dad, tourist_grandpa, tourist_gym, tourist_swimmer,
hotel, reception_desk, hospital_bed, firstaid_cabinet, bar_stool, alarm_bell, signboard, dock_section, dock_end,
rock_boulder, rock_cluster, umbrella, swim_buoy, beach_towel, lifering, crate, cooler, firstaid_kit, coconut, wallet,
phone, sunglasses, watch, evidence_bag, jetski_keys, robber_backpack, pistol, defibrillator, jetski, pirate_boat, shark.

Suggested order (most visible first): sandy → tourist_bikini_red → tourist_swimmer → robber → umbrella →
beach_towel → lifering → hotel → receptionist → jetski → shark → pirate_captain → the rest.

## What Claude does with each delivery
* Decimates to the budgets, fixes scale, pivot and forward direction, converts materials to URP, compresses textures.
* Characters: maps the auto-rig bones onto the game's skeleton (`AvatarRig.Bone`), so the existing procedural
  animation, the tourist ragdoll, sitting, CPR poses and the first-person hands keep working; adds the two chest
  spring bones on women; overlays animated eyes and mouth if the painted face can't talk.
* Buildings: cuts doorways, builds simple box colliders (never the mesh), re-bakes the navmesh.
* Props and vehicles: adds grip points, seats, muzzle, colliders and navmesh obstacles; keeps the stand-in as a
  fallback until the model is in.
