using Riok.Mapperly.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using NodaTime;

namespace rvs.AlgoTrader.Application.Mappings;

[Mapper]
public partial class StrategyInstanceMapper
{
    public partial StrategyInstanceDto ToDto(StrategyInstance instance);
    public partial IReadOnlyList<StrategyInstanceDto> ToDtoList(IReadOnlyList<StrategyInstance> instances);

    private static DateTimeOffset MapInstantToOffset(Instant instant) =>
        instant.ToDateTimeOffset();

    private static string MapStatus(Domain.Enums.StrategyStatus s) => s.ToString();
    private static string MapMode(Domain.Enums.StrategyMode m) => m.ToString();
}
