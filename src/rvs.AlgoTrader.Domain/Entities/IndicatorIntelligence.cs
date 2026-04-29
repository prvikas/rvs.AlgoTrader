using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Editable intelligence card for a technical indicator.
/// Seeded in migration 045; users can update any field via the API.
/// IndicatorKey is the stable lookup key (e.g. "ADX", "RSI", "VWAP").
/// </summary>
public class IndicatorIntelligence
{
    public Guid    Id                    { get; set; }
    public string  IndicatorKey          { get; set; } = string.Empty;
    public string  DisplayName           { get; set; } = string.Empty;
    public string  WhatItMeasures        { get; set; } = string.Empty;
    public string  CommonMistake         { get; set; } = string.Empty;
    public string  PositiveEvConditions  { get; set; } = string.Empty;
    public string  IgnoreConditions      { get; set; } = string.Empty;
    public string  BestPairedWith        { get; set; } = string.Empty;
    public string  SizingImplications    { get; set; } = string.Empty;
    public string  UserNotes             { get; set; } = string.Empty;
    public Instant UpdatedAt             { get; set; }
}
