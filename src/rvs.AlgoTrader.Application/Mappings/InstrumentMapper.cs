using Riok.Mapperly.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using NodaTime;

namespace rvs.AlgoTrader.Application.Mappings;

[Mapper]
public partial class InstrumentMapper
{
    public partial InstrumentDto ToDto(Instrument instrument);
    public partial IReadOnlyList<InstrumentDto> ToDtoList(IReadOnlyList<Instrument> instruments);

    private static DateTimeOffset MapInstantToOffset(Instant instant) =>
        instant.ToDateTimeOffset();

    // NodaTime.LocalDate → System.DateOnly (used by Mapperly for Expiry, FromDate fields)
    private static DateOnly MapLocalDateToDateOnly(LocalDate date) =>
        new DateOnly(date.Year, date.Month, date.Day);

    private static DateOnly? MapLocalDateToDateOnly(LocalDate? date) =>
        date.HasValue ? new DateOnly(date.Value.Year, date.Value.Month, date.Value.Day) : null;
}
