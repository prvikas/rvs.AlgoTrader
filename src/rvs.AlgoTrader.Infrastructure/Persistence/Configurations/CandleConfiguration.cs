using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class CandleConfiguration : IEntityTypeConfiguration<Candle>
{
    public void Configure(EntityTypeBuilder<Candle> builder)
    {
        // TimescaleDB hypertable — partition on open_time
        builder.ToTable("candles");
        builder.HasKey(c => new { c.InternalSymbol, c.Timeframe, c.OpenTime });
        builder.Property(c => c.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(c => c.Timeframe).HasColumnName("timeframe").HasMaxLength(10).IsRequired();
        builder.Property(c => c.Open).HasColumnName("open").HasPrecision(18, 4);
        builder.Property(c => c.High).HasColumnName("high").HasPrecision(18, 4);
        builder.Property(c => c.Low).HasColumnName("low").HasPrecision(18, 4);
        builder.Property(c => c.Close).HasColumnName("close").HasPrecision(18, 4);
        builder.Property(c => c.Volume).HasColumnName("volume");
        builder.Property(c => c.IsClosed).HasColumnName("is_closed");
        builder.Property(c => c.OpenTime).HasColumnName("open_time")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));
        builder.Property(c => c.CloseTime).HasColumnName("close_time")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(c => new { c.InternalSymbol, c.Timeframe, c.OpenTime });
    }
}
