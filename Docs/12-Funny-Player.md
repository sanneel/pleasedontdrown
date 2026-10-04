# The funny player (goofy lifeguard)

Players are a generated character now (`AvatarLook.Bodies.Goofy` = 8): a big-headed lifeguard with googly eyes, buck
teeth, a pot belly, noodle arms and big feet. Every player makes him their own in **YOUR LIFEGUARD**.
CHARACTER switches back to the classic code-built body. Saved looks from before were moved onto him once
(PlayerPrefs `pdd.avatar.goofy`).

## Where he comes from

1. Meshy (Free plan): concept picture (image tool, Nano Banana Pro, A-pose described in the prompt), then
   **Image to 3D with Meshy 6 Lite** (7.1 models can't be downloaded on Free), then **Texture** from the same
   picture. License CC BY 4.0: credit Meshy in the game's credits.
2. `ArtSource/Meshy/raw/goofy_raw.glb` (the download), rigged with
   `blender -b --factory-startup -P ArtSource/Tools/prepare_character.py -- ArtSource/Meshy/raw/goofy_raw.glb Assets/_Game/Art/Characters/goofy.glb --height 1.6`.
3. `MeshyCharacters.BakeBatch` bakes him like any body, plus the funny pass (`Editor/MeshyCharacters.Funny.cs`):
   * `Avatar/Bodies/goofy_recolor.png`: the texture with a part id in alpha (skin, hair, top, shorts, teeth, nose),
     found per texel from its colour and where it is on the body;
   * where the painted googly eyes, the nose tip and the teeth are (head-bone space, on the `AvatarBody`);
   * shape keys `BellyRound`, `BellyHuge`, `BellyFlat`, `NoseBig`, `NoseSmall`.

## What can be changed (AvatarLook)

| Row | Values | How |
|---|---|---|
| HEAD | Normal, Big, HUGE, Tiny | head bone scale (`AvatarFunny.HeadScales`) |
| BELLY | Normal, Round, Beach ball, Flat | shape keys |
| NOSE | Normal, Big, Clown (big + red), Button | shape keys + nose recolour |
| EYES | Googly, Huge googly, Cross-eyed, Tiny pupils | code-built googly eyes; pupils slosh with gravity and every move of the head |
| TEETH | Pearly, Bucky (two huge teeth), Gold, Rotten | recolour; Bucky = teeth built in code |
| SKIN / HAIR / TOP / SHORTS COLOUR | palettes | recolour of a per-player copy of the texture (shading kept) |
| HAT, HAT COLOUR, GLASSES, FACIAL HAIR | as before | built in code to fit this head (`AvatarFunnyWear`) |
| SUNSCREEN NOSE, ARM FLOATIES | yes/no | nose recolour; floaties on the upper arms |

The new fields pack into the look's ulong with a new marker (top nibble 0xB, Body in 7 bits); old packs (0xA7) still
read. Code: `Avatar/AvatarFunny.cs` (colours, shape, googly eyes), `Avatar/AvatarFunnyWear.cs` (things to wear),
`UI/AvatarCustomizer.cs`.

Look at combinations without running the game: ReviewCapture `avatar goofy:head:belly:nose:eyes:teeth:hat:glasses:face x y z yaw pose`
(numbers). In ReviewCapture the pupils sit still and the head size isn't shown (both happen in the game's LateUpdate).
