# Beach sound direction

Water audio now uses 20 recorded clips from the local *How to Fish* Unity build (`sharedassets0.assets`), as requested by the user. Other effects remain synthesized. Exact source clip names, asset IDs, decoded hashes and processing are recorded in `WaterAudioSources.json`; these water recordings are attributed to How to Fish, not original project recordings.

## Water replacement

The synthetic water sounds were rejected and removed completely, including the old legacy splash and generated surf. `WaterFoley` now loads recorded assets from `Resources/Audio/Water`, with no synthetic fallback.

* Five `Footstep_Water` recordings serve wading, quiet swimming strokes and wet takeoffs. Selection avoids immediate repeats. Strokes follow movement effort at a gentler cadence and do not fire for idle surface bobbing.
* Nine `ItemHitWater` recordings cover light, medium and heavy impacts.
* Two entry and two exit recordings cover submersion/surfacing. A new transition stops the previous tail instead of stacking long recordings.
* `SeaAmbient_Loop_Mono` and `UnderwaterLoop` crossfade with submersion. Both use a 60 ms wrap crossfade instead of periodically fading to silence.
* Import processing downmixes to mono, removes DC, balances levels by role, and adds short one-shot edge fades. The recordings retain their real water transients and tails; no synthesized noise or bubble tones are mixed in. PCM imports preserve those transients.

Reimport with `python Tools/import-howtofish-water.py --source "<path to How to Fish_Data/sharedassets0.assets>"`. The tool reads the installed reference without modifying it and refreshes the provenance manifest.

## Sound map

| Reference pattern | This game's action | Status |
| --- | --- | --- |
| Five surface-footstep variations (sand, wood, hard surface) | Three variations each for sand, wood, rock | Added in `BeachAudio` |
| Light, medium, and heavy water impacts | Item/body splashes use impact strength | Nine recorded variants in `WaterFoley` / `SplashFx` |
| Water footsteps and water entry/exit | Wading, strokes while swimming, head submersion | Five recorded footsteps and four recorded transitions |
| Pickup, drop, equip, swoosh | Inventory actions and punches | Added in `PlayerHands` and `PlayerCombat` |
| Hover and select | Start/pause menu | Added in `DevConnectMenu` |
| Sea/underwater ambience and seagulls | Water ambience and occasional bird call during play | Recorded water loops, synthesized bird in `BeachSoundscape` |
| Outdoor gunfire and reload components | Five guns, suppressed shot, mag and rack | Existing triggers retained; outdoor tails lengthened and clipped peaks fixed |
| Money, doors, rescue, bell, CPR, bite, impact | Matching game actions | Existing `ProceduralAudio` triggers retained |

The reference also includes fishing rods, casino, creatures, lava, and bosses. Those actions are not in this game's current island gameplay, so no sounds were added for them.

## Sound polish (October 2026)

* Dialogue retains the original cartoon babble/subtitle direction. Softer consonants, restrained pitch contours and word pauses replace the sharper vowel chatter. A whole phrase is rendered with each syllable's own pitch; changing one syllable can no longer bend an earlier syllable. Interruptions stop the previous line and release its generated clip. Voice volume changes apply during speech.
* Water synthesis from the initial polish pass has been superseded by the recorded bank described above.
* Accepted jumps produce a short surface scuff, including a wet takeoff for water hops. Landings have more body and scale with fall speed. Sand, wood and rock each have three takeoff and landing variants. A brief landing cooldown prevents a simultaneous footstep. Remote players also produce inferred movement and swimming cues.
* Depth hysteresis and a cooldown prevent rapid waterline sound chatter. A separate source keeps recorded transitions independent of footfalls.
* Pickup/drop/equip lose the obvious pitch swoops. Bird frequency is integrated correctly, distress cries use a narrower range, door hinges use softened friction, and cough/rescue breath effects emphasize breath instead of a sustained hum.
* All generated foley passes through `SoundClip`: removes DC, fades the first/last 5 ms, and attenuates peaks above 0.82 without hard clipping. Gunfire and other retained effects receive the same mastering. This is per-clip headroom, not a limiter for the entire game mix.
* Water impacts use eight reusable spatial sources, at most three new sounds per frame, to bound the noise from simultaneous floating objects.

Non-water effects remain original synthesized effects; dialogue remains cartoon babble. Listening in the game is the final aesthetic check.

## Review

Unity menu: **Tools → PDD → Export audio preview**. The export writes 153 WAV examples to `Screenshots/Review/Audio/`, including all 20 recorded water assets, movement variants and all four voices. It checks silence, invalid samples, clipped peaks, one-shot endpoints and loop seams, then refreshes the water listening page (`index.html`). The directory is ignored by Git. Runtime water files live in `Assets/_Game/Resources/Audio/Water/`.

**Tools → PDD → Build audio review player** builds the current scene to `Builds/AudioReview/PleaseDontDrown.exe`, without regenerating the scene or prefabs. Start it with `-pdd-nosave` for a review session that does not read or write story progress. Walk and jump on sand, the dock and rocks; enter shallow water, swim, dive, surface and chat to Sandy. Compare gentle and long-fall landings and check voice volume while a line is playing.
