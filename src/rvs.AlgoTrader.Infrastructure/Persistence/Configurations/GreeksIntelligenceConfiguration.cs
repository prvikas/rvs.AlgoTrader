using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class GreeksIntelligenceConfiguration : IEntityTypeConfiguration<GreeksIntelligence>
{
    public void Configure(EntityTypeBuilder<GreeksIntelligence> builder)
    {
        builder.ToTable("greeks_intelligence");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.MetricKey).HasColumnName("metric_key").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.WhatItMeasures).HasColumnName("what_it_measures").IsRequired();
        builder.Property(e => e.WhyItMatters).HasColumnName("why_it_matters").IsRequired();
        builder.Property(e => e.CommonMisuse).HasColumnName("common_misuse").IsRequired();
        builder.Property(e => e.PositiveEvConditions).HasColumnName("positive_ev_conditions").IsRequired();
        builder.Property(e => e.RegimeContext).HasColumnName("regime_context").IsRequired();
        builder.Property(e => e.SizingImplications).HasColumnName("sizing_implications").IsRequired();
        builder.Property(e => e.PortfolioImpact).HasColumnName("portfolio_impact").IsRequired();
        builder.Property(e => e.UserNotes).HasColumnName("user_notes").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => e.MetricKey).IsUnique();
    }
}
