# Blender face and gun polish

## Tripo characters: texture and face (2026-10-19)

The four characters reduced from Sandro's two-million-triangle Tripo models (`pirate_king`, `tourist_blacktee`,
`hotel_attendant_a`, `hotel_attendant_b`) had been given hundreds of small Smart UV Project islands and a 2048 colour
bake: soft faces cut through the nose and eyes, black gutters creeping into the island edges, and none of the source's
fine shape (hair strands, lids, lips, cloth folds). Their shape, rig and weights are unchanged; what's new:

- **UVs**: islands cut on a smoothed copy (no more pieces round every hair groove; the face front one piece), then
  flattened on the real surface with Minimum Stretch (no flips); the face front gets 3x and the rest of the head 1.8x
  the body's texel density. Angle-based flattening folded islands (shirt white on a collar ribbon, the king's pupil
  smeared) and keeping the smoothed copy's layout starved the eyes of texels: don't.
- **Colour and a normal map** baked from the full-detail source at 4096 (3 cages, each texel keeping the outermost
  hit that faces it within 7 mm: no dark cleavage or leg lines from a big cage, no shirt on the collar ribbon from a
  small one; texels no ray got right take the colour of the nearest full-detail surface), then taken down to 2048 with
  the gutters filled. The normal map is new for these bodies: `MeshyCharacters` puts
  `Art/Characters/Textures/<model>_baked_normal.png` on the body material when it exists.
- **Faces stay smooth**: the normal map is flat over the face (skin, brows, eyes, lips; only near-black hair, beards
  and sunglasses keep their shape). With it, the sculpted brows came out as straps with a lit rim and the nostrils as
  rings in the game's light. The attendants' small noses also get `--nostrils 0.9`: the dark inside of the source's
  nostril holes, painted on the reduced nose's shallow dent, looked like a nose ring from the side.

Reproduce (originals in `Originals/`, the source models in `ArtSource/Tripo/Latest/`: `pirate_king_red`,
`tourist_blacktee`, `attendant_1` for A, `attendant_2` for B; the decoded 65 MB copies are not in git, the raw
downloads are in `raw/`):

    blender -b --factory-startup -P ArtSource/Tools/polish_tripo_character.py -- ArtSource/Polish/Originals/<model>.glb ArtSource/Tripo/Latest/<source>.glb <out.glb> <bake.png> [--review <prefix>]
    python ArtSource/Tools/polish_character_texture.py <out.glb> <bake.png> Assets/_Game/Art/Characters/Textures/<model>_color3.jpg Assets/_Game/Art/Characters/Textures/<model>_baked_normal.png

then copy `<out.glb>` over `Assets/_Game/Art/Characters/<model>.glb`, re-bake with
`MeshyCharacters.BakeOnlyBatch -pdd-bodies <models>` and, for the attendants, `LegWeightFix.FixWebBatch` then
`FixStretchBatch` and `FixStretchBatch -ratio 2 -min 0.03`. `ArtSource/Tools/polish_review.py` renders a GLB with its
textures (front, back, torso, face) for checking before Unity.

## The men: a normal map on their own UVs (2026-10-19)

The dad (`tourist_buddy`, and his ten look-alikes), the thief (`robber`), the hotel director and the three pirates
(`pirate_fat`, `pirate_marksman`, `pirate_quartermaster`) keep their UVs and painted colour: their faces are fitted
by vertex (`MeshyCharacters` BuildFace) and the look-alikes are repaints of the same UVs, so a re-UV would break both.
They get a normal map baked from their full-detail source onto the existing UVs (cloth folds, buttons, belts, hair,
beards; the face kept flat), and their own colour with its gutters filled and the front of the head lightly sharpened.

    blender -b --factory-startup -P ArtSource/Tools/polish_tripo_character.py -- Assets/_Game/Art/Characters/<model>.glb <source>.glb <unused.glb> <bake.png> --keep-uvs 1 --turn <-90 for Tripo, 0 for Meshy>
    python ArtSource/Tools/polish_character_texture.py - <bake.png> Assets/_Game/Art/Characters/Textures/<model>_color.jpg Assets/_Game/Art/Characters/Textures/<model>_baked_normal.png --colour-from ArtSource/Polish/Originals/Textures/<model>_color.jpg --coverage-glb Assets/_Game/Art/Characters/<model>.glb --sharpen 0.35 --normal-size 2048 [--size 4096 for the dad]

Sources: pirates and director in `ArtSource/Tripo/Pirates/` and `ArtSource/Tripo/Island2/`, the dad
`ArtSource/Meshy/raw/tourist_buddy.glb`, the thief `ArtSource/Meshy/raw/robber_apose.glb`. Always pass
`--coverage-glb`: Meshy's thief has 770 triangles (a hip, 5% of him) whose UVs are squashed to a point; they show the
one texel under it, which a gutter fill painted dark (a black wedge down his hip). The coverage keeps those texels and
the normal map stays flat on them. The dad's colour imports (`tourist_buddy*_color.jpg.meta`) must stay at 4096 or the
bake drops his compressed 4096 copies.

## Tourists and guns

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
