using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
{
    public void Configure(EntityTypeBuilder<Exchange> builder)
    {
        builder.ToTable("exchanges");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.MarketCode)
            .HasColumnName("market_code")
            .HasColumnType("char(2)")
            .IsRequired();

        builder.Property(e => e.TimezoneId)
            .HasColumnName("timezone_id")
            .IsRequired();

        // Foreign keys
        builder.HasOne(e => e.Timezone)
            .WithMany()
            .HasForeignKey(e => e.TimezoneId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
