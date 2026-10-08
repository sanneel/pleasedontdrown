# Lifeguard customization

The Goofy character now has 13 hats and 7 eyewear styles, plus None for both. Six hats are new: Beanie, Cowboy, Pirate, Crown, Party Hat and Headphones. New eyewear: Goggles, Aviators, Stars and Sport.

The **Outfit preset** arrows apply eight coordinated looks: Rescue crew, Beach party, Coast cowboy, Pirate patrol, Beach royalty, Winter swimmer, Disco guard and Sunset surfer. Presets preserve skin, hair colour and body/face shapes; individual controls can be adjusted afterwards. Random includes the new accessories. Classic keeps its original compatible accessory range.

Hats, glasses, facial hair and buck teeth now use Blender-authored geometry. Hat and hair palettes still work. Accessories follow head size, with adjusted frames and brim clearance for large eyes. The menu camera frames the body and accessories after each change, including tall hats.

## Blender source

Open `ArtSource/Customization/lifeguard_customization.blend`. Each accessory has a named collection, in the Goofy body's head-local space; Cowboy and Sunglasses are visible initially. Hide/show collections to work on one fitted piece at a time. The source has bevels and editable curves.

`ArtSource/Tools/customization_blender.py` recreates the collection and exports evaluated meshes to `ArtSource/Customization/wearables.json`. Unity's **Tools → PDD → Import Blender customization** imports those into `Assets/_Game/Art/Customization` and updates `Resources/AvatarWear.asset`. Game builds use the imported assets and do not need Blender or the JSON file.

## Compatibility and review

The packed look uses a new 0xC marker to allow the larger accessory ranges. Both previous save layouts (0xA7 and 0xB) still decode. New and old players should use the same game build for multiplayer; old clients cannot decode the expanded layout.

`CustomizationReview.ImportAndCapture` validates the accessory library, 448 hat/glasses/head combinations, 512 random saved-look round trips, and fixtures for both old layouts. It renders 16 looks, including every head size and large eyes, to `Screenshots/Review/Customization`.

**Tools → PDD → Build customization review player** builds the current saved Game scene into `Builds/CustomizationReview`, with a separate multiplayer build stamp, without rebuilding the scene or prefabs.
