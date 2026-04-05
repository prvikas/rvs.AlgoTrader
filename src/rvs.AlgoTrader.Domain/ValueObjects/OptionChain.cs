using NodaTime;

namespace rvs.AlgoTrader.Domain.ValueObjects;

// ── Expiry descriptor ─────────────────────────────────────────────────────

/// <summary>Identifies a specific NSE derivative expiry.</summary>
public record OptionChainExpiry(
    LocalDate Date,
    string    Type    // "WEEKLY" or "MONTHLY"
);

// ── Option chain data types ────────────────────────────────────────────────

/// <summary>
/// CE or PE leg for a single strike in an option chain.
/// All monetary values are in INR. OI in lots. Volume in lots.
/// </summary>
public record OptionLeg(
    decimal StrikePrice,
    string  OptionType,         // "CE" or "PE"
    decimal LastTradedPrice,
    long    OpenInterest,
    long    OiChange,           // change from previous day
    long    Volume,
    decimal ImpliedVolatility,  // annualised IV in percent (e.g. 18.5 = 18.5%)
    decimal BidPrice,
    decimal AskPrice,
    decimal Delta               // from Black-Scholes; 0 if not provided
);

/// <summary>
/// Full option chain snapshot for one expiry — all strikes at a point in time.
/// Constructed by IOptionChainService and pre-fetched before strategy evaluation.
///
/// Usage in strategies:
///   var snapshot = context.OptionChain;
///   var pcr = snapshot.PutCallRatioOI;     // &gt; 1.2 → bearish sentiment
///   var maxPain = snapshot.MaxPainStrike;  // price magnet for expiry
///   var support = snapshot.PeMaxOiStrike;  // highest PE OI strike = major support
///   var resist  = snapshot.CeMaxOiStrike;  // highest CE OI strike = major resistance
/// </summary>
public record OptionChainSnapshot(
    string                       UnderlyingSymbol,   // e.g. "NIFTY 50"
    NodaTime.Instant             FetchedAt,          // UTC instant of snapshot
    decimal                      SpotPrice,
    NodaTime.LocalDate           Expiry,
    IReadOnlyList<OptionLeg>     Options             // all CE + PE legs for this expiry
)
{
    // ── Pre-computed analytics ─────────────────────────────────────────────

    /// <summary>
    /// Put-Call Ratio by Open Interest.
    /// PCR = total PE OI / total CE OI.
    /// &gt; 1.2 = bearish sentiment / strong support;  &lt; 0.8 = bullish sentiment / strong resistance.
    /// </summary>
    public decimal PutCallRatioOI
    {
        get
        {
            var ceOi = Options.Where(o => o.OptionType == "CE").Sum(o => (decimal)o.OpenInterest);
            var peOi = Options.Where(o => o.OptionType == "PE").Sum(o => (decimal)o.OpenInterest);
            return ceOi == 0 ? 0 : peOi / ceOi;
        }
    }

    /// <summary>
    /// Strike with maximum CE (Call) open interest — acts as a strong resistance level.
    /// Market makers typically try to keep spot below this on expiry day.
    /// </summary>
    public decimal CeMaxOiStrike
    {
        get
        {
            var top = Options.Where(o => o.OptionType == "CE")
                             .OrderByDescending(o => o.OpenInterest)
                             .FirstOrDefault();
            return top?.StrikePrice ?? 0;
        }
    }

    /// <summary>
    /// Strike with maximum PE (Put) open interest — acts as a strong support level.
    /// </summary>
    public decimal PeMaxOiStrike
    {
        get
        {
            var top = Options.Where(o => o.OptionType == "PE")
                             .OrderByDescending(o => o.OpenInterest)
                             .FirstOrDefault();
            return top?.StrikePrice ?? 0;
        }
    }

    /// <summary>
    /// Max Pain Strike — the strike at which total option buyer losses are maximised.
    /// Calculated as: for each strike, sum (CE + PE intrinsic value × OI) across all strikes,
    /// then pick the strike that minimises total payout to option buyers.
    /// Used as a price magnet, especially near expiry.
    /// </summary>
    public decimal MaxPainStrike
    {
        get
        {
            var strikes = Options.Select(o => o.StrikePrice).Distinct().OrderBy(s => s).ToList();
            if (strikes.Count == 0) return 0;

            decimal minPain = decimal.MaxValue;
            decimal maxPainStrike = strikes[0];

            foreach (var candidate in strikes)
            {
                // Total payout at this candidate settlement price
                decimal pain = 0;
                foreach (var leg in Options)
                {
                    if (leg.OptionType == "CE")
                        pain += Math.Max(0, candidate - leg.StrikePrice) * leg.OpenInterest;
                    else
                        pain += Math.Max(0, leg.StrikePrice - candidate) * leg.OpenInterest;
                }
                if (pain < minPain) { minPain = pain; maxPainStrike = candidate; }
            }

            return maxPainStrike;
        }
    }

    /// <summary>
    /// Average implied volatility of ATM options (±2 strikes around spot).
    /// Used to gauge market uncertainty at time of signal.
    /// </summary>
    public decimal AtmIv
    {
        get
        {
            var atmStrikes = Options
                .Where(o => Math.Abs(o.StrikePrice - SpotPrice) <= 200)  // within 200 points of spot
                .ToList();
            return atmStrikes.Count == 0 ? 0 : atmStrikes.Average(o => o.ImpliedVolatility);
        }
    }

    /// <summary>
    /// Determines if the option chain signals a bullish bias.
    /// Bullish = PCR below 0.8 (heavy CE writing = market expects upside) OR
    ///           spot well above max pain (momentum driving price up).
    /// </summary>
    /// <summary>
    /// Put-Call Ratio by Change in Open Interest (intraday or day-over-day OI build).
    /// PCR = total PE OI change / total CE OI change (signed; 0 when CE side is not building).
    /// Preferred over PutCallRatioOI for intraday directional bias (STRAT-003).
    /// Returns 0 (neutral) when CE OI change is ≤ 0 to avoid division-by-zero / inverted ratios.
    /// </summary>
    public decimal PutCallRatioChangeOI
    {
        get
        {
            var ceChange = Options.Where(o => o.OptionType == "CE").Sum(o => (decimal)o.OiChange);
            var peChange = Options.Where(o => o.OptionType == "PE").Sum(o => (decimal)o.OiChange);
            return ceChange > 0 ? peChange / ceChange : 0m;
        }
    }

    public bool IsBullishBias => PutCallRatioOI < 0.8m || SpotPrice > MaxPainStrike + (MaxPainStrike * 0.005m);

    /// <summary>
    /// Determines if the option chain signals a bearish bias.
    /// Bearish = PCR above 1.2 (heavy PE writing = hedging downside) OR
    ///           spot well below max pain.
    /// </summary>
    public bool IsBearishBias => PutCallRatioOI > 1.2m || SpotPrice < MaxPainStrike - (MaxPainStrike * 0.005m);

    /// <summary>
    /// Nearest resistance level from CE OI concentration.
    /// Returns the CE strike with highest OI that is ABOVE spot price.
    /// </summary>
    public decimal NearestCeResistance
    {
        get
        {
            return Options
                .Where(o => o.OptionType == "CE" && o.StrikePrice > SpotPrice)
                .OrderByDescending(o => o.OpenInterest)
                .FirstOrDefault()?.StrikePrice ?? 0;
        }
    }

    /// <summary>
    /// Nearest support level from PE OI concentration.
    /// Returns the PE strike with highest OI that is BELOW spot price.
    /// </summary>
    public decimal NearestPeSupport
    {
        get
        {
            return Options
                .Where(o => o.OptionType == "PE" && o.StrikePrice < SpotPrice)
                .OrderByDescending(o => o.OpenInterest)
                .FirstOrDefault()?.StrikePrice ?? 0;
        }
    }
}
