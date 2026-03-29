using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using rvs.AlgoTrader.Application.Commands.Broker;
using rvs.AlgoTrader.Application.DTOs.Auth;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.DTOs.Common;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IMediator mediator, IConfiguration config) : ControllerBase
{
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
        var jwtToken = GenerateJwtToken(brokerResult.BrokerName);

        return Ok(ApiResponse<LoginResultDto>.Ok(new LoginResultDto(
            Token: jwtToken,
            BrokerName: brokerResult.BrokerName,
            ExpiresAt: DateTimeOffset.UtcNow.AddHours(24)
        )));
    }

    private string GenerateJwtToken(string brokerName)
    {
        var jwtSecret = config["JWT__SECRET"] ?? throw new InvalidOperationException("JWT__SECRET not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, brokerName),
            new Claim(ClaimTypes.Name, brokerName),
            new Claim("broker", brokerName)
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

// LoginResultDto is defined in Application/DTOs/Auth/AuthDtos.cs
