namespace rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

public record GreeksIntelligenceDto(
    Guid   Id,
    string MetricKey,
    string DisplayName,
    string WhatItMeasures,
    string WhyItMatters,
    string CommonMisuse,
    string PositiveEvConditions,
    string RegimeContext,
    string SizingImplications,
    string PortfolioImpact,
    string UserNotes,
    DateTimeOffset UpdatedAt
);

public record UpdateGreeksIntelligenceRequest(
    string WhatItMeasures,
    string WhyItMatters,
    string CommonMisuse,
    string PositiveEvConditions,
    string RegimeContext,
    string SizingImplications,
    string PortfolioImpact,
    string UserNotes
);
