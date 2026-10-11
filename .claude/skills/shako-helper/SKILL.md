---
name: shako-helper
description: How to work with Shako on the "Please Don't Drown" Unity game (repo sanneel/pleasedontdrown). Use for ANY request about this game or repo — screenshots with plain-language asks like "ტექსტურა დაუმატე", "ეს გამისწორე", bug fixes, art/texture changes, gameplay changes, commits and pushes, or "make it like in How to Fish" (look up the mechanic in the How to Fish reference build).
---

> Copy of Shako's personal skill (2026-10-11) so Claude on his other PC works the same way. Paths under C:\Users\shako are from his main PC: find the equivalents on this one.

# Working with Shako on Please Don't Drown

## Who the user is
- Shako has **no coding, game-dev, or Claude/tooling experience**. They usually send a **screenshot** plus a short plain-words request, often in Georgian (e.g. "ტექსტურა დაუმატე" = "add a texture", "ეს გამისწორე" = "fix this").
- Reply in the language they wrote in (usually Georgian), in simple words. No jargon without a one-line explanation. Never ask them to edit code or run complex commands themselves; do the work, and if they must click something, give exact step-by-step instructions.

## Be precise — ask when unsure
- Before changing anything, make sure you know **exactly** what on the screenshot they mean and what result they expect. Look at the screenshot carefully and find the matching scene/prefab/script/asset in the repo.
- If the request is ambiguous (which object, which texture, what "fixed" should look like), **ask a short, concrete question first** — offer 2–3 options when possible rather than guessing.
- If the task needs a decision Shako can't make (architecture, art direction, licensing, anything risky), say so plainly and ask them to bring in a **knowledgeable teammate** before you proceed. Don't guess on these.
- After a change, tell them in plain words what changed and how to see it in Unity.

## Project facts
- Repo: `C:\Users\shako\OneDrive\Desktop\please dont drown\pleasedontdrown` (GitHub `sanneel/pleasedontdrown`, default branch `main`).
- Unity **6000.3.25f1**. Folders: `Assets/`, `ArtSource/`, `Docs/`, `Tools/`, `Packages/`, `ProjectSettings/`.
- Images (png/jpg etc.) are in **Git LFS** — after pulls, make sure LFS files are real files, not pointers (`git lfs ls-files` shows `*`, not `-`).
- Git is at `C:\Program Files\Git`. Git identity is already configured.

## Git rules (mandatory)
1. **Never commit or push to `main`.** All work goes on our branch **`shako-dev`** (created 2026-10-11 from main, no upstream to main; first push with `git push -u origin shako-dev`). Don't reuse someone else's branch like `claude/*`.
2. **Before every push, check `main`:**
   - `git fetch origin`
   - Look at what's new on `origin/main` (`git log HEAD..origin/main --oneline`) and tell Shako briefly if teammates changed something.
   - Merge `origin/main` into the working branch, resolve conflicts carefully (ask if a conflict touches something you don't understand), and make sure the project still makes sense after the merge.
   - Then push the working branch only.
3. Only commit/push when Shako asks (or clearly agrees). Use clear commit messages describing the change in plain words.

## Reference game: How to Fish
- The game concept is based on **How to Fish**; the team **has a license to use its mechanics**.
- Installed build: `C:\Program Files (x86)\Steam\steamapps\common\How to Fish\How to Fish`
- It's a Unity **Mono** build, so the game code is in compiled DLLs, not source files:
  - `How to Fish_Data\Managed\Assembly-CSharp.dll` (main game code), `Assembly-CSharp-firstpass.dll`, plus FishNet networking DLLs (`FishNet.Runtime.dll`, etc.).
- When asked "make X like in How to Fish" (e.g. "გამიკეთე ისეთი იარაღის ამოღება როგორიც How to Fish-შია" = "make weapon/tool pulling-out like in How to Fish"):
  1. Find the relevant code. **Already decompiled (2026-10-11)** to readable C# here — search this first:
     `C:\Users\shako\OneDrive\Desktop\please dont drown\HowToFish_Decompiled\` → `Assembly-CSharp\` (329 .cs files, main game: e.g. `Weapon.cs`, `FishingRod.cs`, `Boat.cs`) and `Assembly-CSharp-firstpass\` (34 files). This folder is outside the repo — never copy it into the repo or commit it.
     To re-decompile (e.g. after a game update): .NET 8 SDK and `ilspycmd` 8.2 are installed; run with env `DOTNET_ROLL_FORWARD=Major`:
     `ilspycmd -p -o <out>\Assembly-CSharp -r "<Managed>" "<Managed>\Assembly-CSharp.dll"`
  2. Understand how their mechanic works (classes, animations, timings, inputs), explain it to Shako in plain words.
  3. Re-implement it in our project in our own code style. **Don't copy their assets or paste large chunks of their code verbatim into our repo**; if unsure what the license allows, ask Shako to confirm with the person who holds the license.
- Never modify files in the How to Fish install folder.
- The game must **not look like a How to Fish copy**: take what makes it fun (instant feedback, style bonuses,
  escalating tools, bosses, a clear ending), not its look or its fish-killing loop.

## The plan and what Shako wants first (learned 2026-10-11)
- **Read `Docs/16-Plan-Islands-2-3.md` first.** It holds the agreed story for islands 2-3, money numbers, the
  Kleptopus and Old Snapper bosses, the core-system status table and test commands. Keep it updated when decisions change.
- Priority: **core gameplay logic first** (how NPCs fight, shoot, move; creatures; bosses; mission retries), then
  visuals (models go in later), money last. Don't drift into economy/menus when he asks about gameplay quality.
- He judges by playing: after each step give a dev build and the exact console commands to try (`brawltest 4 mix`,
  `fightstats`, `hurt 50`...), and wait for his verdict before the next step. Aim for "9/10" on fight feel.
- He is blunt and swears when something is wrong; that means "stop and fix it", not hostility. Admit the mistake in
  one line, stop the wrong work (especially anything spending credits), and fix it.
- Story / UI preferences he stated: no numbers or percent on the gala bar (plain bar, top left); remove stunt money
  rewards and screen-covering text (confirm by screenshot first); the death/down screen must be minimal.
- Be honest and precise: when he asks "are you sure?", check (search the web if needed) and correct yourself if you
  overstated something. Don't moralise about content he chose (the love hut, CPR jiggle stay).

## Building and testing without him
- Unity 6000.3.25f1 is installed at `C:\Program Files\Unity\Hub\Editor\6000.3.25f1\Editor\Unity.exe` (Unity CLI:
  `%LOCALAPPDATA%\Unity\bin\unity.exe`; he is signed in with a Personal license). Unity must not be open in the editor
  while you run batch mode.
- Compile check: `Unity.exe -batchmode -quit -nographics -projectPath <repo> -logFile <log>` then grep `error CS`.
- **Dev build of the scene as is** (cheats on, scene file untouched): `-executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build`.
  Don't use `GameSceneBuilder.BuildPlayerBatch` unless asked: it regenerates `Game.unity` first.
- Headless tests: `Builds/Win64/PleaseDontDrown.exe -batchmode -nographics -pdd-nosteam -pdd-port <p> -pdd-host-offline
  -pdd-nosave -pdd-nostory -pdd-exec "wait 6; ...; fightstats" -logFile <log>`; a second copy with `-pdd-join localhost`
  tests multiplayer. Story autoplay: `-pdd-autoplay <beat>` (it currently sticks at 2.1 on main too).
- **The repo path has spaces** ("please dont drown"): quote every path, use bash arrays for argument lists.
- No Python on this PC (use Blender's Python or bash); Blender 5.2 is at `C:\Program Files\Blender Foundation\Blender 5.2\`.
  Render GLBs for review with an EEVEE ortho camera (front/side/3-4) and show him a contact sheet, next to a reference.

## Tripo (see also `.claude/skills/pdd-tripo` in the repo)
- He signs in; he has premium credits. Get one "yes" per batch with the list and the credit cost; when he says
  "just one first", make one and show it before the rest.
- **Style that's right: stylised-realistic like his `THIEFASPIRATE.glb`** (PBR on, detailed worn leather/cloth/brass,
  natural proportions, A-pose). Flat cartoon (PBR off, "bright flat colours") was rejected. Style blocks live in
  `prompts/pirates.md` (characters) and `prompts/island3.md` (creatures).
- Prompt box limit is 1000 characters. The page resizes itself: don't click by coordinates in long batches; use
  `find` refs (`textbox`, `Generate`) and `form_input`, and zoom to check the box text before pressing Generate.
- Download via the page's pinia `serverProjects[].model_url` (reload the page if a new project is missing), decode with
  `ArtSource/Tools/tripo_decode.py`, keep raw files in `raw/`.
