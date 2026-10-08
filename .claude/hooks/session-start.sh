#!/usr/bin/env bash
# Tells Claude at the start of a session whether this copy of the game is behind GitHub or has unsaved work.

cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

GIT_TERMINAL_PROMPT=0 git fetch -q origin main >/dev/null 2>&1
branch=$(git branch --show-current 2>/dev/null)
changed=$(git status --porcelain 2>/dev/null | wc -l | tr -d ' ')
counts=$(git rev-list --left-right --count HEAD...origin/main 2>/dev/null)
ahead=${counts%%[[:space:]]*}
behind=${counts##*[[:space:]]}

echo "Git: branch ${branch:-unknown}. Uncommitted changed files: ${changed}. Local commits not on GitHub main: ${ahead:-?}. New commits on GitHub main: ${behind:-?}."
if [ "${behind:-0}" != 0 ] && [ "${behind:-0}" != "?" ]; then
  echo "This copy is behind the team. Use the get-latest skill before changing anything."
fi
if [ "${changed:-0}" != 0 ]; then
  echo "There is unsaved work here. Don't discard it; save it with the save-work skill when the person is ready."
fi
exit 0
