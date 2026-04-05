using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MassTransit;
using Hangfire;
using Hangfire.PostgreSql;
using StackExchange.Redis;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Brokers.MStock;
using rvs.AlgoTrader.Brokers.MStock.Auth;
using rvs.AlgoTrader.Brokers.Zerodha;
using rvs.AlgoTrader.Brokers.Zerodha.Auth;
using rvs.AlgoTrader.Brokers.Upstox;
using rvs.AlgoTrader.Brokers.Upstox.Auth;
using rvs.AlgoTrader.Infrastructure.Clock;
using rvs.AlgoTrader.Infrastructure.Secrets;
using rvs.AlgoTrader.Infrastructure.Messaging.Consumers;
using rvs.AlgoTrader.Infrastructure.Persistence;
using rvs.AlgoTrader.Infrastructure.Redis;
using rvs.AlgoTrader.Infrastructure.Repositories;
using rvs.AlgoTrader.Infrastructure.Services;
namespace rvs.AlgoTrader.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration config,
        Action<IBusRegistrationConfigurator>? configureAdditionalConsumers = null)
    {
        // Clock — production singleton
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<NodaTime.IClock>(NodaTime.SystemClock.Instance);

        // Migration runner — auto-applies all numbered *.sql files on startup
        services.AddScoped<rvs.AlgoTrader.Infrastructure.Persistence.DatabaseMigrationRunner>();

        // EF Core — PostgreSQL
        services.AddDbContext<AlgoTraderDbContext>(opts =>
            opts.UseNpgsql(
                config.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres",
                npgsql => npgsql.UseNodaTime()));

        // Redis — optional. Try to connect; fall back to in-memory implementations if unavailable.
        var redisConnectionString = config.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false,connectTimeout=2000,syncTimeout=2000";
        bool redisAvailable = false;
        try
        {
            var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ConnectTimeout = 2000;
            redisConfig.SyncTimeout = 2000;
            var mux = ConnectionMultiplexer.Connect(redisConfig);
            // Verify connection is actually working by pinging
            var db = mux.GetDatabase();
            db.Ping();
            redisAvailable = true;
            services.AddSingleton<IConnectionMultiplexer>(mux);
        }
        catch (Exception)
        {
            // Redis not available — register a dummy multiplexer placeholder so services that
            // optionally depend on it can still resolve. Redis-backed services are replaced below.
            redisAvailable = false;
        }

        // Application services
        services.AddSingleton<IFieldEncryptionService, FieldEncryptionService>();

        if (redisAvailable)
        {
            services.AddScoped<IIdempotencyService, IdempotencyService>();
            services.AddScoped<IKillSwitchService, KillSwitchService>();
            services.AddScoped<ICapitalAllocator, RedisCapitalAllocator>();
            services.AddScoped<ICandleCache, CandleCache>();
        }
        else
        {
            // In-memory fallbacks when Redis is not running.
            // WARN: state is lost on restart — kill switch and idempotency are non-persistent.
            var env = config["ASPNETCORE_ENVIRONMENT"] ?? config["Environment"] ?? "Development";
            if (env is "Production" or "Staging")
            {
                throw new InvalidOperationException(
                    "Redis is required in Production/Staging. In-memory fallbacks must not run in production " +
                    "— they lose kill-switch state and idempotency keys on restart. " +
                    "Set the Redis connection string in ConnectionStrings:Redis.");
            }
            services.AddSingleton<IIdempotencyService, InMemoryIdempotencyService>();
            services.AddSingleton<IKillSwitchService, InMemoryKillSwitchService>();
            services.AddSingleton<ICapitalAllocator, InMemoryCapitalAllocator>();
            services.AddSingleton<ICandleCache, InMemoryCandleCache>();
        }

        services.AddScoped<IAuditService, AuditService>();

        if (redisAvailable)
            services.AddScoped<IAppConfigService, AppConfigService>();
        else
            services.AddSingleton<IAppConfigService, InMemoryAppConfigService>();
        services.AddScoped<ITransactionCostCalculator, TransactionCostCalculator>();
        services.AddScoped<ITrailingStopLossService, TrailingStopLossService>();
        if (redisAvailable)
            services.AddScoped<IRiskManagementService, RiskManagementService>();
        else
            services.AddSingleton<IRiskManagementService, InMemoryRiskManagementService>();
        services.AddScoped<IForwardTestFillSimulator, ForwardTestFillSimulator>();
        services.AddScoped<ILiveExecutionEngine, LiveExecutionEngine>();
        services.AddScoped<IHistoricalDownloadService, HistoricalDownloadService>();
        services.AddScoped<IInstrumentRefreshService, InstrumentRefreshService>();
        services.AddScoped<IStrategyInstanceManager, StrategyInstanceManager>();
        services.AddScoped<IPositionReconciliationService, PositionReconciliationService>();
        services.AddSingleton<ISymbolDataPreferencesService, SymbolDataPreferencesService>();
        services.AddScoped<IMarketCalendarService, MarketCalendarService>();
        services.AddScoped<IMarketBreadthService, MarketBreadthService>();
        services.AddScoped<IEventCalendarService, EventCalendarService>();
        services.AddScoped<IHistoricalDataManager, HistoricalDataManager>();
        services.AddSingleton<IDataFeedHealthMonitor, DataFeedHealthMonitor>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IIndicatorService, IndicatorService>();

        // Option chain service — pre-fetches OC snapshot before strategy evaluation
        // Scoped so each request gets its own in-process cache entry; a Redis-backed
        // singleton would be preferred in multi-replica deployments.
        services.AddScoped<IOptionChainService, OptionChainService>();

        // Strategy scheduler — evaluates session windows; uses IMarketCalendarService + IClock
        services.AddScoped<IStrategyScheduler, StrategyScheduler>();

        // Startup orchestrator
        services.AddScoped<IStartupOrchestrator, StartupOrchestrator>();

        // Backtest service stubs (replace with real implementations later)
        services.AddScoped<IBacktestService, BacktestService>();
        // #137: Pre-market readiness check
        services.AddScoped<IPreMarketReadinessService, PreMarketReadinessService>();
        // #136: SLO tracker — singleton so counters accumulate across requests
        services.AddSingleton<SloTracker>();
        services.AddScoped<IBacktestReproductionService, BacktestReproductionService>();

        // Secrets + Token Store
        services.AddSingleton<EnvironmentSecretsProvider>();
        services.AddSingleton<VaultSecretsProvider>();
        services.AddSingleton<ISecretsProviderFactory, SecretsProviderFactory>();
        // #129: Encrypted token store — stores broker JWTs at rest using AES-256-GCM.
        // Requires "TokenStore:EncryptionKey" (base64 32-byte key) via env var / Vault.
        // Falls back silently when Redis is unavailable (registered only when Redis is up).
        if (redisAvailable)
            services.AddSingleton<ITokenStore, RedisEncryptedTokenStore>();

        // ── Repository registrations ──────────────────────────────────────────
        // Existing EF Core implementations
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<IInstrumentUniverseRepository, InstrumentUniverseRepository>();
        services.AddScoped<IStrategyInstanceRepository, StrategyInstanceRepository>();
        services.AddScoped<IStrategyRunRepository, StrategyRunRepository>();
        services.AddScoped<ICandleRepository, CandleRepository>();

        // EF Core implementations (backed by migration 010_StubRepositoriesSchema.sql)
        services.AddScoped<IAuditLogRepository, EfAuditLogRepository>();
        services.AddScoped<IAlertLogRepository, EfAlertLogRepository>();
        services.AddScoped<IDownloadJobRepository, EfDownloadJobRepository>();
        services.AddScoped<ISignalJournalRepository, EfSignalJournalRepository>();
        services.AddScoped<ICapitalAllocationRepository, EfCapitalAllocationRepository>();
        services.AddScoped<IUserPreferencesRepository, EfUserPreferencesRepository>();
        services.AddScoped<IWatchlistRepository, EfWatchlistRepository>();

        // Remaining in-memory implementations (no DB table required)
        services.AddSingleton<IAppConfigRepository, AppConfigRepository>();
        services.AddScoped<IBrokerLatencyRepository, BrokerLatencyRepository>();
        services.AddScoped<IBacktestCostProfileRepository, BacktestCostProfileRepository>();

        services.AddScoped<IBacktestRunRepository, BacktestRunRepository>();
        services.AddScoped<IForwardTestSessionRepository, ForwardTestSessionRepository>();
        services.AddScoped<IForwardTestTradeRepository, ForwardTestTradeRepository>();
        services.AddScoped<IStrategyScenarioRepository, StrategyScenarioRepository>();

        // ── Options engine (#68, #65, #72, #73, #84) ────────────────────────
        services.AddSingleton<IBlackScholesEngine, BlackScholesEngine>();
        services.AddScoped<IOptionLegSelector, OptionLegSelector>();
        services.AddScoped<IOptionIvRankService, OptionIvRankService>();
        services.AddScoped<IOptionIvHistoryRepository, EfOptionIvHistoryRepository>();
        // IOrderManager is Scoped (not Singleton) because OrderManager depends on
        // IOrderRepository which is Scoped (EF Core-backed). A Singleton cannot
        // consume a Scoped service — doing so causes AggregateException at startup.
        services.AddScoped<IOrderManager, OrderManager>();
        services.AddScoped<ISpreadOrderManager, SpreadOrderManager>();
        services.AddScoped<ISpreadPositionRepository, EfSpreadPositionRepository>();

        // ── FX rates (#64) ───────────────────────────────────────────────────
        services.AddScoped<IFxRateProvider, EfFxRateProvider>();

        // ── Trade Journal (v0.8) ─────────────────────────────────────────────
        services.AddScoped<ITradeJournalRepository, EfTradeJournalRepository>();
        services.AddScoped<ITaxLotReportService, TaxLotReportService>();

        // ── P4 Approval Gate ─────────────────────────────────────────────────
        services.AddScoped<IStrategyApprovalRepository, StrategyApprovalRepository>();
        services.AddScoped<IApprovalService, ApprovalService>();

        // ── Risk profiles ─────────────────────────────────────────────────────
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();

        // ── Risk & Execution Engine (#85, #86, #87, #88, #92, #93, #100) ──
        services.AddSingleton<IPositionSizingEngine, PositionSizingEngine>();
        services.AddSingleton<ISlippageModel, SlippageModel>();
        services.AddSingleton<ICommissionModel, IndianMarketCommissionModel>();
        services.AddScoped<IPortfolioRiskManager, PortfolioRiskManager>();
        services.AddScoped<IPaperOrderSimulator, PaperOrderSimulator>();
        services.AddSingleton<ITrailingStopManager, TrailingStopManager>();
        services.AddSingleton<IScalingManager, ScalingManager>();
        services.AddSingleton<IStrategyCorrelationAnalyser, StrategyCorrelationAnalyser>();

        // Broker HTTP clients (typed clients via IHttpClientFactory)
        // #134: Wire Polly retry (3 attempts, exponential back-off) + circuit breaker
        // (50% failure ratio, 30s window, 10-request minimum) on all 6 broker clients.
        // Policies are shared singletons so circuit-breaker state is preserved across requests.
        var brokerRetry          = BrokerResiliencePolicies.Retry;
        var brokerCircuitBreaker = BrokerResiliencePolicies.CircuitBreaker;
        services.AddHttpClient<MStockAuth>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);
        services.AddHttpClient<MStockClient>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);
        services.AddHttpClient<ZerodhaAuth>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);
        services.AddHttpClient<ZerodhaClient>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);
        services.AddHttpClient<UpstoxAuth>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);
        services.AddHttpClient<UpstoxClient>()
            .AddPolicyHandler(brokerRetry).AddPolicyHandler(brokerCircuitBreaker);

        // Broker options from config
        services.Configure<MStockOptions>(config.GetSection("Broker:MStock"));
        services.Configure<ZerodhaOptions>(config.GetSection("Broker:Zerodha"));
        services.Configure<UpstoxOptions>(config.GetSection("Broker:Upstox"));

        // Broker clients as IFullBrokerClient — delegate to typed HttpClient registrations
        // (AddHttpClient<T>() registers T as transient with a managed HttpClient;
        //  we expose each as IFullBrokerClient so BrokerClientFactory.GetServices works)
        services.AddSingleton<IBrokerClientFactory, BrokerClientFactory>();
        services.AddTransient<IFullBrokerClient>(sp => sp.GetRequiredService<MStockClient>());
        services.AddTransient<IFullBrokerClient>(sp => sp.GetRequiredService<ZerodhaClient>());
        services.AddTransient<IFullBrokerClient>(sp => sp.GetRequiredService<UpstoxClient>());

        // DB-backed session persistence — used by InMemoryBrokerSessionManager as write-through
        // backing store so tokens survive app restarts when Redis is unavailable.
        // Registered regardless of Redis availability (StartupOrchestrator uses it only when needed).
        services.AddSingleton<DbBrokerSessionPersistence>();

        // Broker session manager — use in-memory when Redis is not available
        if (redisAvailable)
        {
            services.AddScoped<BrokerSessionManager>();
            services.AddScoped<rvs.AlgoTrader.Brokers.Abstractions.IBrokerSessionManager>(
                sp => sp.GetRequiredService<BrokerSessionManager>());
            services.AddScoped<IAppBrokerSessionManager>(
                sp => sp.GetRequiredService<BrokerSessionManager>());
        }
        else
        {
            services.AddSingleton<InMemoryBrokerSessionManager>();
            services.AddSingleton<rvs.AlgoTrader.Brokers.Abstractions.IBrokerSessionManager>(
                sp => sp.GetRequiredService<InMemoryBrokerSessionManager>());
            services.AddSingleton<IAppBrokerSessionManager>(
                sp => sp.GetRequiredService<InMemoryBrokerSessionManager>());
        }

        // Broker auth service (Application interface → Infrastructure implementation)
        services.AddScoped<IBrokerAuthService, BrokerAuthService>();

        // Instrument token resolver — must be Singleton so the in-memory cache persists across requests.
        // InstrumentTokenResolver uses ConcurrentDictionary; registering as Scoped would lose the cache
        // on every new HTTP request scope.
        services.AddSingleton<IInstrumentTokenResolver, InstrumentTokenResolver>();

        // CandleAggregatorService: registered as singleton (IHostedService requirement)
        // Uses IServiceScopeFactory to create scopes for accessing scoped services
        services.AddSingleton<CandleAggregatorService>();
        services.AddHostedService(sp => sp.GetRequiredService<CandleAggregatorService>());

        // MassTransit — RabbitMQ when explicitly enabled, in-memory otherwise.
        // Set RabbitMQ:Enabled=true (or RABBITMQ__ENABLED=true env var) to use RabbitMQ.
        // Defaults to in-memory so the app starts cleanly in dev without RabbitMQ running.
        // Note: SignalRHubConsumer is registered separately in the API project.
        var rabbitEnabled = config.GetValue<bool>("RabbitMQ:Enabled");

        services.AddMassTransit(cfg =>
        {
            cfg.AddConsumer<AlertTriggeredConsumer>();
            cfg.AddConsumer<AuditLogConsumer>();
            cfg.AddConsumer<OrderFilledConsumer>();
            cfg.AddConsumer<StrategyEvaluationQueue>();
            configureAdditionalConsumers?.Invoke(cfg);

            if (rabbitEnabled)
            {
                var rabbitHost = config["RabbitMQ__Host"] ?? config["RabbitMQ:Host"] ?? "localhost";
                var rabbitUser = config["RabbitMQ__Username"] ?? config["RabbitMQ:Username"] ?? "guest";
                var rabbitPass = config["RabbitMQ__Password"] ?? config["RabbitMQ:Password"] ?? "guest";
                cfg.UsingRabbitMq((ctx, rmq) =>
                {
                    rmq.Host(rabbitHost, "/", h =>
                    {
                        h.Username(rabbitUser);
                        h.Password(rabbitPass);
                    });
                    rmq.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                cfg.UsingInMemory((ctx, mem) => mem.ConfigureEndpoints(ctx));
            }
        });

        // Hangfire — PostgreSQL storage
        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opts =>
                opts.UseNpgsqlConnection(
                    config.GetConnectionString("DefaultConnection") ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres")));
        services.AddHangfireServer();

        return services;
    }
}
