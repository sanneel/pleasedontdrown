---
name: add-model
description: Bring a 3D model into the game or replace one: from Tripo (Unity bridge), Meshy, a GLB/FBX/OBJ file someone has, or Blender. Use when the person wants a new building, prop, vehicle, creature or decoration, or wants a model recoloured, cleaned up, made lighter or swapped.
---

# Add or replace a 3D model

## 1. Get the file

- **Tripo, through the bridge:** in Unity, **Tools > Tripo Bridge** must be open with the service running. In
  Tripo Studio (browser) they pick the model, then DCC Bridge, Unity, Export, Send to Unity, and switch back to
  Unity. It lands in `Assets/TripoModels/<name>/`. Details: `Docs/Tripo-Unity-Bridge.md`.
- **A file they have** (Tripo or Meshy download, Sketchfab and so on): ask them to drop it into the chat or tell you
  its path. Keep the untouched original in `ArtSource/` (`ArtSource/Meshy/raw/` for Meshy, `ArtSource/Resort/` or
  a new folder named after the area otherwise). Never edit the original.
- Check the licence for anything not made by the team (Meshy free plan and Sketchfab models usually need credit:
  write it in the doc for that area).

## 2. Look at it before it goes in

- Render it from four sides and print size and triangle count:
  `blender -b --factory-startup -P ArtSource/Tools/preview_glb.py -- <file.glb> <output prefix>`
  (Blender path on the owner's PC: `C:/Program Files/Blender Foundation/Blender 5.2/blender.exe`).
- AI models arrive huge (100k to 2M triangles) with 4K textures. Budgets in this game: small prop 2k to 5k
  triangles, vehicle or big prop 10k to 25k, building 25k to 60k for the near view plus lighter LODs.

## 3. Make it game-ready (Blender scripts in `ArtSource/Tools/`)

| Script | For |
|---|---|
| `polish_prop.py` | Meshy/Tripo props: clean, decimate and **re-bake the texture** so the paint survives (decimating directly shreds AI textures). Options in `Docs/04-Meshy-Integration.md`. |
| `prepare_prop.py` | Vehicles and creatures: orient (nose forward), scale to a length, decimate, optional tail split. |
| `decimate_glb.py` | Quick triangle reduction for simple models. |
| `prepare_character.py` | Rigged characters onto the game's skeleton (`AvatarRig`). |
| `prepare_tripo_hotel.py`, `colour_tripo_bar.py` | Worked examples for big Tripo buildings and recolouring. |

Output GLBs go under `Assets/_Game/Art/` (`Props/`, `Characters/`, `Meshy/`, ...). Units are metres, Y up, the
model standing on its lowest point.

## 4. Put it in the game

- Materials: plain matte URP Lit with the colour texture (smoothness about 0.25, no shiny AI gloss). Downscale
  textures to 1K or 2K unless it's huge and close to the camera.
- Collision: simple shapes (boxes, capsules) on the parts people touch, never a high-poly MeshCollider.
- Make a prefab, and place it through the builder code for that area (`Assets/_Game/Editor/GameSceneBuilder.*.cs`,
  for example `GameSceneBuilder.MeshyProps.cs` or `GameSceneBuilder.Resort.cs`) plus a small menu item that places or
  updates it in the open scene, so it survives a scene rebuild. Don't run the full **Rebuild Game scene** without
  asking the owner (see `CLAUDE.md`).
- If players walk into it, check doorways are at least 1.2 m wide and 2.1 m high, and rebake the navmesh for
  that island if NPCs need to walk around it.

## 5. Check and save

Give a `test-change` recipe (fly there with `noclip` or `goto`, look from near and far, walk into it). Large
models go through Git LFS automatically; tell the person the size and use `save-work`.
