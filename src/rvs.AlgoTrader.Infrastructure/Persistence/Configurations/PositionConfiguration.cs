using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.BrokerName).HasColumnName("broker_name").HasMaxLength(50).IsRequired();
        builder.Property(p => p.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(p => p.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(p => p.AvgPrice).HasColumnName("average_price").HasPrecision(18, 4).IsRequired();
        builder.Property(p => p.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
        builder.Property(p => p.CurrentPrice).HasColumnName("last_price").HasPrecision(18, 4);
        builder.Property(p => p.RealizedPnl).HasColumnName("realized_pnl").HasPrecision(18, 4);
        builder.Property(p => p.UnrealizedPnl).HasColumnName("unrealized_pnl").HasPrecision(18, 4);
        builder.Property(p => p.StopLoss).HasColumnName("stop_loss").HasPrecision(18, 4);
        builder.Property(p => p.TakeProfit).HasColumnName("take_profit").HasPrecision(18, 4);
        builder.Property(p => p.ProductType).HasColumnName("product_type").HasMaxLength(10).IsRequired();
        builder.Property(p => p.IsOpen).HasColumnName("is_open");
        builder.Property(p => p.StrategyRunId).HasColumnName("strategy_run_id");
        builder.Property(p => p.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
        builder.Property(p => p.CloseReason).HasColumnName("close_reason").HasMaxLength(50);

        // ── Short Premium Velocity — leg tagging ─────────────────────────────
        builder.Property(p => p.LegType).HasColumnName("leg_type")
            .HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.HedgeType).HasColumnName("hedge_type")
            .HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.HedgeNetCost).HasColumnName("hedge_net_cost")
            .HasPrecision(18, 4);
        builder.Property(p => p.LinkedShortLegId).HasColumnName("linked_short_leg_id");

        // Self-referencing FK: Hedge leg → ShortPremium leg (no cascade delete)
        builder.HasOne<Position>()
            .WithMany()
            .HasForeignKey(p => p.LinkedShortLegId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Ignore computed alias properties
        builder.Ignore(p => p.AveragePrice);
        builder.Ignore(p => p.LastPrice);

        builder.Property(p => p.OpenedAt).HasColumnName("opened_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(p => p.ClosedAt).HasColumnName("closed_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(p => p.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(p => new { p.BrokerName, p.IsOpen });
        builder.HasIndex(p => p.StrategyRunId);
        builder.HasIndex(p => new { p.InternalSymbol, p.IsOpen });

        builder.HasOne<StrategyRun>()
            .WithMany()
            .HasForeignKey(p => p.StrategyRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
