#!/usr/bin/env bash
# Runs before every shell command Claude starts. For git commit and git push it refuses when a file GitHub would
# reject (over 100 MB and not stored in Git LFS) is about to be committed or pushed.

input=$(cat)
case "$input" in
  *"git commit"*|*"git push"*) ;;
  *) exit 0 ;;
esac

cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

limit=95000000
found=""

# Changed or new files on disk (they get in with "git add -A"). LFS files are fine, they're stored elsewhere.
while IFS= read -r -d '' f; do
  [ -f "$f" ] || continue
  size=$(wc -c < "$f" 2>/dev/null) || continue
  [ "$size" -gt "$limit" ] || continue
  git check-attr filter -- "$f" | grep -q ': filter: lfs$' && continue
  found="$found
  $f ($((size / 1000000)) MB, on disk)"
done < <(git ls-files -z -mo --exclude-standard)

# Staged files (an LFS file is staged as a tiny pointer, so only real big files show up here).
while IFS= read -r -d '' f; do
  size=$(git cat-file -s ":$f" 2>/dev/null) || continue
  [ "$size" -gt "$limit" ] && found="$found
  $f ($((size / 1000000)) MB, staged)"
done < <(git diff --cached --name-only --diff-filter=AM -z)

# Every commit that isn't on GitHub yet, including files added and removed again along the way.
base=$(git rev-parse -q --verify '@{u}' 2>/dev/null || git rev-parse -q --verify origin/main 2>/dev/null)
if [ -n "$base" ]; then
  while read -r type size path; do
    [ "$type" = blob ] && [ "$size" -gt "$limit" ] && found="$found
  $path ($((size / 1000000)) MB, in a commit not pushed yet)"
  done < <(git rev-list --objects "$base..HEAD" 2>/dev/null | git cat-file --batch-check='%(objecttype) %(objectsize) %(rest)' 2>/dev/null)
fi

[ -z "$found" ] && exit 0

cat >&2 <<EOF
Blocked: GitHub refuses files over 100 MB, and the whole push would fail. These files are too big:$found

Leave them out of git: add them to .gitignore (builds, archives, renders), or store them in Git LFS if the game
really needs them (add their type to .gitattributes). If one is already in a commit that isn't pushed yet, take it
out of that commit first (git rm --cached <file>, then amend or redo the commit). Explain this to the person in
plain words before going on.
EOF
exit 2
