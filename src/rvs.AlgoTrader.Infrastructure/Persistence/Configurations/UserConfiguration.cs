using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(200).IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(u => u.BrokerAccounts)
               .WithOne(a => a.User)
               .HasForeignKey(a => a.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserBrokerAccountConfiguration : IEntityTypeConfiguration<UserBrokerAccount>
{
    public void Configure(EntityTypeBuilder<UserBrokerAccount> builder)
    {
        builder.ToTable("user_broker_accounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.BrokerName).HasColumnName("broker_name").HasMaxLength(50).IsRequired();
        builder.Property(a => a.Market).HasColumnName("market").HasMaxLength(10).IsRequired();
        builder.Property(a => a.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(a => a.IsActive).HasColumnName("is_active");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.UserId, a.BrokerName, a.Market }).IsUnique();
    }
}
