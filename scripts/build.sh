#!/usr/bin/env bash
# Build the mod against the installed game and stage it in out/SpireMind.
set -euo pipefail
. "$(dirname "$0")/env.sh"

"$DOTNET" build "$REPO_ROOT/src/SpireMind/SpireMind.csproj" -c Release -p:GameDir="$GAME_DIR" -o "$REPO_ROOT/out/build"

stage="$REPO_ROOT/out/SpireMind"
rm -rf "$stage" && mkdir -p "$stage"
cp "$REPO_ROOT/out/build/SpireMind.dll" "$REPO_ROOT/src/SpireMind/SpireMind.json" "$stage/"

# The in-game brain's prompt and strategy notes, for when the repo can't be found, and the
# repo's path, so the brain finds the checkout without "repo" in config.json (docs/brain.md).
brain="$stage/brain"
mkdir -p "$brain/.claude/commands"
cp "$REPO_ROOT/.claude/commands/play.md" "$brain/.claude/commands/"
(cd "$REPO_ROOT" && find strategy -type f -name '*.md') | while IFS= read -r note; do
  mkdir -p "$brain/$(dirname "$note")"
  cp "$REPO_ROOT/$note" "$brain/$note"
done
printf '%s\n' "$REPO_ROOT" > "$brain/repo_path"
echo "Staged $stage"
