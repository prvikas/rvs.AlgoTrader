using rvs.AlgoTrader.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace rvs.AlgoTrader.Infrastructure.Secrets;

public sealed class SecretsProviderFactory : ISecretsProviderFactory
{
    private readonly IServiceProvider _sp;
    private readonly string _providerType;

    public SecretsProviderFactory(IServiceProvider sp, IConfiguration config)
    {
        _sp = sp;
        _providerType = config["Secrets:Provider"] ?? "environment";
    }

    public ISecretsProvider Create() => _providerType.ToLowerInvariant() switch
    {
        "vault" => _sp.GetRequiredService<VaultSecretsProvider>(),
        _ => _sp.GetRequiredService<EnvironmentSecretsProvider>()
    };
}
