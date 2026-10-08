---
name: save-work
description: Save the person's work to GitHub so the team gets it (commit and push safely). Use when they say save, push, upload, commit, send it to the team, "I'm done", or when a finished change should be shared.
---

# Save work to GitHub

The person is probably not a programmer. Do every step yourself and only stop to ask when something below says so.

1. **Unity first.** Ask them to press Ctrl+S in Unity and use File > Save Project, so scenes and prefabs are written
   to disk. Wait for them to confirm if Unity has unsaved scene changes.
2. **See what changed:**
   ```
   git status --short
   git diff --stat
   ```
   Read the list. Explain anything surprising to them before going on (lots of files they didn't expect, deleted
   assets, changes in `ProjectSettings/` or `Packages/`).
3. **Never commit these.** Unstage or leave out:
   - `Builds/`, `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Screenshots/`
   - archives (`.rar`, `.zip`, `.7z`), installers, game builds
   - any single file over **100 MB** (GitHub refuses the whole push). Find them with:
     ```
     git ls-files -mo --exclude-standard | while IFS= read -r f; do [ -f "$f" ] && s=$(wc -c < "$f") && [ "$s" -gt 50000000 ] && echo "$s $f"; done
     ```
     A big file whose type is in `.gitattributes` (png, fbx, glb, blend, wav...) goes through Git LFS and is fine
     up to 2 GB, but it uses the team's small LFS quota: ask whether it is really needed in the repo.
     A big file of any other type must not be committed: add it to `.gitignore` or ask what to do.
   - If one of these is already in an unpushed commit, take it out of that commit before pushing
     (`git rm --cached <file>` then `git commit --amend` for the last commit; ask before rewriting older commits).
4. **Every asset with its `.meta`.** New or deleted assets must have their `.meta` file added or deleted too.
5. **Commit:**
   ```
   git add -A
   git commit -m "<title>" -m "<optional body>"
   ```
   Title: a short plain-English sentence about what changed in the game ("Banana boat only throws riders on a
   U-turn"), not "update" or "fixes". Body: a few lines on why, if useful.
6. **Get the team's latest, then push:**
   ```
   git pull --no-rebase origin main
   git push origin HEAD:main
   ```
   If the pull has conflicts, follow step 6 of the `get-latest` skill. Never use `--force`.
   If the push is rejected:
   - "exceeds GitHub's file size limit": a file over 100 MB got in; go back to step 3.
   - LFS "quota" or "budget" errors: the team's LFS storage is full. Explain that the owner must buy a data pack or
     remove big files; don't try to work around it.
   - "permission denied" / 403: their GitHub account has no write access; the owner must add them as a collaborator.
   - "non-fast-forward": someone pushed in between; pull again and push.
7. **Tell them it's saved,** with the commit title and a link: `https://github.com/sanneel/pleasedontdrown/commits/main`.
