using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class InstrumentUniverseConfiguration : IEntityTypeConfiguration<InstrumentUniverse>
{
    public void Configure(EntityTypeBuilder<InstrumentUniverse> builder)
    {
        builder.ToTable("instrument_universe");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Symbol).HasColumnName("symbol").HasMaxLength(50).IsRequired();
        builder.Property(u => u.Exchange).HasColumnName("exchange").HasMaxLength(10).IsRequired();
        builder.Property(u => u.Category).HasColumnName("category").HasMaxLength(30).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(u => new { u.Symbol, u.Exchange, u.Category }).IsUnique();
        builder.HasIndex(u => new { u.Category, u.IsActive });
    }
}
