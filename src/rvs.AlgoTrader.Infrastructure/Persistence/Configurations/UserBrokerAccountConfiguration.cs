using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class UserBrokerAccountConfiguration : IEntityTypeConfiguration<UserBrokerAccount>
{
    public void Configure(EntityTypeBuilder<UserBrokerAccount> builder)
    {
        builder.ToTable("user_broker_accounts");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(u => u.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(u => u.BrokerId)
            .HasColumnName("broker_id")
            .IsRequired();

        builder.Property(u => u.BrokerToken)
            .HasColumnName("broker_token")
            .HasMaxLength(500);

        builder.Property(u => u.ApiKey)
            .HasColumnName("api_key")
            .HasMaxLength(500);

        builder.Property(u => u.ApiSecret)
            .HasColumnName("api_secret")
            .HasMaxLength(500);

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(100);

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        // Foreign keys
        builder.HasOne(u => u.User)
            .WithMany()
            .HasForeignKey(u => u.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(u => u.Broker)
            .WithMany()
            .HasForeignKey(u => u.BrokerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique constraint
        builder.HasIndex(u => new { u.UserId, u.BrokerId })
            .IsUnique()
            .HasDatabaseName("uq_user_broker_account");

        // Index for queries
        builder.HasIndex(u => u.UserId)
            .HasDatabaseName("idx_user_broker_accounts_user");
    }
}
