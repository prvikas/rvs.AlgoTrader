using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Instruments;

/// <summary>
/// Triggers a refresh of the instrument master data from the specified broker.
/// Pass BrokerName = "all" to refresh all connected brokers.
/// </summary>
public record RefreshInstrumentsCommand(string BrokerName) : IRequest<bool>;

public class RefreshInstrumentsHandler(IInstrumentRefreshService refreshService)
    : IRequestHandler<RefreshInstrumentsCommand, bool>
{
    public async Task<bool> Handle(RefreshInstrumentsCommand request, CancellationToken ct)
    {
        if (request.BrokerName.Equals("all", StringComparison.OrdinalIgnoreCase))
            await refreshService.RefreshAllBrokersAsync(ct);
        else
            await refreshService.RefreshAsync(request.BrokerName, ct);
        return true;
    }
}
