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
        builder.Property(s => s.RiskProfileId).HasColumnName("risk_profile_id");
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

        builder.Property(s => s.AllocatedCapital).HasColumnName("allocated_capital").HasPrecision(18, 4);
        builder.Property(s => s.IsActive).HasColumnName("is_active");
        builder.Property(s => s.ConfigJson).HasColumnName("config_json").HasColumnType("jsonb");
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        builder.Property(s => s.WatchlistId).HasColumnName("watchlist_id");

        // Foreign keys to related entities (Migration 021 #192, #197)
        builder.HasOne<RiskProfile>().WithMany().HasForeignKey(s => s.RiskProfileId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Domain.Entities.Watchlist>().WithMany().HasForeignKey(s => s.WatchlistId).OnDelete(DeleteBehavior.SetNull);

        // Approval Gate (P4)
        builder.Property(s => s.ApprovalReady).HasColumnName("approval_ready");
        builder.Property(s => s.ApprovedAt).HasColumnName("approved_at")
            .HasConversion(
                v => v.HasValue ? v.Value.ToDateTimeUtc() : (DateTime?)null,
                v => v.HasValue ? Instant.FromDateTimeUtc(v.Value) : (Instant?)null);

        // Ignore computed/derived properties with no DB column
        builder.Ignore(s => s.StrategyName);

        // Navigation properties to related entities (1:1 relationships)
        builder.HasOne(s => s.RuntimeState)
            .WithOne(r => r.StrategyInstance)
            .HasForeignKey<StrategyRuntimeState>(r => r.StrategyInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Credential)
            .WithOne(c => c.StrategyInstance)
            .HasForeignKey<BrokerCredential>(c => c.StrategyInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.InternalSymbol, s.Status });
    }
}
