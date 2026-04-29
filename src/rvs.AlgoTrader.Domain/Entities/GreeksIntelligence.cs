using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Editable intelligence card for an options metric (Greeks, IV, VIX).
/// Seeded in migration 046; users can update any field via the API.
/// MetricKey is the stable lookup key (e.g. "Delta", "Theta", "IndiaVIX").
/// </summary>
public class GreeksIntelligence
{
    public Guid    Id                    { get; set; }
    public string  MetricKey             { get; set; } = string.Empty;
    public string  DisplayName           { get; set; } = string.Empty;
    public string  WhatItMeasures        { get; set; } = string.Empty;
    public string  WhyItMatters          { get; set; } = string.Empty;
    public string  CommonMisuse          { get; set; } = string.Empty;
    public string  PositiveEvConditions  { get; set; } = string.Empty;
    public string  RegimeContext         { get; set; } = string.Empty;
    public string  SizingImplications    { get; set; } = string.Empty;
    public string  PortfolioImpact       { get; set; } = string.Empty;
    public string  UserNotes             { get; set; } = string.Empty;
    public Instant UpdatedAt             { get; set; }
}
