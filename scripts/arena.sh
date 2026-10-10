#!/usr/bin/env bash
# The arena: seeded fights in a simulator of the game's combat (v0.107.1), to compare how the models play.
#   scripts/arena.sh baseline [--scenarios N] [--seed S]        the scripted policies; costs nothing
#   scripts/arena.sh run --models a,b,c [--scenarios N] ...      Claude Code on your subscription
#   scripts/arena.sh report DIR|results.jsonl                    the report of a run again
#   scripts/arena.sh selfcheck [--checks A,B]                    the simulator and the tool path still hold
#   scripts/arena.sh honesty [--scenarios N] [--seed S]          what snapshots promise against what happens
#   scripts/arena.sh content                                     what the simulator holds of the game, and what not
#   scripts/arena.sh snapshot SCENARIO-ID                        a scenario's first snapshot
# See docs/arena.md, or scripts/arena.sh --help.
set -euo pipefail
. "$(dirname "$0")/env.sh"
project="$REPO_ROOT/src/SpireMind.Arena"
# Built first and then started directly, not through "dotnet run": this way the process this
# script becomes is the arena itself, so a Ctrl+C or kill reaches it and it stops its Claude
# Code sessions instead of leaving them running on the subscription.
if ! log="$("$DOTNET" build "$project" -c Release --nologo -v quiet 2>&1)"; then
  printf '%s\n' "$log" >&2
  exit 1
fi
exec "$DOTNET" "$project/bin/Release/net9.0/SpireMind.Arena.dll" "$@"
