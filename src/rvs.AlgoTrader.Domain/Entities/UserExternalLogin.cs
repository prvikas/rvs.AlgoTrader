namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Links an application user to an external OAuth provider identity.
/// One user may have multiple rows — e.g. Google AND Microsoft linked to the same account.
/// Adding a new provider (GitHub, LinkedIn, …) requires zero schema changes.
/// </summary>
public class UserExternalLogin
{
    public Guid    Id          { get; set; } = Guid.NewGuid();
    public Guid    UserId      { get; set; }
    /// <summary>Provider canonical name: "Google" | "Microsoft" | "Apple".</summary>
    public string  Provider    { get; set; } = string.Empty;
    /// <summary>Stable, unique user ID issued by the provider. Never changes for a given account.</summary>
    public string  ProviderSub { get; set; } = string.Empty;
    /// <summary>Email reported by provider at the time of link — informational only.</summary>
    public string? Email       { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User? User { get; set; }
}
