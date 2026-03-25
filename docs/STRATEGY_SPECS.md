# STRATEGY_SPECS.md

## STRAT-001 VCP Swing
universe: liquid equities, configurable top-N
timeframe: daily primary, weekly optional
mode: swing equity

filters:
- price > 200DMA
- 200DMA slope >= threshold
- market breadth >= threshold (BreadthService — % above SMA20)

setup:
- prior uptrend
- >= 2 contractions
- each depth < previous
- tightening near resistance
- optional volume contraction confirmation

entry: near final contraction support
alt_entry: breakout above resistance + volume
stop: below final contraction low
sizing: 70-80% initial; scale on profitable EMA bounce only
partial_exit: at prior resistance/target
trend_exit: close below 5EMA or 10EMA (configurable)
invalidation: structure breaks OR price < final contraction low OR breadth < threshold
data_required: daily OHLCV, SMA/EMA, BreadthService

---

## STRAT-002 Fibonacci Hedged Option Spread
universe: Nifty50 + liquid stocks
mode: options hedged spreads only | naked selling BANNED
data_dependency: IV per strike from mStock (VERIFY_LIVE) or Black-Scholes fallback

filters:
- IVP >= threshold (requires 60-day IV history warmup)
- no event within exclusion_days window (EventCalendarService)
- option liquidity/spread constraints pass

setup:
- detect swing high/low
- compute fib levels
- 1.618 = entry zone | 0.786 = invalidation zone

direction: uptrend -> put credit spread | downtrend -> call credit spread
entry: spread in 1.618 zone
stop: underlying breaches 0.786 | daily capital loss cap
exit: premium capture target; forced exit before event/expiry
risk: max concurrent positions, per-symbol cap, max daily loss
data_required: OHLCV, mStock option chain, IV/IVP, EventCalendarService

---

## STRAT-003 Intraday PCR/OI/VWAP
universe: index options primary
mode: intraday options
data_dependency: Gamma per strike from mStock (VERIFY_LIVE) or Delta proxy

session:
- observe 09:15-11:00 IST, no trade before window ends
- large gap (> threshold points) -> delay to 13:00 or 14:00 IST

bias:
- PCR(change in OI) > upper_threshold -> bullish -> calls only
- PCR(change in OI) < lower_threshold -> bearish -> puts only
- else no trade

strike: target delta 0.30-0.35 | prefer high gamma | liquidity check
entry: option price within VWAP tolerance
stop: near option day low/high
exit: fixed points target or RR | early on PCR reversal | session cutoff
special: expiry day -> use next expiry contract
data_required: mStock option chain, OI snapshots, computed VWAP, gap detection
