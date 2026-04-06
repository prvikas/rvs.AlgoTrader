namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// DB-owned lookup row for all domain primitive values shown in UI dropdowns.
/// Adding a new value is a DB INSERT — no frontend code change required.
/// </summary>
public class EnumValue
{
    public string Domain    { get; set; } = string.Empty;
    public string Value     { get; set; } = string.Empty;
    public string Label     { get; set; } = string.Empty;
    public int    SortOrder { get; set; }
    public bool   IsActive  { get; set; } = true;
}
