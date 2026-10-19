#!/bin/bash
# Dresses the beach women in their swimwear (ArtSource/Tools/paint_swimwear.py), from the untouched colour textures in
# ArtSource/Swimwear/src into Assets/_Game/Art/Characters/Textures. Then bake them (MeshyCharacters.BakeOnlyBatch).
# paint_all.sh [preview dir] [body ...]
cd "$(dirname "$0")/../.."
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
PREVIEW=${1:-}
shift
declare -A STYLE=( [tourist_lola]=lola [tourist_girl_red]=sporty [tourist_girl_blonde]=ruffle [tourist_girl_redhead]=belted
                   [tourist_girl_black]=rash [tourist_girl_pink]=retro [tourist_girl_lavender]=tankini )
bodies=${@:-tourist_lola tourist_girl_red tourist_girl_blonde tourist_girl_redhead tourist_girl_black tourist_girl_pink tourist_girl_lavender}
for b in $bodies; do
  args=("$PWD/Assets/_Game/Art/Characters/$b.glb" "$PWD/ArtSource/Swimwear/src/${b}_color.jpg"
        "$PWD/Assets/_Game/Art/Characters/Textures/${b}_color.jpg" "${STYLE[$b]}")
  [ -n "$PREVIEW" ] && args+=(--preview "$PREVIEW/$b")
  "$B" -b --factory-startup -P ArtSource/Tools/paint_swimwear.py -- "${args[@]}" 2>&1 | grep -a "\[swim\]\|Error\|Traceback" -A2
done
