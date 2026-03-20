using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> builder)
    {
        builder.ToTable("risk_profiles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(r => r.MaxCapitalPerTradePct).HasColumnName("max_position_size_pct").HasPrecision(6, 4);
        builder.Property(r => r.MaxDailyDrawdownPct).HasColumnName("max_daily_loss_pct").HasPrecision(6, 4);
        builder.Property(r => r.MaxOpenTradesPerSymbol).HasColumnName("max_open_positions");
        builder.Property(r => r.MaxTotalCapitalDeployed).HasColumnName("max_total_capital_deployed").HasPrecision(18, 4);
        builder.Property(r => r.MaxTradesPerDay).HasColumnName("max_trades_per_day");

        builder.Property(r => r.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
    }
}
