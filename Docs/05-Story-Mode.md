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
| 1.1 | **Meet Sandy** | You spawn and Sandy (about 50, blonde, coral shirt) walks over to you from her Lost & Found kiosk and explains the job: rescue tourists; they lose things; bring finds to Lost & Found and she pays. Then she goes back to her kiosk. | Dialogue ends |
| 1.2 | **First rescue** | Rescue **1 tourist** (a man or a woman, picked at random), who was swimming a moment ago and is now yelling their own silly reason for drowning (a [gag](#gags-three-rescues-an-island)). On island 1 everyone collapses once pulled out, so every rescue ends in CPR: women get compressions (the chest jiggles on each push) and **mouth-to-mouth**, men get compressions and a **slap in the face**. Sandy shouts a tip the first time each step comes up. Lost things turn up next to people's towels. | 1 rescued |
| 1.3 | **The thief** | A robber grabs a sunbather's bag; she jumps up screaming "THIEF!". Catch him and punch him (3 hits): he drops **3 stolen items** and runs off (you let him go). | Robber beaten |
| 1.4 | **Return the loot** | Bring the 3 stolen items to Lost & Found. Handing them in, Sandy says there's a robber on the island and gets scared. | 3 returned |
| 1.5 | **Another one** | Rescue **1 tourist** of the other sex (so both kinds of CPR come up on island 1), with another gag. | 1 rescued |
| 1.6 | **The silent one** | A woman (Jenny, Tina or Lola) sinks **without a sound** (no waving, no shouting); her friend swims over next to her yelling "HELP!". Swim out, bring her in, revive her (CPR + mouth-to-mouth). | Revived |
| 1.7 | **Drugs?** | She says she blacked out after taking some pill a guy on the beach gave her. So there are **drugs** on the island. | Dialogue ends |
| 1.8 | **False alarm** | Somebody is screaming for help in the shallows... where the water is knee-deep (or a coin-sized crab has their toe, or seaweed touched their leg). Walk up to them: punchline, Sandy sighs from her hut. Not a rescue. | A lifeguard gets there |
| 1.9 | **The thief again** | The robber is back. Chase him and **hit him 5 times**: he falls, his bag bursts open and **2-3 baggies** fall out. You say you'll have him arrested; he begs and offers his **jet ski keys** to be let go. Take the keys. | Keys picked up |
| 1.10 | **Leave** | Take the jet ski at the dock (keys in your inventory) to the second island. | Jet ski reaches island 2 |

### Chapter 2: The hotel island

| # | Beat | What the player does | Done when |
|---|---|---|---|
| 2.1 | **Check in** | Walk into the hotel and talk to reception (E). The receptionist: you'll rescue guests here, but you need a **weapon** because guests sometimes get attacked. | Dialogue ends |
| 2.2 | **Buy a weapon** | Buy the pistol at reception with the money from island 1. (Short on money? No extra rescues: Marisol takes whatever the team has and gives you the pistol on credit.) | Pistol bought |
| 2.3 | **Harder work** | Rescue **1 guest** (with a gag). Difficulty is up: **10 s** to pass out (20 s on island 1). The hotel's first-aid station has a **defibrillator**. Out cold, this guest **flatlines** after 3 s: CPR no longer works, only the electric shock does, and it pays more. | 1 rescued |
| 2.4 | **Shark!** | A shark **bites a guest's leg off** (Todd, Barry or Duncan, who first thinks it's a dolphin). Rush them to the hotel **infirmary** before they bleed out (60 s). | Delivered |
| 2.5 | **One more guest** | Rescue **1 guest** of the other sex than in 2.3, with another gag. | 1 rescued |
| 2.6 | **Pirates** | A pirate boat lands 4 pirates who attack the hotel. Kill them (pistol or fists). | All pirates down |
| 2.7 | **Your boat now** | The pirate boat is yours. Get on it and leave. *To be continued...* | On the boat |

### Gags: three rescues an island

The story is the same every time; only **three rescues per island** (1.2, 1.5, 1.6 / 2.3, 2.4, 2.5) and each one is a
little scene, so the game is about the story and its jokes rather than grinding rescues. A little is random per
playthrough: man or woman (the second rescue of an island is the other sex), which gag from a short list (never the same
twice in a run), which swimmer, where. Code: `Story/StoryDirector.Gags.cs`.

* **What they yell** instead of "help!" (`TouristProfile.Shouts`, synced by `VictimBrain`, shown by `VictimBody`).
* **The guide's comment** once they're out there (Sandy shouting on island 1, Marisol over the speaker on island 2),
  said when nobody else is talking.
* **What they say once saved**: first by how they were brought back (kissed: "Did you just... KISS me?!", slapped:
  "OW! Who SLAPPED me?!", zapped: "I SAW A LIGHT! ...Oh. It's the sun.", pulled out awake), then their own punchline.
  Some leave a lost thing in the shallows (the influencer's phone).

| Island 1 (Sandy) | Who | Yells | Punchline |
|---|---|---|---|
| hotdogs | man | "I ATE THREE HOT DOGS!" | "...Is the hot dog stand still open?" |
| influencer | either | "LIKE AND SUBSCRIBE!" | "Where's my PHONE?!" (drops it) |
| flamingo | woman | "PRINCESS POPPED!" | "I'm signing up for swimming lessons." |
| olympian | man | "I SWAM IN THE OLYMPICS!" | "Not a word about this." |
| waver | either | "HI! HELLO, BEACH!" | "I WAS waving. And then I wasn't." |
| sunscreen | woman | "I'M SO SLIPPERY!" | "SPF 100. I regret nothing." |

| Island 2 (Marisol) | Who | Yells | Punchline |
|---|---|---|---|
| vip | either | "DO YOU KNOW WHO I AM?!" | "One-star review. For the ocean." |
| mojito | either | "SAVE THE MOJITO!" | "The mojito made it." |
| ring | woman | "I DROPPED MY RING!" | "Don't tell my husband." |
| snorkel | man | "MY SNORKEL'S FULL!" | "A fish looked at me like I was the stupid one." |
| influencer | either | "LIKE AND SUBSCRIBE!" | "Where's my PHONE?!" |

**False alarm** (1.8): one of three, at random: knee-deep water ("You're standing up." / "In my defence, it's VERY
wet."), a tiny crab ("It's a very STRONG coin."), seaweed ("Seaweed with INTENT."). The screamer is somebody who was
wading (lent by the beach crowd) and goes back to wading after.

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
| Pistol (costs) | -250 (or whatever the team has, on credit) |

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
| **Sandy** (guide) | **In the game** (Meshy model, rigged, `AvatarLook.Bodies.Sandy`); boss look baked too (`Bodies.SandyBoss`). Still needs a talking face. |
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

**The beach crowd** (`Story/BeachCrowd.cs`, one per island): people sunbathing on towels (on the back with the hands
behind the head, on the belly kicking their feet, sitting up), wading in the shallows and swimming about; they turn over
now and then and chat when you press E. They're `StoryNpc`s (no physics). When the story needs someone in trouble it
takes a swimmer from the crowd, so the person you saw swimming is the one who starts drowning; scenes borrow people
(the thief's victim, the silent woman's friend) and give them back. Swimmers are topped up out of sight. Towels and
umbrellas are placed by the builder on dry sand away from buildings and palms.

**Story places.** Island 1: the old station shack is Sandy's Lost & Found (door taken off, a painted "LOST & FOUND"
sign on the roof and a "WE PAY" price chalkboard under the counter: pictures painted by `ArtSource/Tools/make_signs.py`,
because engine text looked cheap). She sits inside on a bar stool on a raised booth floor, looking out of the east window; hand things in at the
counter on the window's shelf, or walk in and talk to her (she takes them too, or has a chat). When the story needs
her she gets up, walks out through the doorway and back. The robber's jet ski is tied to the dock.
Island 2 (~200 m south, same terrain mesh): the Grand Coral Hotel (reception desk = shop, infirmary bed, first-aid
shelf), its own dock, the pirate boat parked far out. Coordinates are constants at the top of
`GameSceneBuilder.Story.cs`.

**Placeholders to replace with generated art** (the code only needs the named points): Sandy/receptionist/robber/pirate
looks are `AvatarLook` presets in `StoryDirector.Beats.cs`; the robber's bag is built in `StoryNpc.ShowBag`; the shark,
jet ski, boat, kiosk and hotel are primitives in `GameSceneBuilder.Story.cs`.

**Known limits.**
* NPCs follow a navmesh baked per island by the builder (`Assets/_Game/Data/Navigation`, `NavMeshLoader`), and every
  step is also checked (capsule sweep) against walls and against things that move: lifeguards, items, vehicles,
  tourists and other characters; they slide round them (knockbacks and pushes too). Crates, coolers, vehicles and
  tourists carry a `NavMeshObstacle` that carves the navmesh while they stand still, so routes go round a parked jet
  ski or a tourist lying on the sand. An NPC pinned for a while re-plans, then gives up.
* Players can't be hurt by pirates beyond knockback (the design keeps failure light).
* One save slot, host only. A client that joins mid-chapter-2 spawns on island 1 (`goto`/`tp` or the next beat moves them).
* Dialogue auto-advances; no choices yet.
* The hotel's upper floor is solid.

**Tuning** lives in the scene (`StoryDirector` island setups: sea boxes, difficulty, land area) and in code constants
(`Economy` pay, `VictimBrain` CPR gains, pistol price 250).

---

## 7. After the story: the open shift

The fish-them-out-and-launch-them loop is the game, so it doesn't stop at "To be continued". When beat 2.7 ends (or when a
save with beat `end` is loaded) the director calls `BeginFreePlay` (`Story/StoryDirector.FreePlay.cs`) and the game becomes
an endless **open shift** around the first island and hotel island:

* **Tourists keep coming.** Every 16-32 s (faster with more lifeguards) a tourist gets into trouble in the sea of whichever
  island a lifeguard is near (within 140 m of its sea), up to 3 at once (1 + players). Same gags-free profile and
  difficulty as the island's story rescues; 15 % are silent drowners. Rescues pay as always; lost ones still get the
  airlift bill.
* **Everything the story locked is open**: shop, rental fleet, the pirate boat, Sandy's kiosk (lost things keep turning up
  on island 1).
* **Trickshots pay** (`Fun/TrickShots.cs`, host, always on, not tied to the story): any tourist someone throws, and any
  lifeguard who flies (thrown by a friend, cannon, off a vehicle), is timed from launch to landing. 12 m or more pays
  `1.2 x distance + 2 x height (+20 for a beach-to-sea splash)`, max $150: NICE THROW / LONG SHOT (20 m) / TRICKSHOT (30 m) / ORBITAL (45 m).
  A tourist thrown again and again pays less each time (x1/(1+0.35 n), floor 30 %, forgotten after 5 minutes).
  Thrown back into deep water, the tourist is in trouble again: that is the loop. Disabled during the tsunami.
* **Landing feedback.** The floating reward and cheer are sent by `RescueService.ServerCallout` to all observers.
  Guns have no tourist air-shot reward or landing multiplier.
* Joining friends get a short "open shift" welcome instead of the chapter recap. `Esc > Restart the story` still starts
  over; `story off` stops story spawning, while throw rewards stay available. Stopping the host stops all story
  coroutines and clears trickshot flights. Starting a new host starts one fresh story/spawn loop.

Not done yet: the basketball hoop and cannon don't give tourists a bonus of their own, and open-shift pay isn't tuned.

Host restart check (separate Development player, no saved scene or story changes):
`-pdd-freeplaycheck -pdd-nosave -pdd-host-offline -pdd-nosteam -pdd-noinput -pdd-port 7794`.
It leaves during the open-shift spawner's wait, checks that no tourists spawn while stopped, then rehosts and checks
that fresh spawning resumes. Look for `[FreePlayLifecycle] RESULT PASS`; the process exits 0 on a clean pass.
