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

## Hotel + story only, raised island, polished Tripo hotel (9 October 2026)

- **Decor removed.** Pools, gardens, pavilions, cabanas, loungers, parasols, planters, the beach bar and the resort
  palms are gone. Kept: the hotel, reception (shop desk, receptionist), infirmary bed, first-aid shelf, dock, towels and
  beach crowd, story points and the dev travel pad.
- **Island raised.** Island 2's ground rises from the waterline to a **1.5 m plateau** over ~10 m of beach
  (`Island2Ground` in `GameSceneBuilder.HotelIsland.cs`, used by `BeachHeight`). Everything standing on the island was
  lifted by the rise under it; things at sea did not move. Re-running the update does not lift twice.
- **Hotel = the user's Tripo model** (`ArtSource/Resort/tripo_hotel_upload.glb`, same model as the old FBX), polished by
  `ArtSource/Tools/build_realistic_hotel.py`: every face is classified from the smeared AI texture into cream wall,
  sandstone trim, white mouldings, teal surrounds, glass or roof and gets a clean physical material (`HotelReal_*`,
  reflective tinted glass); wavy walls, slabs and panes are snapped onto true planes; flat faces get flat shading
  normals. LODs `resort_hotel_real_lod0..2.glb`: 243k / 63k / 60k triangles. Blender master: `hotel_realistic.blend`.
  Review renders: `Screenshots/Review/HotelRealistic/`.
- Collision: the LOD1 mesh plus solid cores inside the guest floors; the 24 x 12 m lobby stays open, entered through
  the porte-cochère (paved approach and steps). The lobby box is painted in the facade's cream render.
- Apply: **PLEASE DON'T DROWN → Island two: polished hotel, raised island, no decor**, or batch
  `GameSceneBuilder.RealisticHotelIslandBatch` (add `-pddBuild` to build the Windows player). Result:
  `Logs/hotel-island-result.txt`; pictures: `Screenshots/Review/HotelIsland/`.

### Full exterior polish and gold lobby (9 October 2026, later)

The user found the first polish still looked like an AI 3D model (jagged windows, rails, slab edges). The Blender
pipeline now rebuilds every detail as clean geometry where the model has it (`ArtSource/Tools/hotel_windows.py`,
`hotel_exterior.py`, driven by `build_realistic_hotel.py`; `-- preview cached` re-renders in ~30 s from
`Logs/hotel_realistic_merged.blend`):

- **Windows** (≈420): found from the glass, snapped to each facade's grid, AI pane and reveal removed; clean glass
  (dark / lighter tint / drawn curtains), white frame, mullions, transom, sill, reveal lining, teal surround where the
  facade has one.
- **Balconies** (≈85): found from their teal/metal rail caps or from the slab in front of a window; the AI slab,
  balusters, posts and side rails are removed and rebuilt (one slab with drip moulding, handrail, balusters, corner
  posts, side rails).
- **Floor bands and cornices** (ledges along each facade) and **roof edges** (cornice, gold line, coping) rebuilt straight.
- **Porte-cochère** rebuilt: stone plinths, round teal columns with gold rings, entablature, teal fascia with gold
  band, cornice, coffered soffit; **entrance wall** with a 2.6 m doorway, stone and gold surround, gold-framed
  shopfronts; **stone podium** along the front with steps (Unity).
- Box-projected UVs (metres): plaster grain on walls, 1 m limestone paving on stone.
- Review renders: `Screenshots/Review/HotelRealistic/preview0_*.png` (front, wings, sides, back, roof, eye level).

Unity (`GameSceneBuilder.HotelIsland.cs`): the story lobby is moved 7 m back inside the building (front wall at
hotel-local z -1; all anchors and the receptionist move with it, once). **Gold lobby**: polished marble floor,
burgundy runner and desk rug with gold borders, walnut wainscot with gold dado rail, gold crown moulding, cream
pilasters with gold caps, walnut coffered ceiling with gold edges, five tiered crystal chandeliers with warm lights,
dark marble reception desk with gold fluting and gold lettering, gold-framed backdrop and paintings, columns at the
door, lounge with a gold table, palms in gold-rimmed planters, and a baked box-projected reflection probe
(`hotel_lobby_reflections.exr`). Collision: facade (LOD1) + clean details (LOD0) mesh colliders and solid cores.
Checks: walk-in sphere cast from the steps through the door into the lobby; anchors present; navigation rebaked.
Live frames from the Windows build: `Builds/Win64/Screenshots/GunGrips/hotel_f_*.png`.

**Roof terraces** (24 runs): clean balustrades along every open roof edge (none where a taller wall rises), the AI
balustrade removed. **Gold accents** outside: gold-capped handrails and post finials on balconies and roofs, a gold
fillet inside every teal window surround, gold lines under floor bands and roof cornices, gold column rings and
fascia band, gold "GRAND CORAL RESORT" lettering on the canopy, gold door surround and shopfront frames.
