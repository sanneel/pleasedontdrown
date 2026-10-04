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
| **Human cannon** | East of the tower: Interact to climb in, the fuse fizzes, BOOM, and you fly about 20 m out to sea (and get a cannonball rating when you land). | `Fun/HumanCannon.cs` |
| **Zipline** | A 4.5 m platform with a ramp at (35, 7); grab the handle under the gantry and whizz 37 m out to a post in the sea, let go into deep water. | `Fun/Zipline.cs` |
| **Beach soccer** | A goal at (-37, 12) facing along the beach, a beach ball kept in front of it; kick, punch or throw it in: GOOOOAL! | `Fun/SoccerGoal.cs` |
| **Diving board** | Off the side of the dock near its end, over deep water: step onto the springy tip. | `BouncePad` |
| **Flamingo float** | A Meshy model (text to 3D, Meshy 6 Lite + texture, CC BY 4.0): a slow paddling ride, no keys, near the dock. | `GameSceneBuilder.Attractions.cs` BuildFlamingo |

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

Tested by code: `dunk` scores a swish; a thrown ring brought a tourist in round the dock and credited the thrower; the
island 1 story (with the hut scene in beat 1.2 or 1.5) passes with a client. Not tested: punches between two real
players, the cannonball, how it all feels.
