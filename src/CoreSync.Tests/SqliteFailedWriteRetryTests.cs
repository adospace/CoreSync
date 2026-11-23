using CoreSync.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// A write that fails with an error (typically a foreign key constraint) is not a conflict: the
/// SQLite provider must queue it for the next pass instead of retrying it with force-write, which
/// fails the same way and, for a delete, used to be taken for an already deleted row and dropped.
/// <para>
/// The scenario is a parent row referenced by a child whose foreign key does not cascade, with the
/// parent delete arriving before the child delete in the same change set (the order a server emits
/// when it walks tables parent-first). The local store resolves conflicts with force-write, as a
/// mobile client does.
/// </para>
/// </summary>
[TestClass]
public class SqliteFailedWriteRetryTests
{
    private sealed class CapturingLogger : ISyncLogger
    {
        public List<string> Errors { get; } = new();
        public void Trace(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message) => Errors.Add(message);
    }

    private static async Task Execute(string connectionString, string sql)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> Count(string connectionString, string table)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<SqliteSyncProvider> CreateProvider(string connectionString, ISyncLogger? logger = null)
    {
        await Execute(connectionString, @"
            CREATE TABLE [Parent] ([Id] INTEGER PRIMARY KEY, [Name] TEXT NOT NULL);
            CREATE TABLE [Config] ([Id] INTEGER PRIMARY KEY, [ParentId] INTEGER NOT NULL REFERENCES [Parent]([Id]));
            CREATE TABLE [Child] ([Id] INTEGER PRIMARY KEY, [ParentId] INTEGER NOT NULL REFERENCES [Parent]([Id]) ON DELETE CASCADE);");

        var config = new SqliteSyncConfigurationBuilder(connectionString)
            .Table("Parent")
            .Table("Config")
            .Table("Child")
            .Build();

        var provider = new SqliteSyncProvider(config, logger: logger);
        await provider.ApplyProvisionAsync();
        return provider;
    }

    private static async Task RunInTempDbs(Func<string, string, CapturingLogger, Task> body)
    {
        var localDbFile = Path.Combine(Path.GetTempPath(), $"FailedWriteRetry_local_{Guid.NewGuid()}.sqlite");
        var remoteDbFile = Path.Combine(Path.GetTempPath(), $"FailedWriteRetry_remote_{Guid.NewGuid()}.sqlite");

        try
        {
            await body($"Data Source={localDbFile}", $"Data Source={remoteDbFile}", new CapturingLogger());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);
        }
    }

    /// <summary>
    /// Syncs Parent 1 (with a Config and two Children) to local, deletes everything on the remote,
    /// and returns the delete change set reordered parent-first.
    /// </summary>
    private static async Task<SyncChangeSet> SeedAndGetParentFirstDeletes(
        SqliteSyncProvider local, SqliteSyncProvider remote, string localConn, string remoteConn)
    {
        await Execute(remoteConn, @"
            INSERT INTO [Parent] VALUES (1, 'parent');
            INSERT INTO [Config] VALUES (10, 1);
            INSERT INTO [Child] VALUES (100, 1), (101, 1);");

        await new SyncAgent(local, remote).SynchronizeAsync(conflictResolutionOnLocalStore: ConflictResolution.ForceWrite);
        (await Count(localConn, "Parent")).ShouldBe(1);
        (await Count(localConn, "Config")).ShouldBe(1);

        await Execute(remoteConn, @"
            DELETE FROM [Child];
            DELETE FROM [Config];
            DELETE FROM [Parent];");

        var changeSet = await remote.GetChangesAsync(await local.GetStoreIdAsync());
        changeSet.Items.ShouldAllBe(_ => _.ChangeType == ChangeType.Delete);

        // Parent first, then Config, then the cascaded Children (already gone locally by then).
        var order = new[] { "Parent", "Config", "Child" };
        var parentFirst = changeSet.Items.OrderBy(_ => Array.IndexOf(order, _.TableName)).ToList();

        return new SyncChangeSet(changeSet.SourceAnchor, changeSet.TargetAnchor, parentFirst);
    }

    [TestMethod]
    public async Task ParentDeleteBeforeNonCascadingChildDelete_IsRetriedAndApplied()
    {
        await RunInTempDbs(async (localConn, remoteConn, logger) =>
        {
            var remote = await CreateProvider(remoteConn);
            var local = await CreateProvider(localConn, logger);

            var changeSet = await SeedAndGetParentFirstDeletes(local, remote, localConn, remoteConn);

            await local.ApplyChangesAsync(changeSet, _ => ConflictResolution.ForceWrite);

            (await Count(localConn, "Parent")).ShouldBe(0, "the parent delete must be retried once its Config is gone");
            (await Count(localConn, "Config")).ShouldBe(0);
            (await Count(localConn, "Child")).ShouldBe(0);

            // The first attempt failed but the item was applied in a later pass: nothing to report.
            logger.Errors.ShouldBeEmpty();
        });
    }

    [TestMethod]
    public async Task DeleteBlockedByLocalOnlyRow_IsReportedOnceAndDoesNotLoop()
    {
        await RunInTempDbs(async (localConn, remoteConn, logger) =>
        {
            var remote = await CreateProvider(remoteConn);
            var local = await CreateProvider(localConn, logger);

            var changeSet = await SeedAndGetParentFirstDeletes(local, remote, localConn, remoteConn);

            // A row that exists only locally and is not part of the change set keeps the parent alive.
            // (Created outside the sync configuration, like a local-only table on a device.)
            await Execute(localConn, @"
                CREATE TABLE [LocalNote] ([Id] INTEGER PRIMARY KEY, [ParentId] INTEGER NOT NULL REFERENCES [Parent]([Id]));
                INSERT INTO [LocalNote] VALUES (1, 1);");

            var anchor = await local.ApplyChangesAsync(changeSet, _ => ConflictResolution.ForceWrite);
            anchor.ShouldNotBeNull();

            (await Count(localConn, "Parent")).ShouldBe(1, "the delete cannot succeed while the local-only row references it");
            (await Count(localConn, "Config")).ShouldBe(0, "the other deletes in the change set are still applied");

            logger.Errors.Count.ShouldBe(1, "an item that never applies is reported once, not on every attempt");
            logger.Errors[0].ShouldContain("Parent");
        });
    }
}
