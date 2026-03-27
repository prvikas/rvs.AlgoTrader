# Backtesting Workflow

## Overview
The backtesting system allows you to validate trading strategies on historical data before deploying to live trading.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Frontend                                                                    │
│  - StrategyLabPage: select strategy + parameters                           │
│  - BacktestPage: submit backtest request + view results                    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ API (src/rvs.AlgoTrader.API/Controllers/BacktestController.cs)             │
│  - POST /api/backtest/download-history                                    │
│  - POST /api/backtest/run                                                 │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ Application Layer (Commands/Queries via MediatR)                            │
│  - RunBacktestQuery → BacktestService                                       │
│  - BacktestService orchestrates: validate → download → run → metrics       │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ Infrastructure Services                                                     │
│  - HistoricalDownloadService: fetch from broker, save to candles table    │
│  - BacktestEngine (from rvs.AlgoTrader.Backtesting):                       │
│    1. Load candles from DB (ICandleRepository)                             │
│    2. Validate data (≥50 bars)                                             │
│    3. Instantiate strategy (IStrategyFactory)                              │
│    4. Walk through history (no lookahead)                                  │
│    5. Evaluate strategy signals                                            │
│    6. Simulate fills + calculate P&L (ITransactionCostCalculator)         │
│    7. Compute metrics (Sharpe, Calmar, MaxDD, WinRate, etc.)               │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Step 1: Download Historical Data

```http
POST /api/backtest/download-history
Content-Type: application/json

{
  "internalSymbol": "NIFTY",
  "timeframe": "1d",
  "fromDate": "2023-01-01",
  "toDate": "2024-12-31",
  "brokerName": "MStock"  # optional, defaults to MStock
}
```

**Response (Success):**
```json
{
  "success": true,
  "data": {
    "barCount": 502,
    "dataHash": "abc123def456..."
  },
  "error": null
}
```

**Response (Error):**
```json
{
  "success": false,
  "data": null,
  "error": "No broker token for MStock:NIFTY"
}
```

**What happens:**
1. HistoricalDownloadService fetches from broker's MarketData API
2. Converts bars to ClosedCandle value objects
3. Bulk inserts into `candles` table (idempotent on OpenTime)
4. Returns bar count + SHA-256 data hash for reproducibility

## Step 2: Run Backtest

```http
POST /api/backtest/run
Content-Type: application/json

{
  "strategyName": "PriceActionBreakout",
  "parametersJson": "{\"lookback\": 20, \"atrMultiplier\": 2.0}",
  "internalSymbol": "NIFTY",
  "timeframe": "1d",
  "fromDate": "2023-01-01",
  "toDate": "2024-12-31",
  "initialCapital": 100000,
  "riskPerTradePercent": 1.0,
  "fillModel": 0,           # 0=NextBarOpen (default), 1=NextBarOpenPlusSlippage, 2=SignalBarClose
  "slippageBasisPoints": 5, # default 5 bps
  "brokerageFlatPerSide": 20  # ₹20/order (Zerodha/Upstox model)
}
```

**Response (Success):**
```json
{
  "success": true,
  "data": {
    "strategyName": "PriceActionBreakout",
    "symbol": "NIFTY",
    "timeframe": "1d",
    "totalTrades": 24,
    "winCount": 16,
    "winRate": 0.667,
    "totalPnl": 45320.50,
    "totalReturn": 0.4532,
    "maxDrawdown": 0.128,
    "sharpeRatio": 1.85,
    "calmarRatio": 3.54,
    "profitFactor": 3.2,
    "trades": [
      {
        "direction": "BUY",
        "entryPrice": 21450.50,
        "entryTime": "2023-02-15T09:15:00",
        "exitPrice": 21680.25,
        "exitTime": "2023-02-20T15:30:00",
        "quantity": 47,
        "grossPnl": 10832.50,
        "netPnl": 9632.50,
        "exitReason": "TAKE_PROFIT"
      },
      ...
    ]
  },
  "error": null
}
```

**Response (Error - No Data):**
```json
{
  "success": false,
  "data": null,
  "error": "Insufficient candle data (< 50 bars)"
}
```

## Available Strategies

### 1. PriceActionBreakout
N-bar range breakout with ATR/volume filter.

**Parameters:**
```json
{
  "lookback": 20,         # number of bars for range
  "atrMultiplier": 2.0,   # ATR-based stop distance
  "volumeMultiplier": 1.5 # volume threshold
}
```

### 2. EmaVwapMomentum
EMA crossover + VWAP + Bollinger Bands + Volume + Open-Close pattern (optional).

**Parameters:**
```json
{
  "emaPeriod1": 9,
  "emaPeriod2": 21,
  "bbPeriod": 20,
  "bbStdDev": 2.0,
  "useVwap": true,
  "useOcPattern": false
}
```

### 3. AlertCandleShort
5-EMA alert candle short. Designed for BankNifty/Nifty on 5m timeframe.

**Parameters:**
```json
{
  "emaPeriod": 5,
  "riskPercent": 1.0
}
```

## Backtest Engine Implementation

**File:** `src/rvs.AlgoTrader.Backtesting/Engine/BacktestEngine.cs`

**Key design decisions:**

1. **No lookahead bias**
   - Strategy receives only candles up to and including the current bar
   - Fill on signal bar depends on FillModel (default: next bar open)

2. **Walk-forward simulation**
   - 50-bar warm-up before first signal evaluation
   - One position at a time (no pyramiding)
   - Stop-loss / Take-profit evaluated on every subsequent candle

3. **Cost calculation**
   - Flat brokerage (₹20/order default for Indian discount brokers)
   - STT (0.025% on equity)
   - GST (18% on brokerage)
   - SEBI charges (0.000001%)
   - Stamp duty (0.003%)
   - Slippage (configurable, basis points or percentage)

4. **Position sizing**
   - Risk-based: `position_size = (equity * risk_pct) / stop_distance`
   - Requires non-zero stop loss from strategy

5. **Metrics computation**
   - **Sharpe Ratio**: (avg_return / std_dev) * sqrt(252)
   - **Calmar Ratio**: total_return / max_drawdown
   - **Win Rate**: winning_trades / total_trades
   - **Profit Factor**: gross_profit / gross_loss
   - **Expectancy**: (win_rate * avg_win) - (loss_rate * avg_loss)
   - **Max Drawdown**: peak equity decline from high water mark

## Reproducibility

Every backtest run produces a **SHA-256 data hash** computed from:
- Candle OHLCV data (timestamp, open, high, low, close, volume)
- Strategy name
- Parameters JSON
- Date range
- Initial capital

This hash can be used to:
- Verify reproducibility across runs
- Audit backtest integrity
- Compare results with other systems

## Error Handling

Common failure modes and how to fix:

| Error | Cause | Fix |
|-------|-------|-----|
| "Insufficient candle data (< 50 bars)" | Database is empty or date range too narrow | Call `/api/backtest/download-history` first |
| "Instrument 'XYZ' not found" | Symbol not in `instruments` table | Refresh master data via MasterDataRefreshPage |
| "No broker token for MStock:XYZ" | Instrument record has null broker token | Re-run instrument refresh from broker |
| "Unknown strategy: 'FakeStrategy'" | Strategy not registered in StrategyFactory | Use one of: PriceActionBreakout, EmaVwapMomentum, AlertCandleShort |
| "Invalid JSON in parametersJson" | Strategy config can't be deserialized | Fix JSON syntax in parameters field |

## Testing

**Unit tests:**
```bash
dotnet test tests/rvs.AlgoTrader.Tests.Unit/Services/BacktestEngineTests.cs
```

**Integration tests (full flow with real DB):**
```bash
./run-tests.sh integration
```

## Execution Modes

The same `IStrategy` implementation is used across three execution modes:

| Mode | Class | Use Case |
|------|-------|----------|
| **Backtest** | `BacktestExecutionEngine` | Historical simulation, no real orders |
| **Forward Test** | `SimulatedExecutionEngine` | Paper trading, real market data, no real orders |
| **Live** | `LiveExecutionEngine` | Real orders to broker, real capital at risk |

The strategy logic (`IStrategy.EvaluateAsync`) is **identical** across all three modes. Only the execution (fill simulation) differs.

## Anti-patterns to avoid

- ❌ Using `DateTime.Now` in strategy → use `IClock.NowInstant()`
- ❌ Partial candles in strategy evaluation → wait for `IsClosed=true`
- ❌ Backtesting calling live broker APIs → use HistoricalDownloadService
- ❌ Hardcoded slippage → use configurable `SlippageBasisPoints`
- ❌ Backtesting without stop-loss → position sizing will fail

## Frontend Integration

See `frontend/src/pages/BacktestPage.tsx` for the complete UI flow.

**Key components:**
- `BacktestForm` (right-side drawer): strategy selection, date range, parameters
- `BacktestResults` table: trades with entry/exit prices and P&L
- `MetricCard` grid: Sharpe, Calmar, Max Drawdown, Win Rate, etc.

