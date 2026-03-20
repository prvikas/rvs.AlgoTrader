using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class StrategyRunConfiguration : IEntityTypeConfiguration<StrategyRun>
{
    public void Configure(EntityTypeBuilder<StrategyRun> builder)
    {
        builder.ToTable("strategy_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.StrategyInstanceId).HasColumnName("strategy_instance_id");
        builder.Property(r => r.BrokerName).HasColumnName("broker_name").HasMaxLength(50);
        builder.Property(r => r.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.StartedAt).HasColumnName("started_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(r => r.EndedAt).HasColumnName("ended_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(r => r.EndReason).HasColumnName("end_reason").HasMaxLength(500);
        builder.Property(r => r.TotalPnl).HasColumnName("total_pnl").HasPrecision(18, 4);
        builder.Property(r => r.TradeCount).HasColumnName("trade_count");
        builder.Property(r => r.WinCount).HasColumnName("win_count");
        builder.Property(r => r.LossCount).HasColumnName("loss_count");
        builder.Property(r => r.MaxDrawdown).HasColumnName("max_drawdown").HasPrecision(18, 4);

        builder.HasIndex(r => r.StrategyInstanceId);
        builder.HasIndex(r => r.Status);
    }
}
