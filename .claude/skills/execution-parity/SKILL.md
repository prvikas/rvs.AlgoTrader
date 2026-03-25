---
name: execution-parity
description: Preserve parity across backtest, forward test, and live modes
model: sonnet
---

# Execution parity

## Must hold
- same strategy logic
- same indicator logic
- same candle-close rule
- only execution adapters differ

## Checks
- backtest never calls brokers
- forward test never places real orders
- live requires approval + risk controls
- update parity tests on signal logic changes
