# Imported model polish review — 2026-10-07

## Coverage and evidence

`GLB-Polish-Audit.json` contains the individual inventory, hashes, counts, bounds, image sizes and structural findings for every GLB under `Assets/_Game/Art` and `ArtSource`. Source copies are distinguished by path and exact-copy hashes. Regenerate with `python ArtSource/Tools/audit_glbs.py`.

All 111 imported models were rendered individually to `Screenshots/Review/GLB`, with eight contact sheets reviewed for missing surfaces, texture failures and obvious shape defects. This includes the beer bottle added concurrently. The Blender views use studio/workbench shading; they do not prove Unity material appearance, animated deformation, collisions or in-game scale. The final structural audit covered 195 files (111 imports and 84 source files), with zero findings for the implemented checks.

## Repairs

- **Defibrillator:** removed 40 zero-area triangles by repairing its generator and regenerating that model only. Its post-fix render retains the buttons, paddles, cable and case.
- **Basketball:** face-based seam colouring made stair-step edges visible even in the preview. Replaced it with continuous narrow seam geometry in `model_props.py`; regenerated only the basketball. Its visual diameter changes by approximately 3 mm, with gameplay collision code untouched.
- **Flamingo:** set roughness to 0.06 and metallic to zero for a tight white sun glint on coloured vinyl. Rebuilt from the original source, welding disconnected positions before reducing it to approximately 18,000 triangles (previously 9,000). Smoothed vertex normals, removed the baked faceted normal map and normalized the pink paint in its atlas, retaining eye/beak colours. The cleanup is reproducible with `ArtSource/Tools/polish_flamingo_surface.py`. The material-only helper and scene-builder fallback also retain the glossy setting. Small sculpted folds and the inflatable's joining seam remain; they are part of the source shape.

The other reviewed previews did not reveal a comparable obvious defect requiring a blanket asset rewrite. Existing low-poly silhouettes and authored character differences were retained.

## Practical limits

This is a structural audit and rendered visual pass, not certification that every model is flawless from every angle. Rig animation, all collision shapes, hidden backs/undersides, final scale in every scene, and multiplayer interactions require in-game checks. A GLB does not itself contain this project's Unity colliders. Do not mark those checks complete based on this audit.

Unity renders of the flamingo under four sun/view angles are saved in `Screenshots/Review/Flamingo`. Speech bubble previews are saved in `Screenshots/Review/SpeechBubbles.png`.

## Flamingo rider pose

Moved the hip anchor toward the rear edge of the opening and aligned the seated hips independently of avatar standing height. The flamingo uses a narrow seated leg pose with bent knees and feet hanging inside the opening. Open palms rest on measured mesh surfaces beside the hips, replacing the distant neck grips. Other vehicles keep their existing seated poses. The scene builder retains these settings on regeneration.

Verified the default player through the real avatar animator in four Unity renders at `Screenshots/Review/FlamingoRider/pose-0.png` through `pose-3.png`. The saved-scene Windows build succeeded (`Logs/flamingo-rider-build.log`). These renders check the settled third-person pose; they do not verify moving multiplayer riders or every customized body shape.
