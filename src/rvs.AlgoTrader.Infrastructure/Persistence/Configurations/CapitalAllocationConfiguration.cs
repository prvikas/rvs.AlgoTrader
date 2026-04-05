using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class CapitalAllocationConfiguration : IEntityTypeConfiguration<CapitalAllocation>
{
    public void Configure(EntityTypeBuilder<CapitalAllocation> builder)
    {
        builder.ToTable("capital_allocations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.StrategyInstanceId).HasColumnName("strategy_instance_id");
        builder.Property(c => c.BrokerName).HasColumnName("broker_name").HasMaxLength(50);
        builder.Property(c => c.AllocatedCapital).HasColumnName("allocated_capital").HasPrecision(18, 4);
        builder.Property(c => c.ReservedCapital).HasColumnName("reserved_capital").HasPrecision(18, 4);
        builder.Ignore(c => c.AvailableCapital); // computed: AllocatedCapital - ReservedCapital
        builder.Property(c => c.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(c => c.StrategyInstanceId).IsUnique();

        builder.HasOne<StrategyInstance>()
            .WithMany()
            .HasForeignKey(c => c.StrategyInstanceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
