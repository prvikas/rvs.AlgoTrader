using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class EnumValueConfiguration : IEntityTypeConfiguration<EnumValue>
{
    public void Configure(EntityTypeBuilder<EnumValue> b)
    {
        b.ToTable("enum_values");
        b.HasKey(x => new { x.Domain, x.Value });

        b.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(64).IsRequired();
        b.Property(x => x.Value).HasColumnName("value").HasMaxLength(128).IsRequired();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
        b.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
    }
}
