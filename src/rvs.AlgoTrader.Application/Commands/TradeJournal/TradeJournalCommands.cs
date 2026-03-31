using MediatR;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.TradeJournal;

public record UpdateTradeNotesCommand(Guid Id, string? Notes, string[] Tags) : IRequest<bool>;

public class UpdateTradeNotesCommandHandler(ITradeJournalRepository repo)
    : IRequestHandler<UpdateTradeNotesCommand, bool>
{
    public async Task<bool> Handle(UpdateTradeNotesCommand request, CancellationToken ct)
    {
        await repo.UpdateNotesAndTagsAsync(request.Id, request.Notes, request.Tags, ct);
        return true;
    }
}
