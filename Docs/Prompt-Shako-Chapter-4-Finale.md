# Big task for Shako's Claude: chapter 4, the last island and the finale (2026-10-18)

Paste everything below the line into a new Claude Code session on Shako's PC (or tell it: "read
`Docs/Prompt-Shako-Chapter-4-Finale.md` on origin/main and do it"). It is a big task: plan it in phases, commit and
push after each phase, and keep going across sessions until the whole list is done.

---

You are working on **PLEASE DON'T DROWN** (Unity 6000.3.25f1, URP, FishNet), GitHub `sanneel/pleasedontdrown`, for
Shako. Sandro (owner) asked for this task. Work on your own: when something is unclear, **decide, write the decision
in your report, and keep going**. Ask Shako only for things only he can give (Tripo credits, a yes on cost).

The game's story stops at "To be continued..." after chapter 3 (the pirates' gold vault). The main menu promises
"find out what's really going on at the beach", and the plan has always said: *after island 3 the yacht goes to
island 4* (`Docs/16-Plan-Islands-2-3.md`). A **Sandy (boss)** model has been baked since September and never used
(`AvatarLook.Bodies.SandyBoss`). Your task: **finish the story.** Close chapter 3 properly, build **island 4**, write
and build **chapter 4** with a final boss, an ending and credits, so the game can be played from 1.1 to the credits.

The owners judge by pictures and by playing. **A task is only done when you have seen it working in the real game
build, in screenshots you looked at closely yourself** (crop and zoom at full resolution; a contact sheet is not
enough). They get furious about: hands inside bodies, people standing inside furniture or walls, floating props,
clipping, big heads or big hats, dark boxes behind on-screen text, and "done" claims the pictures don't back up.

## 0. Before you start

1. `git fetch origin`, then on `shako-dev`: `git merge origin/main`. Main has everything up to Sandro's UI/UX passes
   1-6 and the merge of your island 3 work (sewer, throne room, signposts, the arch). Make sure LFS files are real
   files after the merge (`git lfs pull`). Build once (`DevPlayerBuild.Build`) and play a minute: the menus, pause
   screen, options, character screen and in-game HUD are all new (see `Docs/UI-UX-Audit-2026-10-18.md`).
2. Read: `.claude/skills/shako-helper/SKILL.md`, `.claude/skills/pdd-tripo/SKILL.md` (lessons at the end),
   `Docs/05-Story-Mode.md` (the whole story and its systems), `Docs/15-Island-3-Skull-Cove.md`,
   `Docs/16-Plan-Islands-2-3.md`, `Docs/17-Flow-Islands-1-3.md`, `Docs/18-Team-Split.md`, your own
   `Docs/Overnight-Report-Island-3.md`, and `Docs/UI-UX-Audit-2026-10-18.md` (how the HUD works now).
3. Read the code you will build on: `Story/StoryDirector*.cs` (beats: `StoryDirector.Beats.cs`; chapter 3:
   `StoryDirector.Chapter3.cs`, `.Residents.cs`, `.BossBar.cs`, `.Missions.cs`; free play: `.FreePlay.cs`),
   `Dev/StoryAutoplay.cs`, `Dev/DevIsland.cs` (travel destinations), `Creatures/` (the Kleptopus and its tentacles),
   `Rescue/RescueService.cs` (spawning tourists), `Story/Economy.cs` (the rival company's airlift bill).
4. No Unity editor or batch run may be open on the project during a batch run.

## 1. Hard rules

- **Git:** work on `shako-dev` only. Small commits with clear messages (end each with your Co-Authored-By line); push
  `shako-dev` after each phase. **Never push `main` or `sancho-dev`.**
- **Sandro's files (don't edit them; write in the report exactly what you need and why):**
  - character animation and bodies: everything in `Avatar/`, `Rescue/VictimBody.cs`, `Rescue/VictimBrain.cs`,
    `Player/PlayerCarry.cs`, `Player/PlayerAvatar.cs`, `Player/PlayerHands.cs`
  - fighting: everything in `Combat/`, `Story/StoryNpc.Combat.cs`, `Story/StoryNpc.Rivals.cs`
  - **the UI** (reworked today): everything in `UI/`, `Story/StoryHud.cs`, `Story/DialogueService.cs`.
    Allowed exceptions: a new value in `NpcRole` plus one line for it in `StoryHud.RoleOf` (the caption beside the
    speaker's name, e.g. "FINDERS KEEPERS"); new UI you need (the credits) goes in a **new** file that draws only
    through `Hud.*` (titles with `Hud.Title`, outlined text, no dark boxes behind HUD text).
- **Use the systems that exist, don't rebuild them:** boss intros with `BossIntroObservers` (the name flies up into
  the boss bar), mission bars with `ServerBossBar`, chapter cards with `TitleObservers`, the report with
  `ChapterReport`, dialogue with `Say` (the name plate shows a live portrait of whoever talks), objectives and markers
  with `SetObjective`/`Marker`, tourists with the rescue service, money with `Economy`.
- **Scene file:** never run `GameSceneBuilder.BuildPlayerBatch` or anything that regenerates `Game.unity` from scratch.
  Build island 4 with new idempotent batch methods (running twice replaces, never duplicates), in new files like
  `Editor/GameSceneBuilder.Island4.cs`, the way island 3 was built. After each scene patch,
  `git diff --stat Assets/_Game/Scenes/Game.unity` must be the size you expect. Builds: only `DevPlayerBuild.Build`.
  Batch runs leave float noise in `Data/Materials/Prop_*.mat` and `Brass.mat`: `git checkout --` those before
  committing.
- **Islands 1-3:** don't change them, except where this task says so (the chapter 3 ending, a cameo, a dock).
- **Art:** Tripo only with Shako's yes on the credits (ask him once, with a list and the cost). Until then: greybox
  with clean primitives that keep each piece's final position, size and collision, the existing Tripo and Meshy
  models (`Art/Tripo/Island1..3`, `Art/Meshy`), and the island 1-3 props. Run `GlbTextureSlim.Run` on any new GLB.
- **Characters:** no big heads or hats. The lifeguard has very short arms (0.56 m reach). Nobody stands inside
  furniture: check with `overlaps` near people, `npcwatch` for a minute on the island.
- **Multiplayer:** the host is the authority; everything must also work for a joined client. Test with two copies
  (`-pdd-host-offline` and a second copy joining `localhost`).
- **Performance:** measure frame time on Low and High on island 4 (`perf`); it must stay near island 2's numbers.

## 2. The story (follow it; if Shako or Sandro change something, follow them)

The threads the game has left open: the robber/THIEF who keeps turning up, the **pills** a guy handed out on the
beach (1.7), the things tourists keep "losing" that **Sandy pays for** at her Lost & Found, the **rival lifeguard
company** that airlifts every tourist you lose and sends the bill, and the pirates who robbed the casino. The answer:
**Sandy runs all of it.** She owns the rival company, *Finders Keepers Rescue Co.*; the THIEF steals for her, the
pills make tourists black out and drop their things, her company airlifts the ones you don't save and bills their
families, and the pirates were only delivering the casino's gold to her. Her island is the company's HQ.

### Chapter 3, closing it (replace "To be continued...")

| # | Beat | What happens | Done when |
|---|---|---|---|
| 3.8 | The gold vault | as now | the gold is reached |
| 3.9 | Load the gold | 4 money sacks (held items) to carry from the vault to the pirate boat at the dock; the THIEF "helps" and pockets one | 4 sacks on board |
| 3.10 | Back to the hotel | sail to island 2; Marisol counts the casino's money, the team keeps a reward. A sack is short: the THIEF is gone, and on the boat a Lost & Found tag with Sandy's handwriting: "*Finders Keepers. Deliver to the island.*" A sea chart with an island nobody has seen | dialogue ends → chapter 3 report |

### Chapter 4: Finders Keepers (island 4)

| # | Beat | What happens | Done when |
|---|---|---|---|
| 4.1 | Arrival | a glossy private island in daylight: orange-and-white rival lifeguards ("FK RESCUE") at the jetty want a landing fee ("Private beach! That'll be $50.") | you're past the jetty |
| 4.2 | The rescue race | the rival company finally does what the airlift always said: tourists get into trouble and **rival lifeguards race you to them**; one they reach first is airlifted (LOST TO COMPETITION, the bill). A mission bar "Finders Keepers vs you". Save 3 before they take 3 | 3 saved (else retry) |
| 4.3 | The warehouse | a hangar full of everything "lost" on islands 1-3: towers of sunglasses, phones, wallets, the casino's chips. Find 5 pieces of evidence (Lost & Found tags) among the guards | 5 found |
| 4.4 | Blackout Berry | the smoothie bar sells "Blackout Berry": the pills from 1.7. The barman and his regulars fight you (the bar fight that exists) | bar cleared |
| 4.5 | The THIEF talks | cornered at the helipad, he spills it all (the summary above, in his silly voice) | dialogue ends |
| 4.6 | Sandy | in her villa on the cliff, under a giant statue of herself. She thanks you for all the lost things you handed in (she sold them back). She changes into the boss look (`Bodies.SandyBoss`): boss intro "SANDY", *Owner, Finders Keepers Rescue Co.* | she runs |
| 4.7 | The final boss | three phases with a boss bar: **(1)** her helicopter drops tourists into the sea while rival lifeguards try to steal them: a rescue rush on a clock; **(2)** the thing she keeps under her jetty, a bigger cousin of the Kleptopus (reuse `Creatures/` and its tentacles, new look/size, a new phase pattern), dragging tourists under; **(3)** Sandy herself on the jetty with a giant grabber claw from the Lost & Found, until she falls in the deep water | she falls in |
| 4.8 | Please don't drown | **Sandy can't swim.** The title, paid off: swim out, bring her in, CPR (the kiss of life on Sandy: let the team's lines be funny about it). She wakes up and gives up | she's revived |
| 4.9 | The ending | the police boat takes Sandy and the THIEF; the lost things go back (the warehouse empties); everyone from islands 1-3 waves from the beach (a cast call); "CHAPTER 4 COMPLETE" report; then the **credits** roll (title, the team: Sandro, Shako; made with Claude; the models' sources) and the **open shift** starts on all four islands | credits seen |

## 3. Island 4: what to build

- **Where:** open sea at least 250 m from the other islands, inside the water/terrain grid (or extend it the way
  island 3's own ground mesh did). A travel pad, `island4` in the console, Esc > Travel ("Finders Keepers (island 4)"
  in `DevIsland`), and a sailing route the pirate boat can take from island 2.
- **Size and mood:** smaller than island 3 (about 220 × 220 m), **daylight**, too clean, too orange: a corporate resort
  that is really a company HQ. The opposite of the pirates' night.
- **Layout (from the jetty inland):** the jetty and a rival lifeguard tower; a lagoon beach (shallow, where the race
  happens) with a deep drop-off; the warehouse hangar with "FINDERS KEEPERS" on its roof; the smoothie bar; the
  helipad with the company helicopter; a cliff path up to Sandy's villa and a giant Sandy statue; Sandy's private
  jetty over deep water (the final fight); a little police dock for the ending.
- **People:** rival lifeguards (an existing body in an orange uniform until Shako says yes to a Tripo one), tourists
  on loungers, the barman, the THIEF, Sandy.
- **Rescue rules for island 4** (`RescueRules`): like island 2's difficulty. Rescues pay as island 2.

## 4. Tests (all must pass before you call it done)

- `chapter4check` (new, like `chapter3check`): every beat's points exist, each beat's condition moves the story on,
  the race, the evidence, the bar, the three boss phases, Sandy's rescue, the ending, the open shift after it.
- `chapter3check` still ALL PASS (with 3.9 and 3.10), `threatcheck`, `killcheck`, `kleptocheck` ALL PASS.
- Story autoplay knows the new beats: `-pdd-autoplay 4.9 -pdd-timescale 2` plays **1.1 to the credits**, 0 errors
  (windowed; headless logs only rendering errors).
- Two copies (host + client): the race, the boss and the ending work for the client too.
- `npcwatch` 80 s on island 4: nobody stuck or walking through walls. `overlaps`: 0 hands inside bodies.
- Frame time on Low and High at the jetty, the warehouse, the villa and during the boss.

## 5. Report

Write `Docs/Report-Shako-Chapter-4.md` with pictures in `Docs/Chapter-4/` (from the real build, player's eyes, at full
resolution): a short table (task → state), what you built, the decisions you made, the test results, the numbers,
and an honest "still wrong or not checked" list. Everything you need from Sandro's files goes in a "Requests for
Sandro" section, each with the file, what, and why.

Order of work (push after each): **(1)** chapter 3 ending, **(2)** island 4 greybox, travel and sailing,
**(3)** beats 4.1-4.5, **(4)** Sandy and the boss 4.6-4.8, **(5)** the ending and credits 4.9, **(6)** art pass
(Tripo with Shako's yes), **(7)** tests and the report.
