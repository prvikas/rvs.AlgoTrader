using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class StrategyApprovalConfiguration : IEntityTypeConfiguration<StrategyApproval>
{
    public void Configure(EntityTypeBuilder<StrategyApproval> builder)
    {
        builder.ToTable("strategy_approvals");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.StrategyInstanceId).HasColumnName("strategy_instance_id");
        builder.Property(a => a.ApprovedBy).HasColumnName("approved_by").HasMaxLength(200).IsRequired();
        builder.Property(a => a.ApprovalNotes).HasColumnName("approval_notes");
        builder.Property(a => a.BacktestResultId).HasColumnName("backtest_result_id");
        builder.Property(a => a.ForwardTestSessionId).HasColumnName("forward_test_session_id");
        builder.Property(a => a.CagrAtApproval).HasColumnName("cagr_at_approval").HasPrecision(18, 6);
        builder.Property(a => a.DrawdownAtApproval).HasColumnName("drawdown_at_approval").HasPrecision(18, 6);
        builder.Property(a => a.SharpeAtApproval).HasColumnName("sharpe_at_approval").HasPrecision(18, 6);
        builder.Property(a => a.ForwardTestDays).HasColumnName("forward_test_days");
        builder.Property(a => a.ForwardWinRate).HasColumnName("forward_win_rate").HasPrecision(18, 6);
        builder.Property(a => a.AutomatedChecksPassed).HasColumnName("automated_checks_passed");
        builder.Property(a => a.InvalidatedAt).HasColumnName("invalidated_at")
            .HasConversion(
                v => v.HasValue ? v.Value.ToDateTimeUtc() : (DateTime?)null,
                v => v.HasValue ? Instant.FromDateTimeUtc(v.Value) : (Instant?)null);
        builder.Property(a => a.InvalidationReason).HasColumnName("invalidation_reason");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        // IsActive is a computed property — not stored
        builder.Ignore(a => a.IsActive);

        builder.HasIndex(a => a.StrategyInstanceId).HasDatabaseName("idx_strategy_approvals_instance");
        builder.HasIndex(a => a.CreatedAt).HasDatabaseName("idx_strategy_approvals_created").IsDescending();

        builder.HasOne<StrategyInstance>()
            .WithMany()
            .HasForeignKey(a => a.StrategyInstanceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
