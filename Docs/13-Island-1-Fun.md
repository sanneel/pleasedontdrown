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

Sounds for all of it are made in code: `Audio/FunSounds.cs`.

Physics (from the Unity physics-3d-collision skill): the ball is Continuous Speculative with a bouncy material (combine
Maximum); the rim is 16 static capsules and the backboard a box; scoring is a sweep of the ball's path through the
rim's plane each physics step on the host, not a trigger (callbacks only fire where a body is simulated).

Tested by code: `dunk` scores a swish; a thrown ring brought a tourist in round the dock and credited the thrower; the
island 1 story (with the hut scene in beat 1.2 or 1.5) passes with a client. Not tested: punches between two real
players, the cannonball, how it all feels.
