using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class ForwardTestFillSimulator : IForwardTestFillSimulator
{
    public Task<FillResult> SimulateFillAsync(SignalResult signal, IReadOnlyList<ClosedCandle> subsequentCandles, IClock clock, FillSimConfig config, CancellationToken ct)
    {
        if (!subsequentCandles.Any()) return Task.FromResult(new FillResult(false, null, "No subsequent candles"));

        var nextBar = subsequentCandles[0];

        if (signal.Signal is "HOLD") return Task.FromResult(new FillResult(false, null, "HOLD signal"));

        // Market order: fill at next bar's open
        var fillPrice = nextBar.Open * (1 + (signal.Signal == "BUY" ? config.SlippagePct : -config.SlippagePct));
        return Task.FromResult(new FillResult(true, fillPrice, null));
    }
}
