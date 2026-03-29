using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class BacktestRunConfiguration : IEntityTypeConfiguration<BacktestRun>
{
    public void Configure(EntityTypeBuilder<BacktestRun> b)
    {
        b.ToTable("backtest_runs");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StrategyName).HasColumnName("strategy_name").HasMaxLength(100).IsRequired();
        b.Property(x => x.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        b.Property(x => x.Timeframe).HasColumnName("timeframe").HasMaxLength(10).IsRequired();
        b.Property(x => x.FromDate).HasColumnName("from_date").IsRequired();
        b.Property(x => x.ToDate).HasColumnName("to_date").IsRequired();
        b.Property(x => x.InitialCapital).HasColumnName("initial_capital").HasColumnType("numeric(18,2)");
        b.Property(x => x.FinalEquity).HasColumnName("final_equity").HasColumnType("numeric(18,2)");
        b.Property(x => x.TotalPnl).HasColumnName("total_pnl").HasColumnType("numeric(18,2)");
        b.Property(x => x.TotalReturn).HasColumnName("total_return").HasColumnType("numeric(10,6)");
        b.Property(x => x.MaxDrawdown).HasColumnName("max_drawdown").HasColumnType("numeric(10,6)");
        b.Property(x => x.SharpeRatio).HasColumnName("sharpe_ratio").HasColumnType("numeric(10,4)");
        b.Property(x => x.CalmarRatio).HasColumnName("calmar_ratio").HasColumnType("numeric(10,4)");
        b.Property(x => x.ProfitFactor).HasColumnName("profit_factor").HasColumnType("numeric(10,4)");
        b.Property(x => x.WinRate).HasColumnName("win_rate").HasColumnType("numeric(6,4)");
        b.Property(x => x.TotalTrades).HasColumnName("total_trades");
        b.Property(x => x.WinCount).HasColumnName("win_count");
        b.Property(x => x.LossCount).HasColumnName("loss_count");
        b.Property(x => x.AvgWin).HasColumnName("avg_win").HasColumnType("numeric(18,2)");
        b.Property(x => x.AvgLoss).HasColumnName("avg_loss").HasColumnType("numeric(18,2)");
        b.Property(x => x.MaxConsecutiveLosses).HasColumnName("max_consecutive_losses");
        b.Property(x => x.ExpectancyPerTrade).HasColumnName("expectancy_per_trade").HasColumnType("numeric(18,2)");
        b.Property(x => x.DataHash).HasColumnName("data_hash").HasMaxLength(64);
        b.Property(x => x.TradesJson).HasColumnName("trades_json").HasColumnType("text");
        b.Property(x => x.ExtendedStatsJson).HasColumnName("extended_stats_json").HasColumnType("text");
        b.Property(x => x.RanAt).HasColumnName("ran_at");
        b.Property(x => x.ScenarioId).HasColumnName("scenario_id");
        b.Property(x => x.EffectiveParametersJson).HasColumnName("effective_parameters_json").HasColumnType("text");

        b.HasIndex(x => new { x.StrategyName, x.InternalSymbol, x.Timeframe })
            .HasDatabaseName("idx_backtest_runs_strategy_symbol");
        b.HasIndex(x => x.DataHash).HasDatabaseName("idx_backtest_runs_data_hash");
    }
}
