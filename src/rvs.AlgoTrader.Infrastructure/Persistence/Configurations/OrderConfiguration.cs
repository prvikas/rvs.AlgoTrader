using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.BrokerId).HasColumnName("broker_id").IsRequired();
        builder.Property(o => o.BrokerOrderId).HasColumnName("broker_order_id").HasMaxLength(100);
        builder.Property(o => o.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(o => o.OrderType).HasColumnName("order_type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(o => o.FilledQuantity).HasColumnName("filled_quantity");
        builder.Property(o => o.Price).HasColumnName("price").HasPrecision(18, 4);
        builder.Property(o => o.TriggerPrice).HasColumnName("trigger_price").HasPrecision(18, 4);
        builder.Property(o => o.FillPrice).HasColumnName("fill_price").HasPrecision(18, 4);
        builder.Property(o => o.TrailingSl).HasColumnName("trailing_sl").HasPrecision(18, 4);
        builder.Property(o => o.TrailingTp).HasColumnName("trailing_tp").HasPrecision(18, 4);
        builder.Property(o => o.ExchangeId).HasColumnName("exchange_id").IsRequired();
        builder.Property(o => o.ProductTypeId).HasColumnName("product_type_id").IsRequired();
        builder.Property(o => o.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(o => o.StrategyRunId).HasColumnName("strategy_run_id");
        builder.Property(o => o.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
        builder.Property(o => o.RejectionReason).HasColumnName("rejection_reason");
        builder.Property(o => o.PlacedAt).HasColumnName("placed_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(o => o.FilledAt).HasColumnName("filled_at")
            .HasConversion(
                v => v == null ? (DateTime?)null : v.Value.ToDateTimeUtc(),
                v => v == null ? (Instant?)null : Instant.FromDateTimeUtc(v.Value));
        builder.Property(o => o.CreatedAt).HasColumnName("created_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        // Foreign keys
        builder.HasOne(o => o.Broker)
            .WithMany()
            .HasForeignKey(o => o.BrokerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Exchange)
            .WithMany()
            .HasForeignKey(o => o.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ProductType)
            .WithMany()
            .HasForeignKey(o => o.ProductTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StrategyRun>()
            .WithMany()
            .HasForeignKey(o => o.StrategyRunId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(o => o.IdempotencyKey).IsUnique();
        builder.HasIndex(o => o.BrokerOrderId);
        builder.HasIndex(o => new { o.BrokerId, o.PlacedAt })
            .HasDatabaseName("idx_orders_broker_status");
        builder.HasIndex(o => o.StrategyRunId);
    }
}
