# Shared paths for the scripts. Override any of these in your shell or in scripts/local.env.
_here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[ -f "$_here/local.env" ] && . "$_here/local.env"

: "${GAME_DIR:=$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
: "${GAME_APP:=$GAME_DIR/SlayTheSpire2.app}"
: "${MODS_DIR:=$GAME_APP/Contents/MacOS/mods}"

# The game targets net9.0. Homebrew's dotnet@9 is keg-only, so prefer it when present.
if [ -z "${DOTNET:-}" ]; then
  if [ -x /opt/homebrew/opt/dotnet@9/bin/dotnet ]; then
    DOTNET=/opt/homebrew/opt/dotnet@9/bin/dotnet
    export DOTNET_ROOT=/opt/homebrew/opt/dotnet@9/libexec
  else
    DOTNET=dotnet
  fi
fi
REPO_ROOT="$(cd "$_here/.." && pwd)"
