using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Persists broker sessions to the `broker_sessions` PostgreSQL table so tokens
/// survive app restarts when Redis is not available.
///
/// Used as a write-through backing store for InMemoryBrokerSessionManager:
///   • StoreSessionAsync → writes in-memory AND upserts to DB
///   • InvalidateSessionAsync → removes from memory AND deletes from DB
///   • StartupOrchestrator.Step7 calls LoadAllValidAsync to pre-populate memory on boot
///
/// Table is created by Program.cs startup (CREATE TABLE IF NOT EXISTS) so no migration needed.
/// </summary>
public class DbBrokerSessionPersistence(
    IConfiguration config,
    ILogger<DbBrokerSessionPersistence> logger)
{
    private string ConnectionString => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    /// <summary>
    /// Upserts a broker session. Failures are swallowed — DB persistence is best-effort;
    /// the in-memory session is always the authoritative runtime store.
    /// </summary>
    public async Task UpsertAsync(
        string brokerName, string accessToken, string? feedToken,
        string? refreshToken, DateTimeOffset? expiresAt, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO broker_sessions (broker_name, access_token, feed_token, refresh_token, expires_at, stored_at)
                VALUES (@brokerName, @accessToken, @feedToken, @refreshToken, @expiresAt, NOW())
                ON CONFLICT (broker_name) DO UPDATE SET
                    access_token  = EXCLUDED.access_token,
                    feed_token    = EXCLUDED.feed_token,
                    refresh_token = EXCLUDED.refresh_token,
                    expires_at    = EXCLUDED.expires_at,
                    stored_at     = NOW()
                """, conn);

            cmd.Parameters.AddWithValue("@brokerName", brokerName);
            cmd.Parameters.AddWithValue("@accessToken", accessToken);
            cmd.Parameters.AddWithValue("@feedToken", (object?)feedToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)refreshToken ?? DBNull.Value);
            // Use UTC DateTime for TIMESTAMPTZ — Npgsql requires UTC Kind
            if (expiresAt.HasValue)
                cmd.Parameters.Add(new NpgsqlParameter("@expiresAt", NpgsqlDbType.TimestampTz)
                    { Value = expiresAt.Value.UtcDateTime });
            else
                cmd.Parameters.AddWithValue("@expiresAt", DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
            logger.LogDebug("[DbBrokerSessions] Upserted session for {Broker} (expires {Expiry})",
                brokerName, expiresAt?.ToString("yyyy-MM-dd HH:mm zzz") ?? "no expiry");
        }
        catch (Exception ex)
        {
            // Non-fatal — in-memory session is still valid
            logger.LogWarning(ex, "[DbBrokerSessions] Failed to upsert session for {Broker} — in-memory session unaffected", brokerName);
        }
    }

    /// <summary>
    /// Returns all sessions whose token has not expired (with 5-min buffer).
    /// Returns empty list on any DB error.
    /// </summary>
    public async Task<IReadOnlyList<StoredBrokerSession>> LoadAllValidAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand("""
                SELECT broker_name, access_token, feed_token, refresh_token, expires_at
                FROM broker_sessions
                WHERE expires_at IS NULL OR expires_at > NOW() + INTERVAL '5 minutes'
                """, conn);

            var results = new List<StoredBrokerSession>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                DateTimeOffset? expiry = null;
                if (!reader.IsDBNull(4))
                {
                    var utcDt = reader.GetDateTime(4); // Npgsql returns UTC DateTime for timestamptz
                    expiry = new DateTimeOffset(utcDt, TimeSpan.Zero);
                }

                results.Add(new StoredBrokerSession(
                    BrokerName:    reader.GetString(0),
                    AccessToken:   reader.GetString(1),
                    FeedToken:     reader.IsDBNull(2) ? null : reader.GetString(2),
                    RefreshToken:  reader.IsDBNull(3) ? null : reader.GetString(3),
                    ExpiresAt:     expiry));
            }

            logger.LogInformation("[DbBrokerSessions] Loaded {Count} valid session(s) from DB", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DbBrokerSessions] Failed to load sessions from DB — starting with empty session cache");
            return [];
        }
    }

    /// <summary>
    /// Removes a session row. Failures are swallowed.
    /// </summary>
    public async Task DeleteAsync(string brokerName, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM broker_sessions WHERE broker_name = @brokerName", conn);
            cmd.Parameters.AddWithValue("@brokerName", brokerName);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[DbBrokerSessions] Failed to delete session for {Broker}", brokerName);
        }
    }
}

/// <summary>Record loaded from broker_sessions table.</summary>
public record StoredBrokerSession(
    string BrokerName,
    string AccessToken,
    string? FeedToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt);
