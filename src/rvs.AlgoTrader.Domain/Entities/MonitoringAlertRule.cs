namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Persisted alert rule stored in monitoring_alert_rules (InitialMigration.sql).
/// Used by AlertsController CRUD and eventually by MonitoringAlertJob for
/// configurable threshold evaluation.
/// </summary>
public class MonitoringAlertRule
{
    public Guid   Id               { get; set; }
    public string AlertType        { get; set; } = string.Empty;
    public string MetricName       { get; set; } = string.Empty;
    public string Operator         { get; set; } = ">";
    public double ThresholdValue   { get; set; }
    public string Severity         { get; set; } = "WARNING";
    public string[] Channels       { get; set; } = [];
    public bool   IsActive         { get; set; } = true;
    public string MessageTemplate  { get; set; } = string.Empty;
    /// <summary>Owner user. Null = system-wide rule visible to all users (pre-migration 050 rows).</summary>
    public Guid?  UserId           { get; set; }
}
