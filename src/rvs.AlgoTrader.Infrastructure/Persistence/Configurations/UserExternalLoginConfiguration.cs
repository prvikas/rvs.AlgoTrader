using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    public void Configure(EntityTypeBuilder<UserExternalLogin> b)
    {
        b.ToTable("user_external_logins");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.UserId).HasColumnName("user_id");
        b.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        b.Property(e => e.ProviderSub).HasColumnName("provider_sub").HasMaxLength(255).IsRequired();
        b.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
        b.Property(e => e.CreatedAt).HasColumnName("created_at");

        b.HasIndex(e => new { e.Provider, e.ProviderSub }).IsUnique();
        b.HasIndex(e => e.UserId);

        b.HasOne(e => e.User)
            .WithMany(u => u.ExternalLogins)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
