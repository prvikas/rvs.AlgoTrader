using rvs.AlgoTrader.Domain.Constants;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Records that a user has a configured account with a specific broker on a specific market.
/// Credentials (API keys, secrets, tokens) are NOT stored here — they live in
/// Redis/Vault keyed as broker:credentials:{userId}:{brokerName}:{field}.
/// </summary>
public class UserBrokerAccount
{
    public Guid   Id              { get; set; } = Guid.NewGuid();
    public Guid   UserId          { get; set; }
    /// <summary>FK to brokers table.</summary>
    public short  BrokerId        { get; set; }
    /// <summary>Broker-specific access token (encrypted in DB, decrypted at runtime via Vault/Redis).</summary>
    public string? BrokerToken    { get; set; }
    /// <summary>Optional API key credential (encrypted).</summary>
    public string? ApiKey         { get; set; }
    /// <summary>Optional API secret credential (encrypted).</summary>
    public string? ApiSecret      { get; set; }
    /// <summary>Optional friendly label shown in the UI (e.g. "Zerodha - Trading").</summary>
    public string? DisplayName    { get; set; }
    public bool   IsActive        { get; set; } = true;
    /// <summary>Set by the caller using IClock — never defaults to UtcNow (AP-001).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public User? User { get; set; }
    public Broker? Broker { get; set; }
}
