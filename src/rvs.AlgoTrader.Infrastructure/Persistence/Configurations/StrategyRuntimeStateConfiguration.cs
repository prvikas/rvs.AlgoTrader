using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class StrategyRuntimeStateConfiguration : IEntityTypeConfiguration<StrategyRuntimeState>
{
    public void Configure(EntityTypeBuilder<StrategyRuntimeState> builder)
    {
        builder.ToTable("strategy_runtime_states");
        builder.HasKey(r => r.StrategyInstanceId);
        builder.Property(r => r.StrategyInstanceId).HasColumnName("strategy_instance_id");

        builder.Property(r => r.CurrentRunId).HasColumnName("current_run_id");
        builder.Property(r => r.TodayRealizedPnl).HasColumnName("today_realized_pnl").HasPrecision(18, 4);
        builder.Property(r => r.TodayUnrealizedPnl).HasColumnName("today_unrealized_pnl").HasPrecision(18, 4);
        builder.Property(r => r.AutoResumeOnRestart).HasColumnName("auto_resume_on_restart");

        builder.Property(r => r.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(r => r.CurrentRunId);
    }
}
