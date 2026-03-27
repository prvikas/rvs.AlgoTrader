using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Downloads the broker's scrip master, updates the local instrument DB,
/// and rebuilds the in-memory token resolver cache.
///
/// Called on two occasions (mirroring OpenAlgo behaviour):
///   1. Immediately after a successful broker login (so symbol search works straight away).
///   2. Daily at 08:00 IST via the Hangfire InstrumentRefreshJob (picks up newly listed instruments).
/// </summary>
public class InstrumentRefreshService(
    IInstrumentRepository instrumentRepo,
    IInstrumentTokenResolver tokenResolver,
    AlgoTraderDbContext db,
    IAppConfigService config,
    IClock clock,
    ILogger<InstrumentRefreshService> logger) : IInstrumentRefreshService
{
    public async Task RefreshAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[InstrumentRefresh] Starting master-data download for {Broker}", brokerName);

        // 1. Download from broker + rebuild in-memory cache
        logger.LogDebug("[InstrumentRefresh] Calling tokenResolver.RefreshAsync({Broker})", brokerName);
        await tokenResolver.RefreshAsync(brokerName, ct);
        logger.LogDebug("[InstrumentRefresh] tokenResolver.RefreshAsync completed, fetching mappings...");

        var mappings = await tokenResolver.GetAllMappingsAsync(brokerName, ct);
        logger.LogInformation("[InstrumentRefresh] Broker {Broker} returned {MappingCount} mappings", brokerName, mappings.Count);

        if (mappings.Count == 0)
        {
            logger.LogWarning("[InstrumentRefresh] No instruments returned from {Broker} — check session / API access", brokerName);
            return;
        }

        // 2. Load symbol universe from DB and apply filter
        var universe = await LoadUniverseAsync(ct);
        var before   = mappings.Count;
        mappings     = mappings.Where(m => IsInUniverse(m, universe)).ToList();
        logger.LogInformation(
            "[InstrumentRefresh] Universe filter: {Before} → {After} instruments kept for {Broker}",
            before, mappings.Count, brokerName);

        if (mappings.Count == 0)
        {
            logger.LogWarning(
                "[InstrumentRefresh] No instruments remain after universe filter. " +
                "Seed instrument_universe with NSE_EQUITY / OPTIONS_UNDERLYING rows to control which symbols are stored.");
            return;
        }

        // 3. Upsert into the local instruments table
        var (newCount, updatedCount) = await UpsertInstrumentsAsync(brokerName, mappings.ToList(), ct);

        logger.LogInformation(
            "[InstrumentRefresh] {Broker}: {Created} new, {Updated} updated instruments ({Total} total) - COMPLETED",
            brokerName, newCount, updatedCount, mappings.Count);
    }

    public async Task RefreshAllBrokersAsync(CancellationToken ct)
    {
        // Broker list is stored in app_config under key "Brokers:Registered"
        // so operators can add/remove brokers from the Settings UI without code changes.
        // Falls back to the standard set if the key has never been set.
        var raw     = await config.GetAsync<string>("Brokers:Registered", ct)
                      ?? "MStock,Zerodha,Upstox";
        var brokers = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        logger.LogInformation("[InstrumentRefresh] Refreshing {Count} broker(s): {Brokers}", brokers.Length, string.Join(", ", brokers));

        foreach (var broker in brokers)
        {
            try { await RefreshAsync(broker, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "[InstrumentRefresh] Failed to refresh {Broker}", broker);
            }
        }
    }

    // ── Interactive wizard — staging cache ───────────────────────────────────
    //
    // Instruments are held in a process-local dictionary with a 30-minute TTL.
    // A single-server deployment (dev/ops setup) is assumed; for a multi-node
    // cluster, replace this with a Redis-backed staging service.
    //
    private sealed record StagedRefresh(
        string                        BrokerName,
        List<InstrumentTokenMapping>  Instruments,
        DateTimeOffset                ExpiresAt);

    private static readonly ConcurrentDictionary<string, StagedRefresh> _stagingCache = new();

    // ── PreviewAsync (wizard Step 1) ─────────────────────────────────────────

    public async Task<RefreshPreviewDto> PreviewAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[InstrumentRefresh] Preview download starting for {Broker}", brokerName);

        // 1. Download from broker
        await tokenResolver.RefreshAsync(brokerName, ct);
        var instruments = await tokenResolver.GetAllMappingsAsync(brokerName, ct);
        logger.LogInformation("[InstrumentRefresh] Preview: {Count} instruments downloaded from {Broker}",
            instruments.Count, brokerName);

        // 2. Stage in memory with 30-minute TTL
        var token   = Guid.NewGuid().ToString("N")[..12];
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        _stagingCache[token] = new StagedRefresh(brokerName, instruments.ToList(), expiresAt);

        // Evict any other expired entries while we're here
        var expired = _stagingCache
            .Where(kvp => kvp.Value.ExpiresAt < DateTimeOffset.UtcNow)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var k in expired) _stagingCache.TryRemove(k, out _);

        // 3. Load type classification config (for bucketing preview counts)
        var futuresTypesRaw = await config.GetAsync<string>("InstrumentFilter:FuturesTypes", ct)
            ?? "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD";
        var optionsTypesRaw = await config.GetAsync<string>("InstrumentFilter:OptionsTypes", ct)
            ?? "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO";
        var futuresTypes = ToUpperHashSet(futuresTypesRaw);
        var optionsTypes = ToUpperHashSet(optionsTypesRaw);

        // 4. Group by exchange, then by type bucket
        var byExchange = instruments
            .GroupBy(m => (m.Exchange ?? "UNKNOWN").ToUpperInvariant())
            .Select(eg =>
            {
                // Group type codes into friendly buckets
                var byBucket = eg
                    .GroupBy(m => ClassifyTypeBucket(
                        (m.InstrumentType ?? string.Empty).ToUpperInvariant(),
                        futuresTypes, optionsTypes))
                    .Select(bg => new TypeBucketRow(
                        bg.Key,
                        bg.Count(),
                        bg.Select(m => (m.InstrumentType ?? "").ToUpperInvariant())
                          .Distinct()
                          .OrderBy(t => t)
                          .ToArray()))
                    .OrderBy(r => BucketSortOrder(r.Bucket))
                    .ToList();

                return new ExchangePreviewGroup(eg.Key, eg.Count(), byBucket);
            })
            .OrderBy(g => g.Exchange)
            .ToList();

        // 5. Equity-category match counts from universe
        var allUniverseRows = await db.InstrumentUniverse
            .Where(u => u.IsActive)
            .Select(u => new { u.Symbol, u.Category })
            .ToListAsync(ct);

        // Set of equity symbols present in this download (NSE + BSE)
        var downloadedEquitySymbols = instruments
            .Where(m =>
            {
                var exch = (m.Exchange ?? "").ToUpperInvariant();
                var type = (m.InstrumentType ?? "").ToUpperInvariant();
                return (exch is "NSE" or "BSE")
                    && (type is "EQ" or "STK" or "EQUITY" or "");
            })
            .Select(m => NormaliseSymbol(m.TradingSymbol ?? m.InternalSymbol))
            .ToHashSet();

        var categoryLabels = new Dictionary<string, string>
        {
            ["NSE_EQUITY"]   = "NSE Equity",
            ["LARGE_CAP"]    = "Large-cap",
            ["MID_CAP"]      = "Mid-cap",
            ["SMALL_CAP"]    = "Small-cap",
            ["NSE_Z_GROUP"]  = "Z Group",
            ["NSE_B_GROUP"]  = "B Group",
        };

        var categoryRows = allUniverseRows
            .Where(r => r.Category != "OPTIONS_UNDERLYING")
            .GroupBy(r => r.Category.ToUpperInvariant())
            .Select(cg =>
            {
                var matchCount = cg.Count(r =>
                    downloadedEquitySymbols.Contains(r.Symbol.ToUpperInvariant()));
                return new CategoryPreviewRow(
                    cg.Key,
                    categoryLabels.GetValueOrDefault(cg.Key, cg.Key),
                    matchCount);
            })
            .OrderByDescending(r => r.MatchCount)
            .ToList();

        logger.LogInformation(
            "[InstrumentRefresh] Preview staged: token={Token}, {Count} instruments, {Exchanges} exchanges",
            token, instruments.Count, byExchange.Count);

        return new RefreshPreviewDto(
            StagingToken:     token,
            BrokerName:       brokerName,
            TotalDownloaded:  instruments.Count,
            StagedAt:         DateTimeOffset.UtcNow,
            ExpiresInMinutes: 30,
            Exchanges:        byExchange,
            EquityCategories: categoryRows);
    }

    // ── CommitAsync (wizard Step 2) ──────────────────────────────────────────

    public async Task<RefreshCommitResult> CommitAsync(RefreshCommitRequest req, CancellationToken ct)
    {
        if (!_stagingCache.TryGetValue(req.StagingToken, out var staged))
            throw new InvalidOperationException(
                "Staging token not found or expired. Please download the instruments again.");

        if (staged.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _stagingCache.TryRemove(req.StagingToken, out _);
            throw new InvalidOperationException(
                "Staging token has expired (30-minute limit). Please download the instruments again.");
        }

        logger.LogInformation(
            "[InstrumentRefresh] Commit: token={Token}, exchanges=[{Exchanges}], types=[{Types}], categories=[{Cats}]",
            req.StagingToken,
            string.Join(",", req.IncludedExchanges),
            string.Join(",", req.IncludedInstrumentTypes),
            string.Join(",", req.IncludedEquityCategories));

        // Build UniverseConfig from user-supplied filters + DB universe
        var universe = await BuildUniverseFromRequestAsync(req, ct);

        // Apply filter
        var mappings = staged.Instruments.Where(m => IsInUniverse(m, universe)).ToList();
        var skipped  = staged.Instruments.Count - mappings.Count;

        logger.LogInformation(
            "[InstrumentRefresh] Commit filter: {Total} → {Kept} kept, {Skipped} skipped",
            staged.Instruments.Count, mappings.Count, skipped);

        if (mappings.Count == 0)
        {
            _stagingCache.TryRemove(req.StagingToken, out _);
            return new RefreshCommitResult(staged.BrokerName, 0, skipped, 0, 0);
        }

        // Upsert to DB (same logic as RefreshAsync)
        var (newCount, updatedCount) = await UpsertInstrumentsAsync(
            staged.BrokerName, mappings, ct);

        _stagingCache.TryRemove(req.StagingToken, out _);

        logger.LogInformation(
            "[InstrumentRefresh] Commit complete: {New} new, {Updated} updated, {Skipped} skipped",
            newCount, updatedCount, skipped);

        return new RefreshCommitResult(
            staged.BrokerName,
            newCount + updatedCount,
            skipped,
            newCount,
            updatedCount);
    }

    // ── Shared upsert (extracted from RefreshAsync) ──────────────────────────

    private async Task<(int newCount, int updatedCount)> UpsertInstrumentsAsync(
        string brokerName,
        List<InstrumentTokenMapping> mappings,
        CancellationToken ct)
    {
        var now     = clock.NowInstant();
        var symbols = mappings.Select(m => m.InternalSymbol).ToList();

        const int ChunkSize = 10_000;
        var allExisting = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < symbols.Count; i += ChunkSize)
        {
            var chunk = symbols.Skip(i).Take(ChunkSize).ToList();
            var batch = await instrumentRepo.GetBatchByInternalSymbolAsync(chunk, ct);
            foreach (var kv in batch) allExisting[kv.Key] = kv.Value;
        }

        var toAdd    = new List<Instrument>();
        var toUpdate = new List<Instrument>();

        foreach (var m in mappings)
        {
            if (!allExisting.TryGetValue(m.InternalSymbol, out var existing))
            {
                toAdd.Add(BuildInstrument(m, now));
            }
            else
            {
                ApplyBrokerToken(existing, brokerName, m.BrokerToken);
                if (!string.IsNullOrEmpty(m.Name))          existing.Name          = m.Name;
                if (!string.IsNullOrEmpty(m.TradingSymbol)) existing.TradingSymbol = m.TradingSymbol;
                if (m.LotSize > 0)                          existing.LotSize       = m.LotSize;
                if (m.TickSize > 0)                         existing.TickSize      = m.TickSize;
                existing.IsActive        = true;
                existing.LastRefreshedAt = now;
                toUpdate.Add(existing);
            }
        }

        for (int i = 0; i < toAdd.Count; i += ChunkSize)
        {
            var addChunk = toAdd.Skip(i).Take(ChunkSize).ToList();
            bool isLast  = i + ChunkSize >= toAdd.Count;
            await instrumentRepo.BulkUpsertAsync(addChunk, isLast ? toUpdate : [], ct);
        }
        if (toAdd.Count == 0)
            await instrumentRepo.BulkUpsertAsync([], toUpdate, ct);

        return (toAdd.Count, toUpdate.Count);
    }

    // ── Build UniverseConfig from a CommitRequest ────────────────────────────

    private async Task<UniverseConfig> BuildUniverseFromRequestAsync(
        RefreshCommitRequest req, CancellationToken ct)
    {
        var includedEquityCategories = req.IncludedEquityCategories
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        var rows = await db.InstrumentUniverse
            .Where(u => u.IsActive)
            .Select(u => new { u.Symbol, u.Category })
            .ToListAsync(ct);

        var equitySymbols = rows
            .Where(r => includedEquityCategories.Contains(r.Category.ToUpperInvariant()))
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        var optionUnderlyings = rows
            .Where(r => r.Category.ToUpperInvariant() == "OPTIONS_UNDERLYING")
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        // Type classification config stays in app_config (not overridden per-request)
        var futuresTypesRaw = await config.GetAsync<string>("InstrumentFilter:FuturesTypes", ct)
            ?? "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD";
        var optionsTypesRaw = await config.GetAsync<string>("InstrumentFilter:OptionsTypes", ct)
            ?? "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO";
        var expiryWeeks = await config.GetAsync<int?>("InstrumentFilter:NfoExpiryWeeks", ct) ?? 4;

        return new UniverseConfig(
            EquitySymbols:          equitySymbols,
            OptionUnderlyings:      optionUnderlyings,
            ExpiryWeeks:            expiryWeeks,
            FuturesTypes:           ToUpperHashSet(futuresTypesRaw),
            OptionsTypes:           ToUpperHashSet(optionsTypesRaw),
            IncludedExchanges:      req.IncludedExchanges.Select(e => e.ToUpperInvariant()).ToHashSet(),
            IncludedInstrumentTypes: req.IncludedInstrumentTypes.Select(t => t.ToUpperInvariant()).ToHashSet());
    }

    // ── Type-bucket helpers (for preview) ────────────────────────────────────

    private static string ClassifyTypeBucket(
        string uppercasedType,
        HashSet<string> futuresTypes,
        HashSet<string> optionsTypes) =>
        IsIndexType(uppercasedType)             ? "Index"
        : futuresTypes.Contains(uppercasedType) ? "Futures"
        : optionsTypes.Contains(uppercasedType) ? "Options"
        : uppercasedType is "EQ" or "STK" or "EQUITY" or "" ? "Equity"
        // MCX / commodity broker-specific codes not in configured lists
        : uppercasedType.StartsWith("FUT") || uppercasedType is "IF" or "SF" ? "Futures"
        : uppercasedType.StartsWith("OPT") || uppercasedType is "IO" or "SO" ? "Options"
        : "Other";

    private static int BucketSortOrder(string bucket) => bucket switch
    {
        "Equity"  => 0,
        "Index"   => 1,
        "Futures" => 2,
        "Options" => 3,
        _         => 4,
    };

    private static HashSet<string> ToUpperHashSet(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(t => t.ToUpperInvariant())
           .ToHashSet();

    // ── Universe filter ───────────────────────────────────────────────────────

    private sealed record UniverseConfig(
        HashSet<string> EquitySymbols,
        HashSet<string> OptionUnderlyings,
        int ExpiryWeeks,
        HashSet<string> FuturesTypes,
        HashSet<string> OptionsTypes,
        // Configurable scope filters (new):
        HashSet<string> IncludedExchanges,
        HashSet<string> IncludedInstrumentTypes);

    private async Task<UniverseConfig> LoadUniverseAsync(CancellationToken ct)
    {
        var rows = await db.InstrumentUniverse
            .Where(u => u.IsActive)
            .Select(u => new { u.Symbol, u.Category })
            .ToListAsync(ct);

        // ── Equity symbols: union across all enabled equity-type categories ──
        var equityCategoriesRaw = await config.GetAsync<string>("InstrumentFilter:IncludedEquityCategories", ct)
            ?? "NSE_EQUITY";
        var equityCategories = equityCategoriesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => c.ToUpperInvariant())
            .ToHashSet();

        var equitySymbols = rows
            .Where(r => equityCategories.Contains(r.Category.ToUpperInvariant()))
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        var optionUnderlyings = rows
            .Where(r => r.Category == "OPTIONS_UNDERLYING")
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        var expiryWeeks = await config.GetAsync<int?>("InstrumentFilter:NfoExpiryWeeks", ct) ?? 4;

        // Load futures and options type mappings from config
        var futuresTypesRaw = await config.GetAsync<string>("InstrumentFilter:FuturesTypes", ct)
            ?? "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD";
        var optionsTypesRaw = await config.GetAsync<string>("InstrumentFilter:OptionsTypes", ct)
            ?? "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO";

        var futuresTypes        = ToUpperHashSet(futuresTypesRaw);
        var optionsTypes        = ToUpperHashSet(optionsTypesRaw);

        // ── Exchange and instrument-type scope filters ──
        var includedExchangesRaw = await config.GetAsync<string>("InstrumentFilter:IncludedExchanges", ct)
            ?? "NSE,BSE,NFO";
        var includedTypesRaw     = await config.GetAsync<string>("InstrumentFilter:IncludedInstrumentTypes", ct)
            ?? "Equity,Futures,Options,Index";
        var includedExchanges        = ToUpperHashSet(includedExchangesRaw);
        var includedInstrumentTypes  = ToUpperHashSet(includedTypesRaw);

        if (equitySymbols.Count == 0 && optionUnderlyings.Count == 0)
            logger.LogInformation(
                "[InstrumentRefresh] instrument_universe is empty — PASSTHROUGH mode: all instruments on included exchanges will be stored");
        else
            logger.LogDebug(
                "[InstrumentRefresh] Universe: {Equities} equity symbols ({Categories}), {Underlyings} option underlyings, {Weeks} expiry weeks, exchanges: [{Exchanges}], types: [{Types}]",
                equitySymbols.Count, equityCategoriesRaw, optionUnderlyings.Count, expiryWeeks,
                string.Join(",", includedExchanges), string.Join(",", includedInstrumentTypes));

        return new UniverseConfig(equitySymbols, optionUnderlyings, expiryWeeks, futuresTypes, optionsTypes,
            includedExchanges, includedInstrumentTypes);
    }

    /// <summary>
    /// Returns true if the mapping should be persisted based on the configured universe.
    ///
    /// Rules (applied in order):
    ///   1. INDEX instruments on any exchange → always include if Index type is enabled
    ///   2. Exchange filter → drop instruments on exchanges not in IncludedExchanges
    ///   3. Passthrough mode (empty universe) → include all on included exchanges, subject to type filter
    ///   4. Equity exchanges (NSE / BSE) → include if symbol is in EquitySymbols and Equity type is enabled
    ///   5. Indian F&amp;O exchanges (NFO / BFO) → filter by OptionUnderlyings + expiry window
    ///   6. All other explicitly-enabled exchanges (MCX, CDS, NCDEX, BCD, …) → no symbol restriction;
    ///      instrument-type filter only
    /// </summary>
    private bool IsInUniverse(InstrumentTokenMapping m, UniverseConfig u)
    {
        var exch = m.Exchange.ToUpperInvariant();
        var type = (m.InstrumentType ?? string.Empty).ToUpperInvariant();
        var sym  = NormaliseSymbol(m.TradingSymbol ?? string.Empty);

        // ── Step 1: Indexes ──
        // Indexes bypass the exchange filter — Sensex (BSE), Nifty (NSE), VIX, etc.
        // are all useful regardless of which exchange is in IncludedExchanges.
        if (IsIndexType(type))
            return u.IncludedInstrumentTypes.Contains("INDEX");

        // ── Step 2: Exchange gate ──
        if (!u.IncludedExchanges.Contains(exch))
            return false;

        // ── Step 3: Equity exchanges (NSE / BSE) ──
        // NSE and BSE share the same trading symbols (e.g. "RELIANCE"), so the same
        // EquitySymbols set covers both exchanges.
        // When EquitySymbols is empty (no categories configured, or wizard ran without
        // selecting any category) → passthrough: accept all equity-type instruments.
        if (exch is "NSE" or "BSE")
        {
            // Must be an equity-type instrument
            if (!(type is "EQ" or "STK" or "EQUITY" or "")) return false;
            if (!u.IncludedInstrumentTypes.Contains("EQUITY")) return false;
            // Passthrough: no symbol restriction when universe is not configured
            if (u.EquitySymbols.Count == 0) return true;
            return u.EquitySymbols.Contains(sym);
        }

        // ── Step 4: Indian F&O exchanges (NFO / BFO) ──
        // When OptionUnderlyings is empty → passthrough: accept all F&O instruments
        // (subject to expiry window for options).
        if (exch is "NFO" or "BFO")
        {
            if (u.FuturesTypes.Contains(type))
            {
                if (!u.IncludedInstrumentTypes.Contains("FUTURES")) return false;
                // Passthrough when no underlyings configured
                if (u.OptionUnderlyings.Count == 0) return true;
                return InferUnderlying(sym, u.OptionUnderlyings) != null;
            }

            if (u.OptionsTypes.Contains(type))
            {
                if (!u.IncludedInstrumentTypes.Contains("OPTIONS")) return false;
                // Symbol filter only when underlyings are configured
                if (u.OptionUnderlyings.Count > 0 && InferUnderlying(sym, u.OptionUnderlyings) == null)
                    return false;
                if (string.IsNullOrEmpty(m.Expiry)) return false;
                if (LocalDatePattern.Iso.Parse(m.Expiry) is not { Success: true } parsed) return false;
                var today = clock.TodayIst();
                var daysToExpiry = (parsed.Value.ToDateTimeUnspecified() - today.ToDateTimeUnspecified()).Days;
                return daysToExpiry >= 0 && daysToExpiry <= u.ExpiryWeeks * 7;
            }

            return false;
        }

        // ── Step 6: All other explicitly-enabled exchanges (MCX, CDS, NCDEX, BCD, …) ──
        // The exchange was already approved in step 2.  No symbol-universe restriction is applied
        // here because these exchanges have instruments (commodities, currencies) that have no
        // equivalent in the equity/option-underlying universe.  Only the instrument-type filter applies.
        return MatchesIncludedType(type, u);
    }

    /// <summary>True if the raw instrument type string represents an index.</summary>
    private static bool IsIndexType(string uppercasedType) =>
        uppercasedType is "INDEX" or "IDX" or "IX" or "AMXIDX" or "INX" or "INDICES" or "UNDIND";

    /// <summary>
    /// Returns true when the instrument type (already uppercased) matches one of the
    /// user-enabled instrument-type buckets (Equity / Futures / Options / Index).
    /// Unknown types pass through as long as at least one bucket is enabled.
    /// </summary>
    private bool MatchesIncludedType(string type, UniverseConfig u)
    {
        if (IsIndexType(type))             return u.IncludedInstrumentTypes.Contains("INDEX");
        if (u.FuturesTypes.Contains(type)) return u.IncludedInstrumentTypes.Contains("FUTURES");
        if (u.OptionsTypes.Contains(type)) return u.IncludedInstrumentTypes.Contains("OPTIONS");
        if (type is "EQ" or "STK" or "EQUITY" or "") return u.IncludedInstrumentTypes.Contains("EQUITY");
        // Unrecognised type (e.g. broker-specific codes) on an explicitly-approved exchange
        // → treat as Futures if Futures is enabled, else accept when any bucket is enabled.
        // This covers MCX types like FUTCOM, OPTFUT that may not be in the configured lists.
        if (u.IncludedInstrumentTypes.Contains("FUTURES")) return true;
        return u.IncludedInstrumentTypes.Count > 0;
    }

    /// <summary>Normalise broker-specific symbol suffixes (e.g. MStock's "RELIANCE-EQ" → "RELIANCE").</summary>
    private static string NormaliseSymbol(string symbol) =>
        symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase)
            ? symbol[..^3].ToUpperInvariant()
            : symbol.ToUpperInvariant();

    /// <summary>
    /// Infers the underlying name from an NFO symbol by matching known option underlyings as prefixes.
    /// e.g. "NIFTY24JAN19500CE" → "NIFTY", "BANKNIFTY24JAN45000PE" → "BANKNIFTY".
    /// Longer underlyings are checked first to avoid prefix collisions (NIFTY vs NIFTYNXT50).
    /// </summary>
    private static string? InferUnderlying(string symbol, HashSet<string> underlyings)
    {
        foreach (var u in underlyings.OrderByDescending(x => x.Length))
            if (symbol.StartsWith(u, StringComparison.OrdinalIgnoreCase))
                return u;
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Instrument BuildInstrument(InstrumentTokenMapping m, Instant now)
    {
        var instrument = new Instrument
        {
            Id               = Guid.NewGuid(),
            InternalSymbol   = m.InternalSymbol,
            TradingSymbol    = m.TradingSymbol ?? m.InternalSymbol.Split(':').LastOrDefault() ?? m.InternalSymbol,
            Name             = m.Name         ?? m.InternalSymbol,
            Exchange         = m.Exchange,
            InstrumentType   = ParseInstrumentType(m.InstrumentType),
            StrikePrice      = m.StrikePrice,
            OptionType       = ParseOptionType(m.OptionType),
            LotSize          = m.LotSize > 0 ? m.LotSize : 1,
            TickSize         = m.TickSize > 0 ? m.TickSize : 0.05m,
            IsActive         = true,
            LastRefreshedAt  = now,
        };

        // Parse expiry date
        if (!string.IsNullOrEmpty(m.Expiry) &&
            LocalDatePattern.Iso.Parse(m.Expiry) is { Success: true } parsed)
            instrument.Expiry = parsed.Value;

        ApplyBrokerToken(instrument, m.BrokerName, m.BrokerToken);
        return instrument;
    }

    private static void ApplyBrokerToken(Instrument instrument, string brokerName, string token)
    {
        switch (brokerName.ToLower())
        {
            case "zerodha": instrument.ZerodhaToken = token; break;
            case "upstox":  instrument.UpstoxToken  = token; break;
            case "mstock":  instrument.MStockToken  = token; break;
        }
    }

    private static InstrumentType ParseInstrumentType(string? raw) => raw?.ToUpper() switch
    {
        // Equity — all broker variants
        "EQ" or "STK" or "EQUITY"                                           => InstrumentType.Equity,
        // Futures — Zerodha/Upstox/MStock NFO+BFO; MCX (FUTCOM); CDS (FUTCUR); BFO (IO/SO aliases)
        "FUT" or "FUTIDX" or "FUTSTK" or "FUTURES"
            or "FUTCOM" or "FUTCUR" or "FUTIRD"
            or "IF" or "SF"                                                  => InstrumentType.Futures,
        // Options — Zerodha (OPT/CE/PE), Upstox (OPTIDX/OPTSTK), MStock NFO + BFO
        // BFO-specific: IO (Index Option), SO (Stock Option)
        "OPT" or "OPTIDX" or "OPTSTK" or "OPTIONS" or "CE" or "PE"
            or "IO" or "SO"                                                  => InstrumentType.Options,
        // Index — Zerodha (INDEX), Upstox (INDEX), MStock (IX/AMXIDX/INX/INDEX/UNDIND)
        "IDX" or "INDEX" or "IX" or "AMXIDX" or "INX" or "INDICES"
            or "UNDIND"                                                      => InstrumentType.Index,
        _                                                                    => InstrumentType.Equity,
    };

    private static OptionType? ParseOptionType(string? raw) => raw?.ToUpper() switch
    {
        "CE" => Domain.Enums.OptionType.Call,
        "PE" => Domain.Enums.OptionType.Put,
        _    => null,
    };
}
