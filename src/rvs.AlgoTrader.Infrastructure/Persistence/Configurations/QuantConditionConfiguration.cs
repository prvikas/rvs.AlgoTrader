using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class QuantConditionConfiguration : IEntityTypeConfiguration<QuantCondition>
{
    public void Configure(EntityTypeBuilder<QuantCondition> builder)
    {
        builder.ToTable("quant_conditions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Hypothesis).HasColumnName("hypothesis").IsRequired();
        builder.Property(e => e.ConditionsJson).HasColumnName("conditions_json").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SizingRules).HasColumnName("sizing_rules").IsRequired();
        builder.Property(e => e.InvalidationConditions).HasColumnName("invalidation_conditions").IsRequired();
        builder.Property(e => e.NotesJson).HasColumnName("notes_json").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Tags).HasColumnName("tags").IsRequired();
        builder.Property(e => e.IsTemplate).HasColumnName("is_template").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
