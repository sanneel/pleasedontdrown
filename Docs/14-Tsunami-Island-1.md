# Island one: the tsunami and rental fleet

Implemented 2026-10-07. This replaces the repeated thief encounters and drug storyline in chapter one.

1. **Meet Sandy (1.1).** Learn the job and where to return lost items.
2. **First rescue (1.2).** One staged female rescue reaches CPR. Her beach-hut invitation is optional: talk to accept it, or continue when the invitation expires.
3. **Milo's rental dock (1.3).** Talk to Milo when ready; basketball, the human cannon and other beach activities remain free play. The thief steals the orange fleet-key case.
4. **Tsunami (1.4).** The water draws back, a broad foaming surge travels toward shore, and the station warns everyone to move inland. The rental operator and thief are swept into danger.
5. **Rescue the beach (1.5).** Save Milo, the thief and the stranded tourists. Rings and direct towing work; tourists who run out of air need CPR. Solo has three victims and 155 seconds before each becomes unconscious. Two, three and four players have four, five and six victims, respectively, with a modestly shorter air window (down to 105 seconds). Party size is frozen when the warning begins. Missed essential rescues get another recoverable attempt; the story cannot permanently lose its keys.
6. **Recover the fleet keys (1.9).** Recover the orange case after rescuing the thief, carry it to Milo and talk to him. The hotel's call provides the next destination. Returning the case unlocks all four skis for every player.
7. **Head south together (1.10).** Every connected player must reach the hotel before the chapter report. Players may share a ski or take one each. Talk to Milo to recover an unattended ski; existing Escape > Travel is available for a stranded player. Disconnected players do not block the party, and late joiners get the same fleet access.

## Art and play area

The rental area has a striped orange/cream canopy, teal counter, signed timber T-pier, four numbered berths and the existing jet ski models. Milo uses a current tourist-body variant so he matches the island while a dedicated character is considered. Tripo art is not required to play the chapter.

The surge is bounded to the front beach, with the inland station area left as refuge. It recedes after 65 seconds. Basketball and the other attractions remain available afterward. This is a stylized playable event, not a scientific tsunami simulation.

## Technical notes

- `TsunamiState` replicates the event start time. Rendering, buoyancy and swimming sample the same deterministic surge field; ordinary shallow-water damping is bypassed only for the surge.
- A broad moving crest, drawdown and a temporary flood shelf are rendered by the existing ocean shader. The wave mesh expands during the event so the crest can be seen offshore. Surf roar follows the event locally.
- Mooring forces keep the locked rental skis near their numbered berths. Unlocking ends the mooring automatically.
- Fleet authorization is a host-owned shared story state. No player needs to keep a physical key in a pocket after hand-in.
- Save format 2 preserves the fleet state. Old chapter-one thief/drug checkpoints migrate to the rental introduction. Reloading the rescue beat starts a calm, repeatable rescue phase; it does not spawn a wave on top of a loaded player.
- The old story methods remain available as source history but are no longer in the chapter-one beat list.

## Verification and review

Use the normal story autoplay through `1.10`. A client launched with `-pdd-crew` waits through the rescue and drives its own ski at departure. It also logs that it received the synchronized wave. `tsunami start` and `tsunami stop` are development commands for visual/physics inspection.

The automated test moves quickly between rescues. A real solo and four-player session is still needed to tune the rescue time and the feel of the surge; automated completion alone is not a difficulty assessment.

## Follow-up tasks after usage limits reset

Work started 2026-10-07. The previous heading referred incorrectly to rescue limits; these tasks were queued for the account usage reset.

Owners: primary agent handles flamingo polish and Unity integration; Sol handles speech/audio and a separate comprehensive GLB review. Completion requires verification, not just code changes. Reports: `Speech-Audio-Review.md` and `GLB-Polish-Review.md`.

Delivered 2026-10-07: intelligible rescue recordings (eight phrases in two registers); properly measured, wrapped NPC speech bubbles including the main conversation path; glossy flamingo with a repaired connected mesh; model audit and rendered previews, with defibrillator and basketball repairs. See the two reports for scope and remaining manual review limits. The automated solo story reached chapter 2 after the tsunami, key hand-in and jet-ski departure, with zero logged errors (`Logs/polish-story.log`). This does not replace a four-player human playtest or certify rescue timing as fun.

- **Improve the rescue sound pass:** add varied, short emergency barks ("Help!", "Over here!", "I can't swim!") with different voices and distance falloff so a busy rescue sounds urgent without becoming noisy.
- **Keep NPC chat in speech clouds:** ensure pressing **E** shows wrapped text in a readable cloud above the actual speaker. The earlier change did not cover the main dialogue path and needs correction. Tune cloud size, line breaks and lifetime around the voice lines.
- **Polish the flamingo:** make the flamingo catch the sun with a sharp, readable highlight, like a bright mirror glint. Check the material, normals and reflection response from the normal player distance and close up.
- **Review every imported `.glb`:** inspect each added model at gameplay distance and in a close-up pass. Fix visible problems such as broken normals, seams, rough or flat materials, missing colliders, wrong scale, floating parts and shading artifacts before the final art pass.
