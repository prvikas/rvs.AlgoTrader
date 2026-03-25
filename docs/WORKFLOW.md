\# WORKFLOW.md



\## Lifecycle

research -> backtest -> forward test -> approval gate -> live deploy -> monitor -> review



\## Backtest

requirements:

\- deterministic, reproducible

\- realistic costs via BacktestCostProfile

\- output: CAGR, max drawdown, win rate, profit factor, expectancy, trade count, equity curve



\## Forward test

requirements:

\- same signal logic as backtest (IStrategy identical)

\- SimulatedExecutionEngine only

\- no real orders

\- min days defined per strategy



\## Approval gate

a strategy may go live ONLY if ALL of the following pass:



\### Automated checks

\- backtest\_cagr >= strategy.approval\_config.min\_cagr

\- backtest\_max\_drawdown <= strategy.approval\_config.max\_drawdown\_pct

\- forward\_test\_days >= strategy.approval\_config.min\_forward\_test\_days

\- forward\_test\_win\_rate >= strategy.approval\_config.min\_win\_rate (optional)

\- forward\_test\_pnl >= 0 (optional)



\### Manual approval

\- trader reviews results and explicitly approves

\- approval recorded in strategy\_approvals table

\- audit\_log entry written with actor, timestamp, thresholds at time of approval



\### Approval schema

CREATE TABLE strategy\_approvals (

&#x20; id UUID PRIMARY KEY DEFAULT gen\_random\_uuid(),

&#x20; strategy\_instance\_id UUID REFERENCES strategy\_instances(id),

&#x20; approved\_by TEXT NOT NULL,

&#x20; approved\_at TIMESTAMPTZ NOT NULL,

&#x20; backtest\_cagr NUMERIC(8,4),

&#x20; backtest\_max\_drawdown\_pct NUMERIC(8,4),

&#x20; forward\_test\_days INT,

&#x20; forward\_test\_win\_rate NUMERIC(8,4),

&#x20; notes TEXT,

&#x20; thresholds\_snapshot JSONB,

&#x20; created\_at TIMESTAMPTZ DEFAULT NOW()

);



\### Block rule

LiveExecutionEngine.ExecuteAsync() checks:

\- strategy\_approvals row exists for strategy\_instance\_id

\- if missing -> reject with APPROVAL\_REQUIRED error

\- if thresholds changed since approval -> require re-approval



\## Live

\- mStock Type B as primary execution broker

\- capital controls, kill switch, idempotent orders

\- monitor fills, slippage, PnL, drawdown



\## Review

compare live vs forward vs backtest:

\- signal frequency drift

\- fill quality

\- drawdown vs expected

capture lessons in SELF\_LEARNING.md



