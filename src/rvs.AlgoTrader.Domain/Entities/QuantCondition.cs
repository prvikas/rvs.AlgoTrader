using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// P10-C Quant Lab: a user-defined research condition with lifecycle tracking and dated notes.
/// ConditionsJson and NotesJson are JSONB columns; the service layer parses them.
///
/// Lifecycle: Hypothesis → Backtesting → PaperTrading → LiveSmall → LiveFull → Retired
/// IsTemplate = true rows are prebuilt examples; users clone these to create editable copies.
/// </summary>
public class QuantCondition
{
    public Guid     Id                      { get; set; }
    public string   Name                    { get; set; } = string.Empty;
    public string   Hypothesis              { get; set; } = string.Empty;
    /// <summary>JSON: QuantConditionEntry[] — serialized for JSONB storage.</summary>
    public string   ConditionsJson          { get; set; } = "[]";
    public string   SizingRules             { get; set; } = string.Empty;
    public string   InvalidationConditions  { get; set; } = string.Empty;
    /// <summary>JSON: QuantConditionNote[] — serialized for JSONB storage.</summary>
    public string   NotesJson               { get; set; } = "[]";
    public string   Status                  { get; set; } = QuantConditionStatus.Hypothesis;
    public string[] Tags                    { get; set; } = [];
    public bool     IsTemplate              { get; set; }
    public Instant  CreatedAt               { get; set; }
    public Instant  UpdatedAt               { get; set; }
}

/// <summary>Valid lifecycle status values for QuantCondition.</summary>
public static class QuantConditionStatus
{
    public const string Hypothesis   = "Hypothesis";
    public const string Backtesting  = "Backtesting";
    public const string PaperTrading = "PaperTrading";
    public const string LiveSmall    = "LiveSmall";
    public const string LiveFull     = "LiveFull";
    public const string Retired      = "Retired";

    public static readonly IReadOnlyList<string> All =
        [Hypothesis, Backtesting, PaperTrading, LiveSmall, LiveFull, Retired];
}
