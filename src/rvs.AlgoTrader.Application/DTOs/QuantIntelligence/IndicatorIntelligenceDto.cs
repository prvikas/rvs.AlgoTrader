namespace rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

public record IndicatorIntelligenceDto(
    Guid   Id,
    string IndicatorKey,
    string DisplayName,
    string WhatItMeasures,
    string CommonMistake,
    string PositiveEvConditions,
    string IgnoreConditions,
    string BestPairedWith,
    string SizingImplications,
    string UserNotes,
    DateTimeOffset UpdatedAt
);

public record UpdateIndicatorIntelligenceRequest(
    string WhatItMeasures,
    string CommonMistake,
    string PositiveEvConditions,
    string IgnoreConditions,
    string BestPairedWith,
    string SizingImplications,
    string UserNotes
);
