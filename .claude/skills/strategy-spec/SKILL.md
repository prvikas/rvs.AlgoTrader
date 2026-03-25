---
name: strategy-spec
description: Convert discretionary strategy ideas into explicit build-ready specs
---

# Strategy spec

## Required fields
- universe
- timeframe
- mode
- filters
- setup
- entry
- stop
- exit
- sizing
- invalidation
- data_required

## Rules
- no coding from prose-only ideas
- parameterize thresholds
- separate signal logic from execution
- flag missing datasets explicitly

## Output
update `docs/STRATEGY_SPECS.md`
