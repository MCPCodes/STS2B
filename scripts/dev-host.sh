#!/usr/bin/env bash
# Run the bridge against a fake one-fight game, to try the Claude Code hookup without STS2.
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec "$DOTNET" run --project "$REPO_ROOT/src/SpireMind.DevHost" -- "${1:-17772}"
