#!/usr/bin/env bash
set -euo pipefail

input="$(cat)"

cmd="$(echo "$input" | sed -n 's/.*"command":"\([^"]*\)".*/\1/p')"

if echo "$cmd" | grep -Eq 'rm -rf /|git reset --hard|git clean -fdx'; then
  echo "Blocked: dangerous command requires explicit user approval." >&2
  exit 2
fi

if echo "$cmd" | grep -Eq 'dotnet ef migrations add|dotnet ef database update'; then
  echo "Blocked: database/schema commands are not allowed unless explicitly requested." >&2
  exit 2
fi

exit 0
