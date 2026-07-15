using CapacityManager.Api.Data;
using CapacityManager.Api.Models;

namespace CapacityManager.Api.Services;

public record PlacementResult(bool Success, string? NodeId, string? TenantId, string Message);

/// <summary>
/// Owns tenant -> node placement decisions: allocation, deallocation, and
/// priority-aware preemption for BusinessCritical workloads.
/// </summary>
public interface IPlacementService
{
    PlacementResult Allocate(AllocationRequest request);
    bool Deallocate(string tenantId);
}

public class PlacementService : IPlacementService
{
    private const double OverloadedThreshold = 90.0;
    private const double DegradedThreshold = 75.0;

    private readonly ICapacityStore _store;
    private readonly IReliabilityTracker _reliabilityTracker;

    public PlacementService(ICapacityStore store, IReliabilityTracker reliabilityTracker)
    {
        _store = store;
        _reliabilityTracker = reliabilityTracker;
    }

    public PlacementResult Allocate(AllocationRequest request)
    {
        if (request.RequiredCapacityUnits <= 0)
        {
            return new PlacementResult(false, null, null, "RequiredCapacityUnits must be greater than zero.");
        }

        var healthyNodes = _store.Nodes.Values
            .Where(n => n.Status == NodeStatus.Healthy || n.Status == NodeStatus.Degraded)
            .ToList();

        var candidate = SelectBestFit(healthyNodes, request.PreferredRegion, request.RequiredCapacityUnits)
            ?? SelectBestFit(healthyNodes, region: null, request.RequiredCapacityUnits);

        if (candidate is null && request.Priority == WorkloadPriority.BusinessCritical)
        {
            var eligibleForPreemption = _store.Nodes.Values
                .Where(n => n.Status != NodeStatus.Offline)
                .ToList();

            candidate = TryPreemptForBusinessCritical(eligibleForPreemption, request.RequiredCapacityUnits);
        }

        if (candidate is null)
        {
            _reliabilityTracker.RecordEvent(
                ReliabilityEventType.PlacementFailure,
                $"No capacity available for tenant '{request.TenantName}' " +
                $"({request.RequiredCapacityUnits} units, {request.Priority}).");

            return new PlacementResult(false, null, null, "No node with sufficient available capacity was found.");
        }

        var tenant = new Tenant
        {
            Name = request.TenantName,
            RequiredCapacityUnits = request.RequiredCapacityUnits,
            Priority = request.Priority,
            PreferredRegion = request.PreferredRegion,
            CurrentNodeId = candidate.Id
        };

        _store.Tenants[tenant.Id] = tenant;
        candidate.AllocatedCapacityUnits += request.RequiredCapacityUnits;
        candidate.TenantIds.Add(tenant.Id);
        UpdateNodeStatus(candidate);

        return new PlacementResult(
            true,
            candidate.Id,
            tenant.Id,
            $"Tenant '{tenant.Name}' placed on node {candidate.Id} in {candidate.Region}.");
    }

    public bool Deallocate(string tenantId)
    {
        if (!_store.Tenants.TryRemove(tenantId, out var tenant))
        {
            return false;
        }

        if (tenant.CurrentNodeId is not null && _store.Nodes.TryGetValue(tenant.CurrentNodeId, out var node))
        {
            node.AllocatedCapacityUnits = Math.Max(0, node.AllocatedCapacityUnits - tenant.RequiredCapacityUnits);
            node.TenantIds.Remove(tenantId);
            UpdateNodeStatus(node);
        }

        return true;
    }

    private static DatabaseNode? SelectBestFit(IEnumerable<DatabaseNode> nodes, string? region, int requiredUnits)
    {
        var pool = string.IsNullOrWhiteSpace(region)
            ? nodes
            : nodes.Where(n => n.Region.Equals(region, StringComparison.OrdinalIgnoreCase));

        // Tightest-fit-first: pick the node that leaves the least slack, which
        // reduces fragmentation across the fleet (classic bin-packing heuristic).
        return pool
            .Where(n => n.AvailableCapacityUnits >= requiredUnits)
            .OrderBy(n => n.AvailableCapacityUnits - requiredUnits)
            .FirstOrDefault();
    }

    private DatabaseNode? TryPreemptForBusinessCritical(List<DatabaseNode> nodes, int requiredUnits)
    {
        // Look for a node hosting a Standard-priority tenant where evicting the
        // smallest such tenant frees enough room for a BusinessCritical workload.
        // This mirrors priority-aware placement used by mission-critical DB fleets.
        foreach (var node in nodes.OrderByDescending(n => n.AvailableCapacityUnits))
        {
            var evictable = node.TenantIds
                .Select(id => _store.Tenants.TryGetValue(id, out var t) ? t : null)
                .Where(t => t is not null && t!.Priority == WorkloadPriority.Standard)
                .OrderBy(t => t!.RequiredCapacityUnits)
                .FirstOrDefault();

            if (evictable is null)
            {
                continue;
            }

            if (node.AvailableCapacityUnits + evictable.RequiredCapacityUnits >= requiredUnits)
            {
                Deallocate(evictable.Id);
                _reliabilityTracker.RecordEvent(
                    ReliabilityEventType.Preemption,
                    $"Preempted tenant '{evictable.Name}' on node {node.Id} to make room for a BusinessCritical workload.");

                return node;
            }
        }

        return null;
    }

    private static void UpdateNodeStatus(DatabaseNode node)
    {
        if (node.Status == NodeStatus.Offline)
        {
            return;
        }

        node.Status = node.UtilizationPercent switch
        {
            >= OverloadedThreshold => NodeStatus.Overloaded,
            >= DegradedThreshold => NodeStatus.Degraded,
            _ => NodeStatus.Healthy
        };
    }
}
