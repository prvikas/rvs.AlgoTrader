#!/usr/bin/env bash
set -euo pipefail

payload="$(cat)"

if echo "$payload" | grep -qE '"tool_name":"(Edit|Write)"'; then
  # block direct system clock
  if echo "$payload" | grep -qE 'DateTime\.Now|DateTimeOffset\.UtcNow'; then
    echo "Blocked: use IClock, not DateTime.Now" >&2
    exit 2
  fi
  # block secrets in output
  if echo "$payload" | grep -qiE 'api_key=|api_secret=|password=|access_token=|private_key=|bearer [a-z0-9]{20,}'; then
    echo "Blocked: possible secret value in output. Never log or echo credentials." >&2
    exit 2
  fi
fi

exit 0
