#!/usr/bin/env bash
# Build, then copy the staged mod into the game's mods folder. Quit the game first.
set -euo pipefail
. "$(dirname "$0")/env.sh"

if pgrep -f "SlayTheSpire2" >/dev/null; then
  echo "Slay the Spire 2 is running. Quit it first so the DLL can be replaced." >&2
  exit 1
fi

"$(dirname "$0")/build.sh"
mkdir -p "$MODS_DIR"
rm -rf "$MODS_DIR/SpireMind"
cp -R "$REPO_ROOT/out/SpireMind" "$MODS_DIR/SpireMind"
echo "Installed to $MODS_DIR/SpireMind"
echo "Launch the game, accept the mod prompt if shown, and check: curl -s http://127.0.0.1:17772/health"
echo "To let the bot play by itself, open the chat in game (T) and type /start. /help lists the other commands."
