using Microsoft.Extensions.DependencyInjection;

namespace rvs.AlgoTrader.Brokers.Abstractions;

public class BrokerClientFactory(IServiceProvider services) : IBrokerClientFactory
{
    private readonly Dictionary<string, IFullBrokerClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IFullBrokerClient GetClient(string brokerName)
    {
        if (_clients.TryGetValue(brokerName, out var cached)) return cached;

        var allClients = services.GetServices<IFullBrokerClient>();
        var client = allClients.FirstOrDefault(c => c.BrokerName.Equals(brokerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No broker client registered for '{brokerName}'");

        _clients[brokerName] = client;
        return client;
    }

    public IBrokerOrderClient GetOrderClient(string brokerName) => GetClient(brokerName);
    public IBrokerMarketDataClient GetMarketDataClient(string brokerName) => GetClient(brokerName);
    public IBrokerStreamClient GetStreamClient(string brokerName) => GetClient(brokerName);

    public IReadOnlyList<string> GetRegisteredBrokerNames()
        => services.GetServices<IFullBrokerClient>().Select(c => c.BrokerName).ToList();
}
