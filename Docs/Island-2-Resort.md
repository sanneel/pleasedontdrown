# Island two: Grand Coral Island Resort

## Layout

- Island footprint: approximately **360 × 260 metres**, expanded from 120 × 60 metres.
- Arrival shore stays near z=-220; additional land extends west, east and south.
- **One hotel is retained.** The generator now places only the 40m main building. The single-hotel polish updates the remaining live building in place, preserving its position, rotation and scale.
- **Reception is the only accessible building interior.** Guest wings are solid exterior shells.
- Existing reception desk, receptionist, first-aid shelf and patient point remain in the lobby for chapter two.
- Two decorative pool terraces with loungers; southern gardens with palms, paths and six open shade pavilions.
- Hotel colours use the imported ivory/turquoise facade textures with the existing palm art. A dedicated material softens the imported normal map and gloss.

The pools currently provide scenery; they do not have a separate swimming simulation.

## Integration

`GameSceneBuilder.Resort.cs` generates the resort during a full scene build, or updates the existing hotel with **PLEASE DON'T DROWN → Expand island two resort** in edit mode.

The update preserves the rest of the scene. It regenerates the terrain mesh, collider and seabed sampling grid together, and rebakes the island-two navigation asset while retaining its GUID. Dock, arrival and story anchors stay in place. The parked pirate boat moves offshore to x=-210 to avoid the expanded western land.

The original procedural hotel remains a fallback if the Tripo game assets are unavailable. Decorative pool/garden geometry is combined by material. Guest buildings use simple collision shells; the main shell has a real opening around the playable reception.

## Imported hotel

- Original: `Assets/TripoModels/apartment_building_3d_model/apartment_building_3d_model.fbx`, preserved intact.
- Original triangle count: **1,784,804**.
- Game copies: approximately **214,175 / 62,467 / 17,847** triangles per building across three distance levels. The nearest copy removes about 88% of the original triangles.
- `ArtSource/Tools/prepare_tripo_hotel.py` creates geometry-only GLBs while retaining UVs and the original Unity texture references.
- Main-building copies additionally remove triangles intersecting the existing 24 × 12m reception volume. This prevents the imported shell from covering the lobby interior.
- One hotel `LODGroup` switches geometry at distance. The original dense FBX is not used as a scene renderer.
- `GameSceneBuilder.ResortImported.cs` places the buildings and their collision shells.

## Review

- Entrance ray check verifies the lobby doorway remains unobstructed.
- Reception desk, hotel door and patient anchors are checked before saving.
- Newly added west/east/south grounds are checked above sea level.
- Terrain collision and render mesh references are checked for agreement.
- Island-two navigation was rebaked after replacing the hotel buildings.
- Unity preview captures: `Screenshots/Review/Resort/arrival.png`, `overview.png`, `gardens.png`, `reception.png`.
- Previews temporarily generate the game's ocean geometry/shader. Aerial review reduces fog to show the layout; this does not change the game's fog.

These checks do not constitute a full chapter-two or multiplayer playthrough.

The earlier imported-hotel review and Windows build passed. That result predates the following single-hotel beach update.

## Single hotel and richer beach update

- Blender master: `ArtSource/Resort/hotel_polished.blend`. UVs and the original facade textures are retained. Surface smoothing is limited to about 4cm at the main hotel's world scale, with protected silhouettes and architectural edges; weighted normals improve shading.
- New Blender-authored props: a 26 × 16m open beach bar, four cabanas, paired loungers, striped parasols, flower planters and clean reception trim. Editable masters are in `ArtSource/Resort`; game GLBs are in `Assets/_Game/Art/Props`.
- Beach bar integration reuses the existing bartender, beer/coconut racks and network scene registration. Promenades, palms, warm lanterns and garden seating enrich the grounds.
- **PLEASE DON'T DROWN → Polish single hotel and resort beach** works on the active Game scene. It first requires exactly one active hotel, backs up the live scene, removes obsolete wing collision shells, checks access, rebakes island-two navigation and builds the Windows player. It does not recreate deleted hotels.
- A one-shot request at `Logs/resort-single-polish-request.txt` queues this update for the open editor. Completion is recorded in `Logs/resort-single-polish-result.txt`. Until that file reports PASS, the scene update and current Windows build remain unverified.
- Blender renders: `Screenshots/Review/ResortBlender/hotel_polished.png`, `resort_beach_bar.png`, `resort_cabana.png`.
- Tripo replacement-bar prompt: `Docs/Tripo-Coral-Beach-Bar-Prompt.md`; use the hotel screenshot as its style reference.
