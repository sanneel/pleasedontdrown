# PLEASE DON'T DROWN: Story Mode (Chapters 1-2)

> From the user's story outline (Georgian, 2026-09-28). This doc is the plan **and** the as-built reference.
> Everything is built technically with greybox/procedural placeholders; the 3D art is generated later
> (see [§6 Assets to generate](#6-assets-to-generate)).

---

## 1. The story, beat by beat

A host-run **StoryDirector** plays these beats in order. Each beat sets the objective (HUD, top left), an optional
marker, and waits for its condition. Money is one shared team wallet. Everything happens for all players (co-op).

### Chapter 1: The first island

| # | Beat | What the player does | Done when |
|---|---|---|---|
| 1.1 | **Meet Sandy** | You spawn; Sandy (about 50, blonde, coral shirt, teal apron) waves from her Lost & Found kiosk. Talk to her (E). She explains the job: rescue tourists; they lose things; bring finds to Lost & Found and she pays. | Dialogue ends |
| 1.2 | **First shift** | Rescue **5 tourists**. The director sends them one at a time: **3 of them women** (bikini tops). Women need CPR: chest compressions (the chest jiggles on each push) and **mouth-to-mouth**. Men get CPR and a **punch in the face** to wake up. Tourists also drop lost items on the beach (wallet, phone, sunglasses, watch). | 5 rescued |
| 1.3 | **The thief** | A tourist yells "THIEF!". A robber runs along the beach. Catch him and punch him (3 hits): he drops **3 stolen items** and runs off (you let him go). | Robber beaten |
| 1.4 | **Return the loot** | Bring the 3 stolen items to Lost & Found. Handing them in, Sandy says there's a robber on the island and gets scared. | 3 returned |
| 1.5 | **Three more** | Rescue **3 men**. | 3 rescued |
| 1.6 | **The silent one** | A woman sinks **without a sound** (no waving, no shouting); a guest next to her on the shore yells "HELP!". Swim out, bring her in, revive her (CPR + mouth-to-mouth). | Revived |
| 1.7 | **Drugs?** | She says she blacked out after taking some pill a guy on the beach gave her. So there are **drugs** on the island. | Dialogue ends |
| 1.8 | **One more** | Rescue 1 man. | 1 rescued |
| 1.9 | **The thief again** | The robber is back. Chase him and **hit him 5 times**: he falls, his bag bursts open and **2-3 baggies** fall out. You say you'll have him arrested; he begs and offers his **jet ski keys** to be let go. Take the keys. | Keys picked up |
| 1.10 | **Leave** | Take the jet ski at the dock (keys in your inventory) to the second island. | Jet ski reaches island 2 |

### Chapter 2: The hotel island

| # | Beat | What the player does | Done when |
|---|---|---|---|
| 2.1 | **Check in** | Walk into the hotel and talk to reception (E). The receptionist: you'll rescue guests here, but you need a **weapon** because guests sometimes get attacked. | Dialogue ends |
| 2.2 | **Buy a weapon** | Buy the pistol at reception with the money from island 1. (Short on money? Guests keep needing rescues until you can afford it.) | Pistol bought |
| 2.3 | **Harder work** | Rescue **3 guests**. Difficulty is up: **10 s** to pass out (20 s on island 1). The hotel's first-aid station has a **defibrillator**. Someone who's been out too long **flatlines**: CPR no longer works, only the electric shock does, and it pays more. | 3 rescued |
| 2.4 | **Shark!** | A shark **bites a guest's leg off**. Rush them to the hotel **infirmary** before they bleed out (60 s). | Delivered |
| 2.5 | **Two more** | Rescue 2 guests. | 2 rescued |
| 2.6 | **Pirates** | A pirate boat lands 4 pirates who attack the hotel. Kill them (pistol or fists). | All pirates down |
| 2.7 | **Your boat now** | The pirate boat is yours. Get on it and leave. *To be continued...* | On the boat |

---

## 2. Systems (technical)

| System | Where | Authority / sync |
|---|---|---|
| **StoryDirector**: beats as host coroutines, objective, progress, marker, save | `Story/StoryDirector*.cs` | Host; SyncVars (step, objective, progress, marker target) |
| **Dialogue**: speaker + line, auto-advancing subtitles, speech over the NPC | `Story/DialogueService.cs`, `UI/StoryHud.cs` | Host sends lines (ObserversRpc) |
| **Economy**: shared wallet, "+$60" popups | `Story/Economy.cs` | Host SyncVar |
| **StoryNpc**: Sandy, receptionist, robber, pirates, bystander. Procedural avatar, walks, faces you, talks, gestures, can be knocked down | `Story/StoryNpc.cs` | Host moves it (NetworkTransform); look/pose/expression SyncVars |
| **Robber / pirate brains**: flee, get hit, surrender, drop the bag; pirates chase and punch | `Story/NpcBrains.cs` | Host |
| **Damage** (`IDamageable`, `IHeldTool`) | `Combat/Damage.cs` | Host |
| **Punch** (empty hands, LMB) and getting knocked back | `Combat/PlayerCombat.cs` | Owner aims, host resolves |
| **Pistol** (held tool, LMB) | `Combat/Gun.cs` | Owner aims, host resolves, everyone sees the shot |
| **Lost items + Lost & Found** | `Items/LostItem.cs`, `Story/LostAndFound.cs` | Items are normal synced items; the host pays on hand-in |
| **Shop** (hotel reception) | `Story/ShopCounter.cs` | Host validates money, spawns the item |
| **Tourist variants**: gender, bikini, silent drowner, drown timer, flatline, lost leg | `Rescue/VictimBrain.cs`, `VictimBody.cs` | Host SyncVars |
| **CPR steps**: compressions → breaths (women) / punch (men), repeat; flatline needs the defibrillator | `Rescue/VictimBrain.cs` | Host counts, every machine animates |
| **Chest jiggle**: spring bones driven by body motion and CPR pushes | `Avatar/JiggleBone.cs` | Local visual on every machine |
| **Defibrillator** (held tool, LMB on a flatlined tourist) | `Combat/Defibrillator.cs` | Host resolves |
| **Hospital bed** (infirmary) | `Story/Hospital.cs` | Host |
| **Vehicles**: jet ski (needs keys), pirate boat (host autopilot, then yours) | `Vehicles/Vehicle.cs` | Driver's machine simulates (ItemSync ownership, `KeepAuthority` while driven) |
| **Seated player** (motor kinematic, view turns with the vehicle, hands on the handlebars) | `PlayerMotor.SetSeat`, `PlayerLook.AddYaw`, `FirstPersonArms` | Every machine glues the driver to the seat |
| **Story HUD** (objective, money, marker, subtitles, title cards, lost-item sparkles) | `Story/StoryHud.cs` | Local |
| **Shark** | `Creatures/Shark.cs` | Host |
| **Island 2 + hotel** (greybox) | `Editor/GameSceneBuilder.Story.cs` | Scene |

### CPR (story rules)
* Tap **RMB** on an unconscious tourist on land. The prompt tells you the step:
  **Compress** ×5 → women: **Mouth-to-mouth** ×2 / men: **Punch him awake** ×1 → repeat. Each step fills the revive bar.
* Women's chests jiggle with every compression (spring bones, `JiggleBone`); the lifeguard leans down to the face for breaths.
* Men's heads snap to the side with a *POW!*.
* **Flatline** (island 2): unconscious longer than 12 s → no pulse. CPR does nothing; hold the **defibrillator** and press LMB on them.

### Difficulty per island (`RescueRules`)
| | Island 1 | Island 2 |
|---|---|---|
| Seconds from "help!" to unconscious (nobody holding them) | 20 | 10 |
| Seconds of condition once unconscious (CPR window) | 60 | 45 |
| Flatline after being unconscious | never | 12 s |
| Spawn distance from the beach | 22-40 m | 14-26 m |

### Pay
| Event | $ |
|---|---|
| Rescue (brought back awake) | 60 |
| Revived with CPR | 80 |
| Revived with the defibrillator | 150 |
| Shark victim delivered to the infirmary | 200 |
| Lost item handed in | 20-40 |
| Stolen item returned | 30 |
| Baggie handed in as evidence | 25 |
| Pirate taken down | 50 |
| Pistol (costs) | -250 |

---

## 3. Networking notes
* The story is **host-only logic**; clients only see SyncVars (objective, progress, marker) and RPCs (dialogue lines, pay popups).
* NPCs are host-moved `NetworkObject`s with a `NetworkTransform`; their animation is derived from movement on every machine
  (same trick as remote players), plus a synced `Pose` (normal, knocked down, begging, hands up...).
* Punches and gunshots: the shooter picks the target locally (instant feel), the host validates range and applies damage.
* The jet ski uses `ItemSync` ownership: whoever drives it simulates it, everyone else follows the stream.
  Seated players are glued to the seat on every machine.

## 4. Save
`StorySave` (host, JSON in `Application.persistentDataPath/story.json`): chapter step + money. Saved at every beat.
A new host continues from the saved beat (`story reset` starts over; `story goto <step>` for testing).

## 5. Test commands
`story` (status), `story skip`, `story goto 1.9`, `story reset`, `money 500`, `robber`, `shark`, `pirates`,
`tourist f|m [silent]`, `flatline`, `tp island2`, plus the existing `victim`, `vset`, `cpr`.

## 6. Assets to generate
Everything below exists as a greybox/procedural placeholder with the named attach points, so a generated model
can be dropped in without code changes. Characters must be **rigged humanoids** (Mixamo-compatible or our
`AvatarRig` bone names) in the same simple low-poly style as `ArtSource/Concepts/Sandy`.

| Asset | Notes / attach points |
|---|---|
| **Sandy** (guide) | Concept done (`ArtSource/Concepts/Sandy`). Needs rig + expressions (friendly / scared / later "boss"). |
| **Receptionist** | Hotel uniform, name tag. |
| **Robber** | Striped shirt, beanie, backpack (bag bone/transform `Bag` that bursts open). |
| **Pirates** ×2-3 variants | Bandana, eyepatch, cutlass. |
| **Tourists (female)** | Bikini top + bottoms; chest needs **two extra bones** (`BustL`, `BustR`, children of `Chest`) for the jiggle springs. Swimsuit variants. |
| **Tourists (male)** | Trunks/shorts variants. Optional detachable leg (shark bite: hide `ThighL` chain). |
| **Jet ski** | ~2.6 m, seat at `Seat`, handlebars `GripLeft/GripRight`, exhaust spray point. |
| **Pirate boat** | ~7 m, deck walkable, driver seat, 4 landing spots. |
| **Hotel** | 2-3 floors, lobby with reception desk, infirmary room with bed, first-aid wall rack, dock. |
| **Lost & Found kiosk** | Counter with a bin, "LOST & FOUND" sign, Sandy's stool. |
| **Island 2** | Palms, hotel pool area, pier. |
| **Props** | Wallet, phone, sunglasses, watch, necklace, jet ski keys (float + tag), baggie, pistol, defibrillator (paddles), hospital bed. |
| **Shark** | ~3 m, jaw open/close, fin above water. |

---

## 7. As built (2026-09-28)

Everything above exists and runs. Tested headless (host alone, and host + client on port 7790) beat by beat, plus
windowed screenshots of the HUD, dialogue, CPR, the jet ski and the pirate landing.

**Characters.** `AvatarLook.Figure` (1 bit) adds a feminine figure: wider hips, narrower waist and shoulders, and two
bust bones (`AvatarRig.Bone.BustL/BustR`) with spring physics (`AvatarJiggle`). Tops gained `Bikini`; feminine tourists
wear bikinis or swimsuits. The customizer has a *Figure* row. `AvatarAnimator` gained poses (`Down`, `Kneel`, `Scared`,
`HandsUp`, seated), moods (`Happy`, `Scared`, `Angry`, `Hurt`), a talking mouth and aimed gestures (`Punch`, `Breath`,
`Zap`, `Shoot`).

**Tourists.** `TouristProfile` sets the figure, the drowning timer (air runs out in exactly N seconds unless held),
the CPR window, flatline time, silent drowning and bleed time. New state `Injured` (shark bite, on land, bleeding).
CPR is a sequence on RMB: 5 compressions (chest squish + jiggle), then 2 rescue breaths for women (the lifeguard
leans to the face) or a punch for men (head snaps, *POW!*), repeated until revived. Revive = +$80.

**Story places.** Island 1: Sandy's Lost & Found kiosk right of the spawn, the robber's jet ski tied to the dock.
Island 2 (~200 m south, same terrain mesh): the Grand Coral Hotel (reception desk = shop, infirmary bed, first-aid
shelf), its own dock, the pirate boat parked far out. Coordinates are constants at the top of
`GameSceneBuilder.Story.cs`.

**Placeholders to replace with generated art** (the code only needs the named points): Sandy/receptionist/robber/pirate
looks are `AvatarLook` presets in `StoryDirector.Beats.cs`; the robber's bag is built in `StoryNpc.ShowBag`; the shark,
jet ski, boat, kiosk and hotel are primitives in `GameSceneBuilder.Story.cs`.

**Known limits.**
* NPCs walk in straight lines (no navmesh); the robber picks open routes by raycast, pirates can clip through walls.
* Players can't be hurt by pirates beyond knockback (the design keeps failure light).
* One save slot, host only. A client that joins mid-chapter-2 spawns on island 1 (`goto`/`tp` or the next beat moves them).
* Dialogue auto-advances; no choices yet.
* The hotel's upper floor is solid.

**Tuning** lives in the scene (`StoryDirector` island setups: sea boxes, difficulty, land area) and in code constants
(`Economy` pay, `VictimBrain` CPR gains, pistol price 250).
