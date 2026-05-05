using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class BrokerExchangeConfigConfiguration : IEntityTypeConfiguration<BrokerExchangeConfig>
{
    public void Configure(EntityTypeBuilder<BrokerExchangeConfig> builder)
    {
        builder.ToTable("broker_exchange_configs");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasDefaultValueSql("gen_random_uuid()");

        builder.Property(b => b.BrokerId)
            .HasColumnName("broker_id")
            .IsRequired();

        builder.Property(b => b.ExchangeId)
            .HasColumnName("exchange_id")
            .IsRequired();

        builder.Property(b => b.ProductTypeId)
            .HasColumnName("product_type_id")
            .IsRequired();

        builder.Property(b => b.DefaultLotSize)
            .HasColumnName("default_lot_size")
            .HasDefaultValue(1);

        builder.Property(b => b.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        // Foreign keys
        builder.HasOne(b => b.Broker)
            .WithMany()
            .HasForeignKey(b => b.BrokerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Exchange)
            .WithMany()
            .HasForeignKey(b => b.ExchangeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.ProductType)
            .WithMany()
            .HasForeignKey(b => b.ProductTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique constraint
        builder.HasIndex(b => new { b.BrokerId, b.ExchangeId, b.ProductTypeId })
            .IsUnique()
            .HasDatabaseName("uq_broker_exchange_product");

        // Regular indexes
        builder.HasIndex(b => b.BrokerId)
            .HasDatabaseName("idx_broker_exchange_configs_broker");
        builder.HasIndex(b => b.ExchangeId)
            .HasDatabaseName("idx_broker_exchange_configs_exchange");
    }
}
