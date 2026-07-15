using CapacityManager.Api.Data;
using CapacityManager.Api.Models;

namespace CapacityManager.Api.Services;

public record RebalanceReport(int MigrationsPerformed, List<string> Actions);

/// <summary>
/// Continuously (or on-demand) rebalances tenant placement to keep the fleet
/// within healthy utilization bounds, moving lower-priority tenants first.
/// </summary>
public interface IRebalancingService
{
    RebalanceReport Rebalance();
}

public class RebalancingService : IRebalancingService
{
    private const double OverloadedThreshold = 90.0;
    private const double UnderutilizedThreshold = 40.0;
    private const double DegradedThreshold = 75.0;

    private readonly ICapacityStore _store;
    private readonly IReliabilityTracker _reliabilityTracker;

    public RebalancingService(ICapacityStore store, IReliabilityTracker reliabilityTracker)
    {
        _store = store;
        _reliabilityTracker = reliabilityTracker;
    }

    public RebalanceReport Rebalance()
    {
        var actions = new List<string>();

        var overloaded = _store.Nodes.Values
            .Where(n => n.Status != NodeStatus.Offline && n.UtilizationPercent >= OverloadedThreshold)
            .ToList();

        foreach (var node in overloaded)
        {
            var movableTenants = node.TenantIds
                .Select(id => _store.Tenants.TryGetValue(id, out var t) ? t : null)
                .Where(t => t is not null)
                .OrderBy(t => t!.Priority) // Standard first, BusinessCritical last
                .ThenBy(t => t!.RequiredCapacityUnits)
                .ToList();

            foreach (var tenant in movableTenants)
            {
                if (node.UtilizationPercent < OverloadedThreshold)
                {
                    break;
                }

                var target = _store.Nodes.Values
                    .Where(n => n.Id != node.Id
                        && n.Status != NodeStatus.Offline
                        && n.AvailableCapacityUnits >= tenant!.RequiredCapacityUnits
                        && n.UtilizationPercent < UnderutilizedThreshold)
                    .OrderByDescending(n => n.AvailableCapacityUnits)
                    .FirstOrDefault();

                if (target is null)
                {
                    continue;
                }

                MigrateTenant(tenant!, node, target);
                actions.Add(
                    $"Migrated tenant '{tenant!.Name}' from node {node.Id} ({node.Region}) " +
                    $"to node {target.Id} ({target.Region}).");
            }
        }

        foreach (var action in actions)
        {
            _reliabilityTracker.RecordEvent(ReliabilityEventType.RebalanceMigration, action);
        }

        return new RebalanceReport(actions.Count, actions);
    }

    private static void MigrateTenant(Tenant tenant, DatabaseNode from, DatabaseNode to)
    {
        from.AllocatedCapacityUnits = Math.Max(0, from.AllocatedCapacityUnits - tenant.RequiredCapacityUnits);
        from.TenantIds.Remove(tenant.Id);

        to.AllocatedCapacityUnits += tenant.RequiredCapacityUnits;
        to.TenantIds.Add(tenant.Id);

        tenant.CurrentNodeId = to.Id;

        RecalculateStatus(from);
        RecalculateStatus(to);
    }

    private static void RecalculateStatus(DatabaseNode node)
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
