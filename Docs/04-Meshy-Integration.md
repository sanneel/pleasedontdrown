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
