namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Application user. Credentials are BCrypt-hashed.
/// Each user may have zero or more broker accounts (UserBrokerAccount).
/// </summary>
public class User
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Username     { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;  // BCrypt $2a$...
    public string Role         { get; set; } = "Analyst";     // Admin | Trader | Analyst | RiskManager
    public bool   IsActive     { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<UserBrokerAccount> BrokerAccounts { get; set; } = [];
}
