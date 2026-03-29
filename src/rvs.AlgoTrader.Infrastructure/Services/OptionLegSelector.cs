using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Resolves an <see cref="OptionsLegSpec"/> to a concrete tradeable NFO instrument.
/// Queries the instrument master for all CE/PE legs at the target expiry, then selects
/// the strike using the requested <see cref="StrikeSelectionMode"/>.
/// Delta-based selection requires the <see cref="IBlackScholesEngine"/> and uses
/// time-to-expiry inferred from expiry date vs. today via IClock.
/// </summary>
public class OptionLegSelector(
    IInstrumentRepository instrumentRepo,
    IBlackScholesEngine bsEngine,
    IClock clock) : IOptionLegSelector
{
    // RBI repo rate as a rough risk-free rate proxy. Not configurable — doesn't significantly
    // affect delta ordering vs. other strikes.
    private const double RiskFreeRate = 0.065;

    public async Task<OptionLegResolution?> ResolveAsync(
        string underlyingSymbol,
        OptionsLegSpec spec,
        decimal spotPrice,
        LocalDate expiryDate,
        string brokerName,
        CancellationToken ct)
    {
        var all = await instrumentRepo.GetOptionsByUnderlyingAndExpiryAsync(
            underlyingSymbol, expiryDate, ct);

        var targetType = spec.OptionType == OptionType.Call ? "CE" : "PE";
        var legs = all
            .Where(i => i.OptionType.HasValue &&
                        (spec.OptionType == OptionType.Call
                            ? i.OptionType == OptionType.Call
                            : i.OptionType == OptionType.Put) &&
                        i.StrikePrice.HasValue)
            .OrderBy(i => i.StrikePrice!.Value)
            .ToList();

        if (legs.Count == 0) return null;

        var strikes = legs.Select(l => l.StrikePrice!.Value).Distinct().OrderBy(s => s).ToList();

        decimal selectedStrike = spec.SelectionMode switch
        {
            StrikeSelectionMode.Atm         => NearestStrike(strikes, spotPrice),
            StrikeSelectionMode.OtmByPct    => SelectOtmByPct(strikes, spotPrice, spec, targetType),
            StrikeSelectionMode.OtmByStrike => SelectOtmByCount(strikes, spotPrice, spec, targetType),
            StrikeSelectionMode.FixedStrike => NearestStrike(strikes, spec.FixedStrike ?? spotPrice),
            StrikeSelectionMode.ByDelta     => SelectByDelta(strikes, spotPrice, expiryDate, spec),
            _                               => NearestStrike(strikes, spotPrice)
        };

        var instrument = legs.FirstOrDefault(l => l.StrikePrice == selectedStrike);
        if (instrument == null) return null;

        var token = instrument.GetBrokerToken(brokerName) ?? instrument.MStockToken ?? instrument.InternalSymbol;

        return new OptionLegResolution(
            InternalSymbol:  instrument.InternalSymbol,
            BrokerToken:     token,
            StrikePrice:     selectedStrike,
            OptionType:      spec.OptionType,
            Expiry:          expiryDate,
            LastTradedPrice: 0m);  // caller should fetch LTP separately if needed
    }

    private static decimal NearestStrike(List<decimal> strikes, decimal target)
        => strikes.MinBy(s => Math.Abs(s - target));

    private static decimal SelectOtmByPct(
        List<decimal> strikes, decimal spot, OptionsLegSpec spec, string type)
    {
        var pct = spec.OtmPct ?? 0.02m;
        var target = type == "CE" ? spot * (1 + pct) : spot * (1 - pct);
        return NearestStrike(strikes, target);
    }

    private static decimal SelectOtmByCount(
        List<decimal> strikes, decimal spot, OptionsLegSpec spec, string type)
    {
        int n = spec.OtmStrikes ?? 1;
        int atmIdx = strikes.IndexOf(NearestStrike(strikes, spot));
        int targetIdx = type == "CE"
            ? Math.Min(atmIdx + n, strikes.Count - 1)
            : Math.Max(atmIdx - n, 0);
        return strikes[targetIdx];
    }

    private decimal SelectByDelta(
        List<decimal> strikes, decimal spot, LocalDate expiryDate, OptionsLegSpec spec)
    {
        double targetDelta = (double)(spec.TargetDelta ?? 0.30m);
        bool isCall = spec.OptionType == OptionType.Call;

        var today = clock.NowInstant().InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"]).Date;
        double tte = Math.Max((expiryDate - today).Days / 365.0, 1.0 / 365.0);

        // Pick the strike whose absolute delta is closest to targetDelta.
        // IV is approximated as 15% (0.15) for strike ranking — relative ordering is stable.
        return strikes
            .Select(k => (Strike: k, Delta: Math.Abs((double)bsEngine.Compute(spot, k, tte, 0.15, RiskFreeRate, isCall).Delta)))
            .MinBy(x => Math.Abs(x.Delta - targetDelta))
            .Strike;
    }
}
