using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("instruments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(i => i.TradingSymbol).HasColumnName("trading_symbol").HasMaxLength(50).IsRequired();
        builder.Property(i => i.Exchange).HasColumnName("exchange").HasMaxLength(10).IsRequired();
        builder.Property(i => i.InstrumentType).HasColumnName("instrument_type")
            .HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(i => i.LotSize).HasColumnName("lot_size");
        builder.Property(i => i.TickSize).HasColumnName("tick_size").HasPrecision(18, 4);
        builder.Property(i => i.IsActive).HasColumnName("is_active");
        builder.Property(i => i.ZerodhaToken).HasColumnName("zerodha_token").HasMaxLength(20);
        builder.Property(i => i.UpstoxToken).HasColumnName("upstox_token").HasMaxLength(100);
        builder.Property(i => i.MStockToken).HasColumnName("mstock_token").HasMaxLength(100);
        builder.Property(i => i.LastRefreshedAt).HasColumnName("last_refreshed_at")
            .HasConversion(
                v => v.ToDateTimeUtc(),
                v => Instant.FromDateTimeUtc(v));

        // Derivative fields — explicitly mapped so they are included in DB schema
        // and queries regardless of EF convention. Column names match InitialMigration.sql.
        // NOTE: option_type stored as integer (enum int value 0=Call, 1=Put) — do NOT
        // change to string conversion without a DB column-type migration.
        builder.Property(i => i.Underlying).HasColumnName("underlying").HasMaxLength(50);
        builder.Property(i => i.StrikePrice).HasColumnName("strike_price").HasPrecision(18, 4);
        builder.Property(i => i.OptionType).HasColumnName("option_type");
        builder.Property(i => i.Expiry).HasColumnName("expiry");

        // Currency + price multiplier fields added in migration 013
        builder.Property(i => i.QuoteCurrency).HasColumnName("quote_currency").HasMaxLength(10);
        builder.Property(i => i.SettlementCurrency).HasColumnName("settlement_currency").HasMaxLength(10);
        builder.Property(i => i.PriceMultiplier).HasColumnName("price_multiplier").HasPrecision(18, 6);

        builder.HasAlternateKey(i => i.InternalSymbol);
        builder.HasIndex(i => new { i.TradingSymbol, i.Exchange });
        builder.HasIndex(i => i.IsActive);
    }
}
