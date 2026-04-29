using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// P10-C Quant Lab: CRUD + lifecycle management for user-defined research conditions.
/// Templates (IsTemplate=true) are read-only prebuilt conditions; Clone creates a user copy.
/// </summary>
public interface IQuantConditionService
{
    // ── Queries ───────────────────────────────────────────────────────────────

    Task<IReadOnlyList<QuantConditionDto>> GetAllAsync(bool templatesOnly = false, CancellationToken ct = default);
    Task<QuantConditionDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // ── Commands ──────────────────────────────────────────────────────────────

    Task<QuantConditionDto> CreateAsync(CreateQuantConditionRequest req, CancellationToken ct = default);
    Task<QuantConditionDto> UpdateAsync(Guid id, UpdateQuantConditionRequest req, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Append a dated research note to the condition's notes list.</summary>
    Task<QuantConditionDto> AddNoteAsync(Guid id, AddQuantConditionNoteRequest req, CancellationToken ct = default);

    /// <summary>Transition the condition to a new lifecycle status.</summary>
    Task<QuantConditionDto> ChangeStatusAsync(Guid id, ChangeQuantConditionStatusRequest req, CancellationToken ct = default);

    /// <summary>Clone a condition (or template) into a new Hypothesis-status user condition.</summary>
    Task<QuantConditionDto> CloneAsync(Guid sourceId, CancellationToken ct = default);
}
