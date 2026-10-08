# Working on PLEASE DON'T DROWN with Claude

You don't need to know how to program. You tell Claude what you want in your own words (English or Georgian),
Claude changes the game, tells you how to check it in Unity, and saves it for the team. Claude already knows the
project: it reads `CLAUDE.md` and the skills in `.claude/skills/` by itself every time.

## 1. One-time setup (about an hour, mostly downloads)

**Accounts**
- A **GitHub** account. Send your username to sanneel, who adds you to the project (repo Settings > Collaborators).
  Accept the invitation email.
- A **Claude** plan that includes Claude Code (Pro or Max).
- **Steam**, if you want to play online with the others.

**Install, in this order**
1. **Git for Windows**: https://git-scm.com/download/win . Keep the default options (it includes Git Bash and
   Git LFS, which Claude needs). Afterwards open "Git Bash" once and run `git lfs install`.
2. **Unity Hub**: https://unity.com/download . Then install the exact editor version **6000.3.25f1**: paste
   `unityhub://6000.3.25f1/e1dba0a9aba4` into your browser's address bar, or in Unity Hub go to Installs > Install
   Editor > Archive. Tick **Windows Build Support (Mono)**. Any other version will change hundreds of files.
3. **Claude**: the Claude desktop app (https://claude.ai/download, then the **Code** tab), or Claude Code in a
   terminal (https://claude.com/claude-code).
4. Optional, for 3D work: **Blender** 5.x (https://www.blender.org/download/).

**Get the project**
1. Make a folder with a short path, for example `C:\GameDev`.
2. Open Git Bash there (right-click in the folder > Open Git Bash here) and run:
   ```
   git clone https://github.com/sanneel/pleasedontdrown.git
   cd pleasedontdrown
   git lfs pull
   ```
   This downloads a few GB of art and sound. Sign in to GitHub in the window that pops up.
3. Unity Hub > Projects > **Add** > Add project from disk > pick the `pleasedontdrown` folder. Open it. The first
   time takes 10 to 30 minutes.
4. In Unity: Project window > `Assets/_Game/Scenes/Game.unity` (double-click), then press **Play**.

**Connect Claude**
- Desktop app: Code tab > choose the `pleasedontdrown` folder.
- Terminal: open the folder in Git Bash and type `claude`.
- Say: *"Hi, I'm new on the team. Check my setup and tell me what you know about this project."*

## 2. Every day

1. **Start:** *"Get the latest version."* Claude downloads what the others did. (Save in Unity first: Ctrl+S.)
2. **Ask for what you want**, as you would ask a person:
   - *"The banana boat throws people off too easily, make it harder."*
   - *"When I revive the woman with CPR she walks bent over. Fix it."*
   - *"Add a line where Sandy complains about the heat when you first meet her."*
   - *"I made a beach umbrella in Tripo and sent it to Unity, put five of them on island two."*
   - *"Why does the robber sometimes get stuck by the dock?"*
3. **Check it:** Claude gives you numbered steps to see the change in Unity. Do them, and tell Claude what you saw.
   If Unity shows **red errors** in the Console, copy them and paste them to Claude.
4. **Save:** *"Save my work."* Claude checks the files, writes a short description and uploads it to GitHub.

Useful shortcuts you can type to Claude: `/get-latest`, `/save-work`, `/fix-bug`, `/story-change`, `/add-model`,
`/test-change`.

## 3. Team rules (please read)

- **One person on the level at a time.** The whole game world is one file (`Game.unity`). If two people change it at
  the same time, one person's work gets lost. Say in the team chat before you change buildings, terrain or where
  things stand, and save it when you're done.
- **Don't press "Rebuild Game scene"** in the PLEASE DON'T DROWN menu. It throws away work. Ask sanneel.
- **Get the latest before you start, save when you finish.** Small and often is better than one huge save.
- **Never put game builds or zip/rar files in git.** GitHub refuses files over 100 MB and the whole save fails.
  Claude checks this for you, and blocks it if it happens.
- **Read what Claude asks before you click Yes**, especially when it says something will be deleted or overwritten.

## 4. When something goes wrong

| What you see | What to do |
|---|---|
| Pink objects in Unity | Tell Claude; usually a material lost its shader after an update. |
| Red errors in the Unity Console | Copy them to Claude. Don't press Play until they're gone. |
| "Merge conflict" | Tell Claude which of you changed what; it sorts it out or asks you which version to keep. |
| "Permission denied" / 403 when saving | Your GitHub account isn't on the project yet: ask sanneel. |
| "LFS quota" or "budget exceeded" | The team's storage for big files is full: tell sanneel. |
| Claude says it can't run Unity | Normal. It changes the files; you press Play and tell it what happened. |
