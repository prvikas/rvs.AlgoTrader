using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NodaTime;
using Npgsql;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

/// <summary>
/// Raw Npgsql repository for option_chain_snapshots.
/// Stores and retrieves daily EOD option chain data used by BacktestEngine (FIB-5).
/// </summary>
public interface IOptionChainSnapshotRepository
{
    /// <summary>Upsert an EOD snapshot for a symbol and date (idempotent).</summary>
    Task UpsertAsync(
        string symbol,
        LocalDate date,
        LocalDate expiryDate,
        decimal spotPrice,
        decimal? pcr,
        decimal? pcrChange,
        decimal? atmIv,
        decimal? maxPainStrike,
        decimal? ceMaxOiStrike,
        decimal? peMaxOiStrike,
        long? totalCeOi,
        long? totalPeOi,
        string legsJson,
        CancellationToken ct);

    /// <summary>
    /// Retrieve all snapshots for a symbol within [from, to] date range,
    /// ordered by snapshot_date ascending for binary-search per-bar lookups.
    /// </summary>
    Task<IReadOnlyList<OptionChainSnapshotRow>> GetRangeAsync(
        string symbol,
        LocalDate from,
        LocalDate to,
        CancellationToken ct);
}

/// <summary>
/// Raw storage row returned from option_chain_snapshots.
/// Caller reconstructs OptionChainSnapshot from legsJson + aggregate columns.
/// </summary>
public record OptionChainSnapshotRow(
    LocalDate   Date,
    LocalDate   ExpiryDate,
    decimal     SpotPrice,
    decimal?    Pcr,
    decimal?    PcrChange,
    decimal?    AtmIv,
    decimal?    MaxPainStrike,
    decimal?    CeMaxOiStrike,
    decimal?    PeMaxOiStrike,
    long?       TotalCeOi,
    long?       TotalPeOi,
    string      LegsJson);

public class OptionChainSnapshotRepository(IConfiguration config) : IOptionChainSnapshotRepository
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task UpsertAsync(
        string symbol, LocalDate date, LocalDate expiryDate,
        decimal spotPrice,
        decimal? pcr, decimal? pcrChange, decimal? atmIv,
        decimal? maxPainStrike, decimal? ceMaxOiStrike, decimal? peMaxOiStrike,
        long? totalCeOi, long? totalPeOi,
        string legsJson, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO option_chain_snapshots
                (underlying_symbol, snapshot_date, expiry_date, spot_price,
                 pcr, pcr_change, atm_iv, max_pain_strike,
                 ce_max_oi_strike, pe_max_oi_strike,
                 total_ce_oi, total_pe_oi, legs_json)
            VALUES
                (@sym, @date::date, @expiry::date, @spot,
                 @pcr, @pcrChange, @atmIv, @maxPain,
                 @ceMoi, @peMoi,
                 @ceTotalOi, @peTotalOi, @legs::jsonb)
            ON CONFLICT (underlying_symbol, snapshot_date)
            DO UPDATE SET
                expiry_date      = EXCLUDED.expiry_date,
                spot_price       = EXCLUDED.spot_price,
                pcr              = EXCLUDED.pcr,
                pcr_change       = EXCLUDED.pcr_change,
                atm_iv           = EXCLUDED.atm_iv,
                max_pain_strike  = EXCLUDED.max_pain_strike,
                ce_max_oi_strike = EXCLUDED.ce_max_oi_strike,
                pe_max_oi_strike = EXCLUDED.pe_max_oi_strike,
                total_ce_oi      = EXCLUDED.total_ce_oi,
                total_pe_oi      = EXCLUDED.total_pe_oi,
                legs_json        = EXCLUDED.legs_json,
                recorded_at      = NOW()
            """, conn);

        cmd.Parameters.AddWithValue("@sym",        symbol);
        cmd.Parameters.AddWithValue("@date",       date.ToString());
        cmd.Parameters.AddWithValue("@expiry",     expiryDate.ToString());
        cmd.Parameters.AddWithValue("@spot",       spotPrice);
        cmd.Parameters.AddWithValue("@pcr",        (object?)pcr         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pcrChange",  (object?)pcrChange   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@atmIv",      (object?)atmIv       ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@maxPain",    (object?)maxPainStrike   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ceMoi",      (object?)ceMaxOiStrike   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@peMoi",      (object?)peMaxOiStrike   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ceTotalOi",  (object?)totalCeOi   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@peTotalOi",  (object?)totalPeOi   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@legs",       legsJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<OptionChainSnapshotRow>> GetRangeAsync(
        string symbol, LocalDate from, LocalDate to, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            SELECT snapshot_date, expiry_date, spot_price,
                   pcr, pcr_change, atm_iv, max_pain_strike,
                   ce_max_oi_strike, pe_max_oi_strike,
                   total_ce_oi, total_pe_oi, legs_json
            FROM option_chain_snapshots
            WHERE underlying_symbol = @sym
              AND snapshot_date BETWEEN @from::date AND @to::date
            ORDER BY snapshot_date ASC
            """, conn);

        cmd.Parameters.AddWithValue("@sym",  symbol);
        cmd.Parameters.AddWithValue("@from", from.ToString());
        cmd.Parameters.AddWithValue("@to",   to.ToString());

        var list = new List<OptionChainSnapshotRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OptionChainSnapshotRow(
                Date:           reader.GetFieldValue<LocalDate>(0),
                ExpiryDate:     reader.GetFieldValue<LocalDate>(1),
                SpotPrice:      reader.GetDecimal(2),
                Pcr:            reader.IsDBNull(3)  ? null : reader.GetDecimal(3),
                PcrChange:      reader.IsDBNull(4)  ? null : reader.GetDecimal(4),
                AtmIv:          reader.IsDBNull(5)  ? null : reader.GetDecimal(5),
                MaxPainStrike:  reader.IsDBNull(6)  ? null : reader.GetDecimal(6),
                CeMaxOiStrike:  reader.IsDBNull(7)  ? null : reader.GetDecimal(7),
                PeMaxOiStrike:  reader.IsDBNull(8)  ? null : reader.GetDecimal(8),
                TotalCeOi:      reader.IsDBNull(9)  ? null : reader.GetInt64(9),
                TotalPeOi:      reader.IsDBNull(10) ? null : reader.GetInt64(10),
                LegsJson:       reader.GetString(11)));
        }

        return list;
    }
}

// ── JSON serialisation helpers ────────────────────────────────────────────────

/// <summary>
/// Lightweight DTO used only for serialising/deserialising OptionLeg to/from JSONB.
/// Avoids taking a hard dependency on NodaTime in the serialisation path.
/// </summary>
file record OptionLegJsonDto(
    decimal StrikePrice,
    string  OptionType,
    decimal LastTradedPrice,
    long    OpenInterest,
    long    OiChange,
    long    Volume,
    decimal ImpliedVolatility,
    decimal BidPrice,
    decimal AskPrice,
    decimal Delta);

public static class OptionChainSnapshotSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeLegs(IReadOnlyList<OptionLeg> legs)
    {
        var dtos = legs.Select(l => new OptionLegJsonDto(
            l.StrikePrice, l.OptionType, l.LastTradedPrice,
            l.OpenInterest, l.OiChange, l.Volume,
            l.ImpliedVolatility, l.BidPrice, l.AskPrice, l.Delta));
        return JsonSerializer.Serialize(dtos, Opts);
    }

    public static IReadOnlyList<OptionLeg> DeserializeLegs(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var dtos = JsonSerializer.Deserialize<OptionLegJsonDto[]>(json, Opts);
        if (dtos == null) return [];
        return dtos.Select(d => new OptionLeg(
            d.StrikePrice, d.OptionType, d.LastTradedPrice,
            d.OpenInterest, d.OiChange, d.Volume,
            d.ImpliedVolatility, d.BidPrice, d.AskPrice, d.Delta)).ToList();
    }
}
