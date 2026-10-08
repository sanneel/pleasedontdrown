# PLEASE DON'T DROWN: notes for Claude

Co-op (1 to 4 players) first-person lifeguard comedy game. Unity **6000.3.25f1** (Unity 6.3 LTS), URP 17.3,
FishNet 4.7.3 networking, Steam through FishySteamworks. One scene: `Assets/_Game/Scenes/Game.unity`.
Islands: island 1 (story chapter 1), island 2 (Grand Coral resort, chapter 2), a dev island for testing.

## Who you are working with

Most people on this team are **not programmers**. They describe what they want in plain words (sometimes in
Georgian, sometimes with typos); you do all the code, asset and git work.

- Answer in the language they wrote in. Keep explanations short and concrete, with no jargon.
- Work out what they mean from the game, not from the literal words. If two readings would lead to different
  changes, ask one short question first.
- Make the change yourself. Never ask them to edit code or YAML by hand.
- After every change, tell them exactly how to see it in Unity (which menu, which console command, what to look for).
  Use the `test-change` skill.
- You usually cannot run Unity yourself. Say so plainly, never claim something works in game unless it was
  actually run, and read your own diff carefully for compile errors before saying you're done.
- Before anything risky or hard to undo (rebuilding the scene, deleting assets, rewriting history, big refactors),
  explain what will happen and ask.
- Writing style for code, comments, docs and commit messages: plain English, no unnecessary comments, match the
  code around it, never use `--` or em dashes, and don't sound like an AI.

## Skills in this repo (`.claude/skills/`)

| Skill | Use it when |
|---|---|
| `get-latest` | Starting work, or the person says "update", "get the new version", "pull". |
| `save-work` | They say "save", "push", "upload", "send to the team", or a piece of work is finished. |
| `test-change` | After any change: give a test recipe for Unity. |
| `fix-bug` | Something is broken, stuck, glitchy or "weird". |
| `story-change` | Story beats, dialogue, gags, rescues, NPC behaviour. |
| `add-model` | Bringing in or replacing a 3D model (Tripo, Meshy, a GLB/FBX file, Blender). |

## Project map

All game code is in `Assets/_Game/` (assembly `PleaseDontDrown`); editor-only tools in `Assets/_Game/Editor/`.

| Folder | What lives there |
|---|---|
| `Player/` | `PlayerHub` (the player root, links everything), `PlayerMotor` (physics body: walk, swim, climb), `PlayerLook`, `PlayerHands` (inventory, held items), `PlayerCarry` (carrying other players), `PlayerAvatar`, `FirstPersonArms` (your own hands). |
| `Avatar/` | Procedural cartoon characters: `AvatarRig`, `AvatarAnimator` (all procedural animation), `AvatarLook` (looks packed into a ulong), `AvatarHands`. |
| `Rescue/` | Drowning tourists: `VictimBrain` (state, panic, air, CPR, revive; host owned), `VictimBody` (physics body and limbs, standing up, walking), `RescueService` (spawning, console commands), `Shore` (ground and water depth queries). |
| `Story/` | `StoryDirector` (+ `.Beats`, `.Gags`, `.Tsunami`): the story as coroutines. `StoryNpc` (+ `.Brains`): walking, talking characters. `BeachCrowd` (ambient tourists). `DialogueService`, `LostAndFound`, `ShopCounter`, `Economy`, `LoveHut`, `HospitalBed`. |
| `Items/` | `Item`, `ItemSync` (who simulates an item), prefabs in `Items/Prefabs`. |
| `Combat/` | Guns (`Weapons/Weapon`, `ProjectileSystem`, `Melee`), punching (`PlayerCombat`). |
| `Vehicles/` | Jet skis and boats (`Vehicle`), rentals (`RentalMooring`). |
| `World/` | Water (`Water/WaterSurface`, `Seabed`, `SplashFx`, `UnderwaterFx`, `TsunamiState`), doors, palms, signs. |
| `Fun/` | Attractions: banana boat, cannon, basketball, parrots, barista, bounce pads. |
| `Audio/`, `Core/` | Sound (mostly synthesized at run time; recorded water clips in `Resources/Audio`), input, settings (`LookSettings`, `SoundSettings`, `PictureSettings`), dev console (`DevCommands`). |
| `UI/` | HUD and menus, all IMGUI (`OnGUI`). Options menu: `DevConnectMenu`. |
| `Dev/` | Test tools: `StoryAutoplay`, `MoveCheck`, `DevIsland`, teleport pads. |
| `Editor/` | `GameSceneBuilder*` (builds the scene from code, see below), import and review tools for Meshy, Tripo, Blender. |

Other top-level folders: `Docs/` (design and as-built notes), `ArtSource/` (raw models, `.blend` files and the
Python/Blender scripts in `ArtSource/Tools/`), `Tools/` (test and helper scripts), `Packages/com.tripo3d.unitybridge`
(Tripo bridge, locally modified: see `Docs/Tripo-Unity-Bridge.md`).

## Read the doc before touching a system

| Topic | Doc |
|---|---|
| Story, beats, gags, console commands for the story | `Docs/05-Story-Mode.md`, `Docs/14-Tsunami-Island-1.md` |
| Island 1 attractions | `Docs/13-Island-1-Fun.md` |
| Island 2 resort and hotel | `Docs/Island-2-Resort.md`, `Docs/Hotel-Finish.md` |
| Guns | `Docs/08-Weapons.md` |
| Dev island (test sandbox) | `Docs/09-Dev-Island.md` |
| Multiplayer with Steam | `Docs/10-Steam-Friends.md` |
| Player character and customization | `Docs/12-Funny-Player.md`, `Docs/CharacterCustomization.md` |
| Models: Meshy, Tripo, polish | `Docs/04-Meshy-Integration.md`, `Docs/Tripo-Unity-Bridge.md`, `Docs/GLB-Polish-Review.md` |
| Sound | `Docs/AudioDirection.md`, `Docs/Speech-Audio-Review.md` |
| Architecture and networking rules | `Docs/02-Technical-Architecture.md` |
| Controls, console, launch switches, batch commands | `README.md` |

When you change how something works, update its doc in the same commit.

## How the scene is made (important)

- `Game.unity` is **generated by code**: `PLEASE DON'T DROWN > Rebuild Game scene` runs `GameSceneBuilder` and
  **overwrites the whole scene and the Player prefab**. Some later work was applied to the scene in place by
  smaller menu tools (for example `Expand island two resort`, `Finish one hotel and open reception`), so a full
  rebuild can lose things. **Never rebuild the scene without asking the project owner (sanneel) first.**
- To add or move things in the world, prefer changing the builder code for that area (`GameSceneBuilder.*.cs`) and
  a small in-place menu tool, so the change survives a rebuild. If you edit the scene directly, say so.
- `Game.unity` is one huge text file. Two people editing it at the same time produce merge conflicts that are
  very hard to fix. Before scene work, remind the person to tell the team they are editing the scene.

## Networking rules (FishNet)

- The **host** owns story, victims, money, NPCs (`[Server]` methods, `IsServerInitialized`). Clients send
  `ServerRpc`s that the host validates; the host answers with `ObserversRpc`/`TargetRpc` and `SyncVar`s.
- Each player's own movement and view are simulated on their own machine.
- Anything new that changes the game state must work for a client too, not only for the host. Think about
  both when fixing a bug.
- Static state must be reset in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`
  `ResetStatics()`; the existing classes all do this.

## Testing (you usually can't, so give a recipe)

- Unity: open the project, wait for compiling, check the **Console** has no red errors, open `Game.unity`, press Play.
- In game, the dev console (backquote or F2) has commands for nearly everything: `help`, `story goto 1.4`,
  `story reset`, `tourist f`, `cpr`, `victims`, `spawn <item>`, `tp <place>`, `noclip`, `money 500`, `tsunami`,
  `devisland`, `timescale 0.3`. Full list: `help` in game, or `README.md`.
- Full story check (needs a Windows build in `Builds/Win64`): `powershell -File Tools\test-story.ps1`.
- Log lines start with tags like `[Story]`, `[Banana]`, `[Player]`; mention which tag to look for.

## Git rules

- Branch: the team works on `main`. Always get the latest before starting (`get-latest` skill) and save with
  `save-work`. Never force push, never `git reset --hard` or `git clean` on someone's work without asking.
- **Never commit**: `Builds/`, `Library/`, `Temp/`, `Logs/`, `.rar`/`.zip` archives, or any file over 100 MB
  (GitHub refuses the whole push). Big art goes through **Git LFS** (`.gitattributes` covers png, fbx, glb,
  blend, wav and more). The free LFS quota is small, so don't commit huge source files you don't need.
- Every Unity asset has a `.meta` file next to it. Always commit them together, never delete a `.meta` on its own.
- Commit messages: a short plain-English title saying what changed in the game, then a few lines on why if useful.

## Known traps

- `ItemSkin` looks unused in the builder but is still on the committed gun prefabs. Don't delete it.
- Tourists are physics bodies with jointed limbs (`VictimBody`), not animated characters. Posture problems are
  forces and joint targets there. Story characters (`StoryNpc`) use `AvatarAnimator`.
- `StoryNpc` treats any rigidbody in its way (players included) as a wall; story coroutines that wait for an NPC
  to arrive need a timeout and a fallback (see `WalkToHut` in `StoryDirector.Gags.cs`).
- Graphics options apply to a runtime copy of the pipeline asset (`PictureSettings`); don't edit
  `Assets/Settings/PC_RPAsset.asset` from game code.
- The Tripo bridge package has its auto-delete cleanup disabled on purpose. Keep that when updating it.
- Paths in old docs and README use the owner's machine (`F:/GameDev/PleaseDontDrown`, `F:/Unity/...`). Other
  people's paths differ.
