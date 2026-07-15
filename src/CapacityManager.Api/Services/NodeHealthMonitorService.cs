using CapacityManager.Api.Data;
using CapacityManager.Api.Models;

namespace CapacityManager.Api.Services;

/// <summary>
/// Background worker that watches node heartbeats (marking stale nodes Offline /
/// recovering them) and periodically triggers a rebalance cycle. In production
/// this would emit metrics to Azure Monitor and could publish state-change events
/// to Azure Service Bus / Event Grid for downstream consumers.
/// </summary>
public class NodeHealthMonitorService : BackgroundService
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    private readonly ICapacityStore _store;
    private readonly IReliabilityTracker _reliabilityTracker;
    private readonly IRebalancingService _rebalancingService;
    private readonly ILogger<NodeHealthMonitorService> _logger;

    public NodeHealthMonitorService(
        ICapacityStore store,
        IReliabilityTracker reliabilityTracker,
        IRebalancingService rebalancingService,
        ILogger<NodeHealthMonitorService> logger)
    {
        _store = store;
        _reliabilityTracker = reliabilityTracker;
        _rebalancingService = rebalancingService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckHeartbeats();

                var report = _rebalancingService.Rebalance();
                if (report.MigrationsPerformed > 0)
                {
                    _logger.LogInformation("Rebalance cycle performed {Count} migrations.", report.MigrationsPerformed);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during node health monitor cycle.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Expected during shutdown.
            }
        }
    }

    private void CheckHeartbeats()
    {
        var now = DateTime.UtcNow;

        foreach (var node in _store.Nodes.Values)
        {
            var isStale = now - node.LastHeartbeatUtc > HeartbeatTimeout;

            if (isStale && node.Status != NodeStatus.Offline)
            {
                node.Status = NodeStatus.Offline;
                _reliabilityTracker.RecordEvent(
                    ReliabilityEventType.NodeOffline,
                    $"Node {node.Id} ({node.Region}) marked Offline after missed heartbeats.");
                _logger.LogWarning("Node {NodeId} marked Offline due to stale heartbeat.", node.Id);
            }
            else if (!isStale && node.Status == NodeStatus.Offline)
            {
                node.Status = node.UtilizationPercent switch
                {
                    >= 90 => NodeStatus.Overloaded,
                    >= 75 => NodeStatus.Degraded,
                    _ => NodeStatus.Healthy
                };

                _reliabilityTracker.RecordEvent(
                    ReliabilityEventType.NodeRecovered,
                    $"Node {node.Id} ({node.Region}) recovered and resumed heartbeats.");
            }
        }
    }
}
