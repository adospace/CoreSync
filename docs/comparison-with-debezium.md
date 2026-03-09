---
layout: default
title: CoreSync vs Debezium — Choosing the Right CDC Tool
---

# CoreSync vs Debezium — Choosing the Right CDC Tool

Both CoreSync and [Debezium](https://debezium.io/) deal with change data capture (CDC), but they solve fundamentally different problems. This guide helps you decide which tool fits your needs.

## At a Glance

| | **CoreSync** | **Debezium** |
|---|---|---|
| **Primary purpose** | Bidirectional data sync between databases | Unidirectional change streaming from databases |
| **Language / Ecosystem** | .NET (C#) | Java (JVM / Kafka ecosystem) |
| **Sync direction** | Bidirectional | Unidirectional (source to consumers) |
| **Infrastructure required** | None beyond your app and databases | Kafka + Kafka Connect (primary mode), or standalone JVM server |
| **Supported databases** | SQLite, SQL Server, PostgreSQL | MySQL, MariaDB, PostgreSQL, SQL Server, Oracle, MongoDB, Db2, Cassandra, Spanner, Vitess, Informix |
| **Change detection method** | Trigger-based or SQL Server native CT | Transaction log reading (binlog, WAL, redo logs) |
| **Conflict resolution** | Built-in (Skip / ForceWrite with per-item control) | N/A (unidirectional — no conflicts) |
| **Target framework** | .NET Standard 2.0 / .NET 8.0 | Java 11+ |
| **License** | MIT | Apache 2.0 |
| **Deployment** | NuGet package in your .NET app | Kafka Connect cluster, standalone server, or embedded Java library |

## What Each Tool Does

### CoreSync — Bidirectional Database Synchronization

CoreSync keeps two databases in sync. Changes made on either side are detected, transferred, and applied to the other side. Think of it as **merge replication** — a local SQLite database on a mobile app stays synchronized with a central SQL Server, and changes flow both ways.

**Typical architecture:**

```
┌──────────────┐                        ┌──────────────┐
│  Local DB    │                        │  Remote DB   │
│  (SQLite)    │◄──── SyncAgent ──────► │ (SQL Server) │
│              │   1. Upload changes    │              │
│              │   2. Download changes  │              │
│              │   3. Save anchors      │              │
└──────────────┘                        └──────────────┘
```

**Key characteristics:**
- Embedded library — no external infrastructure
- Changes are detected via triggers (or SQL Server native Change Tracking)
- Version anchors track what each side has already seen
- Conflict resolution handles concurrent edits to the same record
- HTTP transport available for network sync with resilience (Polly retries, MessagePack binary format)

### Debezium — Unidirectional Change Streaming

Debezium captures row-level changes from a database's transaction log and streams them as ordered events. Think of it as a **real-time data pipeline** — every INSERT, UPDATE, and DELETE in your source database becomes an event that downstream systems can consume.

**Typical architecture (Kafka Connect mode):**

```
┌──────────┐     ┌──────────────┐     ┌─────────┐     ┌──────────────┐
│ Source DB │────►│ Debezium     │────►│  Kafka   │────►│ Consumers    │
│ (MySQL)  │     │ Connector    │     │  Topics  │     │ (Search,     │
│          │     │              │     │          │     │  Cache, DW)  │
└──────────┘     └──────────────┘     └─────────┘     └──────────────┘
```

**Key characteristics:**
- Reads transaction logs directly (minimal database impact)
- Events are published to Kafka topics (or other messaging systems via Debezium Server)
- Supports snapshotting for initial data load
- Single Message Transforms (SMTs) for lightweight event processing
- Three deployment modes: Kafka Connect (full-featured), Server (standalone), Engine (embedded Java library)

## Detailed Comparison

### Sync Direction

| | CoreSync | Debezium |
|---|---|---|
| **Direction** | Bidirectional | Unidirectional |
| **Use case** | Two databases that both read and write, staying in sync | One source database streaming changes to one or more consumers |
| **Conflict handling** | Yes — Skip or ForceWrite strategies with per-item callbacks | Not applicable — there is only one source of truth |

**Choose CoreSync** if both endpoints need to make changes independently (e.g., offline-capable mobile apps syncing with a server).

**Choose Debezium** if you have a single source of truth and want to replicate or stream changes downstream (e.g., populating a search index from a primary database).

### Infrastructure & Operational Complexity

| | CoreSync | Debezium (Kafka Connect) | Debezium Server |
|---|---|---|---|
| **External dependencies** | None | Kafka + ZooKeeper/KRaft + Kafka Connect | None (standalone JVM) |
| **Setup time** | Minutes (add NuGet, write config) | Weeks to months for production Kafka | Hours to days |
| **Ops team needed** | No dedicated team | Typically 2-6 engineers for large-scale | Minimal |
| **Deployment** | NuGet package in your app | Distributed cluster | Single JVM process |
| **Monitoring** | Application-level logging | Kafka metrics, Connect REST API, JMX | Minimal built-in |

CoreSync is a library you reference from your .NET project — there is nothing to deploy or operate separately. You call `ApplyProvisionAsync()` to set up change tracking, and `SynchronizeAsync()` to sync. That's it.

Debezium's primary deployment (Kafka Connect) requires a running Kafka cluster, which is a significant operational investment. Debezium Server reduces this to a single JVM process, but trades off scaling and fault tolerance features.

### Database Support

| Database | CoreSync | Debezium |
|---|---|---|
| SQLite | Yes | No |
| SQL Server | Yes (custom triggers + native CT) | Yes |
| PostgreSQL | Yes | Yes |
| MySQL / MariaDB | No | Yes |
| Oracle | No | Yes |
| MongoDB | No | Yes |
| Db2 | No | Yes |
| Cassandra | No | Yes |
| Google Spanner | No | Yes |
| Vitess | No | Yes (incubating) |
| Informix | No | Yes (incubating) |

Debezium has broader database coverage. CoreSync focuses on the databases most common in .NET applications and uniquely supports **SQLite** — critical for mobile and desktop offline scenarios.

### Change Detection Method

| | CoreSync | Debezium |
|---|---|---|
| **Method** | Database triggers or SQL Server native CT | Transaction log reading |
| **Database schema changes** | Adds tracking tables and triggers | No schema changes — reads existing logs |
| **Impact on source DB** | Trigger overhead on writes | Minimal (reads logs asynchronously) |
| **Supported operations** | INSERT, UPDATE, DELETE | INSERT, UPDATE, DELETE + schema changes |

CoreSync's trigger-based approach means it creates additional database objects (`__CORE_SYNC_CT` tracking table, triggers on each synced table). This is transparent to your application code but adds slight overhead to write operations.

Debezium reads the database's own transaction log, so it requires no schema changes. However, it needs appropriate database permissions (replication privileges) and the log must be configured to retain sufficient history.

### Performance

| | CoreSync | Debezium |
|---|---|---|
| **Latency** | On-demand (when you call `SynchronizeAsync()`) | Near real-time (20-400ms from commit to event) |
| **Throughput** | Bounded by trigger overhead and network transfer | Tens of thousands of events/second (Kafka Connect) |
| **Sync model** | Pull-based (application triggers sync) | Push-based (continuous streaming) |
| **Binary optimization** | MessagePack over HTTP | Kafka binary protocol |

CoreSync syncs when your application decides to sync — it's pull-based. This is ideal for mobile/desktop apps that sync periodically or on user action.

Debezium continuously streams changes in near real-time. This is ideal for event-driven architectures where consumers need to react to changes immediately.

### Developer Experience

| | CoreSync | Debezium |
|---|---|---|
| **Language** | C# / .NET | Java / JVM |
| **Configuration** | Fluent C# API | JSON/properties files |
| **Integration** | NuGet packages, ASP.NET Core middleware | Kafka Connect REST API, Docker images |
| **Learning curve** | Low — familiar .NET patterns, few concepts | Steep — Kafka, Connect, replication, JVM tuning |
| **Getting started** | `dotnet add package CoreSync.Sqlite` + ~10 lines of code | Deploy Kafka + Connect + configure connector JSON |

**CoreSync example:**

```csharp
var localProvider = new SqliteSyncProvider(localConfig);
var remoteProvider = new SqlSyncProvider(remoteConfig);

await localProvider.ApplyProvisionAsync();
await remoteProvider.ApplyProvisionAsync();

var agent = new SyncAgent(localProvider, remoteProvider);
await agent.SynchronizeAsync();
```

**Debezium example (connector configuration):**

```json
{
  "name": "inventory-connector",
  "config": {
    "connector.class": "io.debezium.connector.mysql.MySqlConnector",
    "database.hostname": "mysql",
    "database.port": "3306",
    "database.user": "debezium",
    "database.password": "dbz",
    "database.server.id": "184054",
    "topic.prefix": "dbserver1",
    "database.include.list": "inventory",
    "schema.history.internal.kafka.bootstrap.servers": "kafka:9092",
    "schema.history.internal.kafka.topic": "schema-changes.inventory"
  }
}
```

## When to Choose CoreSync

CoreSync is the right choice when:

- **You need bidirectional sync** — both endpoints create and modify data independently
- **You're building a .NET application** — CoreSync integrates natively with C#, ASP.NET Core, and the .NET ecosystem
- **You need SQLite support** — for mobile (MAUI/Xamarin), desktop, or embedded applications with offline capability
- **You want zero infrastructure** — no Kafka, no JVM, no external services to deploy and maintain
- **Your sync is on-demand** — you decide when to sync (on app launch, on user action, on a timer)
- **You need conflict resolution** — both sides may modify the same record while disconnected
- **You want simplicity** — a NuGet package and a few lines of code vs. a distributed streaming platform

**Ideal scenarios:**
- Mobile apps with offline-first architecture syncing to a cloud database
- Desktop applications caching data locally
- Field service apps that work without connectivity
- Multi-site databases that need bidirectional replication
- Any .NET application synchronizing between SQLite, SQL Server, and/or PostgreSQL

## When to Choose Debezium

Debezium is the right choice when:

- **You need unidirectional change streaming** — one source database, many consumers
- **You're already running Kafka** — Debezium adds CDC on top of existing infrastructure
- **You need broad database support** — MySQL, Oracle, MongoDB, Cassandra, and more
- **You want real-time streaming** — sub-second event propagation to downstream systems
- **You're building event-driven microservices** — Kafka topics as the backbone of your architecture
- **You need to feed multiple downstream systems** — search indexes, caches, data warehouses, analytics
- **You have a Java/JVM team** — comfortable with Kafka operations and JVM tuning

**Ideal scenarios:**
- Real-time data pipelines (database to Elasticsearch, data warehouse, etc.)
- Event sourcing and CQRS architectures
- Cache invalidation across microservices
- Database migration with zero-downtime cutover
- Audit logging and compliance tracking

## Summary

CoreSync and Debezium address different needs despite both being CDC-related tools:

| Need | Best fit |
|---|---|
| Two databases syncing bidirectionally | **CoreSync** |
| Streaming changes to downstream consumers | **Debezium** |
| .NET ecosystem | **CoreSync** |
| Java/Kafka ecosystem | **Debezium** |
| Offline-capable mobile/desktop apps | **CoreSync** |
| Real-time event-driven pipelines | **Debezium** |
| Zero infrastructure overhead | **CoreSync** |
| Broad database coverage (10+ databases) | **Debezium** |
| Conflict resolution needed | **CoreSync** |
| Single source of truth, many consumers | **Debezium** |

They can even be **complementary** — use CoreSync for edge/mobile bidirectional sync to a central database, and Debezium to stream changes from that central database to analytics, search, and other downstream systems.
