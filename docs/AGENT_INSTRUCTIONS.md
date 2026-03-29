# Agent Instructions — rvs.AlgoTrader

This file governs how **all AI agents** (Claude, Copilot, or any automated tool) must work on this repository.

---

## 0. Mindset

You are building an **Argus-class institutional trading platform** for Indian markets — not a toy algo bot. Every decision must meet the standard of a professional Quant Developer working at a systematic hedge fund. Code quality, correctness, and capital safety are non-negotiable.

---

## 1. Before Writing Any Code

1. **Read the issue body completely** — every issue contains a design sketch, code contracts, and an Acceptance Criteria checklist.
2. **Inspect the relevant `src/` files** — never assume an interface or class exists. Verify first.
3. **Check `docs/IMPLEMENTATION_STATUS.md`** — confirms what is actually built.
4. **Check dependencies** — if the issue lists "Depends on #N", verify #N is closed/implemented first. Do not implement out of order.

---

## 2. Milestone Order — Strict

Always work the **lowest-numbered open milestone** first:

```
v0.1 Core Architecture  →  v0.2 Data Layer  →  v0.3 Options Engine
  →  v0.4 Risk & Execution  →  v0.5 Strategies  →  v0.6 MTF & Signals
  →  v0.7 Research & Analytics  →  v0.8 Production
```

Within a milestone:
1. `bug` label → highest priority
2. `architecture` label → before `enhancement`
3. `enhancement` label → after architecture is stable

---

## 3. Implementation Standards

### C# Conventions
```csharp
// Interfaces always injected — never new() concrete classes in business logic
public class MyService(IIndicatorLibrary indicators, ILogger<MyService> logger)
{
    // Primary constructor injection (C# 12)
    // All dependencies via constructor, never service locator
}

// Records for immutable data transfer
public record SignalResult(
    SignalType Signal,
    decimal? EntryPrice,
    string Reason
);

// Async all the way — no .Result or .Wait()
public async Task<SignalResult> EvaluateAsync(
    StrategyContext context,
    CancellationToken ct)
{
    // Always propagate CancellationToken
}
```

### File placement
```
New strategy:         src/rvs.AlgoTrader.Strategies/{StrategyName}/
New indicator:        src/rvs.AlgoTrader.Indicators/
New risk service:     src/rvs.AlgoTrader.LiveExecution/Risk/
New options service:  src/rvs.AlgoTrader.Options/
New DB migration:     src/rvs.AlgoTrader.Infrastructure/Persistence/Migrations/
New unit test:        tests/unit/{ProjectName}.Tests/
```

### Documentation
- Every `public` type and member must have XML doc comments
- Non-obvious logic must have an inline comment explaining the **why**, not the **what**

### Testing
```csharp
// Minimum 3 unit tests per issue (as specified in Acceptance Criteria)
// Use Arrange / Act / Assert pattern
// Test names: MethodName_Scenario_ExpectedOutcome
public async Task EvaluateAsync_PriceBelow200Sma_ReturnsSkip()
{
    // Arrange
    var candles = CandleBuilder.Build(250, trend: Trend.Down);
    var strategy = new VcpSwingStrategy(config, indicators);

    // Act
    var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);

    // Assert
    result.Signal.Should().Be(SignalType.Skip);
    result.Reason.Should().Contain("200 SMA");
}
```

### Database Migrations
- Every schema change = new numbered SQL file
- Always idempotent (`IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`)
- Next number: check existing files and increment by 1
- Zero-padded 3 digits: `009_`, `010_`, `011_`...

---

## 4. Risk-Critical Code — Extra Rules

The following areas affect real capital. Extra scrutiny required:

| Area | Rule |
|------|------|
| `IPortfolioRiskManager` | `CanPlaceOrder` must be called before **every** order in live mode. Never bypass. |
| `AllowNakedSelling` | Default `false`. Must not be overridden without operator confirmation. |
| `ExecutionMode.Live` | Never place real broker orders from `Paper` or `Backtest` mode code paths. |
| Circuit breakers | Daily Loss Limit must halt ALL strategies, not just the one that tripped it. |
| Slippage model | Backtests must never use `SlippageModel.None` by default — use `Percentage` (0.05%). |
| Token refresh | Access tokens must refresh proactively (Zerodha: 23:45 IST daily), never reactively on 403. |

---

## 5. Pull Request Checklist

Before opening a PR:

- [ ] All acceptance criteria checkboxes in the issue are ticked
- [ ] Unit tests pass (`dotnet test`)
- [ ] No existing test regressions
- [ ] DB migration added (if schema changed)
- [ ] XML doc comments on all new public members
- [ ] PR title format: `[v0.X] Short description (#issue_number)`
- [ ] PR body: `Closes #N` to auto-close the issue on merge
- [ ] No secrets, API keys, or broker tokens in code

---

## 6. What NOT to Do

- ❌ Do not skip milestones — implement v0.3 before v0.5
- ❌ Do not implement features not tracked in an issue
- ❌ Do not use `Thread.Sleep` — use `await Task.Delay` with CancellationToken
- ❌ Do not catch `Exception` broadly — catch specific exceptions
- ❌ Do not use EF Core auto-migrations — use numbered SQL files only
- ❌ Do not hardcode broker credentials — use `IConfiguration` / environment variables
- ❌ Do not place live orders in unit tests — mock `IBrokerClient` always
- ❌ Do not merge without CI passing

---

## 7. Persona Reference — Who Uses This Platform

When designing features, always consider all 9 personas:

| Persona | Primary concern | Key features they need |
|---------|----------------|------------------------|
| **Quant Researcher** | Strategy alpha, robustness | Monte Carlo, Walk-Forward, Sharpe, MAE/MFE |
| **Systematic Trader** | Signal reliability, execution quality | Trailing stops, paper mode, MTF filters |
| **Portfolio Manager** | Portfolio Sharpe, drawdown control | Correlation matrix, Markowitz weights, P&L attribution |
| **Strategy Architect** | Clean strategy interfaces, reusability | `IStrategy`, `StrategyContext`, config schemas |
| **Risk Manager** | Capital preservation, circuit breakers | DLL, margin limits, delta caps, event calendar |
| **Execution Researcher** | Fill quality, slippage modelling | Bid-ask model, volume impact, commission breakdown |
| **Quant Developer** | Extensibility, testability | Interface contracts, DI, mocked tests, migrations |
| **Fund Manager** | P&L reporting, audit trail, compliance | Trade journal, tax export, paper→live gate |
| **Options Trader** | Greek management, IV analysis | Black-Scholes, IV rank, spread orders, leg selector |

---

## 8. Issue Labels Reference

| Label | Meaning |
|-------|---------|
| `bug` | Incorrect behaviour in existing code — fix before enhancements |
| `architecture` | Foundational interface or structural change |
| `enhancement` | New feature or capability |
| `risk` | Affects capital safety — extra review required |
| `live-trading` | Touches live execution path |
| `backtesting` | Affects backtest engine or analytics |
| `options` | Options-specific feature |
| `strategy` | New or modified strategy |
| `data` | Data ingestion, quality, or feeds |
| `research` | Analytics, metrics, simulation |

---

## 9. Getting Help

If a design decision is ambiguous:
1. Check `docs/STRATEGY_SPECS.md` for strategy-level decisions
2. Check `docs/ARCHITECTURE.md` for structural decisions
3. Check existing similar implementations in `src/rvs.AlgoTrader.Strategies/` for patterns
4. If still unclear, leave a comment on the GitHub issue before implementing
