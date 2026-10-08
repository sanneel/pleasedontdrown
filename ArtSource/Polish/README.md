# Blender face and gun polish

Open `Tourists_and_Guns.blend` in Blender. Its scene selector contains the four base tourists and five static gun bodies. Textures are packed. Each asset also has a separate `*_polished.blend` file. The complete guns, including moving parts and attachments, remain assembled in Unity.

The updated game GLBs are in `Assets/_Game/Art/Characters` and `Assets/_Game/Art/Weapons`. Original GLBs are backed up in `Originals`. Existing `ArtSource/Meshy/tourist_variants.blend` was preserved.

## Changes

- All four base tourists and 40 variants: mild smoothing of the front of the head, with displacement capped at 1.8 mm, and continuous head normals across texture seams. Original facial artwork, UVs, triangle counts and skeletons are retained.
- Five guns: join coincident mesh vertices, add small two-segment bevels, and author weighted surface normals. Source materials distinguish metal, wood, polymer and rubber. The existing Unity weapon shader and detail meshes remain in use.
- Unity's tourist bodies were rebaked, and its gun surface meshes refreshed. Blender normals survive import except on the sniper body, where the existing scope-removal operation requires recalculating them.

Static gun triangle counts: pistol 34,286; rifle 28,726; shotgun 6,148; SMG 9,436; sniper 21,526. These are higher than the original source models because the edge bevels add geometry. No performance benchmark or player build was run.

## Verification

All 49 GLBs were reopened in Blender 5.2. The verifier checked finite vertex positions, normalized skin weights, bone names and transforms, tourist triangle counts and texture resolutions, and a 40,000-triangle per-source budget. Maximum bone-matrix round-trip difference was 0.00003225, within the 0.0001 numerical tolerance.

Unity 6000.3.25f1 completed compilation and import successfully. Import checks confirmed that gun item/weapon serialization and collider counts did not change. Four tourist portraits and assembled gun/attachment views were rendered in Unity and reviewed. This is visual and asset validation, not a full gameplay or animation test.

Local previews and verification reports are in `Screenshots/Review/BlenderPolish`; Unity portraits are `Screenshots/Review/blender_*_face.jpg`, and assembled guns are in `Screenshots/GunPolish/After`. Screenshot directories are ignored by Git.

## Reproduce

Run Blender in background mode with `ArtSource/Tools/blender_polish.py -- --apply` for base models and guns, then `-- --apply --variants` for the 40 variants. The script always starts from the preserved original, so repeating the pass does not accumulate smoothing. `-- --inspect` renders the original baselines.

Run `ArtSource/Tools/verify_blender_polish.py` in Blender to verify all exports and regenerate the combined review file. In Unity, run **PLEASE DON'T DROWN / Import Blender face and gun polish** to refresh the baked game assets.
