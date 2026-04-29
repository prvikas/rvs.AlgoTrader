using rvs.AlgoTrader.Domain.Constants;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Records that a user has a configured account with a specific broker on a specific market.
/// Credentials (API keys, secrets, tokens) are NOT stored here — they live in
/// Redis/Vault keyed as broker:credentials:{userId}:{brokerName}:{field}.
/// </summary>
public class UserBrokerAccount
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public Guid   UserId      { get; set; }
    /// <summary>Canonical broker name — matches BrokerNames constants and IFullBrokerClient.BrokerName.</summary>
    public string BrokerName  { get; set; } = string.Empty;
    /// <summary>Market code: IN | US | UK | SG | etc.</summary>
    public string Market      { get; set; } = MarketCodes.India;
    /// <summary>Optional friendly label shown in the UI (e.g. "Zerodha - Trading").</summary>
    public string? DisplayName { get; set; }
    public bool   IsActive    { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
}
