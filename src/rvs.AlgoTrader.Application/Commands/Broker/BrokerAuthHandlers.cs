using MediatR;
using rvs.AlgoTrader.Application.DTOs.Auth;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Application.Commands.Broker;

// ── Generic broker connect ────────────────────────────────────────────────────

public class ConnectBrokerHandler(IBrokerAuthService brokerAuth, ICurrentUser currentUser)
    : IRequestHandler<ConnectBrokerCommand, BrokerAuthResultDto>
{
    public Task<BrokerAuthResultDto> Handle(ConnectBrokerCommand cmd, CancellationToken ct)
        => brokerAuth.AuthenticateAsync(currentUser.UserId, cmd.BrokerName, cmd.Credentials, ct);
}

public class GetBrokerLoginUrlHandler(IBrokerAuthService brokerAuth, ICurrentUser currentUser)
    : IRequestHandler<GetBrokerLoginUrlQuery, string?>
{
    public Task<string?> Handle(GetBrokerLoginUrlQuery q, CancellationToken ct)
        => brokerAuth.GetLoginUrlAsync(q.BrokerName, currentUser.UserId, ct);
}

public class DisconnectBrokerHandler(IBrokerAuthService brokerAuth, ICurrentUser currentUser)
    : IRequestHandler<DisconnectBrokerCommand, bool>
{
    public async Task<bool> Handle(DisconnectBrokerCommand cmd, CancellationToken ct)
    {
        await brokerAuth.DisconnectAsync(currentUser.UserId, cmd.BrokerName, ct);
        return true;
    }
}

// ── Reconciliation ────────────────────────────────────────────────────────────

public class ReconcileBrokerPositionsHandler(IPositionReconciliationService reconciliation)
    : IRequestHandler<ReconcileBrokerPositionsCommand, bool>
{
    public async Task<bool> Handle(ReconcileBrokerPositionsCommand cmd, CancellationToken ct)
    {
        await reconciliation.ReconcileAsync(ct);
        return true;
    }
}

// ── User management ───────────────────────────────────────────────────────────

public class RegisterUserHandler(IUserRepository users, IPasswordHasher hasher, IClock clock)
    : IRequestHandler<RegisterUserCommand, RegisterResultDto>
{
    public async Task<RegisterResultDto> Handle(RegisterUserCommand cmd, CancellationToken ct)
    {
        var existing = await users.GetByUsernameAsync(cmd.Username, ct);
        if (existing != null)
            throw new InvalidOperationException($"Username '{cmd.Username}' is already taken.");

        var now = clock.NowInstant().ToDateTimeOffset();
        var user = new User
        {
            Username     = cmd.Username,
            PasswordHash = hasher.Hash(cmd.Password),
            Role         = cmd.Role,
            IsActive     = true,
            CreatedAt    = now,
            UpdatedAt    = now,
        };

        var id = await users.CreateAsync(user, ct);
        return new RegisterResultDto(id, user.Username, user.Role);
    }
}

public class AddBrokerAccountHandler(
    IUserBrokerAccountRepository accounts,
    IBrokerRepository brokerRepo,
    ICurrentUser currentUser,
    IClock clock)
    : IRequestHandler<AddBrokerAccountCommand, UserBrokerAccountDto>
{
    public async Task<UserBrokerAccountDto> Handle(AddBrokerAccountCommand cmd, CancellationToken ct)
    {
        // Resolve broker name to broker ID
        var broker = await brokerRepo.GetByNameAsync(cmd.BrokerName, ct)
            ?? throw new InvalidOperationException($"Broker '{cmd.BrokerName}' not found.");

        var now = clock.NowInstant();
        var account = new UserBrokerAccount
        {
            Id          = Guid.NewGuid(),
            UserId      = Guid.Parse(currentUser.UserId),
            BrokerId    = broker.Id,
            DisplayName = cmd.DisplayName,
            IsActive    = true,
            CreatedAt   = now.ToDateTimeOffset(),
        };
        var id = await accounts.AddAsync(account, ct);
        return new UserBrokerAccountDto(id, cmd.BrokerName, cmd.Market,
            account.DisplayName, account.IsActive, false);
    }
}

public class RemoveBrokerAccountHandler(IUserBrokerAccountRepository accounts)
    : IRequestHandler<RemoveBrokerAccountCommand, bool>
{
    public Task<bool> Handle(RemoveBrokerAccountCommand cmd, CancellationToken ct)
        => accounts.DeleteAsync(cmd.AccountId, ct);
}
