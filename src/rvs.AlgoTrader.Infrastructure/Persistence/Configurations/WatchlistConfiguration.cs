using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class WatchlistConfiguration : IEntityTypeConfiguration<Watchlist>
{
    public void Configure(EntityTypeBuilder<Watchlist> builder)
    {
        builder.ToTable("watchlists");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");
        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(w => w.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(w => w.CreatedAt).HasColumnName("created_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        builder.HasMany(w => w.Symbols)
            .WithOne()
            .HasForeignKey(s => s.WatchlistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WatchlistSymbolConfiguration : IEntityTypeConfiguration<WatchlistSymbol>
{
    public void Configure(EntityTypeBuilder<WatchlistSymbol> builder)
    {
        builder.ToTable("watchlist_symbols");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.WatchlistId).HasColumnName("watchlist_id");
        builder.Property(s => s.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(s => s.SortOrder).HasColumnName("sort_order");
        builder.Property(s => s.AddedAt).HasColumnName("added_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(s => new { s.WatchlistId, s.InternalSymbol }).IsUnique();
    }
}
