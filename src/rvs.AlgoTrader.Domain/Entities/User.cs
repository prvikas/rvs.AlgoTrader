namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Application user.
/// Local-auth users: Username + PasswordHash (BCrypt) are set.
/// OAuth users:      ExternalLogins has ≥1 row; Username + PasswordHash are null.
/// Both types can coexist — an OAuth user can add a local password later.
/// </summary>
public class User
{
    public Guid    Id           { get; set; } = Guid.NewGuid();
    /// <summary>Canonical email — populated from OAuth provider on first sign-in.</summary>
    public string? Email        { get; set; }
    /// <summary>Friendly display name — from OAuth profile or set manually.</summary>
    public string? DisplayName  { get; set; }
    /// <summary>Avatar URL — from OAuth profile picture. May be null.</summary>
    public string? AvatarUrl    { get; set; }
    /// <summary>Login username — required for local-auth; null for OAuth-only users.</summary>
    public string? Username     { get; set; }
    /// <summary>BCrypt hash — null for OAuth-only users who have never set a password.</summary>
    public string? PasswordHash { get; set; }
    public string  Role         { get; set; } = "Analyst";  // Admin | Trader | Analyst | RiskManager | SuperAdmin
    public bool    IsActive     { get; set; } = true;
    /// <summary>Set by the caller using IClock — never defaults to UtcNow (AP-001).</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Set by the caller using IClock — never defaults to UtcNow (AP-001).</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<UserBrokerAccount>  BrokerAccounts { get; set; } = [];
    public ICollection<UserExternalLogin>  ExternalLogins { get; set; } = [];
}
