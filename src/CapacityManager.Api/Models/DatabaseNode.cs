namespace CapacityManager.Api.Models;

public enum NodeStatus
{
    Healthy,
    Degraded,
    Overloaded,
    Offline
}

/// <summary>
/// Represents a physical/virtual capacity unit (analogous to a SQL DB elastic pool
/// or managed instance node) that can host one or more tenant workloads.
/// </summary>
public class DatabaseNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Region { get; set; } = string.Empty;
    public int TotalCapacityUnits { get; set; }
    public int AllocatedCapacityUnits { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Healthy;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public List<string> TenantIds { get; set; } = new();

    public int AvailableCapacityUnits => TotalCapacityUnits - AllocatedCapacityUnits;

    public double UtilizationPercent =>
        TotalCapacityUnits == 0 ? 0 : Math.Round((double)AllocatedCapacityUnits / TotalCapacityUnits * 100, 2);
}
