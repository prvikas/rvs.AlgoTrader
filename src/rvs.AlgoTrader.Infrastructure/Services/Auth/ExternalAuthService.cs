using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Singleton service that orchestrates the OAuth 2.0 authorization code flow:
///   1. Resolves the correct IExternalAuthProvider by name.
///   2. Manages CSRF state tokens (10-minute TTL).
///   3. Manages one-time exchange tokens (2-minute TTL) so the JWT never appears in a URL.
///   4. Creates / links / updates application user accounts via IUserExternalLoginRepository
///      using a short-lived DI scope (DbContext is scoped, this service is singleton).
///
/// In-memory state dicts are intentional — state tokens have a 2–10 minute lifetime,
/// and the callback hits the same process that issued the login URL.
/// If multi-replica deployment is needed later, swap the dicts for Redis SETNX.
/// </summary>
public sealed class ExternalAuthService : IExternalAuthService
{
    private readonly IReadOnlyDictionary<string, IExternalAuthProvider> _providers;
    private readonly IReadOnlyList<ProviderInfoDto>                      _enabledProviders;
    private readonly IServiceScopeFactory                                _scopes;
    private readonly ILogger<ExternalAuthService>                        _logger;

    // ── In-memory state stores ────────────────────────────────────────────────

    // CSRF state: state-token → expiry
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stateTokens  = new();
    // Exchange tokens: exchange-token → (jwt, expiry)
    private readonly ConcurrentDictionary<string, (string Jwt, DateTimeOffset Expiry)> _exchangeTokens = new();

    // ── Display-name map (shown on login button) ──────────────────────────────

    private static readonly Dictionary<string, (string DisplayName, string IconKey)> ProviderMeta = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["Google"]    = ("Google",    "google"),
        ["Microsoft"] = ("Microsoft", "microsoft"),
        ["Apple"]     = ("Apple",     "apple"),
    };

    public ExternalAuthService(
        IEnumerable<IExternalAuthProvider> providers,
        IOptions<OAuthOptions> opts,
        IServiceScopeFactory scopes,
        ILogger<ExternalAuthService> logger)
    {
        _scopes  = scopes;
        _logger  = logger;

        var cfg  = opts.Value;

        // Only include providers that are both registered in DI and enabled in config.
        var map      = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        var enabled  = new List<ProviderInfoDto>();

        static bool IsEnabled(object? section)
            => section?.GetType().GetProperty("Enabled")?.GetValue(section) is true;

        object?[] sections = [cfg.Google, cfg.Microsoft, cfg.Apple];
        foreach (var section in sections)
        {
            if (section is null || !IsEnabled(section)) continue;
            var nameVal = section.GetType().GetProperty("ClientId")?.GetValue(section) as string ?? "";
            if (string.IsNullOrEmpty(nameVal)) continue;

            // Derive provider name from option type name (GoogleOAuthOptions → "Google")
            var typeName = section.GetType().Name.Replace("OAuthOptions", "");
            if (!map.ContainsKey(typeName)) continue;

            var (display, icon) = ProviderMeta.TryGetValue(typeName, out var m) ? m : (typeName, typeName.ToLower());
            enabled.Add(new ProviderInfoDto(typeName, display, icon));
        }

        _providers        = map;
        _enabledProviders = enabled.AsReadOnly();
    }

    // ── IExternalAuthService ──────────────────────────────────────────────────

    public IReadOnlyList<ProviderInfoDto> GetEnabledProviders() => _enabledProviders;

    public string GenerateLoginUrl(string providerName, string redirectUri, out string state)
    {
        state = Guid.NewGuid().ToString("N");
        _stateTokens[state] = DateTimeOffset.UtcNow.AddMinutes(10);
        PurgeExpired();

        var provider = GetProvider(providerName);
        return provider.GetAuthorizationUrl(state, redirectUri);
    }

    public bool ValidateAndConsumeState(string state)
    {
        if (!_stateTokens.TryRemove(state, out var expiry)) return false;
        return expiry > DateTimeOffset.UtcNow;
    }

    public async Task<User> AuthenticateAsync(
        string providerName, string code, string redirectUri,
        string? rawIdToken, CancellationToken ct)
    {
        var provider = GetProvider(providerName);
        var info     = await provider.ExchangeCodeAsync(code, redirectUri, rawIdToken, ct);

        _logger.LogInformation("[OAuth:{Provider}] Authenticated — sub={Sub} email={Email}",
            providerName, info.Sub, info.Email);

        await using var scope = _scopes.CreateAsyncScope();
        var loginRepo = scope.ServiceProvider.GetRequiredService<IUserExternalLoginRepository>();
        var userRepo  = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        return await UpsertUserAsync(providerName, info, loginRepo, userRepo, ct);
    }

    public string IssueExchangeToken(string jwt)
    {
        var token   = Guid.NewGuid().ToString("N");
        _exchangeTokens[token] = (jwt, DateTimeOffset.UtcNow.AddMinutes(2));
        return token;
    }

    public string? RedeemExchangeToken(string exchangeToken)
    {
        if (!_exchangeTokens.TryRemove(exchangeToken, out var entry)) return null;
        return entry.Expiry > DateTimeOffset.UtcNow ? entry.Jwt : null;
    }

    // ── Account upsert logic ──────────────────────────────────────────────────

    private static async Task<User> UpsertUserAsync(
        string providerName, ExternalUserInfo info,
        IUserExternalLoginRepository loginRepo,
        IUserRepository userRepo,
        CancellationToken ct)
    {
        // 1. Existing link?
        var existing = await loginRepo.FindAsync(providerName, info.Sub, ct);
        if (existing?.User != null)
        {
            // Update profile if provider sent fresher data
            var u = existing.User;
            bool dirty = false;
            if (info.Email       != null && u.Email       != info.Email)       { u.Email       = info.Email;       dirty = true; }
            if (info.DisplayName != null && u.DisplayName != info.DisplayName) { u.DisplayName = info.DisplayName; dirty = true; }
            if (info.AvatarUrl   != null && u.AvatarUrl   != info.AvatarUrl)   { u.AvatarUrl   = info.AvatarUrl;   dirty = true; }
            if (dirty) { u.UpdatedAt = DateTimeOffset.UtcNow; await userRepo.UpdateAsync(u, ct); }
            return u;
        }

        // 2. Account linking by email?
        User? user = null;
        if (!string.IsNullOrEmpty(info.Email))
            user = await loginRepo.FindUserByEmailAsync(info.Email, ct);

        // 3. Create new user if no existing match
        if (user is null)
        {
            user = new User
            {
                Email       = info.Email,
                DisplayName = info.DisplayName,
                AvatarUrl   = info.AvatarUrl,
                Role        = "Analyst",
                IsActive    = true,
            };
            await userRepo.CreateAsync(user, ct);
        }
        else
        {
            // Link new provider to existing account; update profile if stale
            if (info.DisplayName != null && user.DisplayName is null) user.DisplayName = info.DisplayName;
            if (info.AvatarUrl   != null && user.AvatarUrl   is null) user.AvatarUrl   = info.AvatarUrl;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await userRepo.UpdateAsync(user, ct);
        }

        await loginRepo.AddAsync(new UserExternalLogin
        {
            UserId      = user.Id,
            Provider    = providerName,
            ProviderSub = info.Sub,
            Email       = info.Email,
        }, ct);

        return user;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IExternalAuthProvider GetProvider(string providerName)
        => _providers.TryGetValue(providerName, out var p)
            ? p
            : throw new InvalidOperationException($"OAuth provider '{providerName}' is not registered or not enabled.");

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _stateTokens.Where(kv => kv.Value < now).Select(kv => kv.Key))
            _stateTokens.TryRemove(key, out _);
        foreach (var key in _exchangeTokens.Where(kv => kv.Value.Expiry < now).Select(kv => kv.Key))
            _exchangeTokens.TryRemove(key, out _);
    }
}
