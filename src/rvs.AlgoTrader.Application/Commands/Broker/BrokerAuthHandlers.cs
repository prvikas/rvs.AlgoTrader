using MediatR;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Commands.Broker;

// ── mStock ────────────────────────────────────────────────────────────────────
public class AuthenticateMStockHandler(IBrokerAuthService brokerAuth) : IRequestHandler<AuthenticateMStockCommand, BrokerAuthResultDto>
{
    public Task<BrokerAuthResultDto> Handle(AuthenticateMStockCommand cmd, CancellationToken ct)
        => brokerAuth.AuthenticateMStockAsync(cmd.ApiKey, cmd.ClientCode, cmd.Password, cmd.Totp, ct);
}

// ── Zerodha ───────────────────────────────────────────────────────────────────
public class GetZerodhaLoginUrlHandler(IBrokerAuthService brokerAuth) : IRequestHandler<GetZerodhaLoginUrlQuery, string>
{
    public Task<string> Handle(GetZerodhaLoginUrlQuery _, CancellationToken ct)
        => brokerAuth.GetZerodhaLoginUrlAsync(ct);
}

public class AuthenticateZerodhaHandler(IBrokerAuthService brokerAuth) : IRequestHandler<AuthenticateZerodhaCommand, BrokerAuthResultDto>
{
    public Task<BrokerAuthResultDto> Handle(AuthenticateZerodhaCommand cmd, CancellationToken ct)
        => brokerAuth.AuthenticateZerodhaAsync(cmd.RequestToken, ct);
}

// ── Upstox ────────────────────────────────────────────────────────────────────
public class GetUpstoxLoginUrlHandler(IBrokerAuthService brokerAuth) : IRequestHandler<GetUpstoxLoginUrlQuery, string>
{
    public Task<string> Handle(GetUpstoxLoginUrlQuery _, CancellationToken ct)
        => brokerAuth.GetUpstoxLoginUrlAsync(ct);
}

public class AuthenticateUpstoxHandler(IBrokerAuthService brokerAuth) : IRequestHandler<AuthenticateUpstoxCommand, BrokerAuthResultDto>
{
    public Task<BrokerAuthResultDto> Handle(AuthenticateUpstoxCommand cmd, CancellationToken ct)
        => brokerAuth.AuthenticateUpstoxAsync(cmd.AuthCode, ct);
}

public class ReconcileBrokerPositionsHandler(IPositionReconciliationService reconciliation) : IRequestHandler<ReconcileBrokerPositionsCommand, bool>
{
    public async Task<bool> Handle(ReconcileBrokerPositionsCommand cmd, CancellationToken ct)
    {
        await reconciliation.ReconcileAsync(ct);
        return true;
    }
}
