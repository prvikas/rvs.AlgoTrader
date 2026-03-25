## System shape
Modular monolith with three main contexts:
- TradingExecution
- DataIngestion
- Backtesting

## Layers
Domain <- Application <- Infrastructure <- API

## Core rules
- strategies evaluate only on closed candles
- same strategy logic across backtest, forward test, live
- only execution adapters differ by mode
- backtesting never calls broker APIs
- forward testing never places real orders
- live trading uses broker execution, risk controls, and approval gates

## Execution modes
- BacktestExecutionEngine: historical simulation
- SimulatedExecutionEngine: forward/live-paper simulation
- LiveExecutionEngine: real broker execution

## Shared concerns
- IClock for all time operations
- ICandleCache for recent market data cache
- ICapitalAllocator for capital reservation
- IStrategyScheduler for session handling
- ISecretsProvider for secrets
- audit trail for all critical state changes

## Non-goals
- no event sourcing
- no GraphQL
- no CQRS read-model sprawl
- no unnecessary abstractions for simple CRUD
