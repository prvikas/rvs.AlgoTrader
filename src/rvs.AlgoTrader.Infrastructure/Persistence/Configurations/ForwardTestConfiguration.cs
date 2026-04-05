using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class ForwardTestSessionConfiguration : IEntityTypeConfiguration<ForwardTestSession>
{
    public void Configure(EntityTypeBuilder<ForwardTestSession> builder)
    {
        builder.ToTable("forward_test_sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.StrategyInstanceId).HasColumnName("strategy_instance_id");
        builder.Property(s => s.InitialCapital).HasColumnName("initial_capital").HasPrecision(18, 4);
        builder.Property(s => s.FinalCapital).HasColumnName("final_capital").HasPrecision(18, 4);
        builder.Property(s => s.FinalPnl).HasColumnName("final_pnl").HasPrecision(18, 4);
        builder.Property(s => s.TradeCount).HasColumnName("trade_count");
        builder.Property(s => s.WinRate).HasColumnName("win_rate").HasPrecision(6, 4);
        builder.Property(s => s.MaxDrawdown).HasColumnName("max_drawdown").HasPrecision(8, 4);
        builder.Property(s => s.SharpeRatio).HasColumnName("sharpe_ratio").HasPrecision(8, 4);
        builder.Property(s => s.SourceBacktestId).HasColumnName("source_backtest_id");
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(30);
        builder.Property(s => s.StartedAt).HasColumnName("started_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        builder.Property(s => s.EndedAt).HasColumnName("ended_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));

        builder.HasOne<StrategyInstance>()
            .WithMany()
            .HasForeignKey(s => s.StrategyInstanceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForwardTestTradeConfiguration : IEntityTypeConfiguration<ForwardTestTrade>
{
    public void Configure(EntityTypeBuilder<ForwardTestTrade> builder)
    {
        builder.ToTable("forward_test_trades");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.SessionId).HasColumnName("session_id");
        builder.Property(t => t.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(t => t.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired();
        builder.Property(t => t.Quantity).HasColumnName("quantity");
        builder.Property(t => t.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
        builder.Property(t => t.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
        builder.Property(t => t.SimulatedFillPrice).HasColumnName("simulated_fill_price").HasPrecision(18, 4);
        builder.Property(t => t.Slippage).HasColumnName("slippage").HasPrecision(18, 4);
        builder.Property(t => t.Pnl).HasColumnName("pnl").HasPrecision(18, 4);
        builder.Property(t => t.RealizedPnl).HasColumnName("realized_pnl").HasPrecision(18, 4);
        builder.Property(t => t.CloseReason).HasColumnName("close_reason").HasMaxLength(100);
        builder.Property(t => t.EntryTime).HasColumnName("entry_time")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        builder.Property(t => t.ExitTime).HasColumnName("exit_time")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(t => t.OpenedAt).HasColumnName("opened_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(t => t.ClosedAt).HasColumnName("closed_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));

        builder.HasOne<ForwardTestSession>()
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
