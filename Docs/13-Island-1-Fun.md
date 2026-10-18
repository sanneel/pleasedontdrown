# Island 1: fun and games

Things to do on island 1 besides the three story rescues (2026-10-04). Positions are constants at the top of
`Editor/GameSceneBuilder.Fun.cs`; models are in `ArtSource/Tools/model_props.py`.

| What | How it plays | Code |
|---|---|---|
| **Sandy's hatch** | The shack's east window is an open serving hatch with its shutter propped up as an awning; Sandy sits behind the counter. | `model_props.py` shack() |
| **Rescue rings** | A table of three rings by the tower (`ItemRack` keeps it stocked, old ones far away are cleared). Throw one (hold G) near somebody in trouble: within 1.6 m they grab it and kick for the beach, round docks and posts. The thrower gets the rescue and the pay. | `Items/ItemRack.cs`, `Rescue/VictimBody.cs` (paddling), `VictimBrain.Rescued` (credit), `Item.LastHolder` |
| **Basketball** | A hoop behind the station (backboard toward land), three balls at its foot, white dots for the three-point line (6 m). Baskets: +2, +3 from beyond the line, "SWISH" if it never touched the rim; the net kicks, the beach cheers, a toast keeps your total. | `Fun/BasketballHoop.cs`, `Fun/BallSounds.cs`; test: `dunk` |
| **Friendly fire** | Three quick punches on another lifeguard within 3 s knock them flat for 2.5 s: they drop, can't move, the view tips onto the sand, stars circle their head, their googly eyes spin, slide whistle and birds. Anything thrown that hits a person goes BONK; a heavy fast one (coconut) knocks a lifeguard flat too. NPCs shout "OW!". | `Combat/PlayerCombat.Fun.cs`, `Fun/ThrownImpact.cs`, `Fun/DizzyStars.cs`, `AvatarFunny.Dizzy` |
| **Cannonball** | Jump into deep water from height (tower deck, dock): a big splash and a rating out of 10 ("BELLY FLOP" ... "PERFECT!"). | `PlayerCombat.Fun.cs` |
| **The beach hut** | A pink-and-white hut with a heart on the gable and a real door. A woman brought back with the kiss of life on island 1 gets up, says thank you "properly", takes her hero by the hand and walks them in. The door shuts: the hero's screen goes dark ("what happens in the hut stays in the hut"), everyone outside sees the hut wobble and hears bed springs, "ooh"s, a boing and a cuckoo clock. Sandy shouts that she saw nothing. Out they come, she leaves a $25 tip and strolls off. Nothing is shown. | `Story/LoveHut.cs`, `StoryDirector.Gags.cs` (LoveHutScene), `World/Door.ServerSet` |
| **Trampolines** | Two by the station (-20, 14) and (-24.5, 18.5): hop onto the frame and you bounce about 3 m up, again and again; loose things bounce too. | `Fun/BouncePad.cs` |
| **Human cannon** | East of the tower, on a wheeled carriage: Interact to climb in. While the fuse burns (4 s) you aim: the cannon swings round on its wheels (up to 80 degrees either way) to where you look and the barrel tips 15-60 degrees; click or jump to fire early. BOOM, out to sea (and a cannonball rating when you land). Carrying a friend? Interact with the cannon to load *them* in. | `Fun/HumanCannon.cs` |
| **Picking up a lifeguard** | Look at another player and Interact: they lie back in your arms. Tap Drop to put them down, hold Drop and let go to throw them (click throws hard), or load them into the cannon. They mash Jump to wriggle free (6 presses); after 25 s you put them down anyway. | `Player/PlayerCarry.cs` |
| ~~Zipline~~ | Out for now (2026-10-05): `BuildZipline` isn't called; the code (`Fun/Zipline.cs`) and models stay for when it comes back. | |
| **Banana boat** | Towed by the lifeguards' own jet ski (no keys, parked in front of the banana's nose; sit on it and the banana hitches on). One lifeguard drives, another rides the banana; a sharp turn at speed flings the banana rider into the sea. (Only that jet ski tows: the robber's one is the story's ride to the hotel island.) | `Fun/BananaBoat.cs` |
| **Diving board** | Off the side of the dock near its end, over deep water: step onto the springy tip. | `BouncePad` |
| **Flamingo float** | A Meshy model (text to 3D, Meshy 6 Lite + texture, CC BY 4.0): a slow paddling ride, no keys, near the dock. | `GameSceneBuilder.Attractions.cs` BuildFlamingo |
| **Parrots** | Instead of seagulls: 8 cartoon macaws (red, green, blue) on the palm crowns and the beach hut's roof. They look about, squawk and say cheeky lines ("PLEASE DON'T DROWN!"), fly off when you get within 3.5 m, circle the island and glide back to a free perch. Cosmetic, each machine flies its own. | `Fun/Parrot.cs`, `GameSceneBuilder.Parrots.cs` |

**Easier basketball:** a firm throw within 35 degrees of the hoop (1-13 m) goes on a high arc to the rim
(`BasketballHoop.TryAssist`, solved by simulating the flight), and a rim magnet eases near misses through the middle
(on whichever machine simulates the ball). Measured with `shots <distance> <count>`: 3 m 18/20, 6.5 m 15/20, 9 m 5/15.
(Not the ball's collision mode: Continuous instead of Continuous Speculative didn't change it; the misses were the arc.)

**Smaller island 1:** 112 x 48 m instead of 160 x 68 (`IslandCenter`/`IslandHalfSize` in GameSceneBuilder.cs; the station
beach's front edge is unchanged). Palms outside it moved in; the robber now runs off to (+-44, 40). Towels keep clear of
every attraction (`AttractionSpots`).

Sounds for all of it are made in code: `Audio/FunSounds.cs`.

Physics (from the Unity physics-3d-collision skill): the ball is Continuous (swept against the static rim) with a bouncy material (combine
Maximum); the rim is 16 static capsules and the backboard a box; scoring is a sweep of the ball's path through the
rim's plane each physics step on the host, not a trigger (callbacks only fire where a body is simulated).

Removed on request: the water slide and the soccer goal.

Tested by code: `dunk` scores a swish; a thrown ring brought a tourist in round the dock and credited the thrower; the
island 1 story (with the hut scene in beat 1.2 or 1.5) passes with a client. Not tested: punches between two real
players, the cannonball, how it all feels.

## The beach village (2026-10-18)

Asked for by the user: bigger buildings (Sandy's house and the toilets stay as they are), a bigger bar, and the empty
middle filled. Built by `GameSceneBuilder.Island1TownBatch` (`Editor/GameSceneBuilder.Island1Town.cs`) as edits to the
saved scene; it replaces what it made before, so **run it again after taking someone else's Game.unity in a merge**.

| What | Where / how |
|---|---|
| **Lifeguard station** | Replaces the red-roof house (`Environment/Tower` is gone; now `Environment/LifeguardStation`, same spot). Two storeys: a room you walk into (real door), a roof deck over the porch with a LIFEGUARD board, a lookout cabin with open windows and a stool, outside stairs on the west side. Model `lifeguard_station` (model_props.py), colliders from the same numbers (`St*` constants). |
| **Beach bar** | Same object, big model `beach_bar_big`: an 8.8 m counter with 8 stools, a terrace with 3 bar tables and tiki torches. The barista, his 8 serving spots, the wipe and drink spots moved with the counter (ordering a beer tested). |
| **Boardwalks** | From both docks to a promenade behind the station (z 18), up to the toilets, the bar's terrace and the lighthouse (`Island1Boardwalks`). |
| **Welcome arches** | PLEASE DON'T DROWN arches where the west dock and Milo's pier land (`welcome_arch`). |
| **Sunbeds** | 18 sunbeds in pairs with striped umbrellas: two rows in the middle, one on the west side. |
| **Volleyball** | A court with a net east of the bar (`volleyball_net`, its lines on the sand). |
| **Dunes** | Four low dunes in the terrain itself (`Island1Dunes`, added in `BeachHeight`), with beach grass, rocks and a rope fence. |
| **Lighthouse** | Tripo (`.claude/skills/pdd-tripo/prompts/island1.md`), 16 m, at the back (22, 47.5), door toward the beach, rocks round its foot, mesh collider. |

Palms, towels and loose items that stood where these went were moved to free sand (the batch logs every move to
`Logs/island1-town.txt`). Cost on Low: about +0.2 ms a frame at the spawn (3.1 ms). The island 1 story autoplay
still runs through to 2.2.
