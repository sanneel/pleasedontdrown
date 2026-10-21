#!/bin/bash
# The beach girls on the base woman: repaint (ArtSource/Tripo/Bases/girls_spec.json), then each one's hair fitted on
# (ArtSource/Tools/attach_hair.py). Output: Assets/_Game/Art/Characters/base_woman_vNN.glb, then bake with
#   Unity -executeMethod PleaseDontDrown.Editor.MeshyCharacters.BakeOnlyBatch -pdd-bodies base_woman
# Usage: bash ArtSource/Tripo/Hair/make_girls.sh [preview_dir]
cd "$(dirname "$0")/../../.."
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
PREV="$1"
# name : variant number : hairstyle (ArtSource/Tripo/Hair/hair_<style>.glb) : that hairstyle's own colour (the hint) : tint or -
# (Red's own hair came out the colour of her skin and the black-haired girl's sat her generated face on the neck:
# they wear Lola's waves and the pink girl's ponytail. The blonde bob tore at the front strands beside the cheeks
# (Tripo's cheek baked onto their inside), so its three girls wear the ponytail, the waves and the long straight hair.)
GIRLS="lola:01:lola:60,38,28:- red:02:lola:60,38,28:98,62,42 blonde:03:pink:220,200,170:200,160,100 redhead:04:redhead:176,84,40:-
blackhair:05:pink:220,200,170:30,24,22 pink:06:pink:220,200,170:- lavender:07:lavender:28,24,24:-
mia:08:lola:60,38,28:120,80,50 zoe:09:lavender:28,24,24:- ivy:10:redhead:176,84,40:196,96,44
nina:11:lola:60,38,28:58,38,27 kira:12:pink:220,200,170:24,21,21 rosa:13:lola:60,38,28:140,52,30
ella:14:lavender:28,24,24:222,188,126 maya:15:redhead:176,84,40:58,38,27 lily:16:lavender:28,24,24:236,226,204"
for style in lola redhead pink lavender; do
  raw=ArtSource/Tripo/Hair/raw/hair_${style}_meshopt.glb
  dec=ArtSource/Tripo/Hair/hair_${style}.glb
  [ -f "$raw" ] && [ ! -f "$dec" ] && "$B" -b --factory-startup -P ArtSource/Tools/tripo_decode.py -- "$raw" "$dec" 2>&1 | grep tripo_decode
done
"$B" -b --factory-startup -P ArtSource/Tools/make_variants.py -- Assets/_Game/Art/Characters/base_woman.glb Assets/_Game/Art/Characters/base_woman --spec ArtSource/Tripo/Bases/girls_spec.json 2>&1 | grep -E "wrote|Error|Traceback"
for g in $GIRLS; do
  IFS=: read -r name n style hint tint <<< "$g"
  dec=ArtSource/Tripo/Hair/hair_${style}.glb
  out=Assets/_Game/Art/Characters/base_woman_v${n}.glb
  [ -f "$dec" ] || { echo "no hair $style for $name"; continue; }
  extra=""; [ -n "$PREV" ] && extra="--preview $PREV/girl_${name}"
  [ "$tint" != "-" ] && extra="$extra --tint $tint"
  "$B" -b --factory-startup -P ArtSource/Tools/attach_hair.py -- "$out" "$dec" "$out.tmp.glb" --hint "$hint" $extra 2>&1 | grep -E "^\[hair\] (scale|wrote)|Error|Traceback"
  mv -f "$out.tmp.glb" "$out"
done
# Then: Unity -executeMethod PleaseDontDrown.Editor.GlbTextureSlim.Run -pdd-only base_woman_v (the skins out of the
# models, compressed), then the bake above.
