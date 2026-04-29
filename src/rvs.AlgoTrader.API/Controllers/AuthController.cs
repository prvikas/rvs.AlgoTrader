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

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IMediator mediator,
    IConfiguration config,
    IClock clock,
    IOptions<FeaturesOptions> featuresOptions,
    IOptions<LocalAuthOptions> localAuthOptions,
    IUserRepository userRepo) : ControllerBase
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
            var result = await mediator.Send(
                new RegisterUserCommand(req.Username, req.Password, req.Role ?? "Analyst"), ct);
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

        bool ok;
        try { ok = BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash); }
        catch { ok = false; }
        if (!ok)
            return Unauthorized(ApiResponse<LoginResultDto>.Fail("Invalid username or password"));

        var token = GenerateJwtToken(user.Id.ToString(), user.Username, user.Role);
        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(token, "None",
            clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24))));
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
            try { ok = BCrypt.Net.BCrypt.Verify(req.Password, opts.PasswordHash); } catch { ok = false; }
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
            clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24))));
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
            clock.GetCurrentInstant().ToDateTimeOffset().AddHours(24))));
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
            expires: clock.GetCurrentInstant().Plus(Duration.FromHours(24)).ToDateTimeUtc(),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record LocalLoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string? Role = null);
