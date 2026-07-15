# Architecture

## Overview

```
                       ┌─────────────────────────────┐
                       │        REST API (ASP.NET)   │
                       │  /api/nodes  /api/tenants    │
                       │  /api/rebalance  /api/health │
                       │  /api/metrics                │
                       └──────────────┬───────────────┘
                                      │
              ┌───────────────────────┼───────────────────────┐
              │                       │                       │
    ┌─────────▼─────────┐  ┌──────────▼──────────┐  ┌─────────▼─────────┐
    │  PlacementService  │  │ RebalancingService   │  │ ReliabilityTracker │
    │  (allocate/evict)  │  │ (fleet load balance) │  │ (SLO/error budget) │
    └─────────┬─────────┘  └──────────┬───────────┘  └─────────┬─────────┘
              │                       │                        │
              └───────────┬───────────┴────────────────────────┘
                           │
                 ┌─────────▼──────────┐
                 │   ICapacityStore    │   (in-memory today; swap for
                 │  Nodes / Tenants    │    Azure SQL DB / Cosmos DB)
                 └─────────┬──────────┘
                           │
                 ┌─────────▼──────────┐
                 │ NodeHealthMonitor    │  (BackgroundService)
                 │ - heartbeat sweep    │
                 │ - triggers rebalance │
                 └──────────────────────┘
```

## Core concepts

**DatabaseNode** — a capacity unit (analogous to an Azure SQL elastic pool or
managed instance) with a total capacity, current allocation, region, and
health status (`Healthy`, `Degraded`, `Overloaded`, `Offline`).

**Tenant** — a workload requesting a fixed amount of capacity, tagged with a
`WorkloadPriority` (`Standard`, `Premium`, `BusinessCritical`) that drives both
placement and rebalancing decisions.

## Placement algorithm

1. Filter to non-overloaded, non-offline nodes.
2. Prefer the requester's preferred region; fall back to any region.
3. Within the candidate pool, pick the **tightest fit** (least leftover
   capacity) — a standard bin-packing heuristic that reduces fragmentation
   across the fleet.
4. If no node has room and the workload is `BusinessCritical`, attempt
   **preemption**: find a node hosting a `Standard`-priority tenant whose
   eviction would free enough capacity, evict it, and place the critical
   workload there. Evicted tenants are recorded as reliability events so the
   trade-off is auditable.

## Rebalancing algorithm

Runs on a timer (via `NodeHealthMonitorService`) and on-demand via
`POST /api/rebalance/trigger`:

1. Find nodes at or above 90% utilization.
2. For each, pick movable tenants — lowest priority and smallest footprint
   first — and look for a target node under 40% utilization with enough
   available capacity.
3. Migrate and log the action as a reliability event.

## Reliability / SLO tracking

`ReliabilityTracker` records structured events (`NodeOffline`,
`PlacementFailure`, `Preemption`, `RebalanceMigration`, `NodeRecovered`) and
derives a simplified SLO report — observed uptime against a 99.9% target and
remaining error budget — exposed at `GET /api/health`. `GET /api/metrics`
exposes per-node utilization in Prometheus text format for scraping.

## Extending toward real Azure services

This project intentionally uses in-memory storage and a background timer so
it runs anywhere with no cloud dependency. To take it further:

- **Azure SQL Database**: replace `InMemoryCapacityStore` with an
  `ICapacityStore` implementation backed by EF Core against Azure SQL DB,
  giving you durable, queryable state across restarts.
- **Azure Service Bus**: publish `ReliabilityEvent`s to a topic instead of
  (or in addition to) the in-memory list, so downstream services (alerting,
  dashboards) can subscribe.
- **Azure Monitor / Application Insights**: replace the simplified uptime
  math in `ReliabilityTracker` with real availability data pulled from
  Monitor, and forward the `/api/metrics` Prometheus output via the Azure
  Monitor managed Prometheus integration.
- **Azure Event Grid**: emit node status transitions (`Healthy` →
  `Overloaded` → `Offline`) as events for event-driven autoscaling.
