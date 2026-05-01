using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// P10-B: Market Regime Engine.
/// Fetches recent candle history for the given symbol + timeframe, computes
/// ADX / ATR ratio / IVP, and classifies the current market regime into one of
/// six labels with a traffic-light signal-quality indicator.
/// </summary>
public interface IQuantRegimeService
{
    /// <summary>
    /// Classify the current regime for a symbol + timeframe.
    /// Fetches the most recent candles from the candle repository and computes
    /// indicators inline. Degrades gracefully when IVP data is unavailable.
    /// </summary>
    Task<QuantRegimeDto> ClassifyAsync(string symbol, string timeframe, CancellationToken ct);

    /// <summary>
    /// Classify the current regime using the Short Premium Velocity five-label schema.
    /// Returns a <see cref="VelocityRegimeState"/> with stability, tail-risk score,
    /// vol-of-vol, and results-season flag required by the SPV engine components.
    /// Inputs are computed from candle history + India VIX candles.
    /// Classification is deterministic: same inputs always produce the same label.
    /// </summary>
    Task<VelocityRegimeState> ClassifyVelocityRegimeAsync(
        string underlyingSymbol,
        CancellationToken ct);
}
