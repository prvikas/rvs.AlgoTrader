using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class IndicatorIntelligenceConfiguration : IEntityTypeConfiguration<IndicatorIntelligence>
{
    public void Configure(EntityTypeBuilder<IndicatorIntelligence> builder)
    {
        builder.ToTable("indicator_intelligence");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.IndicatorKey).HasColumnName("indicator_key").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.WhatItMeasures).HasColumnName("what_it_measures").IsRequired();
        builder.Property(e => e.CommonMistake).HasColumnName("common_mistake").IsRequired();
        builder.Property(e => e.PositiveEvConditions).HasColumnName("positive_ev_conditions").IsRequired();
        builder.Property(e => e.IgnoreConditions).HasColumnName("ignore_conditions").IsRequired();
        builder.Property(e => e.BestPairedWith).HasColumnName("best_paired_with").IsRequired();
        builder.Property(e => e.SizingImplications).HasColumnName("sizing_implications").IsRequired();
        builder.Property(e => e.UserNotes).HasColumnName("user_notes").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.IndicatorKey).IsUnique();
    }
}
