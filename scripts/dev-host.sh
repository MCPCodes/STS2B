#!/usr/bin/env bash
# Run the bridge against a fake one-fight game, to try the Claude Code hookup without STS2:
#   scripts/dev-host.sh [port]
# Or let the API brain play that fight with your key, to prove the real API calls work:
#   scripts/dev-host.sh --brain [--model <name>] [--max-requests <n>]
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec "$DOTNET" run --project "$REPO_ROOT/src/SpireMind.DevHost" -- "$@"
