#!/usr/bin/env bash
# Run the bridge against a fake one-fight game, to try the Claude Code hookup without STS2:
#   scripts/dev-host.sh [port]
# Or let the API brain play that fight with your key, to prove the real API calls work:
#   scripts/dev-host.sh --brain [--model <name>] [--max-requests <n>] [--fast]
# Or try co-op chat on localhost without the game: fake bot copies, and a fake human copy that
# relays what you type to them as Mark's game does (type hello, /pause, /bots, /bot add Regent):
#   scripts/dev-host.sh 17773 --bot [--character <name>] [--net-id <n>] [--echo]
#   scripts/dev-host.sh --relay [--bot-ports <list>]
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec "$DOTNET" run --project "$REPO_ROOT/src/SpireMind.DevHost" -- "$@"
