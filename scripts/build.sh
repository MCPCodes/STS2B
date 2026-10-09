#!/usr/bin/env bash
# Build the mod against the installed game and stage it in out/SpireMind.
set -euo pipefail
. "$(dirname "$0")/env.sh"

"$DOTNET" build "$REPO_ROOT/src/SpireMind/SpireMind.csproj" -c Release -p:GameDir="$GAME_DIR" -o "$REPO_ROOT/out/build"

stage="$REPO_ROOT/out/SpireMind"
rm -rf "$stage" && mkdir -p "$stage"
cp "$REPO_ROOT/out/build/SpireMind.dll" "$REPO_ROOT/src/SpireMind/SpireMind.json" "$stage/"
echo "Staged $stage"
