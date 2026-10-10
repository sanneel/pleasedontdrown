# Island 3 (Skull Cove): Tripo prompt list

Status 2026-10-09: all 15 generated and kept (wall, gatehouse, cave on the 2nd try): `ArtSource/Tripo/island3/*.glb`.
Review sheets: `Screenshots/Review/Tripo/sheet_front.png`, `sheet_back.png`, `v2/`.

In priority order. `asset_name` is the file name (`ArtSource/Tripo/island3/<asset_name>.glb`). Size is the real-world
target in metres (W × D × H). "Replaces" is the greybox in `GameSceneBuilder.Island3.cs`. Style for all: clean,
bright, stylised low-poly game asset matching the Grand Coral hotel (island 2); no photorealism, no dirt textures.

| # | asset_name | Face limit | Size (m) | Replaces |
|---|---|---|---|---|
| 1 | breakwater_boulders | 8k | each 2–4 across | coast boulders (`Riprap_NN`) |
| 2 | pirate_shack_thatch | 25k | 5.5 × 5 × 5.5 | `PirateHouse_*` |
| 3 | pirate_shack_tarp | 25k | 5.5 × 5 × 5.5 | `PirateHouse_*` |
| 4 | pirate_shack_shingle | 25k | 6 × 5 × 6 | `PirateHouse_*` |
| 5 | castle_round_tower | 40k | 11 × 11 × 26 | corner towers |
| 6 | castle_gatehouse | 60k | 17 × 7 × 15 | gate towers, arch, doors |
| 7 | castle_keep | 60k | 26 × 18 × 30 | keep (hall stays walk-in: needs a real open door) |
| 8 | castle_wall | 15k | 10 × 3 × 11.5 | curtain walls |
| 9 | pirate_throne | 15k | 2.4 × 1.6 × 3.3 | throne |
| 10 | sea_cave_entrance | 40k | 26 × 10 × 18 | cave mouth, pillars, shell front |
| 11 | treasure_chest_open | 10k | 1.6 × 1 × 1.4 | chests |
| 12 | gold_coin_pile | 10k | 3 × 2.5 × 1 | coin heaps |
| 13 | money_sack | 6k | 0.9 × 0.9 × 1.1 | money sacks |
| 14 | casino_chip_stacks | 6k | 0.8 × 0.8 × 0.4 | chip stacks |
| 15 | sea_rock_pinnacle | 10k | 8 × 8 × 22 | crag pinnacles, sea stacks |

## Prompts

**1 breakwater_boulders**
Set of 6 large angular granite breakwater boulders arranged apart on a flat plane, rough broken flat faces and sharp edges, grey granite and dark slate stone with rusty orange-tan patches, stylised low-poly, clean colours, game asset, no ground.

**2 pirate_shack_thatch**
Stylised low-poly pirate shack on short wooden stilts, weathered plank walls, steep thatched gable roof, small front porch with railing and two steps, one wooden door, shuttered window, hanging lantern by the door, bright cartoon colours, game asset, no ground.

**3 pirate_shack_tarp**
Stylised low-poly pirate shack on short wooden stilts, faded teal painted plank walls, steep roof covered with patched red canvas tarp, small front porch with barrels and a crate, one wooden door, shuttered window, hanging lantern, bright cartoon colours, game asset, no ground.

**4 pirate_shack_shingle**
Stylised low-poly pirate house on wooden stilts, dark weathered planks, steep dark wooden shingle gable roof with a small crow's nest lookout on top flying a black skull flag, front porch with steps, one door, two shuttered windows, bright cartoon colours, game asset, no ground.

**5 castle_round_tower**
Stylised low-poly round stone castle tower, light grey stone blocks, red conical roof with a small black pirate skull flag on top, arrow slit windows, a stone band near the top, cartoon game asset, no ground.

**6 castle_gatehouse**
Stylised low-poly castle gatehouse seen from the front, symmetrical, two square stone towers side by side with battlements, a large arched gateway between them with open wooden double doors, black pirate skull banner above the arch, torches on both sides, light grey stone, cartoon game asset, no ground.
(v1 "two square stone towers ... joined by an arch" came out L-shaped.)

**7 castle_keep**
Stylised low-poly stone castle keep for a pirate king, tall rectangular great hall with a wide open arched doorway and tall dark windows, battlements around the flat roof, a smaller square tower with a red pointed roof and a pirate flag on top, light grey stone, cartoon game asset, no ground.

**8 castle_wall**
Stylised low-poly long straight stone castle wall section, about four times longer than it is tall, thin and narrow, battlements along the top edge, light grey stone blocks, flat cut ends, no towers, cartoon game asset, no ground.
(v1 "wall segment" came out a fat cube: say how long and thin.)

**9 pirate_throne**
Stylised low-poly pirate king throne, red velvet seat and backrest, chunky gold frame decorated with skulls, crossed swords and coins, on a small stone step, cartoon game asset.

**10 sea_cave_entrance**
Stylised low-poly rocky sea cliff wall with a large dark arched cave opening in the middle at the bottom, the arch-shaped cave mouth is the main feature and goes deep inside, rough grey rock with rusty streaks, flat back side, cartoon game asset, no water.
(Both versions are a rock block with the arch on ONE side: render all four sides before judging.)

**11 treasure_chest_open**
Stylised low-poly open wooden treasure chest with gold bands, lid open, overflowing with gold coins and a few jewels, cartoon game asset.

**12 gold_coin_pile**
Stylised low-poly low wide pile of shiny gold coins with a few goblets and jewels, cartoon game asset.

**13 money_sack**
Stylised low-poly burlap money sack tied with rope at the top, a big dollar sign printed on it, a few gold coins spilling out, cartoon game asset.

**14 casino_chip_stacks**
Stylised low-poly stacks of casino poker chips of different heights, red, black and blue chips with white edge stripes, cartoon game asset.

**15 sea_rock_pinnacle**
Stylised low-poly tall jagged sea rock pinnacle, grey stone with rusty orange streaks, sharp broken faces, cartoon game asset, no water.

**16 pirate_tavern** (added 2026-10-11: the village's big bar, 2 x 2 houses; 60k, kept first try)
Stylised low-poly big pirate tavern pub building, wide two-storey timber frame house on a low stone base, one large open arched double doorway in the middle of the front, wide wooden front porch with three steps, hanging tavern sign with a beer mug, barrels stacked beside the door, warm lit windows, steep wooden shingle roof with a stone chimney and a small pirate flag, bright cartoon colours, game asset, no ground.
(Came out with nonsense letters "TARB" carved in the sign: tripo_prepare_island3.py replaces the sign front with a plain board and a gold tankard. Its front is the gable end, Blender +X before turning.)

**17 jungle_tree** (added 2026-10-11, 15k, kept; prepared down to 6k for the jungle)
Stylised low-poly tropical jungle tree, tall curved trunk with a few roots at the base, wide umbrella crown of big broad bright green leaves, a few hanging vines, bright cartoon colours, game asset, single tree, no ground.
(Came out short-trunked and leafy: fine mixed with palms.)

**18 jungle_bush** (added 2026-10-11, 6k, kept; prepared to 2.5k)
Stylised low-poly dense tropical jungle bush, round clump of big glossy broad green leaves of different sizes, a few red tropical flowers, bright cartoon colours, game asset, single bush, no ground.

**19 jungle_fern** (added 2026-10-11, 5k, kept; prepared to 1.5k)
Stylised low-poly tropical fern and monstera plant clump, long arching fern fronds and a few big split monstera leaves spreading out from the centre, bright green, bright cartoon colours, game asset, single plant, no ground.
