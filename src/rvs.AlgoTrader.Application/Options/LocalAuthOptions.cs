namespace rvs.AlgoTrader.Application.Options;

/// <summary>
/// Local (non-broker) username/password authentication.
/// Used when Features:BrokerRequired=false so the app can be secured
/// without a broker OAuth flow.
///
/// For development:  set <c>Password</c> to a plaintext value in appsettings.Development.json.
/// For production:   set <c>PasswordHash</c> to a BCrypt hash ($2a$...) and leave <c>Password</c> empty.
///
/// Generate a BCrypt hash: BCrypt.Net.BCrypt.HashPassword("yourpassword", workFactor: 12)
/// </summary>
public sealed class LocalAuthOptions
{
    public const string SectionName = "LocalAuth";

    /// <summary>Login username (case-insensitive comparison).</summary>
    public string Username { get; init; } = "admin";

    /// <summary>
    /// Plaintext password — accepted in any environment when <c>PasswordHash</c> is empty.
    /// Recommended only for development.  Set via User Secrets or environment variable in production.
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// BCrypt hash of the password (e.g. <c>$2a$12$...</c>).
    /// When set, <c>Password</c> is ignored.
    /// </summary>
    public string PasswordHash { get; init; } = string.Empty;
}
