using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.Broker;
using rvs.AlgoTrader.Application.DTOs.Auth;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;
// Alias resolves IClock to the domain interface (not NodaTime.IClock) — consistent with AP-001
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IMediator mediator,
    IConfiguration config,
    IClock clock,
    IOptions<FeaturesOptions> featuresOptions,
    IOptions<LocalAuthOptions> localAuthOptions,
    IOptions<OAuthOptions> oauthOptions,
    IUserRepository userRepo,
    IExternalAuthService externalAuth,
    IPasswordHasher hasher) : ControllerBase
{
    // ── Register ─────────────────────────────────────────────────────────────

    /// <summary>Creates a new application user. Returns user UUID — use POST /api/auth/login to get a JWT.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<RegisterResultDto>>> Register(
        [FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length < 3)
            return BadRequest(ApiResponse<RegisterResultDto>.Fail("Username must be at least 3 characters."));
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(ApiResponse<RegisterResultDto>.Fail("Password must be at least 8 characters."));
        try
        {
            // Role is always Analyst for self-registration — prevents privilege escalation.
            // Admins assign elevated roles via a separate admin-only endpoint.
            var result = await mediator.Send(
                new RegisterUserCommand(req.Username, req.Password, "Analyst"), ct);
            return Ok(ApiResponse<RegisterResultDto>.Ok(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<RegisterResultDto>.Fail(ex.Message));
        }
    }

    // ── Login (DB-backed, multi-user) ─────────────────────────────────────────

    /// <summary>
    /// Authenticates with username + password against the users table.
    /// Returns a JWT: sub = user UUID, name = username, role = user role.
    /// Each user independently connects their own broker accounts after login.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResultDto>>> Login(
        [FromBody] LocalLoginRequest req, CancellationToken ct)
    {
        var user = await userRepo.GetByUsernameAsync(req.Username, ct);
        if (user == null)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        if (string.IsNullOrEmpty(user.PasswordHash))
            return Unauthorized(ApiResponse<LoginResultDto>.Fail(
                "This account uses social login. Please sign in with your identity provider."));
        bool ok = hasher.Verify(req.Password, user.PasswordHash);
        if (!ok)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        var token = GenerateJwtToken(user.Id.ToString(), user.Username ?? user.Email ?? "user", user.Role);
        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(token, "None",
            clock.NowInstant().ToDateTimeOffset().AddHours(24))));
    }

    // ── Local config login (dev / CI) ─────────────────────────────────────────

    /// <summary>
    /// Single-user login backed by LocalAuth config section.
    /// Useful for dev mode and CI. Issues the system-user UUID as sub.
    /// </summary>
    [HttpPost("local")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LoginResultDto>> LocalLogin([FromBody] LocalLoginRequest req)
    {
        var opts = localAuthOptions.Value;
        if (!string.Equals(req.Username, opts.Username, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        bool ok;
        if (!string.IsNullOrEmpty(opts.PasswordHash))
        {
            ok = hasher.Verify(req.Password, opts.PasswordHash);
        }
        else if (!string.IsNullOrEmpty(opts.Password))
        {
            if (featuresOptions.Value.BrokerRequired)
                return StatusCode(500, ApiResponse<LoginResultDto>.Fail(
                    "LocalAuth:PasswordHash must be set when Features:BrokerRequired=true."));
            ok = req.Password == opts.Password;
        }
        else
        {
            return StatusCode(500, ApiResponse<LoginResultDto>.Fail(
                "LocalAuth not configured. Set LocalAuth:Password (dev) or LocalAuth:PasswordHash (prod)."));
        }

        if (!ok) return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        const string systemUserId = "00000000-0000-0000-0000-000000000001";
        var token = GenerateJwtToken(systemUserId, opts.Username, "Analyst");
        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(token, "None",
            clock.NowInstant().ToDateTimeOffset().AddHours(24))));
    }

    // ── Offline dev auto-login ────────────────────────────────────────────────

    [HttpPost("offline")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LoginResultDto>> OfflineLogin()
    {
        if (featuresOptions.Value.BrokerRequired)
            return StatusCode(403, ApiResponse<LoginResultDto>.Fail(
                "Offline auto-login is only available when Features:BrokerRequired=false."));
        const string systemUserId = "00000000-0000-0000-0000-000000000001";
        var token = GenerateJwtToken(systemUserId, "backtester", "Analyst");
        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(token, "None",
            clock.NowInstant().ToDateTimeOffset().AddHours(24))));
    }

    // ── Login (DB-backed, multi-user) — update for nullable Username ─────────

    // ── OAuth provider endpoints ──────────────────────────────────────────────

    /// <summary>
    /// Returns the list of OAuth providers that are enabled in config.
    /// The login page calls this once and renders a button for each entry.
    /// Disabling a provider in config hides its button automatically — no frontend changes needed.
    /// </summary>
    [HttpGet("providers")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<IReadOnlyList<ProviderInfoDto>>> GetProviders()
        => Ok(ApiResponse<IReadOnlyList<ProviderInfoDto>>.Ok(externalAuth.GetEnabledProviders()));

    /// <summary>
    /// Returns the authorization URL the browser should navigate to for the given provider.
    /// The redirect_uri must be registered in the OAuth provider's dashboard.
    /// For local dev: http://localhost:62318/api/auth/oauth/{provider}/callback
    /// </summary>
    [HttpGet("oauth/{provider}/login-url")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<string>> GetOAuthLoginUrl(string provider)
    {
        var redirectUri = BuildBackendCallbackUri(provider);
        var url         = externalAuth.GenerateLoginUrl(provider, redirectUri, out _);
        return Ok(ApiResponse<string>.Ok(url));
    }

    /// <summary>
    /// OAuth callback for Google and Microsoft (GET with ?code=...&amp;state=...).
    /// Exchanges the code, creates/links the user account, issues a short-lived
    /// exchange token and redirects the browser to the frontend callback page.
    /// The JWT never appears in a URL — the frontend redeems the exchange token separately.
    /// </summary>
    [HttpGet("oauth/{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback(
        string provider, string? code, string? state, string? error, CancellationToken ct)
    {
        var frontend = oauthOptions.Value.FrontendBaseUrl.TrimEnd('/');
        if (!string.IsNullOrEmpty(error))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error={Uri.EscapeDataString(error)}");

        if (string.IsNullOrEmpty(code))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error=missing_code");

        if (string.IsNullOrEmpty(state) || !externalAuth.ValidateAndConsumeState(state))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error=invalid_state");

        try
        {
            var redirectUri   = BuildBackendCallbackUri(provider);
            var user          = await externalAuth.AuthenticateAsync(provider, code, redirectUri, null, ct);
            var jwt           = GenerateJwtToken(user.Id.ToString(), user.DisplayName ?? user.Email ?? "User", user.Role);
            var exchangeToken = externalAuth.IssueExchangeToken(jwt);
            return Redirect($"{frontend}/auth/callback?provider={provider}&exchange={exchangeToken}");
        }
        catch (Exception ex)
        {
            var msg = Uri.EscapeDataString(ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            return Redirect($"{frontend}/auth/callback?provider={provider}&error={msg}");
        }
    }

    /// <summary>
    /// Apple-specific callback: Apple uses response_mode=form_post so this is a POST endpoint.
    /// The body contains code, state, id_token, and optionally a "user" JSON field (first login only).
    /// </summary>
    [HttpPost("oauth/apple/callback")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> AppleCallback(
        [FromForm] string? code,
        [FromForm] string? state,
        [FromForm(Name = "id_token")] string? idToken,
        [FromForm] string? error,
        CancellationToken ct)
    {
        const string provider = "Apple";
        var frontend = oauthOptions.Value.FrontendBaseUrl.TrimEnd('/');

        if (!string.IsNullOrEmpty(error))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error={Uri.EscapeDataString(error)}");

        if (string.IsNullOrEmpty(code))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error=missing_code");

        if (string.IsNullOrEmpty(state) || !externalAuth.ValidateAndConsumeState(state))
            return Redirect($"{frontend}/auth/callback?provider={provider}&error=invalid_state");

        try
        {
            var redirectUri   = BuildBackendCallbackUri(provider);
            var user          = await externalAuth.AuthenticateAsync(provider, code, redirectUri, idToken, ct);
            var jwt           = GenerateJwtToken(user.Id.ToString(), user.DisplayName ?? user.Email ?? "User", user.Role);
            var exchangeToken = externalAuth.IssueExchangeToken(jwt);
            return Redirect($"{frontend}/auth/callback?provider={provider}&exchange={exchangeToken}");
        }
        catch (Exception ex)
        {
            var msg = Uri.EscapeDataString(ex.Message.Length > 120 ? ex.Message[..120] : ex.Message);
            return Redirect($"{frontend}/auth/callback?provider={provider}&error={msg}");
        }
    }

    /// <summary>
    /// Redeems a one-time exchange token (2-minute TTL) for the actual JWT.
    /// Called by the frontend's /auth/callback page immediately after the OAuth redirect.
    /// This is the only point where the JWT crosses the wire; it is never in a URL.
    /// </summary>
    [HttpPost("oauth/{provider}/redeem")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LoginResultDto>> Redeem(string provider, [FromBody] RedeemRequest req)
    {
        var jwt = externalAuth.RedeemExchangeToken(req.ExchangeToken);
        if (jwt is null)
            return BadRequest(ApiResponse<LoginResultDto>.Fail("Exchange token is invalid or has expired. Please sign in again."));

        return Ok(ApiResponse<LoginResultDto>.Ok(
            new LoginResultDto(jwt, provider, clock.NowInstant().ToDateTimeOffset().AddHours(24))));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the backend OAuth callback URL from the current request host.
    /// This must match exactly what is registered in the provider's developer console.
    /// </summary>
    private string BuildBackendCallbackUri(string provider)
    {
        var scheme = Request.Scheme;
        var host   = Request.Host.Value;
        // Apple requires lowercase provider name in the path; all others accept either.
        return $"{scheme}://{host}/api/auth/oauth/{provider.ToLower()}/callback";
    }

    // ── JWT generation ────────────────────────────────────────────────────────

    private string GenerateJwtToken(string userId, string username, string role)
    {
        var secret = config["JWT__SECRET"] ?? throw new InvalidOperationException("JWT__SECRET not configured");
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds  = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),    // sub = user UUID
            new Claim(ClaimTypes.Name,           username),  // name = display name
            new Claim("role",                    role),
        };
        var token = new JwtSecurityToken(null, null, claims,
            expires: clock.NowInstant().Plus(Duration.FromHours(24)).ToDateTimeUtc(),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record LocalLoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string? Role = null);
public record RedeemRequest(string ExchangeToken);
