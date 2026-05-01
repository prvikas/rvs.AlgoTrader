using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.Options;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using StackExchange.Redis;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Phase 1: chain display + KPI metrics from live broker or snapshot DB.
/// Phase 2: 10-rule engine + bias score (-100 to +100) + pcrMax session accumulator via Redis.
/// </summary>
public sealed class OptionsIntelligenceService(
    IOptionChainService chainService,
    IOptionChainSnapshotService snapshotService,
    IConnectionMultiplexer? redis,
    IClock clock,
    ILogger<OptionsIntelligenceService> logger) : IOptionsIntelligenceService
{
    private static readonly LocalDatePattern DatePattern =
        LocalDatePattern.CreateWithInvariantCulture("yyyy-MM-dd");

    // ── Public interface ──────────────────────────────────────────────────────

    public async Task<OptionsIntelligenceDto?> GetIntelligenceAsync(
        string symbol, string expiry, CancellationToken ct)
    {
        var chain = await FetchChainAsync(symbol, expiry, ct);
        if (chain == null) return null;

        var (chainDto, totalCeOI, totalPeOI, totalIV, pinIV) = BuildChainDto(symbol, chain);
        var pcrMax = await UpdatePcrMaxAsync(symbol, chainDto.Pcr, ct);
        chainDto = chainDto with { PcrMax = pcrMax };

        var signals = EvaluateRules(chain, chainDto);
        var (bias, score) = ComputeBias(chainDto, signals);

        return new OptionsIntelligenceDto(
            chainDto, signals, bias, score,
            clock.NowInstant().ToString());
    }

    public async Task<OptionChainDto?> GetChainAsync(
        string symbol, string expiry, CancellationToken ct)
    {
        var chain = await FetchChainAsync(symbol, expiry, ct);
        if (chain == null) return null;
        var (chainDto, _, _, _, _) = BuildChainDto(symbol, chain);
        var pcrMax = await UpdatePcrMaxAsync(symbol, chainDto.Pcr, ct);
        return chainDto with { PcrMax = pcrMax };
    }

    public Task<IReadOnlyList<string>> GetExpiriesAsync(string symbol, CancellationToken ct)
    {
        var near = chainService.GetNearestWeeklyExpiry(symbol);
        var far  = chainService.GetNearestMonthlyExpiry(symbol);
        var list = new List<string> { near.Date.ToString(), far.Date.ToString() };
        if (near.Date == far.Date) list.RemoveAt(1);
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    // ── Chain fetch ───────────────────────────────────────────────────────────

    private async Task<OptionChainSnapshot?> FetchChainAsync(
        string symbol, string expiryParam, CancellationToken ct)
    {
        try
        {
            var expiry = ResolveExpiry(symbol, expiryParam);
            // Try live broker first; fall back to latest DB snapshot (≤5 days old)
            var snapshot = await chainService.GetSnapshotAsync(symbol, expiry, ct);
            if (snapshot != null) return snapshot;

            var today = clock.TodayIst();
            var history = await snapshotService.GetHistoryRangeAsync(
                symbol, today.PlusDays(-5), today, ct);
            return history.LastOrDefault().Snapshot;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OptionsIntel] Failed to fetch chain for {Symbol}/{Expiry}",
                symbol, expiryParam);
            return null;
        }
    }

    private OptionChainExpiry ResolveExpiry(string symbol, string expiryParam)
    {
        if (expiryParam.Equals("next", StringComparison.OrdinalIgnoreCase))
            return chainService.GetNearestMonthlyExpiry(symbol);
        if (expiryParam.Equals("nearest", StringComparison.OrdinalIgnoreCase))
            return chainService.GetNearestWeeklyExpiry(symbol);
        // Try ISO date
        var pr = DatePattern.Parse(expiryParam);
        if (pr.Success)
            return new OptionChainExpiry(pr.Value, "WEEKLY");
        return chainService.GetNearestWeeklyExpiry(symbol);
    }

    // ── Chain DTO builder ─────────────────────────────────────────────────────

    private static (OptionChainDto Dto, long TotalCeOI, long TotalPeOI,
        decimal TotalIV, decimal PinIV)
        BuildChainDto(string symbol, OptionChainSnapshot chain)
    {
        var spot = chain.SpotPrice;

        // Infer strike interval from chain
        var sortedStrikes = chain.Options.Select(o => o.StrikePrice)
            .Distinct().OrderBy(s => s).ToList();
        var strikeInterval = sortedStrikes.Count >= 2
            ? sortedStrikes.Zip(sortedStrikes.Skip(1), (a, b) => b - a)
                .Where(d => d > 0).DefaultIfEmpty(50m).Min()
            : 50m;

        var atmStrike = Math.Round(spot / strikeInterval) * strikeInterval;

        var ceByStrike = chain.Options
            .Where(o => o.OptionType == "CE")
            .ToDictionary(o => o.StrikePrice);
        var peByStrike = chain.Options
            .Where(o => o.OptionType == "PE")
            .ToDictionary(o => o.StrikePrice);

        var allStrikes = ceByStrike.Keys.Union(peByStrike.Keys).OrderBy(s => s).ToList();

        var strikeDtos = allStrikes.Select(k =>
        {
            ceByStrike.TryGetValue(k, out var ce);
            peByStrike.TryGetValue(k, out var pe);
            return new OptionStrikeDto(
                k,
                ce?.OpenInterest ?? 0, ce?.OiChange ?? 0,
                ce?.LastTradedPrice ?? 0, ce?.ImpliedVolatility ?? 0,
                pe?.OpenInterest ?? 0, pe?.OiChange ?? 0,
                pe?.LastTradedPrice ?? 0, pe?.ImpliedVolatility ?? 0,
                k == atmStrike);
        }).ToList();

        long totalCeOI = chain.Options.Where(o => o.OptionType == "CE").Sum(o => o.OpenInterest);
        long totalPeOI = chain.Options.Where(o => o.OptionType == "PE").Sum(o => o.OpenInterest);
        long totalOI   = totalCeOI + totalPeOI;

        decimal totalIV = totalOI > 0
            ? chain.Options.Sum(o => o.ImpliedVolatility * o.OpenInterest) / totalOI
            : 0m;

        var pinLeg = chain.Options
            .Where(o => o.StrikePrice == chain.MaxPainStrike)
            .FirstOrDefault();
        decimal pinIV = pinLeg?.ImpliedVolatility ?? 0m;

        var dto = new OptionChainDto(
            symbol, chain.Expiry.ToString(), spot,
            chain.PutCallRatioOI, 0m /* pcrMax set after Redis lookup */,
            pinIV, totalIV,
            totalCeOI, totalPeOI,
            chain.MaxPainStrike, atmStrike,
            strikeDtos,
            chain.FetchedAt.ToString());

        return (dto, totalCeOI, totalPeOI, totalIV, pinIV);
    }

    // ── Redis: session pcrMax ─────────────────────────────────────────────────

    private async Task<decimal> UpdatePcrMaxAsync(
        string symbol, decimal currentPcr, CancellationToken ct)
    {
        if (redis == null) return currentPcr;
        try
        {
            var db   = redis.GetDatabase();
            var date = clock.TodayIst().ToString();
            var key  = $"options:pcrmax:{symbol}:{date}";

            var stored = await db.StringGetAsync(key);
            var storedVal = stored.HasValue && decimal.TryParse(stored, out var v) ? v : 0m;

            if (currentPcr > storedVal)
            {
                await db.StringSetAsync(key, currentPcr.ToString(),
                    TimeSpan.FromHours(28));     // auto-expires after next session open
                return currentPcr;
            }
            return storedVal;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[OptionsIntel] Redis pcrMax update failed for {Symbol}", symbol);
            return currentPcr;
        }
    }

    // ── Rule engine (Phase 2) ─────────────────────────────────────────────────

    private static IReadOnlyList<OptionsRuleSignalDto> EvaluateRules(
        OptionChainSnapshot chain, OptionChainDto dto)
    {
        var spot     = dto.SpotPrice;
        var pcr      = dto.Pcr;
        var maxPain  = dto.MaxPainStrike;
        var totalCeOI = dto.TotalCeOI;
        var totalPeOI = dto.TotalPeOI;
        var totalIV  = dto.TotalIV;
        var pinIV    = dto.PinIV;

        var signals = new List<OptionsRuleSignalDto>(10);

        // 1. PCR_BULLISH
        signals.Add(new("PCR_BULLISH", "PCR Bullish",
            pcr > 1.20m,
            pcr > 1.80m ? "warn" : "info",
            pcr > 1.20m ? "Put writers active — bullish bias" : "PCR below bullish threshold",
            pcr, 1.20m));

        // 2. PCR_BEARISH
        signals.Add(new("PCR_BEARISH", "PCR Bearish",
            pcr < 0.80m,
            pcr < 0.60m ? "warn" : "info",
            pcr < 0.80m ? "Call writers dominant — bearish pressure" : "PCR above bearish threshold",
            pcr, 0.80m));

        // 3. PCR_EXTREME
        var extreme = pcr > 1.80m || pcr < 0.60m;
        signals.Add(new("PCR_EXTREME", "Extreme Positioning",
            extreme, "alert",
            extreme ? "Extreme PCR — mean reversion likely" : "PCR within normal range",
            pcr, null));

        // 4. MAX_PAIN_ABOVE_SPOT
        signals.Add(new("MAX_PAIN_ABOVE_SPOT", "Max Pain Above Spot",
            maxPain > spot, "info",
            maxPain > spot
                ? $"Max pain ₹{maxPain:F0} above CMP ₹{spot:F0} — upward pin expected"
                : "Max pain at or below CMP",
            maxPain, spot));

        // 5. MAX_PAIN_BELOW_SPOT
        signals.Add(new("MAX_PAIN_BELOW_SPOT", "Max Pain Below Spot",
            maxPain < spot * 0.99m, "warn",
            maxPain < spot * 0.99m
                ? $"Max pain ₹{maxPain:F0} below CMP — downside pin risk"
                : "Max pain near or above CMP",
            maxPain, spot * 0.99m));

        // 6. CE_OI_BUILDUP
        var topCe  = chain.Options.Where(o => o.OptionType == "CE").MaxBy(o => o.OpenInterest);
        var cePct  = totalCeOI > 0 && topCe != null ? (decimal)topCe.OpenInterest / totalCeOI : 0m;
        signals.Add(new("CE_OI_BUILDUP", "Call Wall",
            cePct > 0.30m, "warn",
            cePct > 0.30m
                ? $"Heavy call wall at ₹{topCe?.StrikePrice:F0} — strong resistance ({cePct * 100:F0}% of CE OI)"
                : "No dominant call wall",
            cePct * 100m, 30m));

        // 7. PE_OI_BUILDUP
        var topPe  = chain.Options.Where(o => o.OptionType == "PE").MaxBy(o => o.OpenInterest);
        var pePct  = totalPeOI > 0 && topPe != null ? (decimal)topPe.OpenInterest / totalPeOI : 0m;
        signals.Add(new("PE_OI_BUILDUP", "Put Wall",
            pePct > 0.30m, "info",
            pePct > 0.30m
                ? $"Heavy put wall at ₹{topPe?.StrikePrice:F0} — strong support ({pePct * 100:F0}% of PE OI)"
                : "No dominant put wall",
            pePct * 100m, 30m));

        // 8. IV_ELEVATED
        signals.Add(new("IV_ELEVATED", "Elevated IV",
            totalIV > 20m, totalIV > 25m ? "warn" : "info",
            totalIV > 0m
                ? (totalIV > 20m ? $"Elevated IV {totalIV:F1}% — options expensive, theta favours sellers" : $"IV normal at {totalIV:F1}%")
                : "IV data unavailable",
            totalIV, 20m));

        // 9. IV_CRUSH_RISK
        var ivCrush = totalIV > 25m && pinIV > 0m && pinIV < totalIV * 0.7m;
        signals.Add(new("IV_CRUSH_RISK", "IV Crush Risk",
            ivCrush, "alert",
            ivCrush
                ? $"IV skew at max pain — pin IV {pinIV:F1}% vs avg {totalIV:F1}%. Crush risk on pin."
                : "No IV crush pattern detected",
            pinIV > 0m ? pinIV : null, totalIV > 0m ? totalIV * 0.7m : null));

        // 10. DELTA_OI_SURGE — fresh OI buildup at any single strike > 10% of total
        OptionLeg? surgeStrike = null;
        string surgeDir = "";
        foreach (var leg in chain.Options)
        {
            if (leg.OptionType == "CE" && totalCeOI > 0
                && Math.Abs(leg.OiChange) > totalCeOI * 0.10m)
            { surgeStrike = leg; surgeDir = "CE"; break; }
            if (leg.OptionType == "PE" && totalPeOI > 0
                && Math.Abs(leg.OiChange) > totalPeOI * 0.10m)
            { surgeStrike = leg; surgeDir = "PE"; break; }
        }
        signals.Add(new("DELTA_OI_SURGE", "Fresh OI Surge",
            surgeStrike != null, "warn",
            surgeStrike != null
                ? $"Fresh {surgeDir} OI buildup at ₹{surgeStrike.StrikePrice:F0} — watch for directional move"
                : "No abnormal OI surge detected",
            surgeStrike != null ? (decimal?)Math.Abs(surgeStrike.OiChange) : null, null));

        return signals;
    }

    // ── Bias score ────────────────────────────────────────────────────────────

    private static (string Bias, decimal Score) ComputeBias(
        OptionChainDto dto, IReadOnlyList<OptionsRuleSignalDto> signals)
    {
        var pcr   = dto.Pcr;
        var spot  = dto.SpotPrice;
        var maxPain = dto.MaxPainStrike;

        // PCR: map [0.5, 2.0] → [-40, +40]
        var pcrScore = Math.Clamp((pcr - 1.0m) * 40m, -40m, 40m);

        // Max pain direction: ±20
        var mpScore = maxPain > spot ? 20m : maxPain < spot * 0.99m ? -20m : 0m;

        // OI walls: CE wall = -15 (resistance), PE wall = +15 (support)
        var ceWall = signals.FirstOrDefault(s => s.RuleId == "CE_OI_BUILDUP")?.Triggered == true ? -15m : 0m;
        var peWall = signals.FirstOrDefault(s => s.RuleId == "PE_OI_BUILDUP")?.Triggered == true ? 15m : 0m;

        // Delta OI surge: CE surge = -10 (fresh shorts/resistance), PE surge = +10 (fresh longs/support)
        var surge = signals.FirstOrDefault(s => s.RuleId == "DELTA_OI_SURGE");
        var surgeScore = 0m;
        if (surge?.Triggered == true && surge.Reason.Contains("CE")) surgeScore = -10m;
        if (surge?.Triggered == true && surge.Reason.Contains("PE")) surgeScore = 10m;

        var total = Math.Clamp(pcrScore + mpScore + ceWall + peWall + surgeScore, -100m, 100m);
        var bias  = total > 20m ? "Bullish" : total < -20m ? "Bearish" : "Neutral";
        return (bias, total);
    }
}
