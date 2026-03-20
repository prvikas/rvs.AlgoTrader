using Riok.Mapperly.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Application.DTOs.Orders;
using NodaTime;

namespace rvs.AlgoTrader.Application.Mappings;

[Mapper]
public partial class OrderMapper
{
    // Mapperly generates the mapping body at compile time (zero-alloc, no reflection).
    // Custom conversions are handled via partial methods below.

    public partial OrderDto ToDto(Order order);

    public partial IReadOnlyList<OrderDto> ToDtoList(IReadOnlyList<Order> orders);

    // NodaTime ZonedDateTime → DateTimeOffset (UTC)
    private static DateTimeOffset MapZonedToOffset(ZonedDateTime? zdt) =>
        zdt.HasValue ? zdt.Value.ToInstant().ToDateTimeOffset() : default;

    private static DateTimeOffset MapInstantToOffset(Instant instant) =>
        instant.ToDateTimeOffset();

    private static string MapOrderType(Domain.Enums.OrderType t) => t.ToString().ToUpperInvariant();
    private static string MapDirection(Domain.Enums.OrderDirection d) => d.ToString().ToUpperInvariant();
    private static string MapStatus(Domain.Enums.OrderStatus s) => s.ToString().ToUpperInvariant();
}
