# The new game: plan (2026-10-20)

> **Claude: read this, `21-Base-Body-Prompts.md` and `18-Team-Split.md` before starting any part.** This replaces
> the story of chapters 1-4 (Sandy secretly owning Finders Keepers, the Sandy boss fight). Decided by Sandro on
> 2026-10-20: the new loop **replaces** the old story, the **mechanics come first** on today's characters, and
> **everyone** (the goofy lifeguard included) moves to the new chunky clay-toy style on two shared base bodies.

## 1. The game in one breath

You're a lifeguard. Tourists get into trouble; you grab them and **throw** them: to a friend who catches them in
the air, onto a trampoline, behind a boat, out of a cannon. Every throw is a **trickshot** with a silly name and a
score. Anyone can be picked up (E) and thrown: tourists, beach-goers, guests, pirates, your friends.

**Sandy is the beach doctor.** She runs a little clinic by the lifeguard station and hires you: "Bring them to me,
alive or not. I'll save them." Every person you hand in (out cold, half drowned, or already dead) she takes through
the curtain, pays you, and later tells you they pulled through: "Your guy from yesterday? Walking again. Lovely
man." A rescue on the beach still pays best; a body pays less, but she takes those too.

**The twist (island 2):** at the Grand Coral hotel you find out where the people really went. Sandy is the head of
a trafficking ring; nobody you brought her was ever saved. She doesn't deny it. She threatens you: every receipt has
your name on it, you brought her every one of them, and the police would love to hear it. From now on you work for
her, or else.

**The ending:** you play along, learn how the business works, and in the end turn it on her and take it over. The
last scene: you behind her desk, the ring is yours. (Sandro, 2026-10-22.)

Rescuing must still be the better choice on the beach: a live tourist pays more than a body, and the best
trickshots need the tourist alive at the end.

## 2. Tone (keep it a cartoon)

- Slapstick ragdolls, never gore: no blood, no wounds, no body parts shown. A body is a floppy tourist with X-ed
  eyes and a stuck smile, maybe a little ghost puff once.
- The organ trade is only **implied** and only in the back room: cooler boxes stamped "FRAGILE", "KEEP COLD",
  "DO NOT SHAKE", an order board with silly code words, a price list with blanked-out items, a phone ringing. The
  players work it out; nothing is spelled out or shown.
- Sandy never changes her friendly voice. The joke is how normal she keeps it.
- Same rules on friends: players can throw, catch and launch each other (they don't die: knocked silly for a
  few seconds, then up again).

## 3. Art: one style, two bodies (Phase 0, before any new model)

Why: today every character is its own generated model, so every pose, the CPR hands, the faces, the bust and belly
jiggle and the body checks have to be fitted 22 times, and they keep breaking (2026-10-20: people floating over the
sea, sunk in the sand, CPR hands on the bust). With one man and one woman sharing one skeleton and one UV layout,
an animation, a grip or a ragdoll is made once and works on everyone.

- **Bases**: GPT turnarounds (`21-Base-Body-Prompts.md` P1, P2), then image to 3D (Tripo), then Blender: symmetry,
  15-20k triangles, one shared UV layout, eyes as separate spheres (blinks and looking work on every character).
- **Rig once**: the game's own skeleton with fingers (`AvatarRig` bones), weights painted once, a ragdoll set up
  once (capsules per limb, joint limits loose for comedy).
- **Sliders** (shape keys): belly, wide/thin, bust, older/younger face; height and head size by bone scale.
- **Face**: eyes (separate, they look and blink), mouth shapes (open/smile/frown/O) as shape keys. Shako's living
  face code (`AvatarAnimator.Face.cs`, `AvatarFace.shader`) is reused, re-fitted once to the base.
- **Characters = variants**: a texture on the shared UV + hair/hat/glasses pieces on the head bone + slider values.
  Concepts from `21-...` P4/P5. Goofy keeps his googly eyes and buck teeth as his gag (P3).
- **Done when**: both bases rigged in the game, 6 variants (goofy, Sandy, the dad, two girls, a pirate) walking,
  idling, talking, swimming, carried, thrown (ragdoll), CPR'd, on every island, measured by `ClipCheck.FeetBatch`,
  `CprBatch` and filmed in the build.

**Runs in parallel with Phases 1-3**, which are built on today's characters first (Sandro's call) and moved onto
the bases when they're ready.

## 4. The mechanics, in order

Each phase ends playable and tested in the built game with two players (host + client).

### Done so far (2026-10-22)

- E picks anyone up: tourists, players, beach-goers, guests, pirates, robbers (they become tourist bodies, so throws,
  air shots, CPR and deliveries work on them). Talking is **T** now. Sandy, the receptionist, the rival and the police
  only talk (they carry the story).
- Throw anyone high (hold G); shots in the air do double damage and kill; bodies stay 90 s.
- No sliding: people knocked down or out drop where they stand (NPCs, stunned players, shot bodies on the sand).
- Bodies stay out of the sand (colliders fitted to each drawn body, `VictimBody.Fit.cs`).

### Phase 1: throw and catch (the core)

- **Pick up** (exists: `PlayerCarry` for players, the tourist carry in `PlayerHands`): any tourist, conscious or not,
  and any player.
- **Throw**: hold the throw button to charge (an arc preview, a short one), release: the tourist leaves the hands
  as a **ragdoll** with the throw's velocity plus the thrower's own (from a running jump, a jet ski, a boat).
  Server-authoritative ragdoll, clients interpolate (one owner sim, the root and 10-12 limb poses synced at 20 Hz).
- **Catch**: a player in the flight path with free hands catches automatically when the tourist passes within arm's
  reach in front of them (a short "catch" window and a grunt); caught = carried. Bystander NPCs can be hit (both go
  down, slapstick).
- **Landing**: in water → a swimmer (calm or panicking, by the drop) in the normal rescue flow; on sand from low
  → a bounce and sits up dazed; from high or into something hard → **knocked out** (needs CPR); very high / very
  hard / many hits → **dead** (see Phase 3). A soft landing: water, a trampoline, a beach umbrella, a pool float,
  a sunbed, a friend catching.
- **Ricochets**: bounces off umbrellas, signs, buoys, boats, the pier, other tourists; each counts (Phase 2).
- **Done when**: throw a tourist to a friend 10 m away in co-op and they catch it; throw into the sea and it swims;
  throw onto the pier and it's knocked out; all seen identically on both machines.

### Phase 2: trickshots

- Scored at the end of a flight: airtime, distance, height, ricochets, a mid-air catch, thrown from a moving
  vehicle, through a ring/buoy, off a trampoline, out of the cannon, a catch on a jet ski, a chain (caught and thrown
  on again before landing).
- A name pops up over the landing (big, wobbly, with a sting sound), stacking for combos: `ACCIDENTAL RESCUE`,
  `FLYING TOURIST`, `ABSOLUTELY UNNECESSARY`, `AIR MAIL`, `SPLASHDOWN`, `RETURN TO SENDER`, `TOURIST BOWLING`,
  `HOLE IN ONE` (into the pool), `FREQUENT FLYER` (the same tourist three times), `INSURANCE WILL NOT COVER THIS`.
- Score turns into money only if the tourist ends **rescued** (alive and ashore or in the pool): a trickshot into a
  body pays nothing extra.
- **Done when**: 10 named trickshots fire correctly in a scripted test (`-pdd-exec`), with the right names and pay.

### Phase 3: rescue pay, tips and bodies

- **Rescue pay** (money exists: `[Money]`): base pay + a tip by how they feel (condition left, how scared, how
  long it took) + the trickshot bonus. Now and then a gift: a better float, a whistle that calms panickers, a
  sunhat, jet ski fuel, a coupon.
- **Death**: replaces "Lost / airlifted by the rival company" (`VictimState.Lost`): the condition runs out, a very
  hard landing, a shark (today's leg bite). The tourist becomes a **body**: a ragdoll that can be picked up, stacked,
  dropped, thrown (it doesn't count as a rescue), floats in the sea.
- CPR and the defibrillator stay: they bring back the unconscious, not the dead.
- **Done when**: rescue pay and tips show for 5 different rescues; a body can be carried, thrown and stacked on a
  cart in co-op.

### Phase 4: Dr. Sandy's beach clinic (deliveries)

- A beach booth by the lifeguard station on island 1: "DR. SANDY: BEACH CLINIC", hand-painted, a bell, a curtain,
  a freezer chest behind the counter that she calls "the recovery room".
- Bring anyone to the counter (carried, on a cart, or thrown onto it) and press E: out cold, injured or **dead**
  all count. A **receipt** pops up to sign (one line each, absurd): "1 x patient, slightly used", "Condition:
  relaxed", "Prognosis: excellent", "Signed: ______", with the player's name scribbled. Sandy pays (a body less than
  someone breathing), takes them through the curtain, says one of her lines: "I'll save them, don't worry." /
  "Lovely. Any more?" / "Ooh, this one's fresh." / "Don't worry about the paperwork."
- A while later she mentions how they're doing ("Your redhead from this morning? Up and dancing.").
- **Done when**: a delivery works in co-op from both players, pays, plays a line, and the person is gone.

### Phase 5: transport chaos

- A **beach cart** (a wheelbarrow / luggage trolley) pushed by one player: bodies and knocked-out tourists stack on
  it as ragdolls; take a corner too fast and one slides off, a leg flops over the side, a bump launches one. A
  friend runs behind to pick them up.
- Towing from a boat or a jet ski on a rope (a tourist on a float behind, or a body).
- **Done when**: three bodies on a cart across the beach and the pier with at least one comedy spill, synced.

### Phase 6: launchers

- **Trampolines** on the beach and at the hotel pool (bounce height by drop height, a bounce counts as a trickshot).
- The **human cannon** (exists: `Fun/HumanCannon.cs`, players fire out of it) loads tourists and bodies too.
- A **seesaw / catapult** gag on island 1, a **slide** off the hotel roof into the pool on island 2, a pirate
  **cannon** on island 3.
- **Done when**: each launcher fires a tourist and a player, each has its trickshot name.

### Phase 7: the truth about Sandy (island 2)

On island 1 only small signs, driven by the number of deliveries (server-side, saved), each visible to everyone:

| Deliveries | What changes |
|---|---|
| 3 | A second freezer chest appears behind the clinic |
| 6 | A white van parks behind the clinic now and then, a man in sunglasses carries a cooler in, nods |
| 10 | A sign: "PLEASE DON'T NAME THE PATIENTS" |
| 15 | A third freezer; Sandy hums; the van comes every day; the van drives off toward the hotel island |

On island 2 (the Grand Coral hotel) the players find out:

- In the hotel's service corridor: the white van, the coolers, an order board with code words, and people they
  delivered (recognisable by look and name) being moved, not "recovering". A guest list with "SOLD" stamps.
- Sandy turns up behind them, calm as ever. She doesn't deny it: she's the head of it. **The threat**: a stack of
  receipts, every one signed by the players. "You brought me every single one. Imagine what the police would
  think." From here the players work for her: her deliveries, her jobs, her prices.
- Gameplay after the reveal: Sandy's jobs on islands 2-3 (deliver this many, this kind of person, by tonight), each
  one paying better and getting nastier; refusing has consequences (the police are tipped off about you, a raid on
  the lifeguard station).

### Phase 9: taking over (the ending)

- While working for her, the players collect what they need to turn it around (her ledger, the buyers' names, a
  recording of her), and win over her people (the van driver, the man in sunglasses).
- The final job: Sandy is the one delivered. The players take the clinic, the van and the ring. The last scene:
  the players behind Sandy's desk; the van driver: "Where to, boss?"
- Keep it a cartoon (section 2): implied, never shown; slapstick, no gore.

### Phase 8: the old story goes

- `StoryDirector` chapters 1-4 (beats 1.1-4.9, the Finders Keepers company, the THIEF's confession, the Sandy boss,
  her CPR, the credits) are switched off, then removed once the new loop covers the islands. Sandy stays as the
  booth keeper and the guide's voice on island 1.
- The islands stay as places, each with its own tourists and launchers: island 1 (the beach, the booth, the
  lifeguard station), island 2 (the Grand Coral hotel: the pool, the slide, the bar, the bungalows), island 3 (Skull
  Cove at night: pirates, the pirate cannon), island 4 (the corporate HQ: repurposed, see 7).
- A short intro instead of chapter 1.1: Dr. Sandy hires you ("Save them if you can. If you can't... bring them to me.
  I'll save them.").

## 5. Who does what

As in `18-Team-Split.md`: **Sandro (A)** owns code in Avatar/, Player/, Combat/, UI/, the ragdoll and the networking;
**Shako (B/C)** owns the islands, props, Game.unity and the scene batches.

| Phase | Sandro (A) | Shako (B/C) |
|---|---|---|
| 0 Art | rig, ragdoll, sliders, face fit, the variant system in `AvatarRig`/`AvatarLook` | GPT/Tripo bases and variants, Blender cleanup, textures, hair/hat pieces |
| 1 Throw/catch | throw, ragdoll flight and sync, catch, landings | soft things to land on (umbrellas, floats, sunbeds) placed per island |
| 2 Trickshots | scoring, names, HUD pop-up, pay | rings/buoys/targets on each island |
| 3 Pay/bodies | rescue pay, tips, gifts, death, bodies | gift item models |
| 4 Lost & Found | the delivery, receipt UI, pay, lines | the booth, the freezer, the curtain (`Game.unity`) |
| 5 Transport | the cart physics, stacking, spills, towing | the cart model, ramps and bumps |
| 6 Launchers | trampoline/catapult/slide physics, cannon loading | the props on each island |
| 7 Sandy | the delivery counter and steps, the van's route | freezers, van, signs, the back room set |
| 8 Story | switch the old chapters off, the new intro | island 4's new role |

Only one person changes `Game.unity` at a time (`18-Team-Split.md`).

## 6. Tests that must stay green

`ClipCheck.RunBatch`, `ClipCheck.FeetBatch` (feet on the ground everywhere), `ClipCheck.CprBatch` (hands on the
chest), `GunHoldCheck.RunBatch`; a co-op film of every new mechanic in the **built** game (host + client) before
it's called done; `perf` on islands 1-3 (keep island 1 under 8 ms a frame on this PC, `pdd-low-end-perf` notes).

## 7. Open questions for Sandro

1. ~~After the back room opens~~: decided 2026-10-22 (section 1, phases 7 and 9): Sandy is revealed on island 2 as
   the head of a trafficking ring, blackmails the players into working for her, and in the end they take it over.
2. Island 4 (the Finders Keepers HQ): keep it as a place (Sandy's "warehouse"?), or drop it?
3. Score and money: per player or shared by the team? A leaderboard?
4. Can tourists die from players' throws on purpose (e.g. throwing one off the hotel roof onto the sand), or only
   by accident and time? (Default: yes, it's slapstick, but it pays far less than a rescue.)
5. Kids on the beach: none (default), or only as non-throwable bystanders?
