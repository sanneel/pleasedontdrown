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

The prefab is also placed in Game under `Environment/Hotel/ResortBeachLife/LargeBeachBar/ColouredTripoBar`. It replaces the earlier placeholder pavilion. The surrounding bar root adds deck, U-shaped counter, chair and post collisions; bartender interactions; four serving spots; and beer/coconut racks. The deck is about 26cm high and serving spots sit just above the counter. The rear staff aisle stays open.

`Finish one hotel and open reception` rebuilds this beach setup and checks the customer approaches, staff aisle and bartender interaction distance. The live scene update reports PASS in `Logs/hotel-finish-result.txt`. A visual Unity review is saved at `Screenshots/Review/Resort/beach-club.png`.
