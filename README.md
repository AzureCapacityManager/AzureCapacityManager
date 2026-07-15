# Azure Capacity Manager (Simulator)

A small but complete C#/.NET 8 service that simulates **capacity allocation,
placement, and lifecycle management** for database workloads across a fleet
of nodes — the kind of problem a database capacity-management platform team
works on. Built to demonstrate systems-level backend engineering in C#, plus
reliability/observability practices (SLOs, error budgets, structured
metrics), independent of any single cloud provider so it can be cloned and
run anywhere.

## What it demonstrates

- **Capacity allocation & placement** — tightest-fit bin packing across
  nodes, with region affinity and a fallback path.
- **Priority-aware preemption** — `BusinessCritical` workloads can preempt
  lower-priority tenants when the fleet is full.
- **Lifecycle management** — nodes report heartbeats; a background worker
  marks stale nodes `Offline` and recovers them automatically.
- **Fleet rebalancing** — a background cycle (and an on-demand endpoint)
  migrates tenants off overloaded nodes onto underutilized ones.
- **Reliability engineering** — an SLO/error-budget report and a
  Prometheus-style `/api/metrics` endpoint.
- **Testability** — unit tests around the placement, rebalancing, and
  reliability-tracking logic.
- **Modern engineering practices** — Docker, GitHub Actions CI (build +
  test), Swagger/OpenAPI docs.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the design in detail,
including how each piece maps onto real Azure services (Azure SQL DB, Service
Bus, Monitor, Event Grid).

## Project layout

```
AzureCapacityManager/
├── src/CapacityManager.Api/     # ASP.NET Core minimal API
│   ├── Models/                  # DatabaseNode, Tenant, request DTOs
│   ├── Services/                # Placement, Rebalancing, Reliability, Health monitor
│   ├── Data/                    # In-memory capacity store
│   └── Program.cs               # Endpoint wiring
├── tests/CapacityManager.Tests/ # xUnit tests
├── docker/                      # Dockerfile + docker-compose
├── docs/ARCHITECTURE.md
├── scripts/seed-demo-data.sh    # curl-based demo seeding script
└── .github/workflows/ci.yml     # build + test on push/PR
```

## Running locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone <this-repo-url>
cd AzureCapacityManager

dotnet restore
dotnet run --project src/CapacityManager.Api
```

The API listens on `http://localhost:5080` by default (see
`Properties/launchSettings.json`) and opens Swagger UI at `/swagger` in
Development mode.

## Running with Docker

```bash
docker compose -f docker/docker-compose.yml up --build
```

The API will be available at `http://localhost:8080`.

## Running the tests

```bash
dotnet test
```

## Trying it out

With the API running, seed some demo data:

```bash
./scripts/seed-demo-data.sh
```

Or drive it manually with curl:

```bash
# Register a node
curl -X POST http://localhost:5080/api/nodes \
  -H "Content-Type: application/json" \
  -d '{"region": "eastus", "totalCapacityUnits": 100}'

# Allocate a tenant workload
curl -X POST http://localhost:5080/api/tenants/allocate \
  -H "Content-Type: application/json" \
  -d '{"tenantName": "orders-db", "requiredCapacityUnits": 40, "preferredRegion": "eastus", "priority": "Standard"}'

# View fleet state
curl http://localhost:5080/api/nodes

# Trigger a manual rebalance
curl -X POST http://localhost:5080/api/rebalance/trigger

# SLO / error-budget report
curl http://localhost:5080/api/health

# Prometheus-style metrics
curl http://localhost:5080/api/metrics
```

## API summary

| Method | Route                        | Description                                  |
|--------|-------------------------------|-----------------------------------------------|
| POST   | `/api/nodes`                  | Register a new capacity node                  |
| GET    | `/api/nodes`                  | List all nodes with current utilization       |
| GET    | `/api/nodes/{id}`              | Get a single node                              |
| POST   | `/api/nodes/{id}/heartbeat`    | Send a heartbeat (keeps node out of Offline)   |
| POST   | `/api/tenants/allocate`        | Request placement for a new workload           |
| DELETE | `/api/tenants/{id}`            | Deallocate a tenant, freeing its capacity       |
| GET    | `/api/tenants`                 | List all placed tenants                        |
| POST   | `/api/rebalance/trigger`       | Manually trigger a rebalance cycle             |
| GET    | `/api/health`                  | SLO / error-budget report                      |
| GET    | `/api/events`                  | Recent reliability events                      |
| GET    | `/api/metrics`                 | Prometheus-style utilization metrics           |

## Why this project

This was built to close a specific skills gap for a database capacity
platform role: hands-on C# systems code, and a concrete example of
capacity-allocation, placement, and reliability-engineering thinking, in a
form that's easy to read end-to-end in one sitting.

## License

MIT — see [`LICENSE`](LICENSE).
