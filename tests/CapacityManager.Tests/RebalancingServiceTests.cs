using CapacityManager.Api.Data;
using CapacityManager.Api.Models;
using CapacityManager.Api.Services;
using Xunit;

namespace CapacityManager.Tests;

public class RebalancingServiceTests
{
    [Fact]
    public void Rebalance_MigratesTenant_FromOverloadedToUnderutilizedNode()
    {
        var store = new InMemoryCapacityStore();
        var tracker = new ReliabilityTracker();

        var busyNode = new DatabaseNode
        {
            Region = "eastus",
            TotalCapacityUnits = 100,
            AllocatedCapacityUnits = 95,
            Status = NodeStatus.Overloaded
        };
        var quietNode = new DatabaseNode
        {
            Region = "eastus",
            TotalCapacityUnits = 100,
            AllocatedCapacityUnits = 10,
            Status = NodeStatus.Healthy
        };

        var tenant = new Tenant
        {
            Name = "movable-tenant",
            RequiredCapacityUnits = 30,
            Priority = WorkloadPriority.Standard,
            CurrentNodeId = busyNode.Id
        };
        busyNode.TenantIds.Add(tenant.Id);

        store.Nodes[busyNode.Id] = busyNode;
        store.Nodes[quietNode.Id] = quietNode;
        store.Tenants[tenant.Id] = tenant;

        var rebalancer = new RebalancingService(store, tracker);
        var report = rebalancer.Rebalance();

        Assert.Equal(1, report.MigrationsPerformed);
        Assert.Equal(quietNode.Id, tenant.CurrentNodeId);
        Assert.Contains(tenant.Id, quietNode.TenantIds);
        Assert.DoesNotContain(tenant.Id, busyNode.TenantIds);
    }

    [Fact]
    public void Rebalance_DoesNothing_WhenAllNodesAreBalanced()
    {
        var store = new InMemoryCapacityStore();
        var tracker = new ReliabilityTracker();

        var nodeA = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100, AllocatedCapacityUnits = 50, Status = NodeStatus.Healthy };
        var nodeB = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100, AllocatedCapacityUnits = 55, Status = NodeStatus.Healthy };

        store.Nodes[nodeA.Id] = nodeA;
        store.Nodes[nodeB.Id] = nodeB;

        var rebalancer = new RebalancingService(store, tracker);
        var report = rebalancer.Rebalance();

        Assert.Equal(0, report.MigrationsPerformed);
    }

    [Fact]
    public void Rebalance_SkipsOfflineNodes_AsMigrationTargets()
    {
        var store = new InMemoryCapacityStore();
        var tracker = new ReliabilityTracker();

        var busyNode = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100, AllocatedCapacityUnits = 95, Status = NodeStatus.Overloaded };
        var offlineNode = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100, AllocatedCapacityUnits = 0, Status = NodeStatus.Offline };

        var tenant = new Tenant { Name = "tenant-x", RequiredCapacityUnits = 20, Priority = WorkloadPriority.Standard, CurrentNodeId = busyNode.Id };
        busyNode.TenantIds.Add(tenant.Id);

        store.Nodes[busyNode.Id] = busyNode;
        store.Nodes[offlineNode.Id] = offlineNode;
        store.Tenants[tenant.Id] = tenant;

        var rebalancer = new RebalancingService(store, tracker);
        var report = rebalancer.Rebalance();

        Assert.Equal(0, report.MigrationsPerformed);
    }
}
