using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.Application.Commands.Broker;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Queries.Broker;

namespace rvs.AlgoTrader.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrokerController(IMediator mediator) : ControllerBase
{

    // ── Status / Latency / Funds / Positions ─────────────────────────────────

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BrokerConnectionStatusDto>>>> GetStatus(CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrokerConnectionStatusQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<BrokerConnectionStatusDto>>.Ok(result));
    }

    [HttpGet("latency")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BrokerLatencyDto>>>> GetLatency(CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrokerLatencyQuery(), ct);
        return Ok(ApiResponse<IReadOnlyList<BrokerLatencyDto>>.Ok(result));
    }

    [HttpGet("{brokerName}/funds")]
    public async Task<ActionResult<ApiResponse<BrokerFundsDto>>> GetFunds(string brokerName, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrokerFundsQuery(brokerName), ct);
        if (result is null)
            return NotFound(ApiResponse<BrokerFundsDto>.Fail("Broker funds not available — live integration not yet implemented"));
        return Ok(ApiResponse<BrokerFundsDto>.Ok(result));
    }

    [HttpGet("{brokerName}/positions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BrokerPositionDto>>>> GetPositions(string brokerName, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrokerPositionsQuery(brokerName), ct);
        return Ok(ApiResponse<IReadOnlyList<BrokerPositionDto>>.Ok(result));
    }

    [HttpPost("{brokerName}/reconcile")]
    public async Task<ActionResult<ApiResponse<bool>>> Reconcile(string brokerName, CancellationToken ct)
    {
        var result = await mediator.Send(new ReconcileBrokerPositionsCommand(brokerName), ct);
        return Ok(ApiResponse<bool>.Ok(result));
    }

    // ── mStock Type B Authentication ─────────────────────────────────────────

    /// <summary>
    /// Initiates mStock session exchange using User ID, Password, and TOTP.
    /// TOTP is a 6-digit code from Google Authenticator / any TOTP app.
    /// Never store TOTP — it expires every 30 seconds.
    /// </summary>
    [HttpPost("mstock/login")]
    [AllowAnonymous] // Pre-auth — no JWT yet
    public async Task<ActionResult<ApiResponse<BrokerAuthResultDto>>> MStockLogin(
        [FromBody] MStockLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Totp) || req.Totp.Length != 6)
            return BadRequest(ApiResponse<BrokerAuthResultDto>.Fail("TOTP must be exactly 6 digits"));

        var result = await mediator.Send(
            new AuthenticateMStockCommand(req.ApiKey, req.ClientCode, req.Password, req.Totp), ct);

        return result.Success
            ? Ok(ApiResponse<BrokerAuthResultDto>.Ok(result))
            : UnprocessableEntity(ApiResponse<BrokerAuthResultDto>.Fail(result.Message ?? "Login failed"));
    }

    // ── Zerodha OAuth ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Kite Connect login URL. Redirect the user's browser here.
    /// After login, Zerodha redirects back to your callback URL with ?request_token=xxx
    /// </summary>
    [HttpGet("zerodha/login-url")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> GetZerodhaLoginUrl(CancellationToken ct)
    {
        var url = await mediator.Send(new GetZerodhaLoginUrlQuery(), ct);
        return Ok(ApiResponse<string>.Ok(url));
    }

    /// <summary>
    /// Exchanges the request_token received from Zerodha's OAuth redirect
    /// for an access_token. Call this after the user completes login on Kite.
    /// </summary>
    [HttpPost("zerodha/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BrokerAuthResultDto>>> ZerodhaCallback(
        [FromBody] ZerodhaCallbackRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new AuthenticateZerodhaCommand(req.RequestToken), ct);
        return result.Success
            ? Ok(ApiResponse<BrokerAuthResultDto>.Ok(result))
            : UnprocessableEntity(ApiResponse<BrokerAuthResultDto>.Fail(result.Message ?? "Login failed"));
    }

    // ── Upstox OAuth2 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Upstox OAuth2 authorization URL. Redirect the user's browser here.
    /// After login, Upstox redirects back to RedirectUri with ?code=xxx
    /// </summary>
    [HttpGet("upstox/login-url")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string>>> GetUpstoxLoginUrl(CancellationToken ct)
    {
        var url = await mediator.Send(new GetUpstoxLoginUrlQuery(), ct);
        return Ok(ApiResponse<string>.Ok(url));
    }

    /// <summary>
    /// Exchanges the auth code from Upstox OAuth2 redirect for access_token + extended_token.
    /// Token expires at 3:30 AM IST the following day.
    /// </summary>
    [HttpPost("upstox/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BrokerAuthResultDto>>> UpstoxCallback(
        [FromBody] UpstoxCallbackRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new AuthenticateUpstoxCommand(req.AuthCode), ct);
        return result.Success
            ? Ok(ApiResponse<BrokerAuthResultDto>.Ok(result))
            : UnprocessableEntity(ApiResponse<BrokerAuthResultDto>.Fail(result.Message ?? "Login failed"));
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────

public record MStockLoginRequest(string ApiKey, string ClientCode, string Password, string Totp);
public record ZerodhaCallbackRequest(string RequestToken);
public record UpstoxCallbackRequest(string AuthCode);
