using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Brokers.Abstractions;
using NodaTime;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Provides the identity of the current authenticated user.
/// Implemented by HttpContextCurrentUser in the API layer (reads JWT claims).
/// Falls back to "System" when running outside an HTTP context (background jobs, scheduler).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Returns the authenticated user's name/identity, or "System" when no HTTP context is present.</summary>
    string Actor { get; }
}

public interface IMonitoringAlertEvaluator
{
    Task EvaluateAllAsync(CancellationToken ct);
}

public interface IStartupOrchestrator
{
    Task RunAsync(CancellationToken ct);
}

public interface IIdempotencyService
{
    Task<IdempotencyResult> CheckAsync(string key, CancellationToken ct);
    Task StoreAsync(string key, object response, CancellationToken ct);
}

public record IdempotencyResult(bool IsDuplicate, object? CachedResponse);

public interface IFieldEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public interface ISecretsProvider
{
    Task<string?> GetSecretAsync(string path, CancellationToken ct);
    Task SetSecretAsync(string path, string value, CancellationToken ct);
}

public interface ISecretsProviderFactory
{
    ISecretsProvider Create();
}

public interface IStrategyInstanceManager
{
    Task<Guid> StartAsync(Guid instanceId, CancellationToken ct);
    Task PauseAsync(Guid instanceId, string reason, CancellationToken ct);
    Task StopAsync(Guid instanceId, string reason, CancellationToken ct);
}

public interface IMarketCalendarService
{
    /// <summary>Async check against the market_calendar DB table.</summary>
    Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct);

    /// <summary>
    /// Synchronous check used by IStrategyScheduler.IsWithinScheduledSession (which is sync).
    /// Implementations should cache the holiday set at startup.
    /// </summary>
    bool IsTradingDay(LocalDate date);

    bool IsWithinMarketHours(ZonedDateTime time);
}

public interface IIndicatorService
{
    decimal[] Ema(decimal[] closes, int period);
    decimal[] Sma(decimal[] closes, int period);
    decimal[] Vwap(Domain.ValueObjects.ClosedCandle[] candles);
    decimal[] Atr(Domain.ValueObjects.ClosedCandle[] candles, int period);
    (decimal[] Upper, decimal[] Mid, decimal[] Lower) BollingerBands(decimal[] closes, int period, decimal stdDev);
}

public interface IIncrementalIndicator<T>
{
    T Update(decimal close, long? volume = null);
    void Reset();
}

public interface ICandleCache
{
    Task<IReadOnlyList<Domain.ValueObjects.ClosedCandle>> GetAsync(string symbol, string timeframe, int count, CancellationToken ct);
    Task AppendAsync(Domain.ValueObjects.ClosedCandle candle, CancellationToken ct);
    Task WarmAsync(string symbol, string timeframe, IEnumerable<Domain.ValueObjects.ClosedCandle> candles, CancellationToken ct);
}

public interface IStrategyExecutionThrottler
{
    Task<bool> TryAcquireAsync(Guid instanceId, CancellationToken ct);
    void Release(Guid instanceId);
}

public interface IHistoricalDataDownloadService
{
    Task EnqueueAsync(string symbol, string timeframe, DateOnly from, DateOnly to, string brokerName, CancellationToken ct);
    Task<DownloadJob?> GetStatusAsync(Guid jobId, CancellationToken ct);
    Task CancelAsync(Guid jobId, CancellationToken ct);
}

/// <summary>
/// Application-layer session manager — higher-level facade over the broker session store.
/// Renamed from IBrokerSessionManager to avoid ambiguity with
/// rvs.AlgoTrader.Brokers.Abstractions.IBrokerSessionManager.
/// </summary>
public interface IAppBrokerSessionManager
{
    Task EnsureValidSessionAsync(string brokerName, CancellationToken ct);
    Task RefreshAsync(string brokerName, CancellationToken ct);
    Task<bool> IsAuthenticatedAsync(string brokerName, CancellationToken ct);
    /// <summary>Store a broker session after successful authentication.</summary>
    Task StoreSessionAsync(string brokerName, LoginResult result, CancellationToken ct);
    /// <summary>Retrieve a stored access token from persistent storage (Redis).</summary>
    Task<string?> TryGetAccessTokenAsync(string brokerName, CancellationToken ct);
    /// <summary>Retrieve a stored feed token from persistent storage (Redis). mStock only.</summary>
    Task<string?> TryGetFeedTokenAsync(string brokerName, CancellationToken ct);
    /// <summary>Returns true if a non-expired session token exists in Redis.</summary>
    bool IsSessionValid(string brokerName);
}

// IInstrumentTokenResolver is defined in rvs.AlgoTrader.Brokers.Abstractions.
// Application references that project directly, so no duplicate definition needed here.

public interface IDataQualityService
{
    Task<DataQualityReport> AnalyzeAsync(string symbol, string timeframe, CancellationToken ct);
}

public record DataQualityReport(string Symbol, string Timeframe, int GapCount, int BadCandleCount, int SpikeCount, IReadOnlyList<string> Issues);

public interface IAppConfigService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, string actor, string correlationId, CancellationToken ct);
}

public interface INotificationService
{
    Task SendAsync(string channel, string severity, string message, CancellationToken ct);
}

public interface IAuditService
{
    Task LogAsync(string action, string actor, string entityType, string entityId, object? details, string correlationId, CancellationToken ct);
}

public interface ITransactionCostCalculator
{
    TransactionCosts Calculate(decimal tradeValue, bool isBuy, CostProfile profile);
}

public record TransactionCosts(decimal Brokerage, decimal Stt, decimal Gst, decimal SebiCharges, decimal StampDuty, decimal Slippage)
{
    public decimal Total => Brokerage + Stt + Gst + SebiCharges + StampDuty + Slippage;
}

/// <summary>
/// Transaction cost profile for Indian equity markets.
/// BrokerageFlatPerSide: flat ₹ amount per order leg (typical discount-broker model, e.g. ₹20/order).
///   When set > 0, this is used INSTEAD of BrokeragePct.
///   Default = 0 (falls back to percentage-based brokerage).
/// SlippageBasisPoints: additional slippage on top of SpreadPct, in basis points (1 bp = 0.01%).
///   Used by FillModel.NextBarOpenPlusSlippage.
/// </summary>
public record CostProfile(
    decimal BrokeragePct,
    decimal SttPct,
    decimal GstPct,
    decimal SebiChargesPct,
    decimal StampDutyPct,
    decimal SlippagePct,
    decimal BrokerageFlatPerSide = 0m,
    decimal SlippageBasisPoints = 0m);

public interface IKillSwitchService
{
    Task ActivateAsync(string actor, string reason, string correlationId, CancellationToken ct);
    Task DeactivateAsync(string actor, string correlationId, CancellationToken ct);
    Task<bool> IsActiveAsync(CancellationToken ct);
    Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct);
}

public record KillSwitchStatus(bool IsActive, string? ActivatedBy, string? Reason, DateTimeOffset? ActivatedAt);

public interface IPositionReconciliationService
{
    Task ReconcileAsync(CancellationToken ct);
    Task<ReconciliationStatus> GetStatusAsync(CancellationToken ct);
}

public record ReconciliationStatus(DateTimeOffset LastRunAt, bool HasMismatches, IReadOnlyList<string> MismatchedSymbols);

/// <summary>
/// Lightweight value object for stateless trailing-stop calculations (used in unit tests and backtesting).
/// </summary>
public record TrailingStopState(
    decimal EntryPrice,
    decimal CurrentStop,
    string Direction,
    decimal ActivationThresholdPercent,
    decimal TrailStepPercent);

public interface ITrailingStopLossService
{
    Task<decimal?> UpdateTrailingStopAsync(Domain.Entities.Position position, decimal currentPrice, TrailingSLConfig config, CancellationToken ct);

    /// <summary>
    /// Pure stateless trailing-stop update — no repository access.
    /// Used for backtesting, forward tests, and unit tests.
    /// </summary>
    TrailingStopState UpdateStop(TrailingStopState state, decimal currentPrice);
}

public record TrailingSLConfig(decimal ActivationPct, decimal StepPct);

public interface IForwardTestFillSimulator
{
    Task<FillResult> SimulateFillAsync(Domain.Interfaces.SignalResult signal, IReadOnlyList<Domain.ValueObjects.ClosedCandle> subsequentCandles, Domain.Interfaces.IClock clock, FillSimConfig config, CancellationToken ct);
}

public record FillResult(bool Filled, decimal? FillPrice, string? NoFillReason);
public record FillSimConfig(decimal SlippagePct);

public interface IRiskManagementService
{
    Task<RiskCheckResult> CheckAsync(Guid strategyInstanceId, object orderRequest, CancellationToken ct);
}

public record RiskCheckResult(bool Allowed, string? BlockReason);

// ── #87: Position Sizing ──────────────────────────────────────────────────────

/// <summary>
/// Computes position size (in lots) for a given trade setup.
/// Called by LiveExecutionEngine and ForwardTestEngine before order placement.
/// Result is logged for audit trail. Hard cap: never exceed MaxLots or MaxCapitalPct.
/// </summary>
public interface IPositionSizingEngine
{
    /// <param name="model">Sizing algorithm to use.</param>
    /// <param name="equity">Current available equity.</param>
    /// <param name="entryPrice">Proposed entry price.</param>
    /// <param name="stopLoss">Stop-loss price (required for FixedFractional, AtrBased).</param>
    /// <param name="atr">Current ATR value (required for AtrBased, VolatilityTargeting).</param>
    /// <param name="config">Strategy-level sizing configuration.</param>
    /// <returns>Number of lots to trade and the rationale string for audit logging.</returns>
    (int Lots, string Rationale) Compute(
        Domain.Enums.SizingModel model,
        decimal equity,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? atr,
        PositionSizingConfig config);
}

public record PositionSizingConfig(
    int     FixedLots        = 1,
    decimal RiskPerTradePct  = 1.0m,    // % of equity to risk per trade (FixedFractional, AtrBased)
    decimal TargetVolPct     = 15.0m,   // annualised target volatility % (VolatilityTargeting)
    decimal WinRate          = 0.5m,    // required for KellyCriterion (0–1)
    decimal AvgWinLossRatio  = 1.5m,    // avgWin / avgLoss (KellyCriterion)
    int     MaxLots          = 100,     // hard cap regardless of model
    decimal MaxCapitalPct    = 20.0m,   // max % of equity in one trade
    decimal AtrMultiplier    = 2.0m     // ATR stop multiplier for AtrBased sizing (risk = ATR × this)
);

// ── #88: Slippage & Commission Models ────────────────────────────────────────

/// <summary>
/// Computes fill price after applying a slippage model.
/// Used by BacktestEngine and IPaperOrderSimulator to produce realistic fill prices.
/// </summary>
public interface ISlippageModel
{
    /// <returns>Adjusted fill price after slippage.</returns>
    decimal ApplySlippage(decimal idealPrice, Domain.Enums.OrderDirection direction,
        SlippageConfig config, decimal? atr = null, long? volume = null);
}

public record SlippageConfig(
    Domain.Enums.SlippageModelType ModelType,
    decimal FixedPoints      = 0m,   // SlippageModelType.Fixed
    decimal Pct              = 0m,   // SlippageModelType.Percentage (e.g. 0.0005 = 5 bps)
    decimal BidAskSpreadPct  = 0m,   // SlippageModelType.BidAsk
    decimal AtrMultiplier    = 0.1m, // SlippageModelType.AtrFraction
    decimal VolumeImpactPct  = 0m    // SlippageModelType.VolumeImpact
);

/// <summary>
/// Computes total transaction cost for a single-side order.
/// Covers brokerage, STT, GST, SEBI fee, exchange fee, stamp duty.
/// Implementations are broker-specific (Zerodha, Upstox, mStock).
/// </summary>
public interface ICommissionModel
{
    /// <returns>Itemised transaction costs.</returns>
    CommissionBreakdown Calculate(decimal price, int quantity,
        Domain.Enums.OrderDirection direction,
        Domain.Enums.ProductType productType,
        Domain.Enums.InstrumentType instrumentType);
}

public record CommissionBreakdown(
    decimal Brokerage,
    decimal Stt,
    decimal ExchangeFee,
    decimal GstOnBrokerage,
    decimal SebiCharges,
    decimal StampDuty
)
{
    public decimal Total => Brokerage + Stt + ExchangeFee + GstOnBrokerage + SebiCharges + StampDuty;
}

// ── #85/#86: Portfolio Risk Manager ──────────────────────────────────────────

/// <summary>
/// Enforces portfolio-level risk limits across all running strategies.
/// Called by LiveExecutionEngine before each order. Distinct from per-strategy
/// IRiskManagementService (capital reservation) — this guards portfolio-wide state.
/// </summary>
public interface IPortfolioRiskManager
{
    /// <summary>Check if a new order is allowed under current portfolio risk state.</summary>
    Task<RiskCheckResult> CheckOrderAsync(
        Domain.Entities.StrategyInstance instance,
        decimal orderValue,
        CancellationToken ct);

    /// <summary>Get a snapshot of the current portfolio risk state.</summary>
    Task<PortfolioRiskState> GetStateAsync(CancellationToken ct);

    /// <summary>Load (or reload) risk config from DB/config source.</summary>
    Task<PortfolioRiskConfig> GetConfigAsync(CancellationToken ct);

    /// <summary>Persist updated risk config.</summary>
    Task SaveConfigAsync(PortfolioRiskConfig config, CancellationToken ct);
}

public record PortfolioRiskConfig(
    decimal DailyLossLimitPct        = 2.0m,    // % of portfolio equity; triggers kill switch
    int     MaxConcurrentPositions   = 10,
    decimal MaxCapitalDeploymentPct  = 80.0m,   // % of total equity that can be deployed at once
    decimal MaxSymbolExposurePct     = 15.0m,   // max % of equity in one symbol
    decimal MaxMarginUtilisationPct  = 60.0m,   // % of available margin
    decimal MaxPortfolioDeltaPct     = 20.0m    // aggregate delta as % of portfolio value (options)
);

public record PortfolioRiskState(
    decimal TotalEquity,
    decimal DeployedCapital,
    decimal DailyRealizedPnl,
    decimal DailyUnrealizedPnl,
    int     OpenPositionCount,
    decimal MarginUsed,
    bool    DailyLossLimitBreached,
    bool    MaxPositionsBreached,
    bool    MaxDeploymentBreached,
    DateTimeOffset SnapshotAt
);

// ── #92: Paper Trading ────────────────────────────────────────────────────────

/// <summary>
/// Simulates order fills for paper trading mode using live market data.
/// Fills at next bar open with configurable slippage — no real orders placed.
/// Paper P&amp;L is tracked separately from forward-test and live P&amp;L.
/// </summary>
public interface IPaperOrderSimulator
{
    /// <summary>
    /// Submit a paper order and get a simulated fill.
    /// Uses the most recent candle close as the reference price + slippage.
    /// </summary>
    Task<PaperFillResult> SimulateFillAsync(
        Domain.Entities.StrategyInstance instance,
        Domain.Interfaces.SignalResult signal,
        decimal currentPrice,
        CancellationToken ct);
}

public record PaperFillResult(
    bool    Filled,
    decimal FillPrice,
    int     FilledQuantity,
    string  PaperOrderId,
    DateTimeOffset FilledAt
);

// ── #93: Trailing Stop Manager ────────────────────────────────────────────────

/// <summary>
/// Full trailing stop state machine supporting all 7 stop types.
/// Maintains per-position stop state in-memory (keyed by position ID).
/// BacktestEngine: called once per closed bar.
/// LiveExecutionEngine: called on each tick or bar close depending on StopType.
/// </summary>
public interface ITrailingStopManager
{
    /// <summary>Initialise stop tracking for a new position.</summary>
    TrailingStopEntry Register(
        Guid positionId,
        decimal entryPrice,
        decimal initialStop,
        Domain.Enums.OrderDirection direction,
        Domain.Enums.StopType stopType,
        TrailingStopParams parameters);

    /// <summary>
    /// Update stop for a new price observation.
    /// Returns the updated entry (with new stop level and triggered flag).
    /// </summary>
    TrailingStopEntry Update(Guid positionId, decimal currentPrice, decimal? atr = null);

    /// <summary>Remove tracking when a position is closed.</summary>
    void Unregister(Guid positionId);

    TrailingStopEntry? Get(Guid positionId);
}

public record TrailingStopParams(
    decimal TrailOffset        = 0m,   // ₹ for TrailingFixed; % for TrailingPercent
    decimal AtrMultiplier      = 2.0m, // for TrailingAtr, Chandelier
    int     LookbackBars       = 20,   // for Chandelier (highest high / lowest low window)
    int     MaxBarsInTrade     = 0,    // for TimeStop (0 = disabled)
    decimal BreakEvenAt1R      = 1.0m  // R multiple at which BreakEven activates
);

public record TrailingStopEntry(
    Guid                        PositionId,
    decimal                     EntryPrice,
    decimal                     CurrentStop,
    decimal                     BestPrice,     // highest (long) / lowest (short) seen
    Domain.Enums.OrderDirection Direction,
    Domain.Enums.StopType       StopType,
    TrailingStopParams          Parameters,
    bool                        IsTriggered,   // true once stop is breached
    int                         BarsInTrade
);

// ── #100: Scaling Manager ────────────────────────────────────────────────────

/// <summary>
/// Manages scaled entry (pyramid) and exit (partial close) for positions.
/// VCP spec: 70% initial entry + 30% add on EMA bounce = 2-tranche pyramid.
/// Records per-tranche entries for accurate average-cost calculation.
/// </summary>
public interface IScalingManager
{
    /// <summary>
    /// Register a new scaling plan for a position when the initial entry is taken.
    /// Returns the scaling state that will be updated on subsequent triggers.
    /// </summary>
    ScalingState Register(Guid positionId, ScalingConfig config, decimal initialEntryPrice, int initialLots);

    /// <summary>
    /// Evaluate whether the next tranche should be triggered given current price + indicators.
    /// Returns (shouldTrigger: true, lots: N) when the tranche condition is met.
    /// </summary>
    (bool ShouldTrigger, int Lots, string Reason) Evaluate(
        Guid positionId, decimal currentPrice, decimal? ema = null, decimal? atr = null);

    /// <summary>Record execution of a tranche fill.</summary>
    ScalingState RecordFill(Guid positionId, decimal fillPrice, int filledLots);

    void Unregister(Guid positionId);
    ScalingState? Get(Guid positionId);
}

public record ScalingConfig(
    Domain.Enums.ScalingMode      Mode,
    IReadOnlyList<ScaleTranche>   Tranches,
    bool                          CancelRemainingOnStop = true
);

public record ScaleTranche(
    int                            LotsPct,          // % of total position size for this tranche
    Domain.Enums.TrancheTrigger    Trigger,
    decimal                        TriggerPct = 0m,  // % move for PriceMoveUp/Down
    int                            EmaPeriod  = 21   // EMA period for EmaBounce trigger
);

public record ScalingState(
    Guid                           PositionId,
    ScalingConfig                  Config,
    int                            CurrentTranche,   // 0-based index of last filled tranche
    decimal                        AverageEntryPrice,
    int                            TotalLots,
    IReadOnlyList<TrancheExecution> ExecutedTranches
);

public record TrancheExecution(
    int     TrancheIndex,
    decimal FillPrice,
    int     Lots,
    DateTimeOffset FilledAt
);

public interface ISymbolDataPreferencesService
{
    Task<SymbolDataPreferences?> GetPreferencesAsync(string symbol, CancellationToken ct);
    Task UpsertAsync(SymbolDataPreferences preferences, CancellationToken ct);
    Task<IReadOnlyList<SymbolDataPreferences>> GetAllActiveAsync(CancellationToken ct);
}

public record SymbolDataPreferences(Guid Id, string InternalSymbol, string[] Timeframes, DateOnly FromDate, int Priority, bool IsActive);

/// <summary>
/// Fetches and parses NSE option chain data via the active broker's market data API.
///
/// Called by StrategyEvaluationQueue BEFORE building StrategyContext, so that
/// IStrategy.EvaluateAsync implementations can read OptionChain without any I/O (Rule #18).
///
/// The fetch result is cached in Redis for the duration of one candle bar (e.g. 60s for 1m bars)
/// so that multiple strategy instances trading the same underlying share a single network call.
///
/// Key design decisions:
///   - Returns null (not throws) when the option chain is unavailable — strategy falls back to
///     price action only.
///   - Only fetches when the strategy instance has UseOptionChain=true in its parameters_json.
///   - Expiry selection: nearest weekly by default; configurable to monthly.
/// </summary>
/// <summary>
/// Computes Black-Scholes theoretical price and Greeks for European-style options.
/// Used to enrich option chain legs when the broker does not provide Greeks (mStock, Zerodha).
/// </summary>
/// <summary>
/// Resolves an <see cref="Domain.Interfaces.OptionsLegSpec"/> to a concrete tradeable instrument.
/// Looks up the options chain via IInstrumentRepository, selects the right strike using the
/// specified StrikeSelectionMode, and returns the resolved NFO symbol and broker token.
/// </summary>
/// <summary>
/// Tracks the lifecycle of broker orders from submission through terminal states.
/// Replaces the fire-and-forget pattern in LiveExecutionEngine with a proper state machine.
/// Uses increasing-interval polling (1s → 2s → 4s … capped at 30s) plus WebSocket fills.
/// Terminal states: Filled, Cancelled, Rejected, Expired, Error.
/// </summary>
/// <summary>
/// Places multi-leg option spreads atomically (best-effort).
/// All legs are submitted in parallel. If any leg is rejected, the remaining open legs
/// are cancelled. Records a SpreadPosition entity for P&amp;L tracking.
/// </summary>
public interface ISpreadOrderManager
{
    /// <summary>
    /// Execute a spread signal.
    /// Returns the SpreadPosition ID on success, null if placement failed entirely.
    /// </summary>
    Task<Guid?> ExecuteSpreadAsync(
        Domain.Entities.StrategyInstance instance,
        Domain.Interfaces.SpreadSignalResult signal,
        decimal spotPrice,
        NodaTime.LocalDate expiryDate,
        string correlationId,
        CancellationToken ct);

    /// <summary>Close all open legs of a spread position (e.g. on stop-loss or expiry).</summary>
    Task CloseSpreadAsync(Guid spreadPositionId, string reason, string correlationId, CancellationToken ct);

    Task<Domain.Entities.SpreadPosition?> GetSpreadAsync(Guid spreadPositionId, CancellationToken ct);
}

public interface IOrderManager
{
    /// <summary>
    /// Submit a new order and begin tracking its lifecycle.
    /// Returns the internal order ID. The order is polled until a terminal state is reached
    /// or the tracking timeout (5 min) elapses.
    /// </summary>
    Task<Guid> SubmitAsync(
        Domain.Entities.Order order,
        Domain.Entities.StrategyInstance instance,
        string correlationId,
        CancellationToken ct);

    /// <summary>Notify the manager that a fill event arrived via broker WebSocket.</summary>
    void RecordFill(string brokerOrderId, decimal fillPrice, int filledQty, bool isPartial);

    /// <summary>Get the current tracked state of an order. Returns null if unknown.</summary>
    OrderManagerEntry? GetEntry(Guid orderId);

    /// <summary>All currently tracked (non-terminal) order entries.</summary>
    IReadOnlyList<OrderManagerEntry> GetActive();
}

public record OrderManagerEntry(
    Guid OrderId,
    string? BrokerOrderId,
    Domain.Enums.OrderManagerState State,
    decimal? FillPrice,
    int FilledQty,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorReason
);

/// <summary>
/// Computes IV Rank and IV Percentile for an underlying symbol using its 52-week
/// ATM IV history stored in option_iv_history.
/// Returns null when insufficient history is available (&lt; 30 data points).
/// </summary>
/// <summary>
/// Provides FX rates for cross-currency P&amp;L conversion (MCX crude, gold, etc.).
/// Returns the exchange rate to convert from baseCurrency to INR on a given date.
/// Returns 1.0 when baseCurrency == "INR" (no conversion needed).
/// </summary>
public interface IFxRateProvider
{
    /// <param name="baseCurrency">E.g. "USD".</param>
    /// <param name="date">The date to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rate such that: price_in_base × rate = price_in_INR.
    /// Returns the most recent available rate if exact date not found (holiday).</returns>
    Task<decimal> GetRateToInrAsync(string baseCurrency, NodaTime.LocalDate date, CancellationToken ct);

    /// <summary>Store or update an FX rate (called by the RBI import job).</summary>
    Task UpsertAsync(Domain.Entities.FxRate rate, CancellationToken ct);
}

public interface IOptionIvRankService
{
    Task<Domain.ValueObjects.IvRankSnapshot?> GetAsync(
        string underlyingSymbol,
        CancellationToken ct);

    /// <summary>
    /// Record today's ATM IV snapshot. Called by nightly IvHistoryJob.
    /// Idempotent — upserts by (underlying, date).
    /// </summary>
    Task RecordAsync(string underlyingSymbol, decimal atmIv, CancellationToken ct);

    /// <summary>
    /// FIB-3: Pre-fetch raw IV history for a date range (inclusive) for backtesting.
    /// BacktestEngine calls this once before the main candle loop and computes rolling
    /// IV rank per bar in-memory — avoids O(n) database round-trips during backtesting.
    /// Returns records ordered by date descending.
    /// </summary>
    Task<IReadOnlyList<(NodaTime.LocalDate Date, decimal AtmIv)>> GetHistoryRangeAsync(
        string underlyingSymbol,
        NodaTime.LocalDate from,
        NodaTime.LocalDate to,
        CancellationToken ct);
}

/// <summary>
/// Records and retrieves daily EOD option chain snapshots for historical backtesting.
///
/// FIB-5: BacktestEngine calls GetHistoryRangeAsync() once before the main candle loop
/// to pre-load all snapshots for the backtest window. Per-bar it picks the closest
/// snapshot on or before the bar date and populates StrategyContext.OptionChain.
/// Falls back to the synthetic chain (BuildSyntheticLegs) when no snapshot exists.
/// </summary>
public interface IOptionChainSnapshotService
{
    /// <summary>
    /// Persist today's option chain snapshot (idempotent — upserts by symbol+date).
    /// Called by OptionChainSnapshotJob after market close.
    /// </summary>
    Task RecordAsync(
        string underlyingSymbol,
        NodaTime.LocalDate date,
        Domain.ValueObjects.OptionChainSnapshot snapshot,
        CancellationToken ct);

    /// <summary>
    /// FIB-5: Pre-fetch snapshots for a date range (inclusive).
    /// Returns list ordered by date ascending; caller picks nearest on-or-before.
    /// </summary>
    Task<IReadOnlyList<(NodaTime.LocalDate Date, Domain.ValueObjects.OptionChainSnapshot Snapshot)>>
        GetHistoryRangeAsync(
            string underlyingSymbol,
            NodaTime.LocalDate from,
            NodaTime.LocalDate to,
            CancellationToken ct);
}

public interface IOptionLegSelector
{
    /// <param name="underlyingSymbol">E.g. "NIFTY 50" or "BANKNIFTY".</param>
    /// <param name="spec">Strike selection spec from the strategy's SignalResult.</param>
    /// <param name="spotPrice">Current underlying price — used for ATM/OTM calculations.</param>
    /// <param name="expiryDate">Target expiry date.</param>
    /// <param name="brokerName">Active broker — used to resolve the right token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="atmIv">ATM implied volatility (0–1 fraction, e.g. 0.18 = 18%).
    /// Used by ByDelta selection for accurate delta ranking. Falls back to 0.18 when null.
    /// Pass context.OptionChain.AtmIv / 100 when available (BS-2).</param>
    Task<OptionLegResolution?> ResolveAsync(
        string underlyingSymbol,
        Domain.Interfaces.OptionsLegSpec spec,
        decimal spotPrice,
        NodaTime.LocalDate expiryDate,
        string brokerName,
        CancellationToken ct,
        double? atmIv = null);
}

/// <summary>Result of IOptionLegSelector.ResolveAsync — the concrete instrument details.</summary>
public record OptionLegResolution(
    string InternalSymbol,
    string BrokerToken,
    decimal StrikePrice,
    Domain.Enums.OptionType OptionType,
    NodaTime.LocalDate Expiry,
    decimal LastTradedPrice
);

public interface IBlackScholesEngine
{
    /// <summary>
    /// Compute full Greeks snapshot for one option leg.
    /// </summary>
    /// <param name="spotPrice">Current underlying price (S).</param>
    /// <param name="strikePrice">Option strike price (K).</param>
    /// <param name="timeToExpiryYears">Time to expiry in years, e.g. 7/365 for 1 week.</param>
    /// <param name="volatility">Annualised implied volatility as a decimal, e.g. 0.185 for 18.5%.</param>
    /// <param name="riskFreeRate">Annualised risk-free rate as a decimal, e.g. 0.065 for RBI repo rate.</param>
    /// <param name="isCall">True for CE, false for PE.</param>
    Domain.ValueObjects.GreeksSnapshot Compute(
        decimal spotPrice,
        decimal strikePrice,
        double timeToExpiryYears,
        double volatility,
        double riskFreeRate,
        bool isCall);
}

public interface IOptionChainService
{
    /// <summary>
    /// Fetches the current option chain snapshot for the given underlying symbol and expiry date.
    /// Returns null on fetch failure — callers must handle gracefully.
    /// </summary>
    Task<Domain.ValueObjects.OptionChainSnapshot?> GetSnapshotAsync(
        string underlyingSymbol,
        Domain.ValueObjects.OptionChainExpiry expiry,
        CancellationToken ct);

    /// <summary>
    /// Returns the nearest weekly expiry date for an index (NIFTY, BANKNIFTY, etc.)
    /// relative to today's IST date.
    /// </summary>
    Domain.ValueObjects.OptionChainExpiry GetNearestWeeklyExpiry(string underlyingSymbol);

    /// <summary>
    /// Returns the nearest monthly expiry date.
    /// </summary>
    Domain.ValueObjects.OptionChainExpiry GetNearestMonthlyExpiry(string underlyingSymbol);
}

public interface IBacktestReproductionService
{
    Task<DTOs.Backtest.BacktestResultDto?> ReproduceAsync(DTOs.Backtest.BacktestResultDto original, CancellationToken ct);
}

/// <summary>
/// Options Intelligence — derives PCR, IV, rule signals, and bias score from a live
/// or snapshot option chain. pcrMax is persisted in Redis keyed by symbol+date and
/// reset automatically (28h TTL). Rules are evaluated server-side; frontend renders only.
/// </summary>
public interface IOptionsIntelligenceService
{
    /// <summary>Full intelligence result — chain + rule signals + bias score.</summary>
    Task<DTOs.Options.OptionsIntelligenceDto?> GetIntelligenceAsync(
        string symbol, string expiry, CancellationToken ct);

    /// <summary>Raw chain DTO without rule evaluation (lighter endpoint for chart-only use).</summary>
    Task<DTOs.Options.OptionChainDto?> GetChainAsync(
        string symbol, string expiry, CancellationToken ct);

    /// <summary>Available expiries for a symbol (nearest weekly + nearest monthly).</summary>
    Task<IReadOnlyList<string>> GetExpiriesAsync(string symbol, CancellationToken ct);
}

/// <summary>
/// Paper-trading engine for forward test sessions.
/// Receives closed candles from CandleAggregatorService, evaluates strategy,
/// simulates fills, and persists virtual trades to DB.
/// No real orders placed.
/// Defined here (Application layer) so StrategyEvaluationQueue (Infrastructure)
/// can depend on it without referencing the Backtesting project directly.
/// </summary>
public interface IForwardTestEngine
{
    /// <summary>Process one closed candle for a running forward-test instance.</summary>
    Task ProcessCandleAsync(Domain.Entities.StrategyInstance instance, Domain.ValueObjects.ClosedCandle candle, CancellationToken ct);
    /// <summary>Start a new forward-test session for the given instance.</summary>
    Task<Guid> StartSessionAsync(Domain.Entities.StrategyInstance instance, decimal initialCapital, CancellationToken ct);
    /// <summary>Stop a running forward-test session and persist final metrics.</summary>
    Task StopSessionAsync(Guid instanceId, CancellationToken ct);
    /// <summary>
    /// Re-hydrates _activeStates from the DB on startup. Queries all forward_test_sessions
    /// with Status='Running' and restores in-memory state so candle events are not orphaned
    /// after a pod restart or crash. Called from StartupOrchestrator.
    /// </summary>
    Task RecoverActiveSessionsAsync(CancellationToken ct);
}

/// <summary>
/// Abstracts walk-forward execution. Implemented by WalkForwardEngine in rvs.AlgoTrader.Backtesting.
/// Registered in the API composition root (only API references the Backtesting assembly).
/// </summary>
public interface IWalkForwardEngine
{
    Task<DTOs.Backtest.WalkForwardResultDto> RunAsync(DTOs.Backtest.BacktestRequestDto dto, CancellationToken ct);
}

/// <summary>
/// Abstracts backtest execution so the Application layer has no direct dependency
/// on the rvs.AlgoTrader.Backtesting project. Implemented in Infrastructure.
/// </summary>
public interface IBacktestService
{
    Task<DTOs.Backtest.BacktestResultDto> RunAsync(DTOs.Backtest.BacktestRequestDto request, CancellationToken ct);
    Task<object> RunWalkForwardAsync(DTOs.Backtest.BacktestRequestDto request, CancellationToken ct);
}

/// <summary>
/// Manages async backtest jobs. Each call to EnqueueAsync starts a background task
/// and returns a jobId immediately. Use GetStatus to poll or subscribe via BacktestHub.
/// </summary>
public interface IBacktestJobManager
{
    /// <summary>Start a backtest in the background. Returns jobId immediately.</summary>
    Task<string> EnqueueAsync(DTOs.Backtest.BacktestRequestDto request, CancellationToken ct);
    /// <summary>
    /// Start a scenario-tagged backtest. ParametersJson in mergedRequest must already be the
    /// merged effective params (base + override). On success, updates scenario → Backtested
    /// and sets LastBacktestRunId.
    /// </summary>
    Task<string> EnqueueScenarioAsync(Guid scenarioId, DTOs.Backtest.BacktestRequestDto mergedRequest, CancellationToken ct);
    /// <summary>Get current status of a job (null if not found).</summary>
    DTOs.Backtest.BacktestJobStatusDto? GetStatus(string jobId);
    /// <summary>Cancel a running job.</summary>
    void CancelJob(string jobId);
    /// <summary>List all active (Queued/Running) job IDs.</summary>
    IReadOnlyList<string> GetActiveJobIds();
}

/// <summary>
/// Abstracts SignalR progress push out of BacktestJobManager so Infrastructure
/// does not need to reference the API project (avoids circular dependency).
/// Implemented in API as SignalRBacktestProgressPusher.
/// </summary>
public interface IBacktestProgressPusher
{
    Task PushProgressAsync(string jobId, DTOs.Backtest.BacktestJobStatusDto status);
    Task PushCompletedAsync(string jobId, DTOs.Backtest.BacktestJobStatusDto status);
    /// <summary>
    /// Push a rolling chart batch (last ~200 bars) to the frontend during a running backtest.
    /// Sent as a separate SignalR event ("BacktestChartUpdate") to keep the status DTO lean.
    /// </summary>
    Task PushChartUpdateAsync(string jobId, IReadOnlyList<DTOs.Backtest.BacktestChartBarDto> bars);
}

public interface ILiveExecutionEngine
{
    Task ExecuteSignalAsync(
        Domain.Entities.StrategyInstance instance,
        Domain.Interfaces.SignalResult signal,
        string correlationId,
        CancellationToken ct);
}

public interface IHistoricalDownloadService
{
    Task<DownloadResult> DownloadAsync(
        string internalSymbol, string brokerName, string timeframe,
        DateOnly from, DateOnly to, CancellationToken ct);
}

public record DownloadResult(bool Success, int BarCount, string? DataHash, string? Error);

public interface IInstrumentRefreshService
{
    /// <summary>
    /// Legacy / automated path: downloads + immediately saves using the filters
    /// stored in app_config.  Used by Hangfire jobs and the post-login hook.
    /// </summary>
    Task RefreshAsync(string brokerName, CancellationToken ct);
    Task RefreshAllBrokersAsync(CancellationToken ct);

    /// <summary>
    /// Interactive wizard — Step 1.
    /// Downloads instruments from the broker, stages them in memory (30-minute TTL),
    /// and returns a preview with counts grouped by exchange, instrument type, and
    /// equity-universe category so the user can decide what to save before writing to the DB.
    /// </summary>
    Task<Application.DTOs.Instruments.RefreshPreviewDto> PreviewAsync(
        string brokerName, CancellationToken ct);

    /// <summary>
    /// Interactive wizard — Step 2.
    /// Reads the staged instruments identified by <see cref="Application.DTOs.Instruments.RefreshCommitRequest.StagingToken"/>,
    /// applies the user-selected filters, and writes the result to the instruments table.
    /// Clears the staging entry on success.
    /// </summary>
    Task<Application.DTOs.Instruments.RefreshCommitResult> CommitAsync(
        Application.DTOs.Instruments.RefreshCommitRequest request, CancellationToken ct);
}

/// <summary>
/// Abstracts broker authentication flows so the Application layer has no
/// dependency on broker infrastructure projects.
/// Implemented in rvs.AlgoTrader.Infrastructure.
/// </summary>
public interface IBrokerAuthService
{
    // mStock Type B — 2-step: /connect/login then /session/verifytotp
    Task<BrokerAuthResultDto> AuthenticateMStockAsync(string apiKey, string clientCode, string password, string totp, CancellationToken ct);

    // Zerodha — returns Kite OAuth URL for browser redirect
    Task<string> GetZerodhaLoginUrlAsync(CancellationToken ct);

    // Zerodha — exchanges request_token (from Kite OAuth redirect) for access_token
    Task<BrokerAuthResultDto> AuthenticateZerodhaAsync(string requestToken, CancellationToken ct);

    // Upstox — returns OAuth2 authorization URL for browser redirect
    Task<string> GetUpstoxLoginUrlAsync(CancellationToken ct);

    // Upstox — exchanges auth_code (from OAuth2 redirect) for access_token + extended_token
    Task<BrokerAuthResultDto> AuthenticateUpstoxAsync(string authCode, CancellationToken ct);
}

// ── P4: Approval Gate ─────────────────────────────────────────────────────────

/// <summary>
/// Runs the automated pre-approval checks and records manual approvals.
/// A strategy instance must have an active (non-invalidated) approval before
/// LiveExecutionEngine will place any real order.
/// Thresholds: CAGR ≥ 20%, drawdown ≤ 20%, fwd-test days ≥ 15, win-rate ≥ 40%.
/// Manual approval is ALWAYS required even when all automated checks pass.
/// </summary>
public interface IApprovalService
{
    /// <summary>Run automated checks and return the result (does NOT approve).</summary>
    Task<ApprovalCheckResult> RunChecksAsync(Guid instanceId, CancellationToken ct);

    /// <summary>
    /// Record a manual approval.
    /// Calls RunChecksAsync internally, captures the metric snapshot, and persists
    /// a StrategyApproval row + sets strategy_instances.approved_at.
    /// </summary>
    Task<Domain.Entities.StrategyApproval> ApproveAsync(
        Guid instanceId, string approvedBy, string? notes, CancellationToken ct);

    /// <summary>
    /// Revoke an existing approval.
    /// Sets invalidated_at on the approval row and clears approved_at on the instance.
    /// </summary>
    Task RevokeAsync(Guid approvalId, string reason, string actor, CancellationToken ct);

    /// <summary>Returns the currently active approval, or null if none exists.</summary>
    Task<Domain.Entities.StrategyApproval?> GetActiveApprovalAsync(Guid instanceId, CancellationToken ct);

    /// <summary>Full approval history for an instance, newest first.</summary>
    Task<IReadOnlyList<Domain.Entities.StrategyApproval>> GetHistoryAsync(Guid instanceId, CancellationToken ct);
}

// ── #129: Token Store ─────────────────────────────────────────────────────────

/// <summary>
/// Secure, encrypted key-value store for broker JWT tokens and refresh tokens.
/// Tokens are encrypted with AES-256-GCM before being written to the backing store
/// so that secrets at rest (in Redis) are never in plaintext.
///
/// Key format: "tokens:{brokerId}:{tokenType}" — e.g. "tokens:mstock:jwt"
/// TTL: callers supply the expiry so the store can set an absolute expiry.
/// </summary>
public interface ITokenStore
{
    /// <summary>Encrypt and store a token. Pass <paramref name="expiresAt"/> so the store
    /// can evict it automatically when it becomes useless.</summary>
    Task SetAsync(string key, string token, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>Retrieve and decrypt a token. Returns null if missing or expired.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Remove a token immediately (e.g. on logout or token revocation).</summary>
    Task DeleteAsync(string key, CancellationToken ct = default);
}

