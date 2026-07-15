using CapacityManager.Api.Data;
using CapacityManager.Api.Models;
using CapacityManager.Api.Services;
using Xunit;

namespace CapacityManager.Tests;

public class PlacementServiceTests
{
    private static (PlacementService placement, InMemoryCapacityStore store) CreateService()
    {
        var store = new InMemoryCapacityStore();
        var tracker = new ReliabilityTracker();
        var placement = new PlacementService(store, tracker);
        return (placement, store);
    }

    [Fact]
    public void Allocate_PlacesTenant_OnNodeWithEnoughCapacity()
    {
        var (placement, store) = CreateService();
        var node = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100 };
        store.Nodes[node.Id] = node;

        var result = placement.Allocate(new AllocationRequest("tenant-a", 40, "eastus", WorkloadPriority.Standard));

        Assert.True(result.Success);
        Assert.Equal(node.Id, result.NodeId);
        Assert.Equal(40, node.AllocatedCapacityUnits);
    }

    [Fact]
    public void Allocate_PrefersTightestFit_ToReduceFragmentation()
    {
        var (placement, store) = CreateService();
        var roomyNode = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 200 };
        var tightNode = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 50 };
        store.Nodes[roomyNode.Id] = roomyNode;
        store.Nodes[tightNode.Id] = tightNode;

        var result = placement.Allocate(new AllocationRequest("tenant-a", 40, "eastus", WorkloadPriority.Standard));

        Assert.True(result.Success);
        Assert.Equal(tightNode.Id, result.NodeId);
    }

    [Fact]
    public void Allocate_FallsBackToOtherRegion_WhenPreferredRegionIsFull()
    {
        var (placement, store) = CreateService();
        var fullNode = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 10 };
        var otherNode = new DatabaseNode { Region = "westus", TotalCapacityUnits = 100 };
        store.Nodes[fullNode.Id] = fullNode;
        store.Nodes[otherNode.Id] = otherNode;

        var result = placement.Allocate(new AllocationRequest("tenant-a", 50, "eastus", WorkloadPriority.Standard));

        Assert.True(result.Success);
        Assert.Equal(otherNode.Id, result.NodeId);
    }

    [Fact]
    public void Allocate_ReturnsFailure_WhenNoCapacityAnywhere()
    {
        var (placement, store) = CreateService();
        var node = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 10 };
        store.Nodes[node.Id] = node;

        var result = placement.Allocate(new AllocationRequest("tenant-a", 50, "eastus", WorkloadPriority.Standard));

        Assert.False(result.Success);
    }

    [Fact]
    public void Allocate_PreemptsStandardTenant_ForBusinessCriticalWorkload()
    {
        var (placement, store) = CreateService();
        var node = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100 };
        store.Nodes[node.Id] = node;

        var initial = placement.Allocate(new AllocationRequest("standard-tenant", 90, "eastus", WorkloadPriority.Standard));
        Assert.True(initial.Success);

        var critical = placement.Allocate(new AllocationRequest("critical-tenant", 50, "eastus", WorkloadPriority.BusinessCritical));

        Assert.True(critical.Success);
        Assert.Equal(node.Id, critical.NodeId);
        Assert.False(store.Tenants.ContainsKey(initial.TenantId!));
    }

    [Fact]
    public void Deallocate_FreesCapacity_OnNode()
    {
        var (placement, store) = CreateService();
        var node = new DatabaseNode { Region = "eastus", TotalCapacityUnits = 100 };
        store.Nodes[node.Id] = node;

        var result = placement.Allocate(new AllocationRequest("tenant-a", 40, "eastus", WorkloadPriority.Standard));
        var removed = placement.Deallocate(result.TenantId!);

        Assert.True(removed);
        Assert.Equal(0, node.AllocatedCapacityUnits);
        Assert.Empty(node.TenantIds);
    }

    [Fact]
    public void Deallocate_ReturnsFalse_ForUnknownTenant()
    {
        var (placement, _) = CreateService();

        var removed = placement.Deallocate("does-not-exist");

        Assert.False(removed);
    }
}
