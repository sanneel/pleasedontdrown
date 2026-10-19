# Island 4 (Finders Keepers, chapter 4): Tripo prompt list

Asked for by Shako 2026-10-19 ("კი" to the list in `Docs/Report-Shako-Chapter-4.md`: 9 models, 270 credits).
Settings: PBR on, texture 2K, Ultra Mesh off, Remove Lighting on, Triangle; polycount as in the table.
Files: raw `ArtSource/Tripo/Island4/raw/<asset_name>_meshopt.glb`, decoded `ArtSource/Tripo/Island4/<asset_name>.glb`.

Style: characters use the style block in `pirates.md`; the creature the creature block in `island3.md`; props the prop
block in `island2-gala-gold.md`; buildings and boats the island 3 building line ("stylised low-poly ... game asset, no
ground"), kept **clean and new**: island 4 is a too-tidy corporate resort, orange and white. No text anywhere (Tripo
letters come out garbled; the builder adds the signs).

| # | asset_name | Faces | Size (m, W × D × H) | Replaces (GameSceneBuilder.Island4 / story) |
|---|---|---|---|---|
| I1 | fk_lifeguard | 30k | 1.8 tall | the rival lifeguards (now the goofy lifeguard recoloured): an avatar body |
| I2 | fk_helicopter | 30k | 10 × 2.5 × 3.2 | `FKHelicopter` (primitives) |
| I3 | fk_warehouse | 40k | 38 × 24 × 10 | `Warehouse` shell (walls, roof) |
| I4 | smoothie_bar | 25k | 9 × 8 × 4.5 | `SmoothieBar` |
| I5 | sandy_villa | 40k | 16 × 12 × 8 | `SandyVilla` |
| I6 | grabber_claw | 10k | 2.6 long | `GrabberClaw` (and the copy in Sandy's hand) |
| I7 | police_launch | 30k | 3 × 9 × 3 | `PoliceBoat` |
| I8 | fk_tower | 20k | 3.4 × 6 × 6.5 | `FKTower` |
| I9 | grabby | 30k | ~8 across | Grabby (the Kleptopus's cousin, boss phase 2) |

## Prompts (style block first, then the line)

**I1 fk_lifeguard** (character block): Fit young male beach lifeguard working for a slick corporate rescue company, bright orange polo shirt with white stripes on the shoulders, white board shorts, orange sun visor, silver whistle on a white cord round his neck, white sneakers, tanned skin, short neat blond hair, smug confident smile.

**I2 fk_helicopter** (prop block): Small civilian rescue helicopter, glossy orange body with white stripes, large rounded glass cockpit, two-blade main rotor on top and a small tail rotor, white landing skids, sliding side door, brand new and spotless.

**I3 fk_warehouse** (building): Stylised low-poly large modern corporate warehouse hangar, long rectangular building, clean white corrugated metal walls with bright orange corner trims, low slightly curved white roof, one very large open rectangular doorway in the middle of the long front wall showing a dark interior, small windows high on the sides, orange stripe along the base, very clean and new, game asset, no ground, no text.

**I4 smoothie_bar** (building): Stylised low-poly modern tropical beach smoothie bar kiosk, white counter with an orange countertop, four bar stools in front, flat orange canopy roof on four slim white posts, back shelf with blenders and bottles of purple berry smoothie, bowls of berries and fruit on the counter, clean bright colours, game asset, no ground, no text.

**I5 sandy_villa** (building): Stylised low-poly modern luxury villa, two storeys, white cube shapes, ground floor with a large glass front and a wide open doorway in the middle, smaller upper storey set back with a terrace and glass railing, thin orange accent stripes, flat roofs, very clean, game asset, no ground, no text.

**I6 grabber_claw** (prop block): Giant reach-and-grab litter picker tool, a long straight orange metal pole, black pistol grip handle with a trigger at one end, three curved shiny gold claw fingers at the other end, lying horizontally.

**I7 police_launch** (building): Stylised low-poly small police patrol boat, white hull with a dark blue stripe, small white wheelhouse cabin with windows, red and blue light bar on the cabin roof, low railing round the deck, black rubber fenders, clean, game asset, no water, no text.

**I8 fk_tower** (building): Stylised low-poly tall beach lifeguard tower on four white wooden legs, a small square hut on top painted bright orange with white horizontal stripes, flat white roof, small deck with railing in front of the hut, a slatted ramp going up the back, orange flag on a pole, clean and new, game asset, no ground, no text.

**I9 grabby** (creature block): Giant orange octopus, bigger cousin of a jewel-thief octopus, big round sunset-orange head with large greedy eyes, a cream sun hat and big black sunglasses, gold chains round its neck, glossy orange skin with pale yellow suckers, eight thick curling tentacles.
