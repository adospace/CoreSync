---
layout: default
title: CoreSync vs Datasync Community Toolkit — Choosing the Right Sync Library
---

# CoreSync vs Datasync Community Toolkit — Choosing the Right Sync Library

Both CoreSync and the [Datasync Community Toolkit](https://github.com/CommunityToolkit/Datasync) are .NET libraries for synchronizing data between databases. They share the same ecosystem but take fundamentally different architectural approaches. This guide helps you decide which one fits your project.

## At a Glance

| | **CoreSync** | **Datasync Community Toolkit** |
|---|---|---|
| **Primary purpose** | Bidirectional database-to-database sync | Client-server offline sync (successor to Azure Mobile Apps) |
| **Architecture** | Library — direct provider-to-provider | Client-server — requires ASP.NET Core controllers |
| **Sync direction** | Bidirectional (peer-to-peer) | Bidirectional (client-server only) |
| **Server databases** | SQLite, SQL Server, PostgreSQL | SQL Server, PostgreSQL, MySQL, Cosmos DB, MongoDB, LiteDB |
| **Client databases** | Any supported provider | SQLite only |
| **Change detection** | Database triggers or SQL Server native CT | Timestamp-based (`UpdatedAt` field) |
| **Entity requirements** | No changes to your schema | Must implement `ITableData` (Id, UpdatedAt, Version, Deleted) |
| **Infrastructure** | None beyond your app | ASP.NET Core web server with OData controllers |
| **Minimum .NET version** | .NET Standard 2.0 / .NET 8.0 (HTTP) | .NET 10.0 |
| **License** | MIT | MIT |

## What Each Tool Does

### CoreSync — Database-to-Database Sync

CoreSync synchronizes two databases directly. There is no required server component — you connect two database providers and sync between them. Either side can be local or remote, and any supported database can sync with any other.

```
┌──────────────┐                        ┌──────────────┐
│  SQLite      │◄──── SyncAgent ──────► │  SQL Server  │
│              │   Direct sync — no     │              │
│              │   server required      │              │
└──────────────┘                        └──────────────┘
```

CoreSync creates tracking infrastructure (triggers and internal tables) inside the database itself. Your application code and schema remain unchanged. When you call `SynchronizeAsync()`, changes are detected, transferred, and applied — both directions — in a single operation.

### Datasync Community Toolkit — Client-Server Offline Sync

The Datasync Toolkit follows a strict client-server model inherited from Azure Mobile Apps. You build an ASP.NET Core web API server with dedicated `TableController<T>` endpoints for each synchronized entity. Clients use an `OfflineDbContext` (extending EF Core's `DbContext`) backed by local SQLite to queue operations offline and sync when connectivity is available.

```
┌──────────────┐        HTTPS/OData       ┌──────────────────┐
│  Client      │◄───────────────────────► │  ASP.NET Core    │
│  (SQLite)    │   Pull changes           │  Server          │
│  OfflineDb   │   Push operations        │  TableController │
│              │                          │  + EF Core / DB  │
└──────────────┘                          └──────────────────┘
```

Every synchronized entity must implement `ITableData`, adding four required properties: `Id` (string), `UpdatedAt` (DateTimeOffset), `Version` (byte[]), and `Deleted` (bool). The server maintains these fields automatically.

## Detailed Comparison

### Architecture & Topology

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Topology** | Any-to-any (peer-to-peer capable) | Client-server only |
| **Server requirement** | Optional (HTTP transport available but not required) | Mandatory ASP.NET Core server |
| **Database-to-database** | Yes — direct provider connection | No — always through server API |
| **Multiple clients** | Supported via HTTP server | Supported — primary design goal |
| **API style** | Binary (MessagePack) or custom | REST with OData v4 |

CoreSync can sync two databases directly in the same process (e.g., in an integration test or batch job) or over HTTP. The Datasync Toolkit always requires a deployed web server with controller endpoints.

**Choose CoreSync** if you want direct database-to-database sync without building a server API.

**Choose the Datasync Toolkit** if you already plan to build a REST API server and want a structured client-server sync framework.

### Schema & Entity Requirements

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Schema changes required** | No — tracking tables and triggers are separate | Yes — every entity needs `ITableData` (4 fields) |
| **Entity constraints** | Any table with a primary key | Flat entities only — no relationships across the wire |
| **Complex types** | Supported | Not supported over sync |
| **Existing database support** | Works with existing schemas | Requires schema modification |

CoreSync's trigger-based approach works with your existing tables without modification. It creates separate tracking infrastructure (`__CORE_SYNC_CT` tables and triggers) that are invisible to your application.

The Datasync Toolkit requires every synchronized entity to carry `Id`, `UpdatedAt`, `Version`, and `Deleted` fields. Relationships between entities (foreign keys, navigation properties) are explicitly not supported across the sync boundary — only flat/primitive data types are allowed.

### Change Detection

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Method** | Database triggers or SQL Server native CT | Timestamp-based (`UpdatedAt` comparison) |
| **Granularity** | Row-level with operation type (INSERT/UPDATE/DELETE) | Row-level via timestamp |
| **Maintained by** | Database engine (triggers fire automatically) | Server-side middleware or database |
| **Precision requirement** | None specific | Millisecond-accurate `UpdatedAt` required |
| **Risk of missed changes** | None — triggers capture every write | Possible if many records share the same millisecond timestamp |

CoreSync's trigger-based detection guarantees that every INSERT, UPDATE, and DELETE is captured with its operation type. The version counter is incremented atomically.

The Datasync Toolkit relies on the `UpdatedAt` timestamp to detect changes. The client stores the last-seen timestamp and requests all records newer than that value. This works well in most scenarios but can miss changes when high write volumes produce records with identical millisecond timestamps.

### Conflict Resolution

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Detection** | Version-based (anchor comparison) | Optimistic concurrency (`Version` byte array) |
| **Strategies** | Skip, ForceWrite | Client wins, Server wins, Custom |
| **Per-item control** | Yes — callback function per item | Yes — `IConflictResolver<T>` interface |
| **Default behavior** | Configurable per side | Conflict returned to caller (no auto-resolution) |
| **Merge support** | No (binary choice) | Custom resolver can merge fields |

Both libraries support per-item conflict resolution. CoreSync uses a simpler model (Skip or ForceWrite), while the Datasync Toolkit returns both the client and server versions of the conflicting entity, letting you implement custom merge logic.

### Database Support

| Database | CoreSync | Datasync Toolkit (Server) | Datasync Toolkit (Client) |
|---|---|---|---|
| SQLite | Yes | Not recommended (timestamp precision) | Yes (only option) |
| SQL Server / Azure SQL | Yes (custom triggers + native CT) | Yes | — |
| PostgreSQL | Yes | Yes | — |
| MySQL / MariaDB | — | Yes | — |
| Cosmos DB | — | Yes | — |
| MongoDB | — | Yes | — |
| LiteDB | — | Yes | — |
| In-Memory | — | Yes (testing) | — |

CoreSync uniquely supports **SQLite as both a source and target** for sync — not just as a client-side cache. This means you can sync SQLite-to-SQLite, SQLite-to-SQL Server, or SQLite-to-PostgreSQL in any direction.

The Datasync Toolkit has broader server-side database support (including NoSQL options), but the client is always SQLite.

### Sync Capabilities

| Capability | CoreSync | Datasync Toolkit |
|---|---|---|
| **Filtered sync** | Yes — parameterized queries | Yes — LINQ/OData queries with QueryId |
| **Initial snapshot** | Yes — automatic or skippable | Yes — first pull fetches all data |
| **Incremental sync** | Yes — version anchors | Yes — delta tokens (timestamp-based) |
| **Soft delete** | No — physical deletes tracked and synced | Yes — configurable `EnableSoftDelete` |
| **Offline queue** | No — sync is on-demand | Yes — operations queued in `OfflineDbContext` |
| **Bulk transfer** | Yes — configurable page size | Yes — paged responses (default 100 items) |
| **Parallel sync** | Sequential per sync call | 1-8 concurrent HTTP requests (configurable) |

The Datasync Toolkit has a more mature offline story with its operation queue — writes are captured locally and pushed when connectivity returns. CoreSync handles offline scenarios by syncing on-demand when the app decides to sync, but doesn't queue individual operations.

### Developer Experience

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Setup effort** | Add NuGet, configure tables, call sync | Create server project, add controllers, configure client DbContext |
| **Boilerplate** | Minimal — ~10 lines for basic sync | Moderate — controller per table, entity base class, client config |
| **Project template** | No | Yes — `dotnet new datasync-server` |
| **API documentation** | No built-in | OpenAPI/Swagger packages available |
| **Minimum .NET** | .NET Standard 2.0 (broad compatibility) | .NET 10.0 (latest only) |
| **Framework lock-in** | None — works with any .NET app | ASP.NET Core controllers (no minimal APIs) |

**CoreSync setup:**

```csharp
var local = new SqliteSyncProvider(localConfig);
var remote = new SqlSyncProvider(remoteConfig);

await local.ApplyProvisionAsync();
await remote.ApplyProvisionAsync();

var agent = new SyncAgent(local, remote);
await agent.SynchronizeAsync();
```

**Datasync Toolkit setup (server):**

```csharp
// Entity
public class TodoItem : EntityTableData  // inherits ITableData fields
{
    public string Title { get; set; }
    public bool IsComplete { get; set; }
}

// Controller
[Route("tables/[controller]")]
public class TodoItemController : TableController<TodoItem>
{
    public TodoItemController(AppDbContext context) : base()
    {
        Repository = new EntityTableRepository<TodoItem>(context);
    }
}
```

**Datasync Toolkit setup (client):**

```csharp
public class OfflineDb : OfflineDbContext
{
    public OfflineDb(DbContextOptions<OfflineDb> options) : base(options) { }
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    protected override void OnDatasyncInitialization(DatasyncOfflineOptionsBuilder builder)
    {
        builder.Entity<TodoItem>(cfg =>
        {
            cfg.ClientName = "datasync";
            cfg.Endpoint = new Uri("/tables/todoitem", UriKind.Relative);
        });
    }
}

// Sync
await db.PushAsync();
await db.PullAsync();
```

### .NET Version Compatibility

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Core library** | .NET Standard 2.0 | .NET 10.0 |
| **Compatible with** | .NET Framework 4.6.1+, .NET Core 2.0+, .NET 5-10 | .NET 10.0 only |
| **HTTP packages** | .NET 8.0 | N/A (client handles HTTP internally) |

CoreSync's .NET Standard 2.0 targeting means it works across a very wide range of .NET versions and platforms. The Datasync Toolkit requires the latest .NET version, which may be a barrier for projects that can't immediately adopt .NET 10.

### Authentication & Access Control

| | CoreSync | Datasync Toolkit |
|---|---|---|
| **Built-in auth** | No — transport-level only | Yes — `IAccessControlProvider<T>` |
| **Row-level security** | Via filtered sync queries | Via `GetDataView()` method |
| **Per-operation auth** | No | Yes — `IsAuthorizedAsync()` per CRUD operation |
| **Pre-commit hooks** | No | Yes — `PreCommitHookAsync()` to modify entities |

The Datasync Toolkit has a richer security model since it's designed as a server framework. CoreSync is a sync engine — security is handled at the transport or application layer.

## When to Choose CoreSync

CoreSync is the right choice when:

- **You need database-to-database sync** — no server API required
- **You want any-to-any database combinations** — SQLite ↔ SQL Server ↔ PostgreSQL in any direction
- **You need SQLite as a full sync participant** — not just a client cache
- **Your existing schema should stay unchanged** — no base classes or required fields
- **You need broad .NET compatibility** — .NET Standard 2.0 supports everything from .NET Framework 4.6.1 to .NET 10
- **You want minimal setup** — add a NuGet package, configure tables, sync
- **You don't want to build a server** — sync directly between databases
- **You have complex entity relationships** — CoreSync syncs table data as-is

**Ideal scenarios:**
- Mobile/desktop apps syncing a local SQLite database to a central server database
- Multi-site database replication (e.g., branch offices syncing to headquarters)
- Batch synchronization jobs between different database systems
- .NET Framework applications that need sync capabilities
- Projects where adding `ITableData` to every entity is not feasible

## When to Choose the Datasync Community Toolkit

The Datasync Toolkit is the right choice when:

- **You're building a client-server app with a REST API** — the server is already part of the architecture
- **You want a structured offline-first framework** — operation queuing, `OfflineDbContext`, delta tokens
- **You need built-in access control** — row-level security, per-operation authorization
- **You need NoSQL support on the server** — Cosmos DB, MongoDB, LiteDB
- **You want OData query capabilities** — rich filtering, sorting, and paging over HTTP
- **You're migrating from Azure Mobile Apps** — direct successor with backward-compatible clients
- **You need soft delete** — built-in support for logical deletes with purging
- **Your entities are flat/simple** — no complex relationships needed over the wire

**Ideal scenarios:**
- .NET MAUI apps with cloud backend (the primary design target)
- Enterprise mobile apps with per-user data access control
- Azure Mobile Apps migration projects
- Applications that need a REST API for both sync and direct access
- Projects already on .NET 10 that want a full-featured offline sync framework

## Side-by-Side Summary

| Need | Best fit |
|---|---|
| Database-to-database sync (no server) | **CoreSync** |
| Client-server with REST API | **Datasync Toolkit** |
| SQLite as a full sync peer | **CoreSync** |
| NoSQL databases (Cosmos, MongoDB) | **Datasync Toolkit** |
| Unchanged existing schema | **CoreSync** |
| Built-in access control / auth | **Datasync Toolkit** |
| .NET Standard 2.0 / broad compatibility | **CoreSync** |
| .NET 10 with latest patterns | **Datasync Toolkit** |
| Complex entity relationships | **CoreSync** |
| Flat entities with offline queue | **Datasync Toolkit** |
| Minimal infrastructure / setup | **CoreSync** |
| Structured server framework | **Datasync Toolkit** |
| Azure Mobile Apps migration | **Datasync Toolkit** |
| Any-to-any database combination | **CoreSync** |
| OData query support | **Datasync Toolkit** |
| Trigger-based change tracking | **CoreSync** |

Both libraries are MIT-licensed .NET projects solving data synchronization, but they target different architectural patterns. CoreSync is a sync engine you embed anywhere; the Datasync Toolkit is a client-server framework you build your application around.
