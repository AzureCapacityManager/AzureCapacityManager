namespace CapacityManager.Api.Models;

public record RegisterNodeRequest(string Region, int TotalCapacityUnits);

public record AllocationRequest(
    string TenantName,
    int RequiredCapacityUnits,
    string PreferredRegion,
    WorkloadPriority Priority);

public record HeartbeatRequest(NodeStatus? ReportedStatus);
