# Island three: Skull Cove (the pirates' island)

Chapter 3: the pirates robbed the hotel casino. Find their island and get the money back. Layout follows the
hand-drawn map (2026-10-09): rocks across the north, pirate houses through the middle, the cliffs in the south with the
Pirate King's castle, and a waterfall hiding the cave where the money is.

Built by `GameSceneBuilder.Island3.cs` (**PLEASE DON'T DROWN → Build island three (Skull Cove)**, or write `build` to
`Logs/island3-request.txt` while the editor is open; the result goes to `Logs/island3-result.txt`). Greybox: every
piece is a placeholder for a Tripo model and keeps its position, size and collision.

## Where it is

- ~220 m south of the hotel island. Footprint about **340 × 420 m**, centre (20, -900).
- The shared terrain grid now reaches z = -1290. Island 3 has **its own ground mesh** (`Island3Ground`, same 2 m grid,
  seamless) painted with vertex colours: sand at the water, grass inland, dirt roads and yards, grey rock on steep
  ground and the crags. It uses the character shader (soft light, shadows) with rim/backlight off.
- Arrival: the **pirate dock on the east coast** (z = -905). A travel pad is next to it; `island3` in the console and
  Esc > Travel go there too.

## Layout (north → south)

| Area | What's there |
|---|---|
| **Coast** | A breakwater of big angular boulders all round (grey granite, dark slate, olive, rusty tan; darker where wet), like a riprap sea wall: a row half in the water, a row on the edge, a looser row behind, loose blocks in the shallows; under the cliffs only the water rows. Gaps for the dock, the river mouth and the waterfall cave. Generated shapes merged into ~90 m pieces with mesh collision (`Riprap_NN`). |
| **North crags** (blue on the map) | A rock ridge up to ~40 m high meeting the sea in cliffs, covered in ~75 big boulders and 9 pinnacles (the coast rock model, scaled up, mesh collision). |
| **Spring and river** | A pond at the foot of the crags; the river runs south-west through the meadows to the west coast. It's cut below sea level, so the sea fills it. |
| **Pirate village** (yellow) | 7 groups of up to 3 stilt shacks: plank walls, steep gable roofs (thatch, red/blue tarp, shingles), porch with steps facing the road, door, shutters, lantern; some fly the Jolly Roger or have crates. Dirt roads link the dock, village, castle and the pools. |
| **South plateau** (purple) | 18 m high, sheer cliffs into deep water on the sea side, a ~45 m slope up from the village. |
| **Pirate King's castle** | On the plateau, gate to the north. Curtain walls 10 m with battlements, four round towers with red cone roofs and flags, gatehouse with open doors and a big flag. The **keep is a walk-in hall**: red carpet, dais, throne, gold heaps, banners, torchlight. |
| **Pools, stream, waterfall** | Two pools on the plateau; the stream runs to the south cliff and falls 18 m into the sea (scrolling sheets, mist). |
| **Money cave** | Behind the waterfall at sea level: swim through the falls into a 16 × 34 m cave, walk up a ramp to a dry ledge with treasure chests, gold heaps, cash bricks, money sacks and the casino's chips, by torchlight. |

## Checks (run by the build)

- Seabed grid matches the terrain; collision equals render mesh.
- Castle corners on the plateau and at least 4 m from the cliff edge.
- Ray through the waterfall reaches the back of the cave (mouth open).
- Ray through the gate and the keep door reaches the throne.
- Navmesh routes from the dock to the village, the castle yard, the throne room and the pools.
- Review pictures: `Screenshots/Review/Island3/*.png`.

## Tripo prompts (replace the placeholders)

Same style as the hotel: stylised low-poly, clean colour blocks, no tiny detail. Use a **face limit** (Smart Low Poly):
~10k for props, ~25k for a house, ~60k per castle piece. Real-world scale, feet at the origin.

| Asset | Prompt | Replaces |
|---|---|---|
| Pirate shack | "Stylised low-poly pirate shack on short wooden stilts, weathered plank walls, steep thatched gable roof, small front porch with railing and steps, one door, shuttered window, hanging lantern, bright cartoon colours, game asset" | `PirateHouse_*` (4.4–6.6 × 4–5.6 m) |
| Shack variants | Same, with "patched red canvas roof" / "dark wooden shingle roof and a crow's nest" / "teal painted planks and barrels on the porch" | variety |
| Castle tower | "Stylised low-poly round stone castle tower with red conical roof, arrow slits, pirate flag on top, cartoon game asset" | corner towers (Ø10 m, 17 m + roof) |
| Castle gatehouse | "Stylised low-poly stone castle gatehouse, two square towers with battlements, open wooden double doors with iron bands, pirate skull banner over the arch" | gate (6 m opening) |
| Castle keep | "Stylised low-poly stone castle keep, tall rectangular hall with arched door and tall windows, small square tower with red roof on top" | keep (26 × 18 m, 13 m) |
| Wall segment | "Stylised low-poly stone castle wall segment with battlements, 10 m long, tileable" | curtain walls |
| Throne | "Stylised low-poly pirate king throne, red velvet seat, gold frame decorated with skulls and coins" | throne |
| Cave entrance | "Stylised low-poly sea cave entrance in a grey rock cliff, wide arch, cartoon game asset" | cave mouth and pillars |
| Treasure | "Stylised low-poly open treasure chest overflowing with gold coins" / "pile of gold coins" / "burlap money sack with dollar sign" / "stack of casino chips" | loot on the ledge |
| Crags | "Stylised low-poly tall jagged sea rock pinnacle, grey stone" | pinnacles and sea stacks |
| Breakwater rocks | "Set of 6 large angular granite breakwater boulders, rough broken faces, grey and dark slate with rusty orange-tan patches, stylised low-poly, game asset" | coast boulders |

## Next

- Chapter 3 beats in `StoryDirector` (arrival, sneaking in, rescuing pirates who can't swim, the vault, the Pirate King).
- Pirates walking the village (the navmesh is baked).
- Waterfall sound.

## Tripo models in (2026-10-10)

The 15 Tripo models (`.claude/skills/pdd-tripo`, list `prompts/island3.md`) replace the greybox where they exist:
`ArtSource/Tools/tripo_prepare_island3.py` turns each one to face +z, sizes it, puts its feet at the origin, makes it
one-sided and cuts openings (the keep's front door and the inside of its hall; the cave facade's arch), writing
`Assets/_Game/Art/Tripo/Island3/*.glb`. The builder (`TripoModel`, `TripoHouse`, `DressCastleWithTripo`,
`TripoPinnacle`) places them; with a model missing, that part stays greybox.

- **Village:** the three shack models (thatch, tarp, shingle) by turns, porch to the road, one box collider each.
- **Castle:** round towers on the corners, the wall model in ~30 m pieces, the gatehouse (arch ~3.7 m: two jamb
  colliders narrow the greybox gate to match), the keep as a shell over a smaller walk-in hall (16 x 14 m, 12 m high)
  with the Tripo throne and coin piles. The greybox outside keeps its collision, its renderers are removed.
- **Money cave:** the rock facade over the mouth (arch 9.2 m, collision boxes round it), the falls hang in front of it;
  the loot is Tripo chests, coin piles, money sacks and chip stacks (the cash bricks stay).
- **Rocks:** the crag pinnacles and sea stacks are the rock spire model with capsule colliders. The coast breakwater
  stays the generated boulders (the Tripo boulder set would be ~2 M triangles round the coast).
