using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;

// Implements IQuantRegimeService.ClassifyVelocityRegimeAsync via partial class.
// QuantRegimeService.cs defines the primary class and ClassifyAsync.
// This file adds only the Short Premium Velocity five-label classifier.
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed partial class QuantRegimeService
{
    // ── Label-change tracking (in-memory; reset on process restart) ───────────
    // Keyed by underlyingSymbol — stores the prior label so we can log changes.
    private static readonly ConcurrentDictionary<string, MarketRegime> _priorVelocityLabels = new();

    // VolOfVol denominator: the maximum expected VolOfVol (annualised %) used to
    // normalise RegimeStability to 0–100.  Represents a severe, sustained vol-of-vol
    // spike (e.g. VIX jumping 30 pts in five sessions — Covid / March-2020 level).
    private const decimal MaxExpectedVolOfVol = 20m;

    // ── Public interface implementation ───────────────────────────────────────

    public async Task<VelocityRegimeState> ClassifyVelocityRegimeAsync(
        string underlyingSymbol,
        CancellationToken ct)
    {
        var now         = clock.NowInstant();
        var nowInIst    = now.InZone(DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30)));
        var currentMonth = nowInIst.Month;

        // ── Fetch underlying candles (daily) ──────────────────────────────────
        // We need at least 56 candles: 50-bar rolling window for VolOfVol + 5-day RV + a buffer.
        const int requiredBars = 75;
        IReadOnlyList<ClosedCandle> spotBars;
        try
        {
            spotBars = await candles.GetLastNAsync(underlyingSymbol, "1d", requiredBars, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "VelocityRegimeClassifier: candle fetch failed for {Symbol}", underlyingSymbol);
            spotBars = [];
        }

        // ── Fetch India VIX candles (daily) ───────────────────────────────────
        // "INDIAVIX" is the NSE index symbol. Fall back to IVP proxy when unavailable.
        const string vixSymbol = "INDIAVIX";
        IReadOnlyList<ClosedCandle> vixBars;
        try
        {
            vixBars = await candles.GetLastNAsync(vixSymbol, "1d", 15, ct);
        }
        catch
        {
            vixBars = [];
        }

        // ── Derive VIX metrics ────────────────────────────────────────────────
        decimal indiaVix   = DeriveIndiaVix(vixBars, underlyingSymbol);
        decimal vixRoC5d   = DeriveVixRoC5d(vixBars);

        // ── Compute spot realised vols and R² ─────────────────────────────────
        decimal spot5dRv   = spotBars.Count >= 6  ? ComputeAnnualisedRv(spotBars, 5)  : 0m;
        decimal spot20dRv  = spotBars.Count >= 21 ? ComputeAnnualisedRv(spotBars, 20) : 0m;
        decimal trendR2    = spotBars.Count >= 10 ? ComputeTrendR2(spotBars, 20)       : 0m;

        // ── 1-day spot move for panic detection ───────────────────────────────
        decimal spot1dMove = ComputeLatestLogReturn(spotBars);
        decimal sigma20d   = ComputeLogReturnStdDev(spotBars, 20);
        bool    isPanic3Sigma = sigma20d > 0 && Math.Abs(spot1dMove) > 3m * sigma20d;

        // ── VolOfVol (rolling std-dev of 5-day RV windows) ───────────────────
        decimal volOfVol = ComputeVolOfVol(spotBars);

        // ── Derived scoring inputs ─────────────────────────────────────────────
        decimal regimeStability = Math.Clamp(
            (1m - volOfVol / MaxExpectedVolOfVol) * 100m, 0m, 100m);

        decimal tailRiskScore = ComputeTailRiskScore(indiaVix, vixRoC5d, trendR2);

        bool isResultsSeason = currentMonth is 1 or 2 or 4 or 5 or 7 or 8 or 10 or 11;

        // ── Classify (first match wins) ───────────────────────────────────────
        MarketRegime label = ClassifyVelocity(indiaVix, isPanic3Sigma, vixRoC5d, trendR2);

        // ── Log on label change ───────────────────────────────────────────────
        string configVersion = ComputeConfigVersion(string.Empty);
        LogLabelChangeIfNeeded(underlyingSymbol, label, indiaVix, vixRoC5d, trendR2,
            spot5dRv, spot20dRv, regimeStability, tailRiskScore, configVersion, now);

        return new VelocityRegimeState(
            Label:            label,
            RegimeStability:  Math.Round(regimeStability, 2),
            TailRiskScore:    Math.Round(tailRiskScore,   2),
            VolOfVol:         Math.Round(volOfVol,         4),
            IsResultsSeason:  isResultsSeason,
            ClassifiedAt:     now,
            ConfigVersion:    configVersion);
    }

    // ── Classification rules (deterministic — same inputs → same label) ───────

    private MarketRegime ClassifyVelocity(
        decimal indiaVix,
        bool    isPanic3Sigma,
        decimal vixRoC5d,
        decimal trendR2)
    {
        // 1. Panic: indiaVix > 28 OR spot 1-day move > 3σ of 20-day returns
        if (indiaVix > 28m || isPanic3Sigma)
            return MarketRegime.VelocityPanic;

        // 2. HighVolExpansion: VIX in [20,28] AND vixRoC5d > 15%
        if (indiaVix is >= 20m and <= 28m && vixRoC5d > 15m)
            return MarketRegime.VelocityHighVolExpansion;

        // 3. PostPanicNormalization: VIX in [18,24] AND vixRoC5d < 0 (VIX falling)
        if (indiaVix is >= 18m and <= 24m && vixRoC5d < 0m)
            return MarketRegime.VelocityPostPanicNormalization;

        // 4. ChoppyMeanReversion: VIX in [14,20] AND trendR² < 0.4
        if (indiaVix is >= 14m and <= 20m && trendR2 < 0.4m)
            return MarketRegime.VelocityChoppyMeanReversion;

        // 5. LowVolCompression: VIX < 16 AND trendR² ≥ 0.4
        if (indiaVix < 16m && trendR2 >= 0.4m)
            return MarketRegime.VelocityLowVolCompression;

        // 6. Fallback
        log.LogDebug("VelocityRegimeClassifier: no rule matched " +
            "(VIX={Vix:F1}, VixRoC={RoC:F1}, R2={R2:F2}) — using Choppy fallback",
            indiaVix, vixRoC5d, trendR2);
        return MarketRegime.VelocityChoppyMeanReversion;
    }

    // Fallback static logger field for the static ClassifyVelocity method —
    // primary logger is available via primary constructor parameter.
    // We use the primary-constructor `log` field for instance-level calls.

    // ── Tail risk score ───────────────────────────────────────────────────────

    private static decimal ComputeTailRiskScore(decimal indiaVix, decimal vixRoC5d, decimal trendR2)
    {
        decimal vixComponent   = Math.Clamp(indiaVix / 40m * 100m,               0m, 100m);
        decimal rocComponent   = Math.Clamp(vixRoC5d  / 50m * 100m,               0m, 100m);
        decimal trendComponent = Math.Clamp((1m - trendR2) * 100m,                0m, 100m);

        return Math.Clamp(
            0.40m * vixComponent + 0.30m * rocComponent + 0.30m * trendComponent,
            0m, 100m);
    }

    // ── VIX derivation ────────────────────────────────────────────────────────

    private decimal DeriveIndiaVix(IReadOnlyList<ClosedCandle> vixBars, string underlyingSymbol)
    {
        if (vixBars.Count > 0)
            return vixBars[^1].Close;   // VIX index close = VIX level

        // Proxy: IVP × 0.40 maps the 0–100 IVP scale to a rough VIX equivalent.
        try
        {
            var snap = ivRank.GetAsync(underlyingSymbol, CancellationToken.None).GetAwaiter().GetResult();
            if (snap?.IvPercentile is { } ivp)
                return Math.Clamp(ivp * 0.40m, 0m, 80m);
        }
        catch { /* best-effort */ }

        return 15m; // conservative neutral fallback
    }

    private static decimal DeriveVixRoC5d(IReadOnlyList<ClosedCandle> vixBars)
    {
        if (vixBars.Count < 6) return 0m;
        var today    = vixBars[^1].Close;
        var fiveDaysAgo = vixBars[^6].Close;
        return fiveDaysAgo > 0 ? (today - fiveDaysAgo) / fiveDaysAgo * 100m : 0m;
    }

    // ── Realised vol helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Annualised realised volatility (%) over <paramref name="period"/> trading days,
    /// computed as √(252 × variance of log-returns).
    /// </summary>
    private static decimal ComputeAnnualisedRv(IReadOnlyList<ClosedCandle> bars, int period)
    {
        var slice = bars.Skip(Math.Max(0, bars.Count - period - 1)).ToList();
        if (slice.Count < 2) return 0m;

        var returns = new List<double>(slice.Count - 1);
        for (int i = 1; i < slice.Count; i++)
        {
            if (slice[i - 1].Close <= 0) continue;
            returns.Add(Math.Log((double)(slice[i].Close / slice[i - 1].Close)));
        }
        if (returns.Count < 2) return 0m;

        double mean     = returns.Average();
        double variance = returns.Average(r => (r - mean) * (r - mean));
        return (decimal)(Math.Sqrt(variance * 252) * 100);
    }

    /// <summary>Standard deviation of daily log-returns over <paramref name="period"/> days (raw, not annualised).</summary>
    private static decimal ComputeLogReturnStdDev(IReadOnlyList<ClosedCandle> bars, int period)
    {
        var slice = bars.Skip(Math.Max(0, bars.Count - period - 1)).ToList();
        if (slice.Count < 3) return 0m;

        var returns = new List<double>(slice.Count - 1);
        for (int i = 1; i < slice.Count; i++)
        {
            if (slice[i - 1].Close <= 0) continue;
            returns.Add(Math.Log((double)(slice[i].Close / slice[i - 1].Close)));
        }
        if (returns.Count < 2) return 0m;

        double mean = returns.Average();
        double variance = returns.Average(r => (r - mean) * (r - mean));
        return (decimal)Math.Sqrt(variance);
    }

    /// <summary>Latest single-day log-return. Zero when insufficient data.</summary>
    private static decimal ComputeLatestLogReturn(IReadOnlyList<ClosedCandle> bars)
    {
        if (bars.Count < 2 || bars[^2].Close <= 0) return 0m;
        return (decimal)Math.Log((double)(bars[^1].Close / bars[^2].Close));
    }

    // ── VolOfVol ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Volatility-of-volatility: std-dev of 10 sequential 5-day RV windows.
    /// Requires 56+ candles (10 × 5 + 1 reference bar).
    /// Returns 0 when insufficient history.
    /// </summary>
    private static decimal ComputeVolOfVol(IReadOnlyList<ClosedCandle> bars)
    {
        const int windows    = 10;
        const int windowSize = 5;
        const int needed     = windows * windowSize + 1;

        if (bars.Count < needed) return 0m;

        var vols = new double[windows];
        for (int w = 0; w < windows; w++)
        {
            int startIdx = bars.Count - needed + w * windowSize;
            var slice    = bars.Skip(startIdx).Take(windowSize + 1).ToList();
            vols[w]      = (double)ComputeAnnualisedRv(slice, windowSize);
        }

        double mean     = vols.Average();
        double variance = vols.Average(v => (v - mean) * (v - mean));
        return (decimal)Math.Sqrt(variance);
    }

    // ── Trend R² ─────────────────────────────────────────────────────────────

    /// <summary>
    /// R² of an OLS linear regression of close prices against time over
    /// the last <paramref name="period"/> bars.
    /// High R² (≥ 0.4) indicates a sustained trend; low R² indicates chop.
    /// </summary>
    private static decimal ComputeTrendR2(IReadOnlyList<ClosedCandle> bars, int period)
    {
        var closes = bars.TakeLast(period).Select(c => (double)c.Close).ToArray();
        int n      = closes.Length;
        if (n < 3) return 0m;

        double xMean = (n - 1) / 2.0;
        double yMean = closes.Average();

        double ssXX = 0, ssXY = 0, ssYY = 0;
        for (int i = 0; i < n; i++)
        {
            double dx = i - xMean;
            double dy = closes[i] - yMean;
            ssXX += dx * dx;
            ssXY += dx * dy;
            ssYY += dy * dy;
        }

        if (ssXX == 0 || ssYY == 0) return ssYY == 0 ? 1m : 0m;
        double r2 = ssXY * ssXY / (ssXX * ssYY);
        return (decimal)Math.Clamp(r2, 0, 1);
    }

    // ── Config version ────────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 hex digest of the effective ParametersJson string.
    /// Empty string → digest of empty bytes (all-zeros scenario).
    /// </summary>
    private static string ComputeConfigVersion(string parametersJson)
    {
        var bytes  = Encoding.UTF8.GetBytes(parametersJson);
        var hash   = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    // ── Label-change logging ──────────────────────────────────────────────────

    private void LogLabelChangeIfNeeded(
        string       underlyingSymbol,
        MarketRegime newLabel,
        decimal      indiaVix,
        decimal      vixRoC5d,
        decimal      trendR2,
        decimal      spot5dRv,
        decimal      spot20dRv,
        decimal      regimeStability,
        decimal      tailRiskScore,
        string       configVersion,
        Instant      now)
    {
        var prior = _priorVelocityLabels.GetOrAdd(underlyingSymbol, newLabel);
        if (prior == newLabel) return;

        _priorVelocityLabels[underlyingSymbol] = newLabel;

        log.LogInformation(
            "VelocityRegimeClassifier: label change for {Symbol} | " +
            "Prior={Prior} → New={New} | " +
            "IndiaVIX={Vix:F1} VixRoC5d={RoC:F1}% TrendR2={R2:F3} " +
            "Spot5dRV={Rv5:F1}% Spot20dRV={Rv20:F1}% " +
            "RegimeStability={Stability:F1} TailRisk={Tail:F1} " +
            "ConfigVersion={Config} At={At}",
            underlyingSymbol,
            prior,       newLabel,
            indiaVix,    vixRoC5d,  trendR2,
            spot5dRv,    spot20dRv,
            regimeStability, tailRiskScore,
            configVersion,   now);
    }
}
