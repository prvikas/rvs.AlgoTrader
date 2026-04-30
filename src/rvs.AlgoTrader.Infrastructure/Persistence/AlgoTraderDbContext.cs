using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Infrastructure.Persistence;

public class AlgoTraderDbContext : DbContext
{
    public AlgoTraderDbContext(DbContextOptions<AlgoTraderDbContext> options) : base(options) { }

    // Core trading tables
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Candle> Candles => Set<Candle>();

    // Strategy tables
    public DbSet<StrategyInstance> StrategyInstances => Set<StrategyInstance>();
    public DbSet<StrategyRuntimeState> StrategyRuntimeStates => Set<StrategyRuntimeState>();
    public DbSet<BrokerCredential> BrokerCredentials => Set<BrokerCredential>();
    public DbSet<StrategyRun> StrategyRuns => Set<StrategyRun>();
    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
    public DbSet<CapitalAllocation> CapitalAllocations => Set<CapitalAllocation>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistSymbol> WatchlistSymbols => Set<WatchlistSymbol>();

    // Forward test tables
    public DbSet<ForwardTestSession> ForwardTestSessions => Set<ForwardTestSession>();
    public DbSet<ForwardTestTrade> ForwardTestTrades => Set<ForwardTestTrade>();

    // Backtest history
    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();

    // Strategy scenarios
    public DbSet<StrategyScenario> StrategyScenarios => Set<StrategyScenario>();

    // Instrument universe — controls which symbols are downloaded and stored
    public DbSet<InstrumentUniverse> InstrumentUniverse => Set<InstrumentUniverse>();

    // Options engine
    public DbSet<OptionIvHistory>    OptionIvHistory    => Set<OptionIvHistory>();
    public DbSet<SpreadPosition>     SpreadPositions    => Set<SpreadPosition>();
    public DbSet<SpreadPositionLeg>  SpreadPositionLegs => Set<SpreadPositionLeg>();

    // FX rates
    public DbSet<FxRate>             FxRates            => Set<FxRate>();

    // Trade journal
    public DbSet<TradeJournalEntry> TradeJournalEntries => Set<TradeJournalEntry>();

    // Strategy approvals (P4 Approval Gate)
    public DbSet<StrategyApproval> StrategyApprovals => Set<StrategyApproval>();

    // Domain enum lookup table — single source of truth for UI dropdowns
    public DbSet<EnumValue> EnumValues => Set<EnumValue>();

    // Monitoring alert rules — user-defined threshold rules evaluated by MonitoringAlertJob
    public DbSet<MonitoringAlertRule> MonitoringAlertRules => Set<MonitoringAlertRule>();

    // P10 Quant Intelligence — editable knowledge cards for indicators and options metrics
    public DbSet<IndicatorIntelligence> IndicatorIntelligence => Set<IndicatorIntelligence>();
    public DbSet<GreeksIntelligence>    GreeksIntelligence    => Set<GreeksIntelligence>();

    // P10-C Quant Lab — user-defined research conditions with lifecycle and notes
    public DbSet<QuantCondition> QuantConditions => Set<QuantCondition>();

    // Multi-user support (migration 050) + OAuth logins (migration 051)
    public DbSet<User>               Users               => Set<User>();
    public DbSet<UserBrokerAccount>  UserBrokerAccounts  => Set<UserBrokerAccount>();
    public DbSet<UserExternalLogin>  UserExternalLogins  => Set<UserExternalLogin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AlgoTraderDbContext).Assembly);
    }
}
