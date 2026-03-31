using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class StrategyInstanceConfiguration : IEntityTypeConfiguration<StrategyInstance>
{
    public void Configure(EntityTypeBuilder<StrategyInstance> builder)
    {
        builder.ToTable("strategy_instances");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.StrategyType).HasColumnName("strategy_name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(s => s.Timeframe).HasColumnName("timeframe").HasMaxLength(10).IsRequired();
        builder.Property(s => s.BrokerName).HasColumnName("broker_name").HasMaxLength(50);
        builder.Property(s => s.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.ParametersJson).HasColumnName("parameters_json").HasColumnType("jsonb");
        builder.Property(s => s.ScheduleJson).HasColumnName("schedule_json").HasColumnType("jsonb");
        builder.Property(s => s.FailureBehaviorJson).HasColumnName("failure_behavior_json").HasColumnType("jsonb");
        builder.Property(s => s.AutoResumeOnRestart).HasColumnName("auto_resume_on_restart");
        builder.Property(s => s.RiskProfileId).HasColumnName("risk_profile_id");
        builder.Property(s => s.AllocatedCapital).HasColumnName("allocated_capital").HasPrecision(18, 4);
        builder.Property(s => s.TodayRealizedPnl).HasColumnName("today_realized_pnl").HasPrecision(18, 4);
        builder.Property(s => s.TodayUnrealizedPnl).HasColumnName("today_unrealized_pnl").HasPrecision(18, 4);
        builder.Property(s => s.Exchange).HasColumnName("exchange").HasMaxLength(10).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<rvs.AlgoTrader.Domain.Enums.Exchange>(v));
        builder.Property(s => s.ProductType).HasColumnName("product_type").HasMaxLength(10).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<rvs.AlgoTrader.Domain.Enums.ProductType>(v));
        builder.Property(s => s.LotSize).HasColumnName("lot_size");
        builder.Property(s => s.BrokerToken).HasColumnName("broker_token").HasMaxLength(100);
        builder.Property(s => s.ExecutionMode).HasColumnName("execution_mode").HasMaxLength(20).IsRequired()
            .HasConversion(v => v.ToString(), v => Enum.Parse<rvs.AlgoTrader.Domain.Enums.ExecutionMode>(v));
        builder.Property(s => s.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        builder.Property(s => s.IsActive).HasColumnName("IsActive");
        builder.Property(s => s.ConfigJson).HasColumnName("ConfigJson").HasColumnType("jsonb");
        builder.Property(s => s.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(200);
        builder.Property(s => s.WatchlistId).HasColumnName("WatchlistId");
        builder.Property(s => s.CurrentRunId).HasColumnName("CurrentRunId");

        // Ignore computed/derived properties with no DB column
        builder.Ignore(s => s.StrategyName);

        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.InternalSymbol, s.Status });
    }
}
