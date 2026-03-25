using Riok.Mapperly.Abstractions;
using NodaTime;

namespace rvs.AlgoTrader.Application.Mappings;

/// <summary>
/// Mapperly partial mapper — provides shared NodaTime conversion helpers used by
/// other mappers in this project.
///
/// NOTE: InstrumentDto mapping is intentionally NOT in this class.
/// Use Queries.Instruments.InstrumentMapper.ToDto() directly — that mapper handles
/// the computed boolean fields (HasZerodha, HasUpstox, HasMStock) which Mapperly
/// cannot derive automatically from nullable source tokens.
/// </summary>
[Mapper]
public partial class InstrumentMapper
{
    // NodaTime.Instant → DateTimeOffset
    public static DateTimeOffset MapInstant(Instant instant) =>
        instant.ToDateTimeOffset();

    // NodaTime.LocalDate → System.DateOnly (used by Mapperly for Expiry, FromDate fields)
    public static DateOnly MapLocalDate(LocalDate date) =>
        new DateOnly(date.Year, date.Month, date.Day);

    public static DateOnly? MapLocalDateNullable(LocalDate? date) =>
        date.HasValue ? new DateOnly(date.Value.Year, date.Value.Month, date.Value.Day) : null;
}
