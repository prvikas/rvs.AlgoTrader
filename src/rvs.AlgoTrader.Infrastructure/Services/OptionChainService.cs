using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Domain.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Fetches NSE option chain data via the active broker's market data API and
/// computes analytics (PCR, Max Pain, OI concentration) on the snapshot.
///
/// ARCHITECTURE CONTRACT (Rule #18):
///   This service is called by StrategyEvaluationQueue BEFORE building StrategyContext.
///   IStrategy.EvaluateAsync reads StrategyContext.OptionChain — no I/O inside strategy.
///
/// CACHING:
///   Results are cached in-process for DefaultCacheTtl (60s by default).
///   Production deployments should prefer Redis for multi-instance scenarios.
///
/// BROKER API PATHS:
///   Zerodha: GET /quote → batch quotes; OI/IV returned in full mode
///   Upstox:  GET /v2/option/chain?instrument_key=...&amp;expiry_date=...
///   mStock:  GET /optionchain?symbol=...&amp;expiry=...
///
/// EXPIRY SELECTION:
///   - Weekly: nearest Thursday (NSE standard for index options)
///   - Monthly: last Thursday of the month
/// </summary>
public sealed class OptionChainService(
    IBrokerClientFactory brokerFactory,
    IInstrumentRepository instrumentRepo,
    IBrokerRepository brokerRepo,
    IAppConfigService appConfig,
    IClock clock,
    ILogger<OptionChainService> logger) : IOptionChainService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    // In-process snapshot cache: keyed by "symbol:expiry-date"
    private readonly Dictionary<string, (OptionChainSnapshot Snapshot, Instant ExpiresAt)> _cache = new();
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private static readonly Duration DefaultCacheTtl = Duration.FromSeconds(60);

    // ── IOptionChainService ────────────────────────────────────────────────

    public async Task<OptionChainSnapshot?> GetSnapshotAsync(
        string underlyingSymbol,
        OptionChainExpiry expiry,
        CancellationToken ct)
    {
        var cacheKey = $"{underlyingSymbol}:{expiry.Date:yyyy-MM-dd}";

        // Check in-process cache first (fast path, no await)
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > clock.NowInstant())
            {
                logger.LogDebug("[OptionChain] Cache hit for {Key}", cacheKey);
                return cached.Snapshot;
            }
        }
        finally { _cacheLock.Release(); }

        logger.LogInformation("[OptionChain] Fetching chain for {Symbol} expiry {Expiry}", underlyingSymbol, expiry.Date);

        try
        {
            var brokerName = await appConfig.GetAsync<string>("ActiveBroker", ct) ?? BrokerNames.Default;
            var snapshot = await FetchFromBrokerAsync(underlyingSymbol, expiry, brokerName, ct);

            if (snapshot != null)
            {
                await _cacheLock.WaitAsync(ct);
                try { _cache[cacheKey] = (snapshot, clock.NowInstant().Plus(DefaultCacheTtl)); }
                finally { _cacheLock.Release(); }

                logger.LogInformation("[OptionChain] Fetched {Count} legs for {Symbol}. PCR={PCR:F2} MaxPain={MaxPain}",
                    snapshot.Options.Count, underlyingSymbol, snapshot.PutCallRatioOI, snapshot.MaxPainStrike);
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            // Never let option chain failure block strategy evaluation — return null
            logger.LogError(ex, "[OptionChain] Failed to fetch option chain for {Symbol} — strategy will run without OC data", underlyingSymbol);
            return null;
        }
    }

    public OptionChainExpiry GetNearestWeeklyExpiry(string underlyingSymbol)
    {
        // NSE index weekly options: every Thursday
        var today = clock.NowIst().LocalDateTime.Date;
        var daysUntil = ((int)IsoDayOfWeek.Thursday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // Already past Thursday this week → next Thursday
        return new OptionChainExpiry(today.PlusDays(daysUntil), "WEEKLY");
    }

    public OptionChainExpiry GetNearestMonthlyExpiry(string underlyingSymbol)
    {
        // NSE monthly: last Thursday of the month
        var today = clock.NowIst().LocalDateTime.Date;
        var candidate = LastThursdayOfMonth(today.Year, today.Month);
        if (today > candidate)
        {
            // This month's monthly expiry has already passed → use next month
            var nextMonth = today.Month == 12
                ? new LocalDate(today.Year + 1, 1, 1)
                : new LocalDate(today.Year, today.Month + 1, 1);
            candidate = LastThursdayOfMonth(nextMonth.Year, nextMonth.Month);
        }
        return new OptionChainExpiry(candidate, "MONTHLY");
    }

    // ── Private ────────────────────────────────────────────────────────────

    private async Task<OptionChainSnapshot?> FetchFromBrokerAsync(
        string underlying, OptionChainExpiry expiry, string brokerName, CancellationToken ct)
    {
        // Step 1: load option instruments from DB (populated by IInstrumentRefreshService each morning)
        var instruments = await instrumentRepo.GetOptionsByUnderlyingAndExpiryAsync(underlying, expiry.Date, ct);
        if (instruments.Count == 0)
        {
            logger.LogWarning("[OptionChain] No option instruments found for {Symbol} expiry {Expiry}. " +
                              "Ensure IInstrumentRefreshService ran today before market open.",
                underlying, expiry.Date);
            return null;
        }

        // Resolve broker name to ID for token lookup
        var broker = await brokerRepo.GetByNameAsync(brokerName, ct);
        if (broker is null)
        {
            logger.LogWarning("[OptionChain] Broker '{Broker}' not found", brokerName);
            return null;
        }

        // Step 2: build the list of broker tokens for this expiry
        var tokens = instruments
            .Select(i => GetTokenForBroker(i, broker.Id))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tokens.Count == 0)
        {
            logger.LogWarning("[OptionChain] No broker tokens available for {Broker}/{Symbol}", brokerName, underlying);
            return null;
        }

        // Step 3: batch-fetch option quotes
        var mdClient = brokerFactory.GetMarketDataClient(brokerName);
        var quotes = await mdClient.GetOptionQuotesAsync(tokens!, ct);

        if (quotes.Count == 0) return null;

        // Step 4: build OptionLeg list
        var legs = new List<OptionLeg>(instruments.Count);
        foreach (var inst in instruments)
        {
            if (inst.InstrumentType != Domain.Enums.InstrumentType.Options) continue;
            var token = GetTokenForBroker(inst, broker.Id);
            if (token == null || !quotes.TryGetValue(token, out var q)) continue;

            legs.Add(new OptionLeg(
                StrikePrice:       inst.StrikePrice ?? 0m,
                OptionType:        inst.OptionType?.ToString() ?? "CE",
                LastTradedPrice:   q.LastTradedPrice,
                OpenInterest:      q.OpenInterest,
                OiChange:          q.OiChange,
                Volume:            q.Volume,
                ImpliedVolatility: q.ImpliedVolatility,
                BidPrice:          q.BidPrice,
                AskPrice:          q.AskPrice,
                Delta:             q.Delta
            ));
        }

        if (legs.Count == 0) return null;

        // Step 5: estimate spot from ATM mid-price or the underlying's quote
        var spot = EstimateSpot(legs, underlying);

        return new OptionChainSnapshot(
            UnderlyingSymbol: underlying,
            FetchedAt:        clock.NowInstant(),
            SpotPrice:        spot,
            Expiry:           expiry.Date,
            Options:          legs.AsReadOnly()
        );
    }

    private static string? GetTokenForBroker(Instrument instrument, short brokerId) =>
        instrument.BrokerTokens
            .FirstOrDefault(bt => bt.BrokerId == brokerId)?
            .Token;

    /// <summary>
    /// Estimates spot price from ATM CE+PE pairs when the index quote is not in the batch.
    /// Put-Call parity: spot ≈ CE_strike + CE_ltp - PE_ltp (for near-ATM options)
    /// </summary>
    private static decimal EstimateSpot(IReadOnlyList<OptionLeg> legs, string underlying)
    {
        if (legs.Count == 0) return 0;

        // Find the ATM strike (midpoint of all strikes)
        var strikes = legs.Select(l => l.StrikePrice).Distinct().OrderBy(s => s).ToList();
        if (strikes.Count == 0) return 0;

        var midStrike = strikes[strikes.Count / 2];

        var atm = legs.Where(l => l.StrikePrice == midStrike).ToList();
        var ce = atm.FirstOrDefault(l => l.OptionType == "CE");
        var pe = atm.FirstOrDefault(l => l.OptionType == "PE");

        if (ce != null && pe != null)
            return midStrike + ce.LastTradedPrice - pe.LastTradedPrice; // Put-Call parity

        // Fallback: use mid-strike as proxy
        return midStrike;
    }

    private static LocalDate LastThursdayOfMonth(int year, int month)
    {
        var last = new LocalDate(year, month, 1).PlusMonths(1).PlusDays(-1);
        while (last.DayOfWeek != IsoDayOfWeek.Thursday)
            last = last.PlusDays(-1);
        return last;
    }
}
