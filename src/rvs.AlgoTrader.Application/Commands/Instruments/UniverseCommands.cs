using MediatR;
using rvs.AlgoTrader.Application.DTOs.Instruments;
using rvs.AlgoTrader.Application.Queries.Instruments;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Application.Commands.Instruments;

// ── Commands ──────────────────────────────────────────────────────────────────

public record CreateUniverseEntryCommand(
    string Symbol, string Exchange, string Category) : IRequest<InstrumentUniverseDto>;

public record UpdateUniverseEntryCommand(
    Guid   Id,
    string? Symbol,
    string? Exchange,
    string? Category,
    bool?   IsActive) : IRequest<InstrumentUniverseDto?>;

public record DeleteUniverseEntryCommand(Guid Id) : IRequest<bool>;

/// <summary>
/// Seeds the universe with Nifty 50 equities + major index option underlyings.
/// Skips entries that already exist (idempotent by Symbol+Category).
/// Returns the number of new rows added.
/// </summary>
public record SeedDefaultUniverseCommand : IRequest<int>;

// ── Known universe categories ─────────────────────────────────────────────────

/// <summary>
/// Well-known category values for instrument_universe entries.
/// Equity-type categories (all except OPTIONS_UNDERLYING) contribute to the equity symbol allowlist
/// during refresh when enabled via InstrumentFilter:IncludedEquityCategories.
/// </summary>
public static class UniverseCategories
{
    // Equity categories — control which symbols are saved for equity exchanges (NSE, BSE)
    public const string NseEquity           = "NSE_EQUITY";
    public const string LargeCap            = "LARGE_CAP";
    public const string MidCap              = "MID_CAP";
    public const string SmallCap            = "SMALL_CAP";
    public const string NseZGroup           = "NSE_Z_GROUP";
    public const string NseBGroup           = "NSE_B_GROUP";

    // Derivative category — controls which underlyings are tracked for NFO/BFO
    public const string OptionsUnderlying   = "OPTIONS_UNDERLYING";

    /// <summary>All equity-type categories (excludes OPTIONS_UNDERLYING).</summary>
    public static readonly IReadOnlyList<string> EquityCategories =
    [
        NseEquity, LargeCap, MidCap, SmallCap, NseZGroup, NseBGroup,
    ];

    /// <summary>All valid category values accepted by the API.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        NseEquity, LargeCap, MidCap, SmallCap, NseZGroup, NseBGroup, OptionsUnderlying,
    };
}

// ── Default universe definition (single source of truth) ─────────────────────
// Moved here so both the seed command and future tests reference the same list.

internal static class UniverseDefaults
{
    public static readonly (string Symbol, string Exchange, string Category)[] Entries =
    [
        // Index option underlyings (NFO)
        ("NIFTY",       "NFO", "OPTIONS_UNDERLYING"),
        ("BANKNIFTY",   "NFO", "OPTIONS_UNDERLYING"),
        ("FINNIFTY",    "NFO", "OPTIONS_UNDERLYING"),
        ("MIDCPNIFTY",  "NFO", "OPTIONS_UNDERLYING"),
        ("SENSEX",      "BFO", "OPTIONS_UNDERLYING"),

        // Nifty 50 large-cap equities (NSE)
        ("RELIANCE",    "NSE", "NSE_EQUITY"),
        ("TCS",         "NSE", "NSE_EQUITY"),
        ("HDFCBANK",    "NSE", "NSE_EQUITY"),
        ("INFY",        "NSE", "NSE_EQUITY"),
        ("ICICIBANK",   "NSE", "NSE_EQUITY"),
        ("HINDUNILVR",  "NSE", "NSE_EQUITY"),
        ("SBIN",        "NSE", "NSE_EQUITY"),
        ("BAJFINANCE",  "NSE", "NSE_EQUITY"),
        ("BHARTIARTL",  "NSE", "NSE_EQUITY"),
        ("KOTAKBANK",   "NSE", "NSE_EQUITY"),
        ("LT",          "NSE", "NSE_EQUITY"),
        ("ITC",         "NSE", "NSE_EQUITY"),
        ("AXISBANK",    "NSE", "NSE_EQUITY"),
        ("ASIANPAINT",  "NSE", "NSE_EQUITY"),
        ("MARUTI",      "NSE", "NSE_EQUITY"),
        ("TATAMOTORS",  "NSE", "NSE_EQUITY"),
        ("WIPRO",       "NSE", "NSE_EQUITY"),
        ("SUNPHARMA",   "NSE", "NSE_EQUITY"),
        ("ULTRACEMCO",  "NSE", "NSE_EQUITY"),
        ("TITAN",       "NSE", "NSE_EQUITY"),
        ("NTPC",        "NSE", "NSE_EQUITY"),
        ("POWERGRID",   "NSE", "NSE_EQUITY"),
        ("HCLTECH",     "NSE", "NSE_EQUITY"),
        ("ONGC",        "NSE", "NSE_EQUITY"),
        ("NESTLEIND",   "NSE", "NSE_EQUITY"),
        ("JSWSTEEL",    "NSE", "NSE_EQUITY"),
        ("TATASTEEL",   "NSE", "NSE_EQUITY"),
        ("ADANIENT",    "NSE", "NSE_EQUITY"),
        ("ADANIPORTS",  "NSE", "NSE_EQUITY"),
        ("M&M",         "NSE", "NSE_EQUITY"),
        ("BAJAJFINSV",  "NSE", "NSE_EQUITY"),
        ("DRREDDY",     "NSE", "NSE_EQUITY"),
        ("DIVISLAB",    "NSE", "NSE_EQUITY"),
        ("GRASIM",      "NSE", "NSE_EQUITY"),
        ("CIPLA",       "NSE", "NSE_EQUITY"),
        ("TECHM",       "NSE", "NSE_EQUITY"),
        ("INDUSINDBK",  "NSE", "NSE_EQUITY"),
        ("HINDALCO",    "NSE", "NSE_EQUITY"),
        ("TATACONSUM",  "NSE", "NSE_EQUITY"),
        ("COALINDIA",   "NSE", "NSE_EQUITY"),
        ("EICHERMOT",   "NSE", "NSE_EQUITY"),
        ("APOLLOHOSP",  "NSE", "NSE_EQUITY"),
        ("BPCL",        "NSE", "NSE_EQUITY"),
        ("HEROMOTOCO",  "NSE", "NSE_EQUITY"),
        ("BRITANNIA",   "NSE", "NSE_EQUITY"),
        ("SHREECEM",    "NSE", "NSE_EQUITY"),
    ];
}

// ── Handlers ──────────────────────────────────────────────────────────────────

public class CreateUniverseEntryHandler(IInstrumentUniverseRepository repo, IClock clock)
    : IRequestHandler<CreateUniverseEntryCommand, InstrumentUniverseDto>
{
    public async Task<InstrumentUniverseDto> Handle(
        CreateUniverseEntryCommand request, CancellationToken ct)
    {
        var symbol   = request.Symbol.Trim().ToUpperInvariant();
        var exchange = request.Exchange.Trim().ToUpperInvariant();
        var category = request.Category.Trim().ToUpperInvariant();

        if (!UniverseCategories.All.Contains(category))
            throw new ArgumentException(
                $"Invalid category '{category}'. Must be one of: {string.Join(", ", UniverseCategories.All)}.");

        // Re-activate if already exists
        var existing = await repo.GetBySymbolAndCategoryAsync(symbol, category, ct);
        if (existing != null)
        {
            existing.IsActive = true;
            existing.Exchange = exchange;
            await repo.UpdateAsync(existing, ct);
            return UniverseMapper.ToDto(existing);
        }

        var entry = new InstrumentUniverse
        {
            Id        = Guid.NewGuid(),
            Symbol    = symbol,
            Exchange  = exchange,
            Category  = category,
            IsActive  = true,
            CreatedAt = clock.NowInstant(),
        };
        await repo.AddAsync(entry, ct);
        return UniverseMapper.ToDto(entry);
    }
}

public class UpdateUniverseEntryHandler(IInstrumentUniverseRepository repo)
    : IRequestHandler<UpdateUniverseEntryCommand, InstrumentUniverseDto?>
{
    public async Task<InstrumentUniverseDto?> Handle(
        UpdateUniverseEntryCommand request, CancellationToken ct)
    {
        var entry = await repo.GetByIdAsync(request.Id, ct);
        if (entry == null) return null;

        if (request.Symbol   != null) entry.Symbol   = request.Symbol.Trim().ToUpperInvariant();
        if (request.Exchange != null) entry.Exchange  = request.Exchange.Trim().ToUpperInvariant();
        if (request.Category != null) entry.Category  = request.Category.Trim().ToUpperInvariant();
        if (request.IsActive.HasValue) entry.IsActive = request.IsActive.Value;

        await repo.UpdateAsync(entry, ct);
        return UniverseMapper.ToDto(entry);
    }
}

public class DeleteUniverseEntryHandler(IInstrumentUniverseRepository repo)
    : IRequestHandler<DeleteUniverseEntryCommand, bool>
{
    public async Task<bool> Handle(DeleteUniverseEntryCommand request, CancellationToken ct)
    {
        var entry = await repo.GetByIdAsync(request.Id, ct);
        if (entry == null) return false;
        await repo.DeleteAsync(entry, ct);
        return true;
    }
}

public class SeedDefaultUniverseHandler(
    IInstrumentUniverseRepository repo,
    IInstrumentRepository instruments,
    IClock clock)
    : IRequestHandler<SeedDefaultUniverseCommand, int>
{
    public async Task<int> Handle(SeedDefaultUniverseCommand request, CancellationToken ct)
    {
        var existingKeys = await repo.GetExistingKeysAsync(ct);
        var now          = clock.NowInstant();

        // Candidates: not already in universe
        var candidates = UniverseDefaults.Entries
            .Where(d => !existingKeys.Contains($"{d.Symbol}|{d.Category}"))
            .ToList();

        if (candidates.Count == 0) return 0;

        // Only insert symbols that exist in the instruments master (FK constraint).
        var symbolsNeeded = candidates.Select(d => d.Symbol).Distinct().ToList();
        var existing      = await instruments.GetBatchByInternalSymbolAsync(symbolsNeeded, ct);
        var knownSymbols  = existing.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = candidates
            .Where(d => knownSymbols.Contains(d.Symbol))
            .Select(d => new InstrumentUniverse
            {
                Id        = Guid.NewGuid(),
                Symbol    = d.Symbol,
                Exchange  = d.Exchange,
                Category  = d.Category,
                IsActive  = true,
                CreatedAt = now,
            })
            .ToList();

        if (toAdd.Count > 0)
            await repo.AddRangeAsync(toAdd, ct);

        return toAdd.Count;
    }
}
