# PLEASE DON'T DROWN: Game Design Doc v0.1

> Title: **PLEASE DON'T DROWN** (confirmed 2026-09-27).
> Status: concept → vertical slice planning. Date: 2026-09-27.

---

## 1. Pitch

You and up to 3 friends run a **terrible little lifeguard station** on a tourist island.
The island constantly throws emergencies at you: drowning swimmers, sharks, runaway jet skis,
a giant octopus. You grab whatever equipment is closest, rush out, drag people back,
pump their chest, get paid, and slowly turn your sad shack into a rescue empire.

**Core feeling:** the satisfying physics tools of *How to Fish* + the co-op panic of *Overcooked* +
the "no big loss, just laugh and fix it" tone of *Lethal Company* on a good day.

## 2. Design pillars

1. **Chaos is the content.** Systems collide (panicking tourists, boats, sharks, friends) and
   produce stories. We build systems, not scripted scenes.
2. **Every rescue is a 1–3 minute story.** No 30-minute runs to lose. Money is banked after every rescue.
3. **Rescue tools feel like weapons.** Recoil, projectile arcs, sounds, impacts, and trick shots are rewarded.
4. **You see what your money built.** Upgrades change the station and island visibly.

## 3. Core loop

```
      ┌──────────────────────────────────────────────────────────────┐
      ▼                                                              │
 SPOT ──► RUSH ──► REACH ──► SECURE ──► RETURN ──► STABILIZE ──► PAYOUT + GRADE ──► UPGRADE
 alarm/    grab      swim /    grab /     carry /     CPR / first    Rescue Report      station,
 binoculars vehicle  drive /   float /    tow /       aid minigame   card with bonuses  gear, vehicles,
 /PA call  + tools   dive      rope line  vehicle                                       island access
```

Second loop (session scale): **Shift → End-of-shift report → Evening (shop/upgrade) → Next shift.**

### 3.1 Shifts (decided 2026-09-27)
| Phase | Length | What happens |
|---|---|---|
| **Morning briefing** | ~30 s | PA announcer reads the day: weather, tourist count, a special "Today's Modifier". Ready-up bell starts the shift. |
| **Shift** | ~15 min (tunable) | Sun moves from morning to sunset. The director **ramps intensity** through the day: calm morning, busy afternoon, chaotic final minutes ("golden hour rush"). |
| **Closing whistle** | – | No new emergencies; active ones can still be finished (overtime pay). |
| **End-of-shift report** | ~30 s | Totals: rescues, lost-to-competition, earnings, best grade. Funny per-player awards: *Employee of the Day*, *Most Rescued Lifeguard*, *Most Property Damage*, *Hit the Most Tourists With a Boat*. |
| **Evening** | untimed | Station is safe: buy upgrades, cosmetics, test tools on dummies. All players ring the bell → next day. |

* **No lost progress:** money is banked after every rescue. Quitting mid-shift keeps everything earned; the shift just restarts next time.
* **Day counter** + **daily modifiers** keep shifts different: *Storm Day* (big waves, rip currents),
  *Festival* (2× tourists), *Shark Week*, *Heatwave* (more unconscious tourists on the beach),
  *Inspector Visit* (grades pay double). Absurd events like the Octopus are tied to special days.
* Solo/2-player: the director scales down so a shift stays manageable.

## 4. Victims (tourists)

A victim is a **physics body you can carry, tow, float and drop** (it's an *Item* in our architecture),
plus a small brain.

### 4.1 Victim stats
| Stat | Meaning |
|---|---|
| **Air** | Drains underwater / while drowning. At 0 → *Unconscious*. |
| **Panic** 0–100 | How badly they fight you. Rises in water, near sharks, when hit; falls when floated, held calmly, or on land. |
| **Condition** | Timer once unconscious. CPR must start before it runs out. |
| **Injury** | Increased by hits (boats, buoys to the face, octopus umbrellas). Reduces payout. |

### 4.2 Victim states
```
 Fine ─► Distressed ─► Panicking ─► Drowning ─► Unconscious ─► (CPR) ─► Saved
                 ▲          │                          │
                 └─ calmed ─┘                          └─ timer runs out ─► "Lost" (soft fail, see 4.4)
```

### 4.3 Panic mechanic (signature comedy system)
* **Calm** victims can be grabbed and towed normally.
* **Panicking** victims **grab onto the nearest swimmer**: they attach to the player, push them down,
  slow their swimming, and drain the *rescuer's* air.
* Now the rescuer is drowning too. **A teammate must rescue the rescuer.** (bonus: *RESCUED THE RESCUER*)
* Ways to lower panic: throw them a float/life ring, approach from behind, a vehicle pickup,
  "calm down" shout (voice-activated? stretch goal), the stun device (penalty: *QUESTIONABLE METHODS*).

### 4.4 Failure (keep it light)
Victims don't die on screen. If the condition timer runs out, the tourist is **"airlifted by the rival
lifeguard company"**: no payout, the rival's helicopter mocks you, and a *LOST TO COMPETITION* stamp appears.
The run continues and nobody loses progress.

## 5. Emergency Director

No mission menu. An **AI director** watches the island and spawns emergencies over time.

* **Intensity budget** rises over a shift, scaled by player count and station level.
* Each emergency has a **cost**; the director spends budget when players are idle and holds back when
  they're busy (at most N active emergencies, with a cooldown after a big one).
* Emergencies spawn **only in unlocked zones** and only if the team *could* solve them (the required gear is owned).
* **Rare absurd events** (Giant Octopus) have a very low chance, a long cooldown, and need a minimum station level.
* **Spotting:** some emergencies start *unspotted* (small splash, faint "help"). Players who spot them first
  (tower binoculars, walking the beach) trigger the alarm and earn **FAST RESPONSE**.
  Alarm = whistle + HUD marker + station PA.

### 5.1 Emergency types (roadmap)
| Tier | Emergencies |
|---|---|
| **Slice** | drowning swimmer, unconscious tourist on beach |
| **Early** | swept away by current, capsized kayak, jellyfish area, inflatable drifting offshore, ridiculous NPC swimming where they shouldn't |
| **Mid** | shark sighting, trapped on rocks, boat out of fuel, surfer carried offshore, tourist fell from cliff, runaway jet ski, missing child (search) |
| **Late** | sinking boat, stuck underwater, underwater cave rescue, multi-victim events |
| **Absurd** | GIANT OCTOPUS, (ideas: jet-ski gang, whale beaching, tourist on a stolen banana boat) |

Every emergency is a **data asset** (see Tech doc): victims to spawn, hazards, zone, timers,
base pay, bonus rules, director cost.

## 6. Payout and grading

The **Rescue Report** card is the dopamine hit at the end of each rescue.

```
 ★★★ PERFECT RESCUE ★★★
 Base pay                  +$200
 Fast Response             +$100
 Jet Ski Pickup             +$75
 Perfect CPR               +$100
 No Tourist Injuries        +$50
 Moving Save                ×1.5
 ──────────────────────────────
 TOTAL                     $787
```

* Bonuses are **detected automatically** from what happened (a tracker records events during the rescue).
* Flat bonuses add; a few special bonuses **multiply** (like How to Fish's stacked sell price).
* Bonus ideas: Fast Response, Long-Range Rescue (>30 m throw/launch), Moving Save (from a moving vehicle) ×1.5,
  Jet Ski Pickup, Perfect CPR, Team CPR, No Tourist Injuries, Rescued the Rescuer, Double Rescue,
  Air Time (vehicle jump during rescue), Close Call (<5 s left).
* Penalties: Tourist Injured, Property Damage, Hit Tourist With Boat, Questionable Methods.
* Grades: PERFECT / GREAT / GOOD / "TECHNICALLY ALIVE".

## 7. CPR / stabilize minigame

Simple, satisfying, readable, and chaotic with 2 players.

* **Compressions:** a timing bar with a moving marker; press on the beat (≈110 BPM feel).
  Good hits fill the *Revive* meter; misses shake the camera and play a comedic "crunch".
* **Rescue breaths:** after a set of compressions, a short 2-press timing input.
* **Two players:** one compresses and one breathes gives the *TEAM CPR* bonus (faster). Two players
  both trying compressions **bump each other**: alternating shoves, meter wobble, chaos.
* A **Perfect CPR** bonus comes from a high hit accuracy.
* Stretch: first-aid variants (jellyfish sting = vinegar spray minigame, shark bite = bandage wrap).

## 8. Equipment (rescue tools that feel like weapons)

| Tool | What it does | Feel |
|---|---|---|
| Life ring (throwable) | Floats, calms victim, victim grabs it | Physics throw, splash |
| **Buoy launcher** | Fires a float 40 m+, calms victim on landing nearby | Big recoil, arc, *LONG-RANGE RESCUE* |
| **Rescue-line gun** | Tethers victim/object to you or a vehicle, reel in | Rope physics, reel sound |
| Grappling launcher | Tether to rocks/boats; swing/pull | Works from moving vehicles → *MOVING SAVE* |
| Water cannon | Push sharks/jellyfish/boats/friends away | Continuous stream + knockback |
| Net launcher | Scoop jellyfish / catch falling tourist | Unfolding net projectile |
| Harpoon / speargun | Hostile sea creatures (sharks, octopus) | Heavy projectile, pins targets |
| Flare launcher | Mark locations, scare sharks, signal helicopter | Bright arc, smoke |
| Inflatable launcher | Spawns an inflatable raft on impact | Comedic inflate pop |
| Stun device | Stops panic instantly (with a penalty) | Zap |
| Rescue board | Paddle out slowly, carry 1 victim | Wobbly, funny |
| Binoculars | Spot emergencies early | Zoom UI |
| First-aid kit | Required for CPR/first aid upgrades | |

**Starting kit:** one rusty rescue board, two cheap life rings, a terrible little boat, a basic first-aid kit.

## 9. Vehicles

| Vehicle | Seats | Notes |
|---|---|---|
| Rusty rescue board | 1 (+1 victim) | Slow paddle, tips over |
| Terrible little boat | 2–3 | Sputters, engine stalls |
| **Jet ski** | 2 (driver + back) | Fast, jumps waves; back-seater can grab/throw/shoot |
| Rescue boat | 4 | Stretcher deck, mounted buoy launcher |
| Beach buggy | 2 | Land speed; skins |
| Underwater scooter | 1 | Late game, caves |
| Helicopter access | 2+ | Winch rescues; very late game |

Vehicles **hurt things on impact**: running over a teammate knocks them flat (ragdoll for a second).
"The boat comes flying onto the beach and knocks everyone over" should happen naturally.

## 10. Island and zones

The island unlocks outward. Each zone enables new emergency types.

1. **Main Beach** (start): swimmers, beach unconscious, drifting inflatables
2. **Surf Bay**: currents, surfers offshore, capsized kayaks
3. **Rocky Point**: trapped on rocks, cliff falls
4. **Reef**: sharks, jellyfish, divers
5. **Harbor**: boats out of fuel, sinking boats, runaway jet skis
6. **Caves** (late): underwater cave rescues
7. **Offshore** (late): helicopter, multi-victim events, the Octopus's home

## 11. Progression

**Station → Equipment → Vehicles → Island access**, all bought with a shared team wallet.

* The station is **modular and visibly upgrades**: shack → tower → proper HQ. Each upgrade replaces
  or adds a physical piece (a new tower, garage, medical tent, dock, helipad).
* Upgrades also unlock director content (e.g., a dock unlocks harbor emergencies).
* **Money sinks for fun:** giant speakers, ridiculous sunglasses, station decorations,
  buggy/jet-ski skins, stupid sirens, a mascot statue.
* Money is saved after **every** rescue.

## 12. Tone and presentation

* Bright, stylized low-poly beach. Readable silhouettes for victims (floaties, sunburns, loud shirts).
* Fake-serious rescue-company voice: the PA announcer, the corporate "Rescue Report" stamps, a rival company.
* Proximity voice chat so yelling "I GOT HIM" happens in world space.

## 13. Open questions

1. ~~Session structure~~ → **Decided: shifts** (see §3.1).
2. **Rival company:** flavor only, or an actual mechanic (steals unattended emergencies)?
3. Player count: 1–4 confirmed. Is solo viable? (The director scales intensity down; NPC helper upgrade?)
4. First-person only, or a third-person option?
5. How much gore/injury? (Suggest: cartoon only, no blood.)

## 14. Vertical slice (first playable)

**Goal:** one complete, fun, co-op drowning rescue on a small beach.

* Small beach + shallow water + the rusty station shack
* 2–4 players (Steam) + solo (local host)
* Walk, swim (stamina/air), dive, pick up, carry, throw
* Victim with air/panic/unconscious states; the panic grab mechanic
* Life ring (throwable) + buoy launcher (projectile)
* Jet ski with 2 seats
* Director spawning drowning swimmers + beach unconscious tourists
* CPR minigame (solo + team)
* Rescue Report card with ~6 bonuses; shared money
* 1–2 purchasable upgrades that visibly change the station
* Dev cheat menu from day one
