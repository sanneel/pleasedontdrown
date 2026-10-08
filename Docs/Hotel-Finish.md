# Single polished hotel

## Assets

The source Tripo hotel has visibly wavy frames and balcony rails. Its original FBX and texture files remain preserved. The replacement game facade retains the stepped silhouette with straight architectural geometry and quiet limestone materials.

- One building, approximately 50.5m wide, 32.6m deep and 40m tall.
- Blender master: `ArtSource/Resort/hotel_clean.blend`.
- Game geometry: `Assets/_Game/Art/Props/resort_hotel_clean_reception_lod0.glb`, `lod1.glb`, `lod2.glb` with the same full prefix.
- Triangle counts: 137,736 / 32,040 / 6,720.
- Five materials: ivory, glass, teal, brass and stone. Two subtle limestone textures are embedded, with mipmaps and trilinear filtering. No noisy normal map on the facade.
- `ArtSource/Tools/build_clean_hotel.py` rebuilds the asset. Original geometry, old LODs and source files remain intact.
- Preview: `Screenshots/Review/HotelClean/exterior.png`.

## Live scene update

**PLEASE DON'T DROWN → Finish one hotel and open reception** backs up the live Game scene, retains the central hotel, removes three extra hotel groups and raw imported FBX instances, refreshes the facade, and restores missing lobby objects and story references.

The entrance adds a broad canopy, a clear vestibule, steps, limestone flooring, brass rails and warm lights. Reception now has resort branding, desk trim, ceiling lamps, a seating area and planters. Existing reception, hospital bed, first-aid and entrance story links are retained or restored. The imported mesh leaves both the lobby and route to the outside empty.

The larger island retains pools, gardens and promenades. Beach cabanas, loungers, palms and planting accompany the user's coloured Tripo beach bar. The cabana/lounge row adapts to the live pool position and is checked for dry ground and pool overlap. Its U-shaped counter, rear staff aisle, bartender and serving spots have matching collisions and placement. Exact duplicate hotel ground collisions are removed.

The queued request is `Logs/hotel-finish-request.txt`. A refresh lets Unity load and apply it; a running preview is stopped first so the edit scene can be updated. Completion is recorded in `Logs/hotel-finish-result.txt`.

## Verification

- The final editor code compiles independently using Unity's compiler and references.
- `ArtSource/Tools/check_hotel_entrance.py` checks actual exported triangles along the entrance route at every LOD, including head/shoulder clearance samples. Results: `Logs/hotel-entrance-blender-check.json`.
- The live update also checks sphere clearance against scene collisions and temporary LOD0 mesh colliders, verifies reception anchors, rebakes island-two navigation, saves the scene and produces Unity review screenshots.
- The live scene update reports PASS: one hotel, restored reception links, clear entrance geometry and collisions, coloured Tripo bar and clear customer/staff approaches. The scene and island-two navigation are saved.
- Unity screenshots were inspected for the exterior, entrance, lobby, bar and cabanas: `Screenshots/Review/Resort/`.
- `FinishHotelIslandBatch` opens the saved Game scene in a separate batch editor, applies the finish and runs the Windows development build. Completion is recorded in `Logs/hotel-island-build-result.txt`; the detailed build log is `Logs/hotel-island-build.log`.
- Final verification on 8 October 2026: batch editor exited with code 0; the Windows development build succeeded after the pool furniture correction. Output: `Builds/Win64/PleaseDontDrown.exe`.
- These checks do not constitute a complete co-op playthrough.
