namespace rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

// ── Sub-objects ───────────────────────────────────────────────────────────────

public record QuantConditionEntryDto(
    string Indicator,
    string Operator,
    string Value,
    string Description
);

public record QuantConditionNoteDto(
    string Id,
    string Date,    // ISO date string: "2026-04-29"
    string Text
);

// ── Main DTO ──────────────────────────────────────────────────────────────────

public record QuantConditionDto(
    Guid   Id,
    string Name,
    string Hypothesis,
    IReadOnlyList<QuantConditionEntryDto> Conditions,
    string SizingRules,
    string InvalidationConditions,
    IReadOnlyList<QuantConditionNoteDto> Notes,
    string   Status,
    string[] Tags,
    bool     IsTemplate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

// ── Request payloads ──────────────────────────────────────────────────────────

public record CreateQuantConditionRequest(
    string Name,
    string Hypothesis,
    IReadOnlyList<QuantConditionEntryDto>? Conditions,
    string SizingRules,
    string InvalidationConditions,
    string[] Tags
);

public record UpdateQuantConditionRequest(
    string Name,
    string Hypothesis,
    IReadOnlyList<QuantConditionEntryDto> Conditions,
    string SizingRules,
    string InvalidationConditions,
    string[] Tags
);

public record AddQuantConditionNoteRequest(string Text);

public record ChangeQuantConditionStatusRequest(string Status);
