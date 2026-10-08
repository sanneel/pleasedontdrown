---
name: get-latest
description: Get the team's newest version of the game from GitHub before starting work. Use when starting a session, or when the person says update, pull, sync, get the latest, "what did the others change", or before any change if the local copy may be behind.
---

# Get the latest version

The person is probably not a programmer. Do the git work yourself and explain the result in one or two plain sentences.

1. **Unity first.** Ask them to save in Unity (Ctrl+S) and, if new files from teammates touch the scene, to close
   Unity or at least leave Play mode. Unity can overwrite files it has open.
2. **Look at the local state:**
   ```
   git status --short
   git branch --show-current
   git fetch origin
   git rev-list --left-right --count HEAD...origin/main
   ```
   The last line prints `<ahead> <behind>`.
3. **Unsaved local changes?** If `git status` lists changed files, they must be kept. Use the `save-work` skill to
   commit them first (ask the person for a one-line description of what they changed if it isn't obvious from the
   diff). Never stash and forget, never discard.
4. **Not on `main`?** Tell them which branch they're on and ask before switching.
5. **Pull:**
   ```
   git pull --no-rebase origin main
   git lfs pull
   ```
6. **Conflicts?** If git reports conflicts:
   - Code (`.cs`) or docs: resolve them by reading both sides and keeping both people's intent, then commit.
   - `Game.unity`, prefabs or other big Unity YAML: don't guess. Explain that two people changed the same scene,
     list who changed what (`git log --oneline -3 -- <file>` on each side), and ask which version to keep. The safe
     default is to keep the teammate's version (`git checkout --theirs <file>`) and redo the person's own scene
     change afterwards.
   - Binary files (models, textures, audio): ask which one to keep.
7. **Report** in plain words: how many new changes came in and the gist of them
   (`git log --oneline HEAD@{1}..HEAD`), and whether Unity will need a minute to re-import.
