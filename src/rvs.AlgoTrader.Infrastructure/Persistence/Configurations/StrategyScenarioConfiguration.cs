using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class StrategyScenarioConfiguration : IEntityTypeConfiguration<StrategyScenario>
{
    public void Configure(EntityTypeBuilder<StrategyScenario> b)
    {
        b.ToTable("strategy_scenarios");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.StrategyInstanceId).HasColumnName("strategy_instance_id").IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        b.Property(x => x.ParametersJsonOverride).HasColumnName("parameters_json_override").HasColumnType("text");
        b.Property(x => x.AllocatedCapital).HasColumnName("allocated_capital").HasColumnType("numeric(18,4)");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1).IsRequired();
        b.Property(x => x.LastBacktestRunId).HasColumnName("last_backtest_run_id");

        b.Property(x => x.CreatedAt).HasColumnName("created_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        b.HasOne(x => x.StrategyInstance)
            .WithMany()
            .HasForeignKey(x => x.StrategyInstanceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.StrategyInstanceId).HasDatabaseName("ix_strategy_scenarios_instance");
    }
}
