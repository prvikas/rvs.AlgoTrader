using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Infrastructure.Persistence.Configurations;

public class MonitoringAlertRuleConfiguration : IEntityTypeConfiguration<MonitoringAlertRule>
{
    public void Configure(EntityTypeBuilder<MonitoringAlertRule> builder)
    {
        builder.ToTable("monitoring_alert_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.AlertType).HasColumnName("alert_type").HasMaxLength(100).IsRequired();
        builder.Property(r => r.MetricName).HasColumnName("metric_name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.Operator).HasColumnName("operator").HasMaxLength(10).IsRequired();
        builder.Property(r => r.ThresholdValue).HasColumnName("threshold_value").IsRequired();
        builder.Property(r => r.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired();
        builder.Property(r => r.Channels).HasColumnName("channels").IsRequired();
        builder.Property(r => r.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(r => r.MessageTemplate).HasColumnName("message_template").IsRequired();
    }
}
