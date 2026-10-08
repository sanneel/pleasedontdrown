# Coloured Tripo beach bar

Source supplied on 8 October 2026: `C:/Users/User/Desktop/thatched hut bar 3d model.glb`.
An unchanged copy is preserved at `ArtSource/Resort/TripoBar/source.glb`.

The source contained one white material, no texture images, no UV coordinates and 1,811,998 triangles. Blender assigns material regions to the existing geometry: golden thatch, timber deck and framing, dark countertop, ivory/turquoise counter, turquoise stools, teal/coral lounge chairs and brass/warm lamps.

## Deliverables

- Editable master: `ArtSource/Resort/tripo_beach_bar_coloured.blend`.
- Game models: `Assets/_Game/Art/Props/resort_tripo_beach_bar_lod0.glb`, `lod1.glb`, `lod2.glb` (same full filename prefix).
- Geometry is approximately 26m wide, 16m deep and 7.2m high. Nonuniform scaling moderates the source's exaggerated roof height and keeps the countertop about 1.3m above the deck.
- Blender preview: `Screenshots/Review/ResortBlender/tripo_bar_coloured.png`.
- Rebuild tool: `ArtSource/Tools/colour_tripo_bar.py`.
- Geometry/material report: `Logs/tripo-bar-colour.json`.

## Unity

The GLBs carry their colours directly; no texture downloads are required. `GameSceneBuilder.PropPaint` also supports the bar's brass, countertop and lantern materials when building URP props.

`PLEASE DON'T DROWN → Import coloured Tripo beach bar` creates `Assets/_Game/Art/Props/TripoBeachBar.prefab` with three distance levels and shared URP materials. A one-shot request is queued at `Logs/tripo-bar-colour-import-request.txt`; refresh Unity to import the new script. The completion report is `Logs/tripo-bar-colour-import-result.txt`.

The prefab is a visual asset. This import does not replace objects in the user's active scene or attach bartender interactions. Scene placement, collision matching and runtime checks remain separate work.
