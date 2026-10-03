# Meshy integration — 27 September 2026

## Imported models

The five Desktop GLBs are preserved under `ArtSource/Meshy/` and imported under
`Assets/_Game/Art/Meshy/`. Unity glTFast 6.19.0 imports their embedded textures and materials.
Importer documentation: https://github.com/Unity-Technologies/com.unity.cloud.gltfast/blob/main/Packages/com.unity.cloud.gltfast/Documentation~/ImportEditor.md

| Project asset | Original Desktop filename | Use |
|---|---|---|
| tourist_dad | Meshy_AI_Chubby_middle_aged_ma_0927161230_texture.glb | Rescue tourist |
| lifeguard | Meshy_AI_Athletic_cartoon_life_0927161512_texture.glb | Player appearance |
| tower | Meshy_AI_Classic_beach_lifegua_0927161816_texture.glb | Watchtower |
| palm_tall | Meshy_AI_Tall_stylized_palm_tr_0927162514_texture.glb | Six beach palms |
| station_rusty | Meshy_AI_Tiny_run_down_wooden__0927162045_texture.glb | Lifeguard shack |

## Update, 27 September 2026 (evening)

* **Characters are no longer Meshy models.** Both GLBs were static sculptures (the lifeguard with a raised fist and
  a hand on the hip), so nothing could move their arms. Lifeguards and tourists now use the code-built avatars in
  `Assets/_Game/Avatar/` (customizable, procedurally animated). The GLBs stay in the repo for a future rigged
  version: regenerate in an A-pose and use Meshy's auto-rig (or Mixamo), then map its bones to `AvatarRig.Bone`.
* **Decimated in Blender** (`ArtSource/Tools/decimate_glb.py`): palm 130k -> 14k, tower 229k -> 24k,
  shack 289k -> 26k triangles. The originals stay in `ArtSource/Meshy/`.
* **Walk-in buildings.** The shack is built at 1.45x and the tower at 1.2x so a lifeguard fits through the doors.
  `MeshyArt.CutOpening` removes the painted door's triangles (saved to `Assets/_Game/Art/Generated/`), and the scene
  builder puts a hinged, networked `Door` with a frame in the gap. The GLB materials are double-sided, so the inside
  walls show the planks. Measurements come from orthographic renders (`ReviewCapture`, `ortho` shots).
* **Textures are still 4K and uncompressed in builds** (about 50 MB per GLB); downscale/compress before release.

## Rebuilding

The existing **PLEASE DON'T DROWN > Rebuild Game scene** command includes these assets.
`MeshyArt` normalizes height and foot pivots and installs simple collision shapes.
The Desktop files are no longer needed to open or rebuild this project.
If an individual GLB is absent, its original primitive character/structure remains available;
palms are omitted. A present GLB that fails to import stops the build with a useful error.

## Characters

Neither character GLB contains a skeleton or animations. They arrived in posed stances,
not an A-pose. The tourist's supplied shape is attached to the existing torso visual,
including CPR compression. Its original limb proxies stay invisible and continue their
physics simulation. CPR, flotation, pickup, and network authority still use the existing
gameplay components. Limb animation requires a rigged export: automatic weights on this
posed, connected shirt/arm geometry caused visible stretching during review and were removed.
Texture colors are retained instead of tinting the
whole tourist texture randomly.

The lifeguard retains the supplied static pose. Remote facing follows the synchronized
head; crouching uses the existing body scaling. The local player sees only its shadow.
Name tags retain per-player identity colors. Walk/swim animation needs a rigged export.

## Geometry and colliders

Original source detail is preserved (approximately 75k lifeguard, 91k tourist, 130k palm,
229k tower, and 289k shack triangles). These are substantially above the art plan's budgets;
retopology/LODs remain a production optimization step. Six palms share the imported mesh.
Physics uses simple proxies: tourist/player capsules, palm trunks, shack volume, and
watchtower stilts, deck, cabin and stair ramp. The cabin is a solid prop; its deck is accessible.

## Visual review

Run `PleaseDontDrown.Editor.MeshyArtReview.CaptureBatch` in Unity batch mode to render
close-ups into `Screenshots/Meshy/` (ignored by Git). It opens the saved scene and places
review characters temporarily, without saving those changes.

## Rigged characters (28 September 2026): Sandy

Characters now go through one pipeline and end up on the game's own procedural skeleton, so every pose, gesture,
sitting, CPR pose and the ragdoll work on them unchanged.

1. **Meshy (Free plan):** an A-pose picture (image AI) → Image to 3D. No rig needed from Meshy.
   The raw download is kept in `ArtSource/Meshy/raw/` (Sandy: `sandy_guide_and_boss.glb`, two figures in one file).
2. **Blender** (`ArtSource/Tools/prepare_character.py`, runs headless in ~10 s):
   ```
   blender -b --factory-startup -P ArtSource/Tools/prepare_character.py -- ArtSource/Meshy/raw/<raw>.glb Assets/_Game/Art/Characters/<file>.glb --pick left|right|only --height 1.74 [--bust 1] [--preview <prefix>]
   ```
   Keeps one figure (drops other figures and floating text), stands it on the origin, finds the joints from the
   front silhouette (armpits, arm axes, leg split, neck), builds bones named like `AvatarRig.Bone`, skins with
   automatic weights (4 per vertex), decimates to 14k triangles and exports a skinned GLB. `--bust 1` (women) adds
   `BustL`/`BustR` spring bones weighted to the chest; it warns when the chest is flat (the jiggle would hardly show).
   `--preview` renders the joints over the model: check them before baking.
3. **Unity** (`Editor/MeshyCharacters.cs`, run by the scene build): moves the rig's bones to the model's joints,
   re-points the skin weights at the rig's bone order and binds the A-pose limbs so the rig's rest pose (arms and legs
   straight down) turns them down. Output: `Assets/_Game/Avatar/Bodies/<file>.asset` (+ mesh) and the library
   `Resources/AvatarBodies.asset`.
4. **Use it:** `AvatarLook.Body` = the body's id (`AvatarLook.Bodies`, packed into the synced look, 0 = code-built).
   Sandy: `StoryDirector.SandyLook.Body = Bodies.Sandy`; `Bodies.SandyBoss` is baked and ready for the boss scenes.
   Tourists (28 Sep): red bikini, sporty, purple bikini (all `--bust 1`) and the sunburnt dad; `AvatarLook.RandomTourist`
   gives 80% of women one of the three and 35% of men the dad, so most of the beach, the drowning tourists and CPR
   use them. The jiggle now has real chest shapes to move.

Limits: the face is Meshy's painted one (no talking mouth or blinking yet), fingers don't bend (the hand is one
piece), and textures are whatever Meshy painted (Sandy has pale streaks down the sides of her trousers).
Review shots: `ReviewCapture` looks `sandy`, `sandyboss`, `sandycode` (the old code-built Sandy), pose `sitchair`.

## Props that keep their painted texture (2 October 2026): backpack, umbrellas

`ArtSource/Tools/polish_prop.py` (Blender). The Meshy mesh (150-250k triangles, its texture cut into hundreds of
scraps) is only the source: a cleaned, decimated copy gets one tidy UV layout and the colour (and a normal map) is
baked onto it from the source, so the paint survives any triangle count. Decimating the Meshy mesh directly
shreds its texture.

```
blender -b --factory-startup -P ArtSource/Tools/polish_prop.py -- ArtSource/Meshy/raw/robber_backpack_raw.glb Assets/_Game/Art/Meshy/robber_backpack.glb --height 0.46 --turn 180 --strip-thin 0.035 --flat-back 0.16 --tris 3500 --flatten 0.2 --preview Screenshots/Review/Meshy/backpack
blender -b --factory-startup -P ArtSource/Tools/polish_prop.py -- ArtSource/Meshy/raw/umbrella_raw.glb Assets/_Game/Art/Meshy/umbrella_red.glb --height 2.35 --tris 5000 --normals 0 --umbrella 0.86,0.2,0.16 --preview Screenshots/Review/Meshy/umbrella_red
```

* `--strip-thin` removes loose ribbons (the backpack's dangling shoulder straps and handle): the model is voxelised,
  worn down until thin parts vanish and grown back; what stands clear of that bulk is deleted, the holes are closed,
  the scars ironed and repainted in the cloth's colour. `--flat-back` then slices the strap side flat (it sits
  against the wearer's back).
* `--flatten` evens out the big light and dark smears Meshy paints in (fake highlights).
* `--umbrella r,g,b` repaints the canopy's panels alternately that colour and white. The ribs are found from the
  shape (12 panels on this model), so the stripes follow them. Teal `0.13,0.62,0.66`, yellow `0.98,0.78,0.2`.
* `ArtSource/Tools/preview_glb.py` renders any GLB (raw or prepared) from four sides and prints its size and
  triangle count: look at a new download with it first. `sheet.py` joins pictures side by side.

In the game (`Editor/GameSceneBuilder.MeshyProps.cs`): each gets a plain matte URP Lit material with its colour
texture. The backpack is saved as `Resources/RobberBackpack.prefab` and worn by the thief (`Story/RobberBag.cs`,
code-built bag as the fallback). The umbrellas replace the grey-box ones beside every other beach towel, three
colours in turn, each with a thin collider on the pole only.

**The robber (3 October 2026)**: the download (`ArtSource/Meshy/raw/robber_raw.glb`) stands with his hands in his
pockets, so `ArtSource/Tools/robber_arms.py` (Blender) frees them first: it welds glTF's split UV seams, cuts each hand
off just past the wrist together with the bit of pocket it was buried in, cuts the forearm loose where it rests on the
trousers, swings both arms down into an A-pose, closes every hole in the colour of its side (skin or trouser) and
gives him mitten hands painted with his own skin. Joint positions in the script were read off
`ArtSource/Tools/ortho_grid.py` renders (orthographic front and both sides on a 5 cm grid).

```
blender -b --factory-startup -P ArtSource/Tools/robber_arms.py -- ArtSource/Meshy/raw/robber_raw.glb ArtSource/Meshy/raw/robber_apose.glb
blender -b --factory-startup -P ArtSource/Tools/prepare_character.py -- ArtSource/Meshy/raw/robber_apose.glb Assets/_Game/Art/Characters/robber.glb --pick only --height 1.78
```

He is body `AvatarLook.Bodies.Robber` (`StoryDirector.RobberLook`). He keeps his painted face (`MeshyCharacters`
skips the eyelids and mouth: his eyes are behind sunglasses) and wears no code-built beanie or face bandana (he has
his own headband).