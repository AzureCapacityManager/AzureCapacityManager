namespace CapacityManager.Api.Services;

public enum ReliabilityEventType
{
    NodeOffline,
    NodeRecovered,
    PlacementFailure,
    Preemption,
    RebalanceMigration
}

public record ReliabilityEvent(ReliabilityEventType Type, string Message, DateTime TimestampUtc);

public record SloReport(
    double TargetUptimePercent,
    double ObservedUptimePercent,
    double ErrorBudgetRemainingPercent,
    int IncidentCount,
    DateTime WindowStartUtc);

/// <summary>
/// Tracks operational events and derives a lightweight SLO / error-budget report,
/// mirroring the "telemetry-driven development" and "incident prevention" goals
/// called out for this role. In production this would be backed by Azure Monitor /
/// Application Insights rather than an in-memory list.
/// </summary>
public interface IReliabilityTracker
{
    void RecordEvent(ReliabilityEventType type, string message);
    IReadOnlyList<ReliabilityEvent> RecentEvents(int count = 50);
    SloReport GetSloReport();
}

public class ReliabilityTracker : IReliabilityTracker
{
    private const double TargetUptimePercent = 99.9;
    private static readonly ReliabilityEventType[] IncidentTypes =
    {
        ReliabilityEventType.NodeOffline,
        ReliabilityEventType.PlacementFailure
    };

    private readonly List<ReliabilityEvent> _events = new();
    private readonly object _lock = new();
    private readonly DateTime _windowStart = DateTime.UtcNow;

    public void RecordEvent(ReliabilityEventType type, string message)
    {
        lock (_lock)
        {
            _events.Add(new ReliabilityEvent(type, message, DateTime.UtcNow));
        }
    }

    public IReadOnlyList<ReliabilityEvent> RecentEvents(int count = 50)
    {
        lock (_lock)
        {
            return _events.OrderByDescending(e => e.TimestampUtc).Take(count).ToList();
        }
    }

    public SloReport GetSloReport()
    {
        lock (_lock)
        {
            var incidents = _events.Count(e => IncidentTypes.Contains(e.Type));

            var windowMinutes = Math.Max(1, (DateTime.UtcNow - _windowStart).TotalMinutes);

            // Simplified model for demo purposes: each incident is treated as ~1 minute
            // of degraded service. Swap in real downtime measurement (e.g. from Azure
            // Monitor availability tests) in production.
            var downtimeMinutes = Math.Min(windowMinutes, incidents);
            var observedUptime = Math.Round(100 - (downtimeMinutes / windowMinutes * 100), 4);

            var errorBudgetTotal = 100 - TargetUptimePercent;
            var errorBudgetUsed = Math.Max(0, TargetUptimePercent - observedUptime);
            var errorBudgetRemaining = errorBudgetTotal <= 0
                ? 0
                : Math.Max(0, Math.Round(100 - (errorBudgetUsed / errorBudgetTotal * 100), 2));

            return new SloReport(TargetUptimePercent, observedUptime, errorBudgetRemaining, incidents, _windowStart);
        }
    }
}
