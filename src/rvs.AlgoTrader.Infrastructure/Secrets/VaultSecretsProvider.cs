using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using rvs.AlgoTrader.Application.Services;
using Microsoft.Extensions.Configuration;
namespace rvs.AlgoTrader.Infrastructure.Secrets;

public sealed class VaultSecretsProvider : ISecretsProvider
{
    private readonly IVaultClient _client;

    public VaultSecretsProvider(IConfiguration config)
    {
        var addr = config["Vault:Address"] ?? "http://localhost:8200";
        var token = config["Vault:Token"] ?? "dev-root-token";
        var authMethod = new TokenAuthMethodInfo(token);
        var settings = new VaultClientSettings(addr, authMethod);
        _client = new VaultClient(settings);
    }

    public async Task<string?> GetSecretAsync(string path, CancellationToken ct)
    {
        try
        {
            // path format: "secret/data/brokers/zerodha" → key "apikey"
            var parts = path.Split('/');
            var mountPath = parts[0];
            var secretPath = string.Join("/", parts.Skip(1).SkipLast(1));
            var key = parts.Last();
            var secret = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(secretPath, mountPoint: mountPath);
            return secret.Data.Data.TryGetValue(key, out var val) ? val?.ToString() : null;
        }
        catch { return null; }
    }

    public async Task SetSecretAsync(string path, string value, CancellationToken ct)
    {
        var parts = path.Split('/');
        var mountPath = parts[0];
        var secretPath = string.Join("/", parts.Skip(1).SkipLast(1));
        var key = parts.Last();
        var data = new Dictionary<string, object> { [key] = value };
        await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(secretPath, data, mountPoint: mountPath);
    }
}
