using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class QuantConditionService(AlgoTraderDbContext db, IClock clock, ICurrentUser currentUser)
    : IQuantConditionService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private Guid? CurrentUserId => Guid.TryParse(currentUser.UserId, out var g) ? g : null;

    // ── Queries ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<QuantConditionDto>> GetAllAsync(
        bool templatesOnly = false, CancellationToken ct = default)
    {
        var uid = CurrentUserId;
        var q = db.QuantConditions.AsQueryable();
        if (templatesOnly)
            q = q.Where(e => e.IsTemplate);
        else
            // Show user's own conditions + templates + legacy rows (user_id == null)
            q = q.Where(e => e.IsTemplate || e.UserId == null || e.UserId == uid);

        var rows = await q.OrderBy(e => e.UpdatedAt).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<QuantConditionDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var uid = CurrentUserId;
        var row = await db.QuantConditions.FirstOrDefaultAsync(
            e => e.Id == id && (e.IsTemplate || e.UserId == null || e.UserId == uid), ct);
        return row is null ? null : ToDto(row);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public async Task<QuantConditionDto> CreateAsync(
        CreateQuantConditionRequest req, CancellationToken ct = default)
    {
        var now = clock.NowInstant();
        var entity = new QuantCondition
        {
            Id                     = Guid.NewGuid(),
            Name                   = req.Name,
            Hypothesis             = req.Hypothesis,
            ConditionsJson         = SerializeEntries(req.Conditions),
            SizingRules            = req.SizingRules,
            InvalidationConditions = req.InvalidationConditions,
            NotesJson              = "[]",
            Status                 = QuantConditionStatus.Hypothesis,
            Tags                   = req.Tags ?? [],
            IsTemplate             = false,
            CreatedAt              = now,
            UpdatedAt              = now,
        };
        db.QuantConditions.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<QuantConditionDto> UpdateAsync(
        Guid id, UpdateQuantConditionRequest req, CancellationToken ct = default)
    {
        var entity = await FindOrThrowAsync(id, ct);
        if (entity.IsTemplate)
            throw new InvalidOperationException("Templates cannot be edited. Clone the template first.");

        entity.Name                   = req.Name;
        entity.Hypothesis             = req.Hypothesis;
        entity.ConditionsJson         = SerializeEntries(req.Conditions);
        entity.SizingRules            = req.SizingRules;
        entity.InvalidationConditions = req.InvalidationConditions;
        entity.Tags                   = req.Tags ?? [];
        entity.UpdatedAt              = clock.NowInstant();

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await FindOrThrowAsync(id, ct);
        if (entity.IsTemplate)
            throw new InvalidOperationException("Templates cannot be deleted.");
        db.QuantConditions.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<QuantConditionDto> AddNoteAsync(
        Guid id, AddQuantConditionNoteRequest req, CancellationToken ct = default)
    {
        var entity = await FindOrThrowAsync(id, ct);

        var notes  = DeserializeNotes(entity.NotesJson);
        var newNote = new QuantConditionNoteDto(
            Id:   Guid.NewGuid().ToString(),
            Date: clock.TodayIst().ToString("yyyy-MM-dd", null),
            Text: req.Text.Trim());
        notes.Add(newNote);

        entity.NotesJson  = JsonSerializer.Serialize(notes);
        entity.UpdatedAt  = clock.NowInstant();

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<QuantConditionDto> ChangeStatusAsync(
        Guid id, ChangeQuantConditionStatusRequest req, CancellationToken ct = default)
    {
        if (!QuantConditionStatus.All.Contains(req.Status))
            throw new ArgumentException($"Invalid status '{req.Status}'. Valid: {string.Join(", ", QuantConditionStatus.All)}");

        var entity = await FindOrThrowAsync(id, ct);
        if (entity.IsTemplate)
            throw new InvalidOperationException("Template status cannot be changed.");

        entity.Status    = req.Status;
        entity.UpdatedAt = clock.NowInstant();

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<QuantConditionDto> CloneAsync(Guid sourceId, CancellationToken ct = default)
    {
        var source = await FindOrThrowAsync(sourceId, ct);
        var now    = clock.NowInstant();

        var clone = new QuantCondition
        {
            Id                     = Guid.NewGuid(),
            Name                   = $"Copy of {source.Name}",
            Hypothesis             = source.Hypothesis,
            ConditionsJson         = source.ConditionsJson,
            SizingRules            = source.SizingRules,
            InvalidationConditions = source.InvalidationConditions,
            NotesJson              = "[]",
            Status                 = QuantConditionStatus.Hypothesis,
            Tags                   = [..source.Tags],
            IsTemplate             = false,
            CreatedAt              = now,
            UpdatedAt              = now,
        };
        db.QuantConditions.Add(clone);
        await db.SaveChangesAsync(ct);
        return ToDto(clone);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<QuantCondition> FindOrThrowAsync(Guid id, CancellationToken ct)
        => await db.QuantConditions.FirstOrDefaultAsync(e => e.Id == id, ct)
           ?? throw new KeyNotFoundException($"Quant condition '{id}' not found.");

    private static string SerializeEntries(IReadOnlyList<QuantConditionEntryDto>? entries)
        => entries is null or { Count: 0 }
            ? "[]"
            : JsonSerializer.Serialize(entries);

    private static List<QuantConditionNoteDto> DeserializeNotes(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]") return [];
        return JsonSerializer.Deserialize<List<QuantConditionNoteDto>>(json, JsonOpts) ?? [];
    }

    private static QuantConditionDto ToDto(QuantCondition e)
    {
        var entries = string.IsNullOrWhiteSpace(e.ConditionsJson) || e.ConditionsJson == "[]"
            ? (IReadOnlyList<QuantConditionEntryDto>)[]
            : JsonSerializer.Deserialize<IReadOnlyList<QuantConditionEntryDto>>(e.ConditionsJson, JsonOpts) ?? [];

        var notes = string.IsNullOrWhiteSpace(e.NotesJson) || e.NotesJson == "[]"
            ? (IReadOnlyList<QuantConditionNoteDto>)[]
            : JsonSerializer.Deserialize<IReadOnlyList<QuantConditionNoteDto>>(e.NotesJson, JsonOpts) ?? [];

        return new QuantConditionDto(
            e.Id,
            e.Name,
            e.Hypothesis,
            entries,
            e.SizingRules,
            e.InvalidationConditions,
            notes,
            e.Status,
            e.Tags,
            e.IsTemplate,
            e.CreatedAt.ToDateTimeOffset(),
            e.UpdatedAt.ToDateTimeOffset());
    }
}
