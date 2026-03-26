using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
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

        // 3. Upsert into the local instruments table (PostgreSQL)
        // Use batch strategy: one IN-query to load all existing rows, then diff in memory,
        // then one AddRange + one SaveChanges — reduces 200k DB calls to 3.
        var now = clock.NowInstant();
        var symbols = mappings.Select(m => m.InternalSymbol).ToList();

        // Chunk the batch to avoid hitting PostgreSQL's parameter limit (~65k params).
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

        // Flush: AddRange new items in chunks (avoids hitting DB param limits),
        // then a single SaveChanges picks up both the new rows and all tracked mutations.
        // Updates are already tracked in the DbContext — they don't need to be passed back.
        logger.LogInformation("[InstrumentRefresh] Starting upsert flush: {AddCount} new, {UpdateCount} to update", toAdd.Count, toUpdate.Count);

        int totalFlushed = 0;
        for (int i = 0; i < toAdd.Count; i += ChunkSize)
        {
            var addChunk = toAdd.Skip(i).Take(ChunkSize).ToList();
            // Pass toUpdate only on the last chunk so SaveChanges runs once for everything.
            bool isLast = i + ChunkSize >= toAdd.Count;
            int chunkNum = (i / ChunkSize) + 1;
            logger.LogDebug("[InstrumentRefresh] Flushing chunk {ChunkNum}: {ChunkSize} items (isLast={IsLast})", chunkNum, addChunk.Count, isLast);
            await instrumentRepo.BulkUpsertAsync(addChunk, isLast ? toUpdate : [], ct);
            totalFlushed += addChunk.Count;
            logger.LogDebug("[InstrumentRefresh] Chunk {ChunkNum} flushed successfully", chunkNum);
        }
        // If there were no new instruments, still flush tracked mutations (updates only).
        if (toAdd.Count == 0)
        {
            logger.LogDebug("[InstrumentRefresh] No new instruments, flushing updates only: {UpdateCount} items", toUpdate.Count);
            await instrumentRepo.BulkUpsertAsync([], toUpdate, ct);
        }

        logger.LogInformation(
            "[InstrumentRefresh] {Broker}: {Created} new, {Updated} updated instruments ({Total} total) - COMPLETED",
            brokerName, toAdd.Count, toUpdate.Count, mappings.Count);
    }

    public async Task RefreshAllBrokersAsync(CancellationToken ct)
    {
        foreach (var broker in new[] { "MStock", "Zerodha", "Upstox" })
        {
            try { await RefreshAsync(broker, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "[InstrumentRefresh] Failed to refresh {Broker}", broker);
            }
        }
    }

    // ── Universe filter ───────────────────────────────────────────────────────

    private sealed record UniverseConfig(
        HashSet<string> NseEquities,
        HashSet<string> OptionUnderlyings,
        int ExpiryWeeks);

    private async Task<UniverseConfig> LoadUniverseAsync(CancellationToken ct)
    {
        var rows = await db.InstrumentUniverse
            .Where(u => u.IsActive)
            .Select(u => new { u.Symbol, u.Category })
            .ToListAsync(ct);

        var nseEquities = rows
            .Where(r => r.Category == "NSE_EQUITY")
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        var optionUnderlyings = rows
            .Where(r => r.Category == "OPTIONS_UNDERLYING")
            .Select(r => r.Symbol.ToUpperInvariant())
            .ToHashSet();

        var expiryWeeks = await config.GetAsync<int?>("InstrumentFilter:NfoExpiryWeeks", ct) ?? 4;

        if (nseEquities.Count == 0 && optionUnderlyings.Count == 0)
            logger.LogInformation("[InstrumentRefresh] instrument_universe is empty — PASSTHROUGH mode: all NSE/NFO instruments will be stored");
        else
            logger.LogDebug("[InstrumentRefresh] Universe: {Equities} equities, {Underlyings} option underlyings, {Weeks} expiry weeks",
                nseEquities.Count, optionUnderlyings.Count, expiryWeeks);

        return new UniverseConfig(nseEquities, optionUnderlyings, expiryWeeks);
    }

    /// <summary>
    /// Returns true if the mapping should be persisted based on the configured universe.
    ///
    /// Rules:
    ///   NSE    INDEX     → always include (all NSE/BSE indexes, ~100 rows, negligible)
    ///   NSE    EQUITY    → include only if symbol is in NseEquities universe
    ///   NFO    OPTIONS   → include only if underlying is in OptionUnderlyings AND expiry within ExpiryWeeks
    ///   NFO    FUTURES   → include only if underlying is in OptionUnderlyings
    ///   other exchanges  → exclude (BSE equities, MCX, CDS, BFO, etc.)
    /// </summary>
    private bool IsInUniverse(InstrumentTokenMapping m, UniverseConfig u)
    {
        var exch = m.Exchange.ToUpperInvariant();
        var type = (m.InstrumentType ?? string.Empty).ToUpperInvariant();
        var sym  = NormaliseSymbol(m.TradingSymbol ?? string.Empty);

        // Always include all indexes regardless of exchange (NSE, BSE)
        if (type is "INDEX" or "IDX" or "IX" or "AMXIDX" or "INX" or "INDICES" or "UNDIND")
            return true;

        // Passthrough mode: if instrument_universe is not seeded yet, include all
        // NSE equities and NFO instruments so data lands in the DB on first run.
        // Once instrument_universe has entries, the filter below takes over.
        if (u.NseEquities.Count == 0 && u.OptionUnderlyings.Count == 0)
            return exch is "NSE" or "NFO";

        switch (exch)
        {
            case "NSE":
                // Equities: filter to configured universe
                return type is "EQ" or "STK" or "EQUITY" or ""
                    && u.NseEquities.Contains(sym);

            case "NFO":
            {
                var underlying = InferUnderlying(sym, u.OptionUnderlyings);
                if (underlying == null) return false;

                // Futures — include all near-term futures on tracked underlyings
                if (type is "FUT" or "FUTIDX" or "FUTSTK" or "FUTURES" or "IF" or "SF")
                    return true;

                // Options — apply expiry window filter
                if (type is "OPT" or "OPTIDX" or "OPTSTK" or "OPTIONS"
                         or "CE" or "PE" or "IO" or "SO")
                {
                    if (string.IsNullOrEmpty(m.Expiry)) return false;
                    if (LocalDatePattern.Iso.Parse(m.Expiry) is not { Success: true } parsed) return false;
                    var today = clock.TodayIst();
                    var daysToExpiry = (parsed.Value.ToDateTimeUnspecified() - today.ToDateTimeUnspecified()).Days;
                    return daysToExpiry >= 0 && daysToExpiry <= u.ExpiryWeeks * 7;
                }

                return false;
            }

            default:
                // BSE equities, BFO, MCX, CDS, NCDEX — excluded by default
                return false;
        }
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
