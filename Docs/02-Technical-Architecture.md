# PLEASE DON'T DROWN: Technical Architecture v0.1

> How we'll build the game in [01-Game-Design.md](01-Game-Design.md).
> Patterns come from our study of How to Fish (`F:\HowToFish-Modding\Architecture\How-To-Fish-Architecture.md`).
> All code is written fresh for this project.

---

## 1. Tech stack

| Area | Choice | Why |
|---|---|---|
| Engine | **Unity 6 LTS**, URP | Same proven stack, good water/physics, free until revenue threshold |
| Networking | **FishNet** | Free, open source, host/client, SyncVars, tick system, prediction |
| Online transport | **FishySteamworks** (Steam P2P relay) + **Multipass** | Friends-only lobbies, NAT-free |
| Offline | Unity Transport on localhost | Solo = local host (one code path) |
| Steam | Steamworks.NET | Lobbies, invites, achievements. Dev app ID **480** until we have our own |
| Input | Unity Input System | Rebinding, gamepad |
| UI | uGUI + TextMeshPro (+ DOTween/LeanTween for juice) | |
| Voice | Evaluate: Dissonance / FishNet voice add-on | Proximity chat, needed early for comedy |
| Art (prototype) | Primitives + CC0 low-poly packs (Kenney, Quaternius) | Fast, legal, replace later |
| Version control | git + Git LFS (on GitHub/GitLab) | |

## 2. Scenes

```
Bootstrap.unity   → loads settings, Steam init, then Game.unity
Game.unity        → persistent: NetworkManager, services, water, player prefabs, UI, director
Zone_MainBeach.unity, Zone_SurfBay.unity, ...  → loaded ADDITIVELY and kept loaded once unlocked
```
Difference from How to Fish: they load **one island at a time**. We have **one island with zones**, so
unlocked zones stay loaded (use LOD and culling). The host syncs a "zones unlocked" bitmask.

## 3. Networking rules

**Topology:** host + up to 3 clients. Solo = host+client on localhost.

### 3.1 Authority table
| Data | Authority | Sync |
|---|---|---|
| Player movement | Owning client | Pos/rot every tick (unreliable), others interpolate |
| Swimming state, air, stamina | Owning client computes, host mirrors | SyncVar (host-validated ranges) |
| Item physics (props, tools, **victims**) | **Current simulator** (whoever touches it; host when at rest) | See §5 |
| Victim brain (panic, air, state) | **Host** | SyncVars |
| Vehicles | **Host simulates**, driver sends input | Input as `half`, host snapshots, passengers in vehicle-local space |
| Emergencies / director | **Host** | NetworkObjects + SyncVars |
| Money, upgrades, zones | **Host** | SyncVars / SyncList |
| Projectiles | Everyone simulates from one spawn message | ObserversRpc (owner, type, tick, pos, vel) |
| Ropes/tethers | Host owns the link; the endpoint simulator runs the joint | SyncVar (endpoints + length) |
| CPR minigame input | Local client | Beat results → host → host resolves revive |

### 3.2 Conventions
* **Per-system network services**, not one god class: `ItemNet`, `VehicleNet`, `RescueNet`,
  `ShopNet`, `CprNet`, `ChatNet`. Every ServerRpc **validates** (exists? in range? holding it? can afford?).
* **Client prediction + reconcile** for pickups, seat entry, tool fire.
* All periodic sends happen in the **network tick** (`TimeManager.OnTick`), never in `Update`.
* Content IDs are **`ushort`** (not byte). Positions on vehicles are sent in **vehicle-local space**.

## 4. Core object model

```
NetworkBehaviour
└── Item                       carry / throw / float / inventory / physics sync / save
    ├── Tool                   held, has input handlers, hand poses
    │   ├── ThrowableTool      life ring, flares (hand-thrown)
    │   ├── LauncherTool       buoy launcher, harpoon, net, flare gun, inflatable → ProjectileSystem
    │   ├── TetherTool         rescue-line gun, grappling launcher → RopeSystem
    │   ├── StreamTool         water cannon (continuous force cone)
    │   └── UtilityTool        binoculars, first-aid kit, stun device
    ├── Victim                 ragdoll body + VictimBrain (panic/air/state) + Rescuable
    ├── Floatable              life ring, inflatable raft, rescue board (victims can grab)
    └── Prop                   beach umbrellas, coolers, debris (octopus ammo)

Interactable (not carried)     shop stands, station upgrade slots, vehicle seats, alarm bell, PA
Vehicle (NetworkBehaviour)     seats[], hidden physics body, buoyancy points, input
Creature (NetworkBehaviour)    shark, jellyfish swarm, octopus (+ tentacles)
```

**Input routing:** `PlayerHolding` forwards Primary/Secondary/Reload/Inspect to the held Tool.
Seats temporarily redirect movement input to the vehicle; the back-seater keeps their tool input,
which is how firing a rescue line from a moving jet ski works.

## 5. Physics sync (`ItemSync`)
Straight from the study, which was proven to work:
1. One **simulator** per item; it runs physics and sends pose each tick.
2. Others go kinematic and blend with a short curve.
3. Authority transfers to whoever **grabs / throws / hits / tethers / seats** it.
4. At rest → hand back to the host → stop sending.
5. Ragdolls (victims) sync **only the torso**. The limbs are local bodies on joints on every machine,
   posed by joint drives from the synced state (waving, thrashing, limp), so they dangle naturally everywhere
   for free. Limbs live outside the torso's hierarchy (moving the torso transform must not drag them rigidly)
   and are shifted along when the torso snaps or teleports.
6. Bodies that move by themselves (a struggling swimmer) set `KeepAwake`: always streamed, never "at rest",
   and handed back to the host 3 s after the last touch. "Float rest" only applies at the surface
   (something lying on the seabed rests like it would on land).
7. Items on a vehicle are synced in the vehicle's local space.

## 6. New systems (not in How to Fish)

### 6.1 Emergency Director (host only)
```
DirectorService (tick @ 1 Hz)
  budget += rate(playerCount, stationLevel, shiftTime) * dt
  if activeEmergencies < max && cooldown <= 0:
      candidates = EmergencyDefinitions where
          zone unlocked && requiredGear owned && cost <= budget && !onCooldown(type)
      pick weighted random (rare events tiny weight + long cooldown)
      spawn → budget -= cost
```
**`EmergencyDefinition`** (ScriptableObject): id, zone tags, spawn-point tags, victims to spawn
(+ initial state), hazards (shark, current volume, jellyfish), time limit, base pay, bonus rules,
director cost, cooldown, "spotted on spawn?" flag.

**`Emergency`** (runtime NetworkObject): state (Unspotted → Active → Resolved/Lost), victims, timer,
`RescueTracker`, HUD marker.

### 6.2 Victim (Item + VictimBrain)
* Stats: Air, Panic, Condition timer, Injury (host SyncVars).
* State machine: Fine → Distressed → Panicking → Drowning → Unconscious → Saved | Lost.
* **Panic grab:** when a panicking victim touches a swimming player, the host creates a **grab link**
  (joint victim ↔ player). The grabbed player gets a movement debuff + downward force + air drain.
  The link breaks when panic drops (float given, teammate pulls, stun) or the pair reaches the shallows.
* Being on a Floatable or vehicle, or being held calmly, reduces panic.
* Unconscious → carryable ragdoll; the condition timer starts; CPR is required.

**As built (M4):**
* `Rescue/VictimBrain` (host): SyncVars name, look seed, state, air, panic, condition, CPR progress, ashore.
  Panic rises in deep water (faster with the head under); air drains while panicking/drowning (faster with the
  head under). Held by a lifeguard or hanging on a `Floatable` = calming + breathing. Conscious and in water
  shallower than `Shore.StandDepth` (0.9 m) = **Saved** (credited to the holder, or whoever let go < 8 s ago);
  a saved tourist only gets back in trouble deeper than `Shore.DeepDepth` (1.4 m) so waves can't flip it.
  Timeline when ignored: ~20 s distressed, ~35 s panicking, ~9 s drowning, then 90 s of condition.
* `Rescue/VictimBody` (every machine): torso + head body (the `Item`), four limb bodies with `ConfigurableJoint`
  slerp drives. Where the torso is simulated it swims: float spring holding the head out, upright torque,
  panic dunks, thrashing, drift to a held float; unconscious = denser (sinks slowly) and tips face-down.
  Saved/fine tourists stand and wade up the seabed slope, then sit on the sand. It also provides the hold pose
  (`IHoldPose`: carried across the arms on land, towed on their back at the surface in water).
* CPR placeholder until M6: unconscious + ashore + not held → Secondary button pumps (15 pumps, decays when
  nobody pumps, condition drains slower meanwhile). `IInteractionSecondary` adds the second prompt.
* `Rescue/RescueService` (scene): drills from the station board (random deep spot, bell, announcement),
  console commands, and the spawn API the director (M9) will use. `UI/RescueHud`: markers (edge-pinned
  off-screen) with state, distance, air or time left, CPR progress, and the carry hint.

### 6.3 Rope / tether system
Lesson from the fishing rod: **a rope is a distance joint with a max length**, not a simulated chain.
* `Tether` = two endpoints (Item/Vehicle/static anchor) + max length + reel speed.
* The simulator of the heavier or owned end runs the joint; length is a host SyncVar.
* Rendered as a sagging curve (catenary approximation) with a LineRenderer.
* Used by: rescue-line gun, grappling launcher, towing victims or boats with vehicles.

### 6.4 Projectile system
Same pattern as the study: projectiles are **data** (struct array), simulated on all clients from
one spawn message, hit-scanned per physics step, drawn with GPU instancing.
Heavy "physical" projectiles that must persist (buoy lands and floats, net, inflatable) **spawn a real
Item on impact** (host), so the flight is cheap and the result is a synced physics object.

### 6.5 Rescue tracking and grading
`RescueTracker` (host, per emergency) records events: first-spotter + time, who touched the victim,
vehicle used, launch distances, whether the shooter was on a moving vehicle, injuries, CPR accuracy,
time remaining. On *Saved*, `RescueGrader` runs a list of **`BonusRule`** ScriptableObjects
(condition + flat or multiplier) → the `RescueReport` is broadcast → a card appears for everyone → money is added.
Pattern taken from the study's kill-score bonus calculator, but data-driven.

### 6.6 CPR minigame
* Starting CPR requires an unconscious victim on land + a first-aid kit nearby (station/kit upgrade).
* Each participant runs the timing UI **locally**, sending beat results (hit quality 0–1, timestamp)
  to the host via `CprNet`.
* Host: revive meter += quality; two compressors in the same window → "bump" event (shove + wobble);
  compressor + breather → team speed bonus.
* Output: success/fail + accuracy → RescueTracker.

### 6.7 Swimming, water and currents
* Ocean: base height + 2–3 Gerstner waves; **the same function on CPU and in the shader** so buoyancy
  matches visuals. Tiled water around players.
* Buoyancy points on items, victims, vehicles.
* **Swimming:** stamina (surface) and air (dive). A panic grab reduces both.
* **Currents:** `CurrentVolume` (box/spline) applies flow force to bodies inside it; the director
  can spawn temporary rip currents.

### 6.8 Vehicles
Generalized from the study's boat: host-simulated hidden rigidbody, buoyancy points, anti-capsize,
motor force at the propeller, `Seat` components (driver / passenger / stretcher), and exit/enter via Interactable.
Vehicle-to-player impacts above a speed threshold → **knockdown** (player ragdoll for ~1.5 s, then
get up). This is the "boat flies onto the beach" comedy.

### 6.9 Station upgrades
`StationUpgrade` (ScriptableObject): cost, prerequisites, unlocks (tools/vehicles/zones/director tags),
and **visual swap**: enables/disables named station parts. `StationService` (host) holds the
purchased set (SyncList), and all clients apply the visuals from it. Cosmetics use the same path.

### 6.10 Shifts (day cycle)
`ShiftService` (host) runs a synced state machine:
`Briefing → Shift → Closing → Report → Evening → (all ready) → Briefing`.
* Host SyncVars: `phase`, `dayNumber`, `shiftStartTick`, `shiftLengthSec`, `todaysModifierId`, ready flags.
* Clients derive the **time of day** (sun angle, lighting, music) from ticks, so there's no per-frame sync.
* The director reads `shiftProgress` (0..1) through an intensity curve (calm → busy → golden-hour rush)
  and is disabled outside `Shift`. `Closing` lets active emergencies finish (overtime).
* `DayModifier` (ScriptableObject): weather preset, wave height multiplier, tourist count multiplier,
  emergency weight overrides, payout multipliers, special-event unlocks.
* `ShiftStats` (host) collects per-player counters for the funny end-of-shift awards.
* Evening phase: shops enabled, director off, "ring the bell" ready-up via Interactable.

## 7. Save system
* **World save** (host): day number, money, purchased upgrades, zones, vehicles and their skins, stats, save version.
  Saved at the end of each shift, after every rescue (money), and on quit. A quit mid-shift resumes at that day's briefing.
* **Local save** (player): cosmetics, settings, personal stats.
* Crash-safe: write `.tmp` → validate → replace → keep `.backup`; load falls back to `.backup`.
* Save after **every rescue**, on the auto-save timer, **and on quit/focus loss**.
* `saveVersion` + migration functions from day one.

## 8. Dev tools (from day 1)
* Cheat console (`~`) + chat `/commands`: `/spawn <emergency>`, `/money`, `/god`, `/unlock all`,
  `/tp <zone>`, `/director pause|fast`, `/victim panic 100`.
* Network debug overlay (ping, simulator of the looked-at item, tick).
* Director debug view (budget, candidates, cooldowns).

## 9. Project layout
```
PleaseDontDrown/                  (git repo root = Unity project)
  Docs/                           these documents
  Assets/_Game/
    Core/        Bootstrap, GameConfig, ContentRegistry, SaveService, Cheats
    Net/         ConnectionService, SteamLobbyService, LobbyAuthenticator, *Net services
    Player/      PlayerHub, Movement, Swim, Camera, Hands, Holding, Vitals, Knockdown
    Items/       Item, ItemSync, Buoyancy, Inventory, Floatable
    Tools/       Tool bases + concrete tools
    Vehicles/    Vehicle, Seat, VehicleSync
    Rescue/      Victim, VictimBrain, Emergency, Director, RescueTracker, Grader, Cpr
    World/       Water, Currents, Zones, Station
    Creatures/   Shark, Jellyfish, Octopus
    UI/          HUD, RescueReportCard, CprUI, Menus
    Data/        *.asset (emergencies, bonus rules, tools, upgrades)
  Assets/ThirdParty/   FishNet, FishySteamworks, art packs
```

## 10. Milestones

| # | Milestone | Definition of done |
|---|---|---|
| **M0** | Project, FishNet, Steam lobby (app 480), solo via localhost | 2 PCs join and see capsules move |
| **M1** | First-person controller, look-interact, hover outline, cheat console | Walk, look, use things |
| **M2** | Item + pickup/carry/throw + **ItemSync** authority | Two players toss a crate smoothly |
| **M3** | Water (waves CPU+GPU), buoyancy, **swimming/diving** (stamina, air) | Swim out; crates bob identically |
| **M4** | **Victim** (ragdoll + brain), drowning → unconscious, carry to shore | Drag a drowning tourist back |
| **M5** | **Panic grab** + rescued-the-rescuer | The funny moment works |
| **M6** | **CPR minigame** (solo + team + bump) + RescueTracker + **Report card** + money | Full loop pays out |
| **M7** | Life ring + **buoy launcher** (projectile system) | Long-range rescue bonus |
| **M8** | **Jet ski** (2 seats), vehicle knockdown | Pickup from a moving jet ski |
| **M9** | **Director** + 2 emergency types, alarm/HUD, **shift cycle** + end-of-shift report | Emergencies arrive on their own during a day |
| **M10** | Station upgrades (visual) + save/load | Vertical slice complete |

After the slice: rescue-line gun + ropes, shark + water cannon/harpoon, currents, more zones, the Octopus.

Progress: M0–M4 done (plus a "feel pass" between M3 and M4: physics-body player, physics-held items).
**Story mode (chapters 1-2)** was built ahead of M5-M10 on request: StoryDirector, NPCs, dialogue, money, Lost & Found,
shop, punching, pistol, defibrillator, shark, jet ski and pirate boat (`Vehicle`), the hotel island. See
[05-Story-Mode.md](05-Story-Mode.md). Several pieces are first versions of milestone systems (money = M6's wallet,
`Vehicle` = M8's jet ski, the story director's spawning = a scripted M9 director).
M4 also brought a first taste of M6/M7: placeholder tap-CPR, and tourists grabbing a floating life ring.
