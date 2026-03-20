using rvs.AlgoTrader.Application.Services;
namespace rvs.AlgoTrader.Infrastructure.Secrets;

public sealed class EnvironmentSecretsProvider : ISecretsProvider
{
    public Task<string?> GetSecretAsync(string path, CancellationToken ct)
    {
        // Convert path like "brokers/zerodha/apikey" → "BROKERS__ZERODHA__APIKEY"
        var envKey = path.Replace("/", "__").Replace("-", "_").ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(envKey);
        return Task.FromResult(value);
    }

    public Task SetSecretAsync(string path, string value, CancellationToken ct)
    {
        // Environment provider is read-only; write is no-op (secrets set externally)
        return Task.CompletedTask;
    }
}
