namespace rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

public record QuantRegimeFactorDto(
    string Name,
    string Value,
    string Interpretation,
    string Impact   // "supporting" | "contradicting" | "neutral"
);

public record QuantRegimeDto(
    string   Regime,           // QuantRegimeLabel.ToString()
    string   TrafficLight,     // "Green" | "Amber" | "Red"
    int      Confidence,       // 0–100
    string   Summary,
    string   StrategyGuidance,
    IReadOnlyList<QuantRegimeFactorDto> ContributingFactors,
    string?  DataWarning,
    string   Symbol,
    string   Timeframe,
    string   ComputedAt
);
