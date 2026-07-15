using System.Collections.Concurrent;
using CapacityManager.Api.Models;

namespace CapacityManager.Api.Data;

/// <summary>
/// Abstraction over the capacity data store. The in-memory implementation is used
/// for this demo/simulation; swap in a real backing store (Azure SQL DB, Cosmos DB,
/// or a distributed cache) for production use without changing consumers.
/// </summary>
public interface ICapacityStore
{
    ConcurrentDictionary<string, DatabaseNode> Nodes { get; }
    ConcurrentDictionary<string, Tenant> Tenants { get; }
}

public class InMemoryCapacityStore : ICapacityStore
{
    public ConcurrentDictionary<string, DatabaseNode> Nodes { get; } = new();
    public ConcurrentDictionary<string, Tenant> Tenants { get; } = new();
}
