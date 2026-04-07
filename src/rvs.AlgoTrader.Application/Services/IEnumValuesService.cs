namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Returns enum lookup data for UI dropdowns from the enum_values table.
/// </summary>
public interface IEnumValuesService
{
    /// <summary>Returns all active enum values grouped by domain, sorted by sort_order.</summary>
    Task<Dictionary<string, List<EnumOptionDto>>> GetAllAsync(CancellationToken ct);
}

/// <summary>A single UI dropdown option returned by <see cref="IEnumValuesService"/>.</summary>
public record EnumOptionDto(string Value, string Label);
