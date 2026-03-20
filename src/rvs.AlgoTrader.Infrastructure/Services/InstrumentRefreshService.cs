using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

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
    IClock clock,
    ILogger<InstrumentRefreshService> logger) : IInstrumentRefreshService
{
    public async Task RefreshAsync(string brokerName, CancellationToken ct)
    {
        logger.LogInformation("[InstrumentRefresh] Starting master-data download for {Broker}", brokerName);

        // 1. Download from broker + rebuild in-memory cache
        await tokenResolver.RefreshAsync(brokerName, ct);
        var mappings = await tokenResolver.GetAllMappingsAsync(brokerName, ct);

        if (mappings.Count == 0)
        {
            logger.LogWarning("[InstrumentRefresh] No instruments returned from {Broker} — check session / API access", brokerName);
            return;
        }

        // 2. Upsert into the local instruments table (PostgreSQL)
        int created = 0, updated = 0;
        foreach (var m in mappings)
        {
            var existing = await instrumentRepo.GetByInternalSymbolAsync(m.InternalSymbol, ct);
            if (existing == null)
            {
                var instrument = BuildInstrument(m, clock.NowInstant());
                await instrumentRepo.UpsertAsync(instrument, ct);
                created++;
            }
            else
            {
                ApplyBrokerToken(existing, brokerName, m.BrokerToken);
                // Refresh metadata if provided
                if (!string.IsNullOrEmpty(m.Name))            existing.Name          = m.Name;
                if (!string.IsNullOrEmpty(m.TradingSymbol))   existing.TradingSymbol = m.TradingSymbol;
                if (m.LotSize > 0)                             existing.LotSize       = m.LotSize;
                if (m.TickSize > 0)                            existing.TickSize      = m.TickSize;
                existing.IsActive          = true;
                existing.LastRefreshedAt   = clock.NowInstant();
                await instrumentRepo.UpsertAsync(existing, ct);
                updated++;
            }
        }

        logger.LogInformation(
            "[InstrumentRefresh] {Broker}: {Created} new, {Updated} updated instruments ({Total} total)",
            brokerName, created, updated, mappings.Count);
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
        "EQ"  or "STK"                       => InstrumentType.Equity,
        "FUT" or "FUTIDX" or "FUTSTK"        => InstrumentType.Futures,
        "OPT" or "OPTIDX" or "OPTSTK"
            or "CE"  or "PE"                 => InstrumentType.Options,
        "IDX" or "INDEX"                     => InstrumentType.Index,
        _                                    => InstrumentType.Equity,
    };

    private static OptionType? ParseOptionType(string? raw) => raw?.ToUpper() switch
    {
        "CE" => Domain.Enums.OptionType.Call,
        "PE" => Domain.Enums.OptionType.Put,
        _    => null,
    };
}
