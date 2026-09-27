# PLEASE DON'T DROWN — Art direction and Meshy prompts

Goal: replace the grey-box primitives with one consistent look. Meshy makes the **models** (characters, buildings,
props, vehicles). Things Meshy is bad at, Claude builds in the engine instead: terrain, water, sky, lighting and
post-processing, the modular dock planks, the beach ball (a textured sphere), UI and signs with text.

## 1. The look: "sunny stylized cartoon"
* Chunky, slightly exaggerated proportions, rounded edges, big readable silhouettes (a tourist must read from 30 m).
* Bright, saturated tropical colours; soft hand-painted textures; few small details.
* Comedy first: loud shirts, sunburns, floaties, bad fashion. Never gross or gory.
* Reference palette (for Claude's materials; Meshy understands colour **names** better than hex):
  sand `#F2D9A0`, shallow sea `#3FD0C9`, deep sea `#1B6CA8`, rescue red `#E23B2E`, sun yellow `#FFD23F`,
  wood `#9C6B43`, foliage `#3FAE5A`, sky `#8FD3FF`.

## 2. Meshy settings (same for every asset, so everything matches)
Checked against Meshy's docs on 2026-09-27. Text to 3D has two steps: **Preview** (the shape) and **Texture/Refine**
(the paint job, with its own optional texture prompt). There is no style picker any more (the style comes from the
prompt text) and no negative prompt field.

1. **Text to 3D**. AI model: **Meshy 6 Lite** on the Free plan (Free downloads only work for that model);
   **Meshy 7.1 / latest** on Pro.
2. Pose: **A-pose** for characters (T-xx, P-01), **none** for everything else.
3. On Pro only: turn on **Smart Topology** (or Remesh) with the face count from the tables. On Free, skip it;
   Claude reduces the polygons on import.
4. Paste the **Prompt**, generate, pick the best shape.
5. Texture step: paste the **Texture prompt** (§4 for Batch 1, the template in §4.1 for the rest), **PBR on**, 2K.
6. Characters only: **Rig / auto-rig** as a humanoid (follow its marker steps). Only the skeleton matters; physics
   moves the tourists. On Free, rig at least `tourist_dad` to test it.
7. **Download FBX**, named like the **File** column (e.g. `tourist_dad.fbx`), into
   `F:\GameDev\PleaseDontDrown\ArtSource\Meshy\`. Claude does the rest (see §9).

Tip: once a tourist body looks right, use Meshy's **retexture** on that same model to make outfit variants.
Same body = same ragdoll setup, just new clothes.

## 3. Free-plan test run (10 models)
Do Batch 1 in this order, so the most important ones are done if credits run out:
`tourist_dad` → `lifeguard` → `station_rusty` → `lifering` → `tourist_mom` → `tower` → `palm_tall` → `rock_boulder`.
That leaves 2 spare for retries.

## 4. Batch 1 texture prompts (paste in the texture step)
| File | Texture prompt |
|---|---|
| `tourist_dad` | Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text. Sunburnt pink skin, dark brown mustache, red Hawaiian shirt with big white hibiscus flowers, royal blue swim shorts, khaki bucket hat, yellow flip-flops. |
| `tourist_mom` | Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text. Warm tanned skin, brown hair, straw-yellow woven sun hat with a pink ribbon, black round sunglasses, turquoise swimsuit, sarong with pink and orange flowers, bright orange arm floaties. |
| `lifeguard` | Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text, no logos. Lightly tanned skin, bright red swim shorts, white tank top with one red horizontal stripe across the chest, red cap, silver whistle on a black cord, black sunglasses, white sunscreen on the nose. |
| `station_rusty` | Hand-painted stylized cartoon texture, warm sunny colors, soft shading, no text, no logos. Faded pale-blue painted wooden planks with chipped paint showing brown wood, rusty orange-brown corrugated metal roof, blank cream-colored sign board, dark wooden stilts, torn red flag. |
| `tower` | Hand-painted stylized cartoon texture, warm sunny colors, soft shading, no text. White painted wooden stilts and railing with red trim, red roof, warm brown wooden ramp slats, red and white life ring. |
| `lifering` | Hand-painted stylized cartoon texture, bright saturated colors, soft shading, no text. Bright red-orange foam ring, four clean white fabric bands, off-white rope. |
| `palm_tall` | Hand-painted stylized cartoon texture, bright tropical colors, soft shading. Light brown trunk with darker brown ring segments, vivid green fronds with lighter yellow-green edges, brown coconuts. |
| `rock_boulder` | Hand-painted stylized cartoon texture, soft shading. Warm grey-beige stone, lighter on top and darker at the bottom, a few white barnacles and soft green algae near the bottom. |

### 4.1 Texture prompt template for everything else
```
Hand-painted stylized cartoon texture with soft shading and bright saturated colors, no photo detail, no text, no logos. <list each part and its colour, e.g. "blue plastic body, white lid, grey handle">.
```

Batch 1 = T-01, T-02, P-01, S-01, S-04, I-01, N-01, N-03.

## 5. Characters (rig these)
Rules for rigging and ragdolls: full body, **A-pose**, arms not touching the body, hands open and empty, legs
apart, facing forward, symmetrical, no loose flowing cloth. Height in game: ~1.75 m (Claude rescales).
Target: **10k triangles**, 2048 texture.

| ID | File | Prompt |
|---|---|---|
| T-01 | `tourist_dad` | Chubby middle-aged male beach tourist, sunburnt pink skin, loud red Hawaiian shirt with white hibiscus flowers worn open, baggy blue swim shorts, flip-flops, bucket hat, bushy mustache, goofy friendly face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |
| T-02 | `tourist_mom` | Middle-aged female beach tourist, tanned skin, huge straw sun hat, big round sunglasses, turquoise one-piece swimsuit with a short floral sarong tied at the waist, orange inflatable arm floaties on both upper arms, cheerful face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |
| T-03 | `tourist_grandpa` | Skinny elderly male tourist, bald head with fluffy white side hair, huge white mustache, round glasses, white tank top, purple striped retro swim trunks, black knee-high socks with sandals, knobbly knees. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |
| T-04 | `tourist_gym` | Very muscular tanned bodybuilder tourist, small orange swim trunks, yellow sweatband on the forehead, spiky blond hair, gold chain, overconfident grin. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky exaggerated proportions, bright tropical colors, soft hand-painted texture. |
| T-05 | `tourist_selfie` | Young adult female tourist, sporty neon-pink swimsuit with white board shorts, heart-shaped sunglasses, big high ponytail, phone in a waterproof pouch on a lanyard around her neck, excited face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |
| T-06 | `tourist_suit` | Middle-aged office worker who went swimming in his suit: soaked grey business suit, loosened red tie, trousers rolled up to the knees, bare feet, slicked-back hair, worried confused face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright colors, soft hand-painted texture. |
| T-07 | `tourist_backpacker` | Lanky young adult backpacker, pale skin, messy brown hair, oversized yellow t-shirt with a pineapple print, green board shorts, white sneakers, earbuds, bored face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright tropical colors, soft hand-painted texture. |
| P-01 | `lifeguard` | Athletic cartoon lifeguard, red swim shorts, white tank top with one red horizontal stripe, red cap worn backwards, silver whistle on a cord around the neck, sunglasses pushed up on the forehead, white sunscreen stripe on the nose, confident face. Full body, A-pose, arms angled down and away from the body, legs slightly apart, hands open, facing forward. Stylized cartoon game character, chunky proportions, bright colors, soft hand-painted texture. |

## 6. Station and structures
Target: **6-10k triangles**, 2048 texture. Sizes are the in-game footprint (Claude rescales).

| ID | File | Size | Prompt |
|---|---|---|---|
| S-01 | `station_rusty` | 5 × 4 m | Tiny run-down wooden lifeguard shack on short stilts, weathered pale-blue planks with gaps, dented rusty corrugated metal roof, one open counter window with a sagging awning, a blank wooden sign board above the window, short wooden steps, a bent flagpole with a torn red flag. Whole building, 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture, simple details. |
| S-02 | `station_good` | 6 × 5 m | Neat wooden lifeguard station on stilts, freshly painted white planks with red trim, red metal roof, wide counter window with open shutters, small front deck with railing and steps, two life rings hanging on the wall, flagpole with a red flag. Whole building, 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture, simple details. |
| S-03 | `station_hq` | 9 × 7 m | Proud two-level beach lifeguard headquarters, white and red wooden building with a glass lookout room on top, wraparound deck with railing, surfboard rack, rescue boards leaning on the wall, flags on the roof. Whole building, 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture, simple details. |
| S-04 | `tower` | 2.2 m platform, 3 m high | Classic beach lifeguard watchtower: small wooden cabin with a red roof on a platform raised on four tall wooden stilts with cross bracing, a slatted ramp leading up to the platform, red and white paint, a life ring hanging on the railing. Whole structure, 3/4 view. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |
| S-05 | `dock_end` | 2.4 × 3 m | End section of a wooden pier: square deck of weathered planks on thick round posts, a metal swim ladder going down on one side, two mooring bollards and a coiled rope. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture. |
| S-06 | `signboard` | 1.5 m wide | Wooden notice board on two posts with a large blank white panel, simple wooden frame and a small slanted roof on top. Stylized cartoon game asset, chunky rounded shapes, warm wood colors, soft hand-painted texture. |

## 7. Beach and nature
Target: **1.5-4k triangles** (palms 4k), 1024 texture.

| ID | File | Size | Prompt |
|---|---|---|---|
| N-01 | `palm_tall` | 7 m | Tall stylized palm tree with a gently curved segmented trunk, a lush crown of seven broad green fronds and a cluster of coconuts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |
| N-02 | `palm_short` | 4 m | Short leaning palm tree with a thick curved trunk, drooping green fronds and three coconuts. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |
| N-03 | `rock_boulder` | 3 m | Large smooth rounded coastal boulder, grey-beige granite with soft cracks, a few barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. |
| N-04 | `rock_cluster` | 4 m | Cluster of three smooth coastal rocks of different sizes fused together, grey-beige granite, barnacles and green algae near the bottom. Stylized cartoon game asset, chunky rounded shapes, soft hand-painted texture. |
| N-05 | `umbrella` | 2.2 m | Open beach umbrella with red and white stripes and a scalloped edge on a wooden pole stuck in the ground. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| N-06 | `sunbed` | 1.9 m | Wooden beach sun lounger with a slatted frame and a thick turquoise cushion. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |
| N-07 | `swim_buoy` | 0.5 m | Floating swim-zone marker buoy: round yellow plastic float with a white band and a small loop on top. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| N-08 | `sandcastle` | 0.8 m | Lumpy sandcastle with four towers, a little moat wall and a seashell flag on top. Stylized cartoon game asset, chunky rounded shapes, warm sand colors, soft hand-painted texture. |
| N-09 | `tiki_bar` | 4 × 3 m | Small tiki beach bar with a thatched straw roof, bamboo counter, three bar stools, a blank chalkboard and hanging string lights. Stylized cartoon game asset, chunky rounded shapes, bright tropical colors, soft hand-painted texture. |

## 8. Items, tools, vehicles
Items: **0.8-2k triangles**, 1024 texture. Vehicles: **8-12k**, 2048.

| ID | File | Size | Prompt |
|---|---|---|---|
| I-01 | `lifering` | 0.7 m | Classic life ring buoy: thick red-orange foam ring with four white fabric bands and a grab rope looped around the outside. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| I-02 | `rescue_tube` | 0.7 m | Red hard-plastic rescue can (torpedo buoy), capsule shaped with molded handles along both sides, a yellow strap and rope attached at one end. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| I-03 | `crate` | 0.6 m | Wooden supply crate made of planks with dark metal corner brackets and a painted blue stripe, lid nailed shut. Stylized cartoon game asset, chunky rounded shapes, warm colors, soft hand-painted texture. |
| I-04 | `cooler` | 0.55 m | Picnic cooler box, blue plastic body, white lid, grey carrying handle, small drain plug. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| I-05 | `firstaid` | 0.4 m | First aid case, green hard plastic box with a white cross symbol on the lid, two latches and a carry handle. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| I-06 | `rescue_board` | 2.8 m | Long foam rescue paddleboard, yellow with a red stripe down the middle, rounded nose, rope handles along both sides. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| I-07 | `buoy_launcher` | 1.1 m | Chunky cartoon rescue launcher held with two hands: fat orange compressed-air cannon barrel with a red and white life buoy loaded at the muzzle, pressure gauge on top, big trigger grip and shoulder stock, metal and orange plastic parts. Side view. Stylized cartoon game asset, chunky rounded shapes, bright colors, soft hand-painted texture. |
| V-01 | `jetski` | 3 m | Rescue jet ski with a red and white hull, two seats in tandem, handlebars, and a flat rescue sled board attached at the back. Side view. Stylized cartoon game vehicle, chunky rounded proportions, bright colors, soft hand-painted texture. |
| V-02 | `rescue_rib` | 4.5 m | Small inflatable rescue boat with orange tubes, grey floor, a steering console and an outboard motor at the back. Side view. Stylized cartoon game vehicle, chunky rounded proportions, bright colors, soft hand-painted texture. |
| V-03 | `rival_heli` | 9 m | Smug cartoon helicopter of a rival rescue company, glossy purple and gold paint, sleek pointed nose, landing skids, big rotor. Side view. Stylized cartoon game vehicle, chunky rounded proportions, soft hand-painted texture. |

Later (creatures): shark `Cartoon shark, blue-grey body, white belly, big toothy grin with goofy eyes, side view` and the
giant octopus `Giant cartoon octopus, purple-pink skin with pale suckers, big expressive eyes with thick eyebrows,
holding a striped beach umbrella in one tentacle`. Add the usual style ending to both.

## 9. Hand-off: what Claude does with the files
* Imports from `ArtSource/Meshy/` into `Assets/_Game/Art/`, fixes scale and pivot, converts materials to URP,
  adds simple physics colliders (never the mesh itself), and swaps them in for the primitives in the scene builder.
* Tourists: maps the rig to the ragdoll (hips/spine to the torso body, upper arms and thighs to the limb bodies;
  forearms and shins follow), keeps the synced torso and local limbs exactly as now.
* Players get a per-player tint (like the coloured capsules now).
* Keeps the grey-box version as a fallback until all of a set exists, so the game never breaks mid-way.

## 10. Rules (legal and store)
* **Licence** (meshy.ai/pricing, checked 2026-09-27): Free = 100 credits/month (up to 10 assets and 10 downloads,
  downloads use Meshy 6 Lite), no Remesh, outputs **CC BY 4.0** (commercial use allowed, but Meshy must be credited,
  and they're public). Pro (~$20/month, 1,000 credits, up to 100 assets) = private and customer-owned, Remesh,
  retries, unlimited downloads. The licence follows the plan you were on when you generated the asset.
  Plan: test the style on Free with Batch 1, then do the final set in one month of Pro. If any Free-plan model
  ships, add "3D models made with Meshy (meshy.ai), CC BY 4.0" to the credits.
* **Steam:** the store page must disclose AI-generated content (Steam's content survey asks). Keep a list of which
  assets came from Meshy (this file is that list).
* **No Red Cross emblem** (red cross on white is legally protected). Use a white cross on green, or a heart.
* No real brands, logos or real people's faces in prompts.
