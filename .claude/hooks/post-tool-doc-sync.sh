#!/usr/bin/env bash
set -euo pipefail

input="$(cat)"

if echo "$input" | grep -Eqi 'Strategy|Backtest|ForwardTest|ExecutionEngine|CLAUDE\.md|README\.md'; then
  echo "Reminder: update docs/IMPLEMENTATION_STATUS.md and docs/REQUIREMENTS_DELTA.md if behavior changed." >&2
fi

exit 0
