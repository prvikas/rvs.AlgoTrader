using Riok.Mapperly.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using NodaTime;

namespace rvs.AlgoTrader.Application.Mappings;

[Mapper]
public partial class StrategyInstanceMapper
{
    public StrategyInstanceDto ToDto(StrategyInstance instance)
    {
        return new StrategyInstanceDto(
            instance.Id,
            instance.Name,
            instance.StrategyType,
            instance.InternalSymbol,
            instance.Timeframe,
            instance.Status.ToString(),
            instance.Mode.ToString(),
            instance.BrokerAccount?.Broker?.Name,
            instance.AllocatedCapital,
            instance.RuntimeState?.AutoResumeOnRestart ?? false,
            instance.ScheduleJson,
            instance.ParametersJson,
            instance.FailureBehaviorJson,
            instance.CreatedAt.ToDateTimeOffset(),
            instance.UpdatedAt.ToDateTimeOffset(),
            instance.RuntimeState?.TodayRealizedPnl ?? 0m,
            instance.RuntimeState?.TodayUnrealizedPnl ?? 0m);
    }

    public IReadOnlyList<StrategyInstanceDto> ToDtoList(IReadOnlyList<StrategyInstance> instances)
        => instances.Select(ToDto).ToList();

    private static DateTimeOffset MapInstantToOffset(Instant instant) =>
        instant.ToDateTimeOffset();

    private static string MapStatus(Domain.Enums.StrategyStatus s) => s.ToString();
    private static string MapMode(Domain.Enums.StrategyMode m) => m.ToString();
}
