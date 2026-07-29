using CapacityManager.Api.Data;
using CapacityManager.Api.Models;
using CapacityManager.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICapacityStore, InMemoryCapacityStore>();
builder.Services.AddSingleton<IReliabilityTracker, ReliabilityTracker>();
builder.Services.AddScoped<IPlacementService, PlacementService>();
builder.Services.AddScoped<IRebalancingService, RebalancingService>();
builder.Services.AddHostedService<NodeHealthMonitorService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/nodes", (RegisterNodeRequest request, ICapacityStore capacityStore) =>
{
    if (request.TotalCapacityUnits <= 0)
    {
        return Results.BadRequest("TotalCapacityUnits must be greater than zero.");
    }

    var node = new DatabaseNode
    {
        Region = request.Region,
        TotalCapacityUnits = request.TotalCapacityUnits
    };

    capacityStore.Nodes[node.Id] = node;
    return Results.Created($"/api/nodes/{node.Id}", node);
})
.WithName("RegisterNode");

app.MapGet("/api/nodes", (ICapacityStore capacityStore) =>
    Results.Ok(capacityStore.Nodes.Values.OrderBy(n => n.Region).ThenBy(n => n.Id)))
.WithName("ListNodes");

app.MapGet("/api/nodes/{id}", (string id, ICapacityStore capacityStore) =>
    capacityStore.Nodes.TryGetValue(id, out var node) ? Results.Ok(node) : Results.NotFound())
.WithName("GetNode");

app.MapPost("/api/nodes/{id}/heartbeat", (string id, HeartbeatRequest? request, ICapacityStore capacityStore) =>
{
    if (!capacityStore.Nodes.TryGetValue(id, out var node))
    {
        return Results.NotFound();
    }

    node.LastHeartbeatUtc = DateTime.UtcNow;
    if (request?.ReportedStatus is not null && node.Status != NodeStatus.Offline)
    {
        node.Status = request.ReportedStatus.Value;
    }

    return Results.Ok(node);
})
.WithName("Heartbeat");

app.MapPost("/api/tenants/allocate", (AllocationRequest request, IPlacementService placementService) =>
{
    var result = placementService.Allocate(request);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
})
.WithName("AllocateTenant");

app.MapDelete("/api/tenants/{id}", (string id, IPlacementService placementService) =>
    placementService.Deallocate(id) ? Results.NoContent() : Results.NotFound())
.WithName("DeallocateTenant");

app.MapGet("/api/tenants", (ICapacityStore capacityStore) =>
    Results.Ok(capacityStore.Tenants.Values.OrderBy(t => t.Name)))
.WithName("ListTenants");

app.MapPost("/api/rebalance/trigger", (IRebalancingService rebalancingService) =>
    Results.Ok(rebalancingService.Rebalance()))
.WithName("TriggerRebalance");

app.MapGet("/api/health", (IReliabilityTracker reliabilityTracker) =>
    Results.Ok(reliabilityTracker.GetSloReport()))
.WithName("HealthSlo");

app.MapGet("/api/events", (IReliabilityTracker reliabilityTracker) =>
    Results.Ok(reliabilityTracker.RecentEvents()))
.WithName("RecentEvents");

app.MapGet("/api/metrics", (ICapacityStore capacityStore) =>
{
    var lines = new List<string>
    {
        "# HELP node_utilization_percent Current utilization percentage per database node.",
        "# TYPE node_utilization_percent gauge"
    };

    foreach (var node in capacityStore.Nodes.Values)
    {
        lines.Add(
            $"node_utilization_percent{{node_id=\"{node.Id}\",region=\"{node.Region}\",status=\"{node.Status}\"}} " +
            $"{node.UtilizationPercent}");
    }

    return Results.Text(string.Join("\n", lines), "text/plain");
})
.WithName("PrometheusMetrics");

app.Run();

public partial class Program
{
}
