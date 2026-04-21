using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class OptionIvHistoryConfiguration : IEntityTypeConfiguration<OptionIvHistory>
{
    public void Configure(EntityTypeBuilder<OptionIvHistory> b)
    {
        b.ToTable("option_iv_history");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.UnderlyingSymbol).HasColumnName("underlying_symbol").HasMaxLength(50).IsRequired();
        b.Property(x => x.Date).HasColumnName("date").IsRequired();
        b.Property(x => x.AtmIv).HasColumnName("atm_iv").HasColumnType("numeric(10,4)");
        b.Property(x => x.RecordedAt).HasColumnName("recorded_at");
        b.HasIndex(x => new { x.UnderlyingSymbol, x.Date }).HasDatabaseName("idx_option_iv_history_symbol_date");
    }
}
