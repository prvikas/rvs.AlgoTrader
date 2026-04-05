using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class TradeJournalEntryConfiguration : IEntityTypeConfiguration<TradeJournalEntry>
{
    public void Configure(EntityTypeBuilder<TradeJournalEntry> builder)
    {
        builder.ToTable("trade_journal_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.StrategyInstanceId).HasColumnName("strategy_instance_id");
        builder.Property(e => e.InternalSymbol).HasColumnName("internal_symbol").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Quantity).HasColumnName("quantity");
        builder.Property(e => e.EntryPrice).HasColumnName("entry_price").HasPrecision(18, 4);
        builder.Property(e => e.ExitPrice).HasColumnName("exit_price").HasPrecision(18, 4);
        builder.Property(e => e.StopLoss).HasColumnName("stop_loss").HasPrecision(18, 4);
        builder.Property(e => e.TakeProfit).HasColumnName("take_profit").HasPrecision(18, 4);
        builder.Property(e => e.EntryTime).HasColumnName("entry_time")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        builder.Property(e => e.ExitTime).HasColumnName("exit_time")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));
        builder.Property(e => e.GrossPnl).HasColumnName("gross_pnl").HasPrecision(18, 4);
        builder.Property(e => e.NetPnl).HasColumnName("net_pnl").HasPrecision(18, 4);
        builder.Property(e => e.Commission).HasColumnName("commission").HasPrecision(18, 4);
        builder.Property(e => e.Stt).HasColumnName("stt").HasPrecision(18, 4);
        builder.Property(e => e.RMultiple).HasColumnName("r_multiple").HasPrecision(10, 4);
        builder.Property(e => e.InitialRisk).HasColumnName("initial_risk").HasPrecision(18, 4);
        builder.Property(e => e.Mae).HasColumnName("mae").HasPrecision(18, 4);
        builder.Property(e => e.Mfe).HasColumnName("mfe").HasPrecision(18, 4);
        builder.Property(e => e.ExitReason).HasColumnName("exit_reason").HasMaxLength(50).IsRequired();
        builder.Property(e => e.EntryReason).HasColumnName("entry_reason");
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.Tags).HasColumnName("tags").HasColumnType("text[]");
        builder.Property(e => e.TaxClassification).HasColumnName("tax_classification").HasMaxLength(30).IsRequired();
        builder.Property(e => e.HoldingDays).HasColumnName("holding_days");
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
        builder.Property(e => e.SourceTradeId).HasColumnName("source_trade_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at")
            .HasConversion(v => v.ToDateTimeUtc(), v => Instant.FromDateTimeUtc(v));

        builder.HasIndex(e => new { e.StrategyInstanceId, e.EntryTime });
        builder.HasIndex(e => e.InternalSymbol);

        builder.HasOne<StrategyInstance>()
            .WithMany()
            .HasForeignKey(e => e.StrategyInstanceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
