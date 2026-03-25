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
    public DbSet<StrategyRun> StrategyRuns => Set<StrategyRun>();
    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
    public DbSet<CapitalAllocation> CapitalAllocations => Set<CapitalAllocation>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistSymbol> WatchlistSymbols => Set<WatchlistSymbol>();

    // Forward test tables
    public DbSet<ForwardTestSession> ForwardTestSessions => Set<ForwardTestSession>();
    public DbSet<ForwardTestTrade> ForwardTestTrades => Set<ForwardTestTrade>();

    // Instrument universe — controls which symbols are downloaded and stored
    public DbSet<InstrumentUniverse> InstrumentUniverse => Set<InstrumentUniverse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AlgoTraderDbContext).Assembly);
    }
}
