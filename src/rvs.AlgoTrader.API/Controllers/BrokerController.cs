using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.Commands.Broker;
using rvs.AlgoTrader.Application.DTOs.Auth;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Queries.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Generic broker management controller.
/// Adding a new broker (Dhan, Alpaca, RobinHood, etc.) requires ONLY registering a new
/// IFullBrokerClient in DI — no new controller methods are needed.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BrokerController(
    IMediator mediator,
    IBrokerClientFactory brokerFactory,
    IAppBrokerSessionManager sessions,
    ICurrentUser currentUser,
    IUserBrokerAccountRepository brokerAccounts,
    IOptions<FeaturesOptions> featuresOptions) : ControllerBase
{
    private ActionResult? BrokerModeDisabledResult() =>
        featuresOptions.Value.BrokerRequired ? null
            : StatusCode(423, ApiResponse<object>.Fail(
                "Broker connections are disabled — system is in backtest-only mode."));

    // ── Broker registry ───────────────────────────────────────────────────────

    /// <summary>
    /// Lists all brokers registered in the system with their market, auth flow type,
    /// and whether the current user has an active session.
    /// Frontend uses this to render the broker connect panel dynamically —
    /// no frontend changes are needed when a new broker is added.
    /// </summary>
    [HttpGet("available")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BrokerInfoDto>>>> GetAvailable(CancellationToken ct)
    {
        var userId   = currentUser.UserId;
        var brokers  = brokerFactory.GetRegisteredBrokers();
        var result   = new List<BrokerInfoDto>();
        foreach (var b in brokers)
        {
            var connected = await sessions.IsAuthenticatedAsync(userId, b.BrokerName, ct);
            result.Add(new BrokerInfoDto(b.BrokerName, b.Market, b.AuthFlowType.ToString(), connected));
        }
        return Ok(ApiResponse<IReadOnlyList<BrokerInfoDto>>.Ok(result));
    }

    // ── User broker accounts ──────────────────────────────────────────────────

    /// <summary>Returns the broker accounts configured by the current user.</summary>
    [HttpGet("my-accounts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserBrokerAccountDto>>>> GetMyAccounts(CancellationToken ct)
    {
        var userId  = currentUser.UserId;
        var records = await brokerAccounts.GetByUserIdAsync(userId, ct);
        var result  = new List<UserBrokerAccountDto>();
        foreach (var r in records)
        {
            var connected = await sessions.IsAuthenticatedAsync(userId, r.BrokerName, ct);
            result.Add(new UserBrokerAccountDto(r.Id, r.BrokerName, r.Market, r.DisplayName, r.IsActive, connected));
        }
        return Ok(ApiResponse<IReadOnlyList<UserBrokerAccountDto>>.Ok(result));
    }

    /// <summary>Registers a broker account for the current user (does not authenticate yet).</summary>
    [HttpPost("my-accounts")]
    public async Task<ActionResult<ApiResponse<UserBrokerAccountDto>>> AddAccount(
        [FromBody] AddBrokerAccountRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(
            new AddBrokerAccountCommand(req.BrokerName, req.Market, req.DisplayName), ct);
        return Ok(ApiResponse<UserBrokerAccountDto>.Ok(result));
    }

    /// <summary>Removes a broker account entry (does not revoke the token at the broker).</summary>
    [HttpDelete("my-accounts/{accountId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> RemoveAccount(Guid accountId, CancellationToken ct)
    {
        var ok = await mediator.Send(new RemoveBrokerAccountCommand(accountId), ct);
        return Ok(ApiResponse<bool>.Ok(ok));
    }

    // ── Generic broker authentication ─────────────────────────────────────────

    /// <summary>
    /// Returns the OAuth login URL for brokers that use an OAuth flow (Zerodha, Upstox, etc.).
    /// Returns null for direct-credential brokers (MStock, Dhan, etc.) — no redirect needed.
    /// </summary>
    [HttpGet("{brokerName}/login-url")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<string?>>> GetLoginUrl(string brokerName, CancellationToken ct)
    {
        if (BrokerModeDisabledResult() is { } blocked) return blocked;
        var url = await mediator.Send(new GetBrokerLoginUrlQuery(brokerName), ct);
        return Ok(ApiResponse<string?>.Ok(url));
    }

    /// <summary>
    /// Generic broker connect endpoint — works for every broker regardless of auth flow.
    /// For DirectCredentials brokers: pass { apiKey, clientCode, password, totp }.
    /// For OAuth brokers: pass { code } or { requestToken } after the OAuth redirect completes.
    /// For ApiKey brokers: pass { apiKey, apiSecret }.
    /// </summary>
    [HttpPost("{brokerName}/connect")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<BrokerAuthResultDto>>> Connect(
        string brokerName,
        [FromBody] Dictionary<string, string> credentials,
        CancellationToken ct)
    {
        if (BrokerModeDisabledResult() is { } blocked) return blocked;
        var result = await mediator.Send(new ConnectBrokerCommand(brokerName, credentials), ct);
        return result.Success
            ? Ok(ApiResponse<BrokerAuthResultDto>.Ok(result))
            : UnprocessableEntity(ApiResponse<BrokerAuthResultDto>.Fail(result.Message ?? "Authentication failed"));
    }

    /// <summary>Disconnects the current user from the named broker (clears Redis session).</summary>
    [HttpDelete("{brokerName}/connect")]
    public async Task<ActionResult<ApiResponse<bool>>> Disconnect(string brokerName, CancellationToken ct)
    {
        var ok = await mediator.Send(new DisconnectBrokerCommand(brokerName), ct);
        return Ok(ApiResponse<bool>.Ok(ok));
    }

    // ── Status / Latency / Funds / Positions ──────────────────────────────────

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
        if (result is null) return NotFound(ApiResponse<BrokerFundsDto>.Fail("Not authenticated with this broker"));
        return Ok(ApiResponse<BrokerFundsDto>.Ok(result));
    }

    [HttpGet("{brokerName}/positions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BrokerPositionDto>>>> GetPositions(string brokerName, CancellationToken ct)
    {
        var result = await mediator.Send(new GetBrokerPositionsQuery(brokerName), ct);
        return Ok(ApiResponse<IReadOnlyList<BrokerPositionDto>>.Ok(result));
    }

    [HttpPost("{brokerName}/reconcile")]
    [Authorize(Policy = PolicyNames.RiskManager)]
    public async Task<ActionResult<ApiResponse<bool>>> Reconcile(string brokerName, CancellationToken ct)
    {
        var result = await mediator.Send(new ReconcileBrokerPositionsCommand(brokerName), ct);
        return Ok(ApiResponse<bool>.Ok(result));
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record AddBrokerAccountRequest(string BrokerName, string Market, string? DisplayName = null);
