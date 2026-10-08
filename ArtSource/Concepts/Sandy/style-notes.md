# Sandy: game-matched concept

This is a visual concept, not an implemented NPC or a 3D model.

## References inspected

- Current game captures: `Screenshots/Review/avatars_front.jpg`, `face_close.jpg`, and `shack_front.jpg`.
- Current avatar geometry: `Assets/_Game/Avatar/AvatarMeshKit.cs` and `AvatarParts.cs`.
- Current vertex-color toon shader: `Assets/_Game/Data/Shaders/Avatar.shader`.
- All five GLBs under `Assets/_Game/Art/Meshy/`; character GLBs have no skeletons and are currently superseded by procedural avatars.
- Local How to Fish decompiled `NPC.cs`, `PlayerSkin.cs`, and `ShaderManager.cs`.
- Local How to Fish character assets: CharacterHead (1,634 vertices), Character (4,104), StoreClercClothes (3,008), StoreClercHair (2,186). Examined in temporary reference files only.

## Accepted constraints from the user

Sandy is about 50, blonde, slim, and wears no glasses. Her friendly guide and villain expressions belong to the same character. Match the actual game's simple geometry and shading.

## Visual direction

Use a primitive oval head, separate round eyes with black pupils, simple eyebrow pieces, a projecting nose, small mouth, thin limbs, and matte color blocks. Hair consists of a few solid blonde shapes and a bun. Keep the coral shirt, teal apron, beige cropped trousers, sandals, and keys simple. Express menace through eyebrow angle, eyelids, mouth, and posture.

Avoid detailed skin, individual hair strands, cloth weave, cosmetic makeup, and polished cinematic character rendering. The current game's procedural avatars, rather than the older static character GLBs, are the primary visual reference.

## Generation

Generated with the built-in image generation tool using the actual game captures as image references. Full prompt is saved in `generation-prompt.txt`.
