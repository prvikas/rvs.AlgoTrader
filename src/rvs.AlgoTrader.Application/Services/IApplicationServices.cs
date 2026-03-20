using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Brokers.Abstractions;
using NodaTime;

namespace rvs.AlgoTrader.Application.Services;

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
    Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct);
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

public record CostProfile(decimal BrokeragePct, decimal SttPct, decimal GstPct, decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct);

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

public interface ISymbolDataPreferencesService
{
    Task<SymbolDataPreferences?> GetPreferencesAsync(string symbol, CancellationToken ct);
    Task UpsertAsync(SymbolDataPreferences preferences, CancellationToken ct);
    Task<IReadOnlyList<SymbolDataPreferences>> GetAllActiveAsync(CancellationToken ct);
}

public record SymbolDataPreferences(Guid Id, string InternalSymbol, string[] Timeframes, DateOnly FromDate, int Priority, bool IsActive);

public interface IBacktestReproductionService
{
    Task<DTOs.Backtest.BacktestResultDto?> ReproduceAsync(DTOs.Backtest.BacktestResultDto original, CancellationToken ct);
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
    Task RefreshAsync(string brokerName, CancellationToken ct);
    Task RefreshAllBrokersAsync(CancellationToken ct);
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
