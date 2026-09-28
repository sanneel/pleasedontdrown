# Beach sound direction

Reference inspected: local *How to Fish* Unity build (`sharedassets0.assets`, 453 embedded AudioClips) and its decompiled audio call sites. These clips are for comparison only; this project contains original synthesized sounds and no extracted reference audio.

## Sound map

| Reference pattern | This game's action | Status |
| --- | --- | --- |
| Five surface-footstep variations (sand, wood, hard surface) | Three variations each for sand, wood, rock | Added in `BeachAudio` |
| Light, medium, and heavy water impacts | Item/body splashes use impact strength | Added in `SplashFx` |
| Water footsteps and water entry/exit | Wading, strokes while swimming, head submersion | Wading retained; strokes and transitions added |
| Pickup, drop, equip, swoosh | Inventory actions and punches | Added in `PlayerHands` and `PlayerCombat` |
| Hover and select | Start/pause menu | Added in `DevConnectMenu` |
| Island ambience and seagulls | Quiet surf and occasional bird call during play | Added in `BeachSoundscape` |
| Outdoor gunfire and reload components | Five guns, suppressed shot, mag and rack | Existing triggers retained; outdoor tails lengthened and clipped peaks fixed |
| Money, doors, rescue, bell, CPR, bite, impact | Matching game actions | Existing `ProceduralAudio` triggers retained |

The reference also includes fishing rods, casino, creatures, lava, and bosses. Those actions are not in this game's current island gameplay, so no sounds were added for them.

## Review

Unity menu: **Tools → PDD → Export audio preview**. The export writes 25 original WAV examples to `Screenshots/Review/Audio/` and checks each clip for silence, invalid samples, and clipped peaks. The directory is ignored by Git. The project does not rely on the preview files at runtime.
