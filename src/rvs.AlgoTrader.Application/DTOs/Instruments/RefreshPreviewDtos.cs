namespace rvs.AlgoTrader.Application.DTOs.Instruments;

// ── Preview (returned after download, before save) ────────────────────────────

/// <summary>
/// Full preview of a broker instrument download.
/// Returned by POST /api/instruments/preview.
/// The <see cref="StagingToken"/> is passed back in the commit call.
/// </summary>
public record RefreshPreviewDto(
    string           StagingToken,
    string           BrokerName,
    int              TotalDownloaded,
    DateTimeOffset   StagedAt,
    int              ExpiresInMinutes,

    // Counts grouped by exchange, each exchange broken down by type bucket
    IReadOnlyList<ExchangePreviewGroup>   Exchanges,

    // How many downloaded equity instruments match each universe category
    IReadOnlyList<CategoryPreviewRow>     EquityCategories
);

/// <summary>One row per exchange in the preview grid.</summary>
public record ExchangePreviewGroup(
    string Exchange,
    int    Total,
    IReadOnlyList<TypeBucketRow> Types
);

/// <summary>
/// Bucketed instrument-type count within an exchange.
/// Bucket values: "Equity" | "Futures" | "Options" | "Index" | "Other"
/// </summary>
public record TypeBucketRow(
    string   Bucket,      // user-friendly label
    int      Count,
    string[] TypeCodes    // raw broker type codes (e.g. ["EQ","BE"] or ["FUT","FUTIDX"])
);

/// <summary>Universe category row shown in the category filter step.</summary>
public record CategoryPreviewRow(
    string Category,   // e.g. "LARGE_CAP"
    string Label,      // e.g. "Large-cap"
    int    MatchCount  // how many downloaded equity instruments have a symbol in this category
);

// ── Commit request / result ───────────────────────────────────────────────────

/// <summary>
/// Sent by the frontend when the user clicks "Save" in the refresh wizard.
/// Only instruments matching all three filter dimensions will be written to the DB.
/// </summary>
public record RefreshCommitRequest(
    string   StagingToken,

    // Exchanges to save (e.g. ["NSE","NFO"])
    string[] IncludedExchanges,

    // Instrument-type buckets to save: "Equity" | "Futures" | "Options" | "Index"
    string[] IncludedInstrumentTypes,

    // Equity universe categories to save (e.g. ["LARGE_CAP","MID_CAP"])
    // Empty = no equity symbols saved; Futures/Options are unaffected by this filter.
    string[] IncludedEquityCategories
);

/// <summary>Result returned after a successful commit.</summary>
public record RefreshCommitResult(
    string BrokerName,
    int    Saved,      // instruments written to the DB (new + updated)
    int    Skipped,    // instruments that didn't match the filters
    int    NewCount,
    int    UpdatedCount
);
