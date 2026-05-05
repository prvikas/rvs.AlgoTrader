using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class BrokerCredentialConfiguration : IEntityTypeConfiguration<BrokerCredential>
{
    public void Configure(EntityTypeBuilder<BrokerCredential> builder)
    {
        builder.ToTable("broker_credentials");

        // PK is now broker_name — credentials belong to the broker, not a strategy run.
        builder.HasKey(c => c.BrokerName);
        builder.Property(c => c.BrokerName)
               .HasColumnName("broker_name")
               .HasMaxLength(50)
               .IsRequired();

        // StrategyInstanceId kept as nullable denorm column — NO FK constraint.
        builder.Property(c => c.StrategyInstanceId)
               .HasColumnName("strategy_instance_id")
               .IsRequired(false);

        builder.Property(c => c.BrokerToken)
               .HasColumnName("broker_token")
               .HasMaxLength(100);

        builder.Property(c => c.Exchange)
               .HasColumnName("exchange")
               .HasMaxLength(10)
               .IsRequired()
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<rvs.AlgoTrader.Domain.Enums.Exchange>(v));

        builder.Property(c => c.ProductType)
               .HasColumnName("product_type")
               .HasMaxLength(10)
               .IsRequired()
               .HasConversion(
                   v => v.ToString(),
                   v => Enum.Parse<rvs.AlgoTrader.Domain.Enums.ProductType>(v));

        builder.Property(c => c.LotSize).HasColumnName("lot_size");
    }
}
