using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Singleton service that orchestrates the OAuth 2.0 authorization code flow:
///   1. Resolves the correct IExternalAuthProvider by name (no switch statements).
///   2. Manages CSRF state tokens (10-minute TTL).
///   3. Manages one-time exchange tokens (2-minute TTL) so the JWT never appears in a URL.
///   4. Creates / links / updates application user accounts via IUserExternalLoginRepository
///      using a short-lived DI scope (DbContext is scoped, this service is singleton).
///
/// In-memory state dicts are intentional — state tokens have a 2–10 minute lifetime
/// and the callback hits the same process that issued the login URL.
/// For multi-replica deployment: swap the ConcurrentDictionary stores for Redis SETNX.
///
/// The enabled provider list is built at construction time from each provider's own
/// IsEnabled property — no reflection, no hardcoded name arrays, no ProviderMeta dict.
/// Adding a new provider requires only: implement IExternalAuthProvider + register in DI.
/// </summary>
public sealed class ExternalAuthService : IExternalAuthService
{
    private readonly IReadOnlyDictionary<string, IExternalAuthProvider> _providers;
    private readonly IReadOnlyList<ProviderInfoDto>                      _enabledProviders;
    private readonly IServiceScopeFactory                                _scopes;
    private readonly IClock                                              _clock;
    private readonly ILogger<ExternalAuthService>                        _logger;

    // ── In-memory state stores ────────────────────────────────────────────────

    // CSRF state: state-token → expiry
    private readonly ConcurrentDictionary<string, DateTimeOffset> _stateTokens   = new();
    // Exchange tokens: exchange-token → (jwt, expiry)
    private readonly ConcurrentDictionary<string, (string Jwt, DateTimeOffset Expiry)> _exchangeTokens = new();

    public ExternalAuthService(
        IEnumerable<IExternalAuthProvider> providers,
        IServiceScopeFactory scopes,
        IClock clock,
        ILogger<ExternalAuthService> logger)
    {
        _scopes  = scopes;
        _clock   = clock;
        _logger  = logger;

        // Each provider self-declares IsEnabled, DisplayName, and IconKey.
        // No reflection, no hardcoded arrays. OCP: adding a new provider requires zero changes here.
        var all = providers.ToList();
        _providers = all.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
        _enabledProviders = all
            .Where(p => p.IsEnabled)
            .Select(p => new ProviderInfoDto(p.ProviderName, p.DisplayName, p.IconKey))
            .ToList()
            .AsReadOnly();
    }

    // ── IExternalAuthService ──────────────────────────────────────────────────

    public IReadOnlyList<ProviderInfoDto> GetEnabledProviders() => _enabledProviders;

    public string GenerateLoginUrl(string providerName, string redirectUri, out string state)
    {
        state = Guid.NewGuid().ToString("N");
        var expiry = _clock.NowInstant().ToDateTimeOffset().AddMinutes(10);
        _stateTokens[state] = expiry;
        PurgeExpired();

        return GetProvider(providerName).GetAuthorizationUrl(state, redirectUri);
    }

    public bool ValidateAndConsumeState(string state)
    {
        if (!_stateTokens.TryRemove(state, out var expiry)) return false;
        return expiry > _clock.NowInstant().ToDateTimeOffset();
    }

    public async Task<User> AuthenticateAsync(
        string providerName, string code, string redirectUri,
        string? rawIdToken, CancellationToken ct)
    {
        var provider = GetProvider(providerName);
        var info     = await provider.ExchangeCodeAsync(code, redirectUri, rawIdToken, ct);

        _logger.LogInformation("[OAuth:{Provider}] Authenticated — sub={Sub} email={Email}",
            providerName, info.Sub, info.Email);

        await using var scope    = _scopes.CreateAsyncScope();
        var loginRepo = scope.ServiceProvider.GetRequiredService<IUserExternalLoginRepository>();
        var userRepo  = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        return await UpsertUserAsync(providerName, info, loginRepo, userRepo, _clock, ct);
    }

    public string IssueExchangeToken(string jwt)
    {
        var token  = Guid.NewGuid().ToString("N");
        var expiry = _clock.NowInstant().ToDateTimeOffset().AddMinutes(2);
        _exchangeTokens[token] = (jwt, expiry);
        return token;
    }

    public string? RedeemExchangeToken(string exchangeToken)
    {
        if (!_exchangeTokens.TryRemove(exchangeToken, out var entry)) return null;
        return entry.Expiry > _clock.NowInstant().ToDateTimeOffset() ? entry.Jwt : null;
    }

    // ── Account upsert logic ──────────────────────────────────────────────────

    private static async Task<User> UpsertUserAsync(
        string providerName, ExternalUserInfo info,
        IUserExternalLoginRepository loginRepo,
        IUserRepository userRepo,
        IClock clock,
        CancellationToken ct)
    {
        var now = clock.NowInstant().ToDateTimeOffset();

        // 1. Existing link for this provider + sub?
        var existingLink = await loginRepo.FindAsync(providerName, info.Sub, ct);
        if (existingLink?.User != null)
        {
            var u     = existingLink.User;
            bool dirty = false;
            if (info.Email       != null && u.Email       != info.Email)       { u.Email       = info.Email;       dirty = true; }
            if (info.DisplayName != null && u.DisplayName != info.DisplayName) { u.DisplayName = info.DisplayName; dirty = true; }
            if (info.AvatarUrl   != null && u.AvatarUrl   != info.AvatarUrl)   { u.AvatarUrl   = info.AvatarUrl;   dirty = true; }
            if (dirty) { u.UpdatedAt = now; await userRepo.UpdateAsync(u, ct); }
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
                CreatedAt   = now,
                UpdatedAt   = now,
            };
            await userRepo.CreateAsync(user, ct);
        }
        else
        {
            // Link new provider to existing account; fill in missing profile fields
            if (info.DisplayName != null && user.DisplayName is null) user.DisplayName = info.DisplayName;
            if (info.AvatarUrl   != null && user.AvatarUrl   is null) user.AvatarUrl   = info.AvatarUrl;
            user.UpdatedAt = now;
            await userRepo.UpdateAsync(user, ct);
        }

        // 4. Create the external login link
        await loginRepo.AddAsync(new UserExternalLogin
        {
            UserId      = user.Id,
            Provider    = providerName,
            ProviderSub = info.Sub,
            Email       = info.Email,
            CreatedAt   = now,
        }, ct);

        return user;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IExternalAuthProvider GetProvider(string providerName)
        => _providers.TryGetValue(providerName, out var p)
            ? p
            : throw new InvalidOperationException(
                $"OAuth provider '{providerName}' is not registered or not enabled.");

    /// <summary>
    /// Removes expired tokens from the in-memory dicts.
    /// Called on every login URL generation — frequency is naturally rate-limited.
    /// </summary>
    private void PurgeExpired()
    {
        var now = _clock.NowInstant().ToDateTimeOffset();
        foreach (var key in _stateTokens.Where(kv => kv.Value < now).Select(kv => kv.Key))
            _stateTokens.TryRemove(key, out _);
        foreach (var key in _exchangeTokens.Where(kv => kv.Value.Expiry < now).Select(kv => kv.Key))
            _exchangeTokens.TryRemove(key, out _);
    }
}
