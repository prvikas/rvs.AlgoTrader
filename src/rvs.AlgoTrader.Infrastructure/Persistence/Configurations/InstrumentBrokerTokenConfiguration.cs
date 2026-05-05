using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class InstrumentBrokerTokenConfiguration : IEntityTypeConfiguration<InstrumentBrokerToken>
{
    public void Configure(EntityTypeBuilder<InstrumentBrokerToken> builder)
    {
        builder.ToTable("instrument_broker_tokens");

        // Composite primary key
        builder.HasKey(t => new { t.InstrumentId, t.BrokerId });

        builder.Property(t => t.InstrumentId)
            .HasColumnName("instrument_id");

        builder.Property(t => t.BrokerId)
            .HasColumnName("broker_id");

        builder.Property(t => t.Token)
            .HasColumnName("token")
            .HasMaxLength(100)
            .IsRequired();

        // Foreign keys
        builder.HasOne(t => t.Instrument)
            .WithMany(i => i.BrokerTokens)
            .HasForeignKey(t => t.InstrumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Broker)
            .WithMany()
            .HasForeignKey(t => t.BrokerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(t => t.BrokerId)
            .HasDatabaseName("idx_instrument_broker_tokens_broker");
    }
}
