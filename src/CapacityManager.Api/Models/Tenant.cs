namespace CapacityManager.Api.Models;

public enum WorkloadPriority
{
    Standard,
    Premium,
    BusinessCritical
}

/// <summary>
/// Represents a database workload (tenant) that has been placed on a node.
/// </summary>
public class Tenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int RequiredCapacityUnits { get; set; }
    public WorkloadPriority Priority { get; set; } = WorkloadPriority.Standard;
    public string? CurrentNodeId { get; set; }
    public string PreferredRegion { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
