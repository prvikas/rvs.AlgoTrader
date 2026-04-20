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
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Options;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IMediator mediator,
    IConfiguration config,
    IClock clock,
    IOptions<FeaturesOptions> featuresOptions,
    IOptions<LocalAuthOptions> localAuthOptions) : ControllerBase
{
    // ── Local (username/password) login ──────────────────────────────────────

    /// <summary>
    /// Local username/password login.  Returns a signed JWT when credentials match
    /// the <c>LocalAuth</c> config section.
    ///
    /// Available in all modes.  When <c>Features:BrokerRequired=false</c> this is the
    /// primary login path.  When broker mode is active, this endpoint still works and
    /// issues an Analyst-role token — useful for CI and headless backtest jobs.
    /// </summary>
    [HttpPost("local")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LoginResultDto>> LocalLogin(
        [FromBody] LocalLoginRequest req)
    {
        var opts = localAuthOptions.Value;

        // Username is case-insensitive
        if (!string.Equals(req.Username, opts.Username, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        // Password verification — prefer BCrypt hash if configured, fall back to plaintext
        bool passwordOk;
        if (!string.IsNullOrEmpty(opts.PasswordHash))
        {
            try { passwordOk = BCrypt.Net.BCrypt.Verify(req.Password, opts.PasswordHash); }
            catch { passwordOk = false; }
        }
        else if (!string.IsNullOrEmpty(opts.Password))
        {
            // Plaintext comparison — acceptable for dev / broker-free mode.
            // Warn if used in a broker-required (production-like) environment.
            if (featuresOptions.Value.BrokerRequired)
            {
                return StatusCode(500, ApiResponse<LoginResultDto>.Fail(
                    "LocalAuth:PasswordHash must be set when Features:BrokerRequired=true. " +
                    "Use BCrypt.Net.BCrypt.HashPassword(\"yourpassword\") to generate a hash."));
            }
            passwordOk = req.Password == opts.Password;
        }
        else
        {
            return StatusCode(500, ApiResponse<LoginResultDto>.Fail(
                "LocalAuth is not configured. Set LocalAuth:Password (dev) or LocalAuth:PasswordHash (prod) in config."));
        }

        if (!passwordOk)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        var jwtToken = GenerateJwtToken(opts.Username, role: "Analyst");

        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(
            Token: jwtToken,
            BrokerName: "None",
            ExpiresAt: clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24)
        )));
    }

    // ── MStock broker login ──────────────────────────────────────────────────

    [HttpPost("mstock/login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<LoginResultDto>>> MStockLogin(
        [FromBody] MStockLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Totp) || req.Totp.Length != 6)
            return BadRequest(ApiResponse<LoginResultDto>.Fail("TOTP must be exactly 6 digits"));

        // Authenticate with MStock broker
        var brokerResult = await mediator.Send(
            new AuthenticateMStockCommand(req.ApiKey, req.ClientCode, req.Password, req.Totp), ct);

        if (!brokerResult.Success)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail(brokerResult.Message ?? "Authentication failed"));

        // Generate app JWT token
        var jwtToken = GenerateJwtToken(brokerResult.BrokerName, role: "Trader");

        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(
            Token: jwtToken,
            BrokerName: brokerResult.BrokerName,
            ExpiresAt: clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24)
        )));
    }

    // ── Offline auto-login (legacy — kept for backward compat) ──────────────

    /// <summary>
    /// Backtest-only auto-login (no credentials required).
    /// Returns HTTP 403 in normal (broker-required) mode.
    /// Deprecated: prefer POST /api/auth/local with explicit credentials.
    /// </summary>
    [HttpPost("offline")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<LoginResultDto>> OfflineLogin()
    {
        if (featuresOptions.Value.BrokerRequired)
            return StatusCode(403, ApiResponse<LoginResultDto>.Fail(
                "Offline auto-login is only available when Features:BrokerRequired=false."));

        var jwtToken = GenerateJwtToken("backtester", role: "Analyst");

        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(
            Token: jwtToken,
            BrokerName: "None",
            ExpiresAt: clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24)
        )));
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private string GenerateJwtToken(string subject, string role)
    {
        var jwtSecret = config["JWT__SECRET"] ?? throw new InvalidOperationException("JWT__SECRET not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(ClaimTypes.Name, subject),
            new Claim("broker", subject),
            new Claim("role", role),
        };

        var token = new JwtSecurityToken(
            issuer: null,
            audience: null,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/auth/local</summary>
public record LocalLoginRequest(string Username, string Password);

// LoginResultDto is defined in Application/DTOs/Auth/AuthDtos.cs
