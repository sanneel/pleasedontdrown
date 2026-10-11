# Pirates (islands 2-3): Tripo prompt list

Batch 1 approved by Shako 2026-10-11 (~290 credits, up to ~870 with retries): 8 characters + 3 weapons. After the v1 style was rejected: ONE v2 test (pirate_king) first, Shako decides whether to continue.
Characters: settings in the style block below (v2: PBR on), Triangle,
polycount 30000, latest model), then `ArtSource/Tools/prepare_character.py` rigs them (needs a clean A-pose, arms away
from the body, empty hands). Weapons: props, polycount 10000. Models are rigged/checked but not placed in the game yet
(the fight logic comes first; visuals later).

Files: raw `ArtSource/Tripo/Pirates/raw/<asset_name>_meshopt.glb`, decoded `ArtSource/Tripo/Pirates/<asset_name>.glb`.

**Style block (characters, all) — v2, 2026-10-11.** v1 ("stylised cartoon ... bright flat colours", PBR off) was
REJECTED by Shako: too cartoonish. The look to match is Shako's `THIEFASPIRATE.glb` (Image to 3D): stylised-realistic,
natural proportions, detailed worn materials (leather with stitching, cloth folds, brass/gold fittings), PBR textures.
Settings for v2: PBR **on**, texture 2K, Ultra Mesh off, Remove Lighting on, Triangle, polycount 30000.

Stylized realistic 3D game character, like a hero from a modern high-end animated adventure game, full body, one person alone, standing straight in an A-pose: arms straight down and 30 degrees away from the body, hands open, legs slightly apart, facing the camera. Realistic adult human proportions, head about one eighth of body height, lightly stylized friendly face with natural skin, real eyes and a natural expression. Detailed realistic materials: worn leather with stitching, cotton and wool cloth with natural folds, brass and gold metal fittings, physically based textures, rich natural colours, clean high quality sculpt. Nothing in the hands, no weapons, no base.

| # | asset_name | Faces | Character line (after the style block) |
|---|---|---|---|
| P1 | pirate_king | 30k | Big broad pirate king, long red coat with gold trim and big gold buttons, black tricorn hat with a white feather and a small skull, black eye patch over his right eye, a curved steel pirate hook instead of his left hand, thick dark curly beard, gold chain, black boots, confident grin. (Shako 2026-10-11: hook + eye patch added.) |
Shako 2026-10-11: P2 fat bald, P3 teen with a bandana, P5 girl in shorts and boots with the king's hat and jewellery
on her hands; the rest my picks. The thief's pirate look is Shako's own `THIEFASPIRATE.glb` (not generated here).

| P2 | pirate_fat | 30k | Very fat round-bellied bald pirate, shiny bald head, bushy black beard, white sleeveless shirt stretched over a huge belly, wide red sash belt, patched brown trousers, black boots, gold earring, jolly laugh. |
| P3 | pirate_teen | 30k | Skinny teenage pirate boy about sixteen, blue bandana with white dots over messy brown hair, loose white shirt with rolled sleeves, short brown vest, rope belt, rolled-up striped trousers, barefoot, freckles, cheeky grin. |
| P4 | pirate_marksman | 30k | Older lean pirate sharpshooter, long dark blue coat, short grey beard, brown wide-brim hat, leather bandolier with powder pouches, grey trousers, tall black boots, calm squinting expression. |
| P5 | pirate_girl | 30k | Young woman pirate, black tricorn hat with a white feather and a small skull, long wavy auburn hair, white blouse, dark leather corset belt, short brown leather shorts, tall black boots, gold bracelets, bangles and rings on both wrists and hands, gold hoop earrings, confident smile. |
| P6 | pirate_quartermaster | 30k | Tall pirate quartermaster with long grey dreadlocks tied back, dark green long coat with brass buttons, white shirt, brown leather bandolier across the chest, dark trousers, tall brown boots, stern look. |
| P8 | tavern_brawler | 30k | Muscular cage fighter, shirtless with a few tattoos, black fight shorts with a red stripe, hands wrapped in white tape, barefoot, short dark mohawk, broken nose, determined look. |

**Weapons (props):** Stylised low-poly cartoon game prop, single object lying flat, side view, clean bright colours, no hands, no base, no text.

| # | asset_name | Faces | Prompt line |
|---|---|---|---|
| W1 | cutlass | 10k | Pirate cutlass sword, curved steel blade, gold basket hand guard, dark wooden grip. |
| W2 | flintlock_pistol | 10k | Old pirate flintlock pistol, dark wood stock, brass fittings, long steel barrel. |
| W3 | musket | 10k | Long old pirate musket rifle, dark wood stock, brass fittings, long steel barrel, flintlock. |
