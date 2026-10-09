#!/usr/bin/env bash
set -euo pipefail
. "$(dirname "$0")/env.sh"
exec "$DOTNET" test "$REPO_ROOT/tests/SpireMind.Core.Tests"
