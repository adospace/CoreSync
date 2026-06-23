using CoreSync.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Stress tests for the SQLite apply path that exercise the bulk SRC stamping and prepared-command
/// reuse optimisations in <see cref="SqliteSyncProvider.ApplyChangesAsync"/>:
/// <list type="bullet">
/// <item><description>a single change set with thousands of items (reuse hot path + bulk SRC stamp over a large id range);</description></item>
/// <item><description>a single change set mixing insert/update/delete (multiple cached commands + the delete path);</description></item>
/// <item><description>null/value alternation across same-shape rows (rebinding a reused parameter between a value and DBNull).</description></item>
/// </list>
/// Every test also asserts the no-echo invariant: changes received from the remote must not be
/// reported back as local changes on the next sync.
/// </summary>
[TestClass]
public class BulkApplyTests
{
    private sealed record ItemRow(int Id, string Name, string? Description, int Quantity, double? Price);

    #region Helpers

    private static async Task CreateItemsTable(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE [Items] (
                [Id] INTEGER PRIMARY KEY,
                [Name] TEXT NOT NULL,
                [Description] TEXT NULL,
                [Quantity] INTEGER NOT NULL,
                [Price] REAL NULL
            )";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(SqliteSyncProvider provider, string connStr)> CreateProvider(string dbFile)
    {
        var connStr = $"Data Source={dbFile}";
        await CreateItemsTable(connStr);

        var config = new SqliteSyncConfigurationBuilder(connStr).Table("Items").Build();
        // No logger: these tests apply thousands of items and the per-item Trace string building
        // (item.ToString()) would dominate; the logger-attached path is covered by the other suites.
        var provider = new SqliteSyncProvider(config);
        await provider.ApplyProvisionAsync();

        return (provider, connStr);
    }

    private static async Task InsertItems(string connectionString, IEnumerable<ItemRow> rows)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var tr = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tr;
        cmd.CommandText = "INSERT INTO [Items] ([Id],[Name],[Description],[Quantity],[Price]) VALUES (@id,@name,@desc,@qty,@price)";
        var id = cmd.Parameters.Add("@id", SqliteType.Integer);
        var name = cmd.Parameters.Add("@name", SqliteType.Text);
        var desc = cmd.Parameters.Add("@desc", SqliteType.Text);
        var qty = cmd.Parameters.Add("@qty", SqliteType.Integer);
        var price = cmd.Parameters.Add("@price", SqliteType.Real);

        foreach (var row in rows)
        {
            id.Value = row.Id;
            name.Value = row.Name;
            desc.Value = (object?)row.Description ?? DBNull.Value;
            qty.Value = row.Quantity;
            price.Value = (object?)row.Price ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }

        tr.Commit();
    }

    private static async Task UpdateItem(string connectionString, ItemRow row)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE [Items] SET [Name]=@name,[Description]=@desc,[Quantity]=@qty,[Price]=@price WHERE [Id]=@id";
        cmd.Parameters.AddWithValue("@id", row.Id);
        cmd.Parameters.AddWithValue("@name", row.Name);
        cmd.Parameters.AddWithValue("@desc", (object?)row.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qty", row.Quantity);
        cmd.Parameters.AddWithValue("@price", (object?)row.Price ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DeleteItem(string connectionString, int id)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM [Items] WHERE [Id]=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountItems(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM [Items]";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<ItemRow?> GetItem(string connectionString, int id)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [Name],[Description],[Quantity],[Price] FROM [Items] WHERE [Id]=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ItemRow(
            id,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3));
    }

    /// <summary>
    /// Asserts that a completed remote→local sync left nothing pending in either direction. This is
    /// the no-echo guard for the bulk SRC stamping: the applied rows are stamped as originating from
    /// the remote store, so the local provider must not report them back as its own changes.
    /// </summary>
    private static async Task AssertNoPendingChanges(SqliteSyncProvider local, SqliteSyncProvider remote)
    {
        var localStoreId = await local.GetStoreIdAsync();
        var remoteStoreId = await remote.GetStoreIdAsync();

        var localPending = await local.GetChangesAsync(remoteStoreId);
        localPending.Items.Count.ShouldBe(0, "applied remote changes must not echo back as local changes");

        var remotePending = await remote.GetChangesAsync(localStoreId);
        remotePending.Items.Count.ShouldBe(0, "no unexpected pending remote changes after a completed sync");
    }

    private static async Task RunInTempDbs(Func<string, string, SqliteSyncProvider, SqliteSyncProvider, Task> body)
    {
        var localDbFile = Path.Combine(Path.GetTempPath(), $"BulkApply_local_{Guid.NewGuid()}.sqlite");
        var remoteDbFile = Path.Combine(Path.GetTempPath(), $"BulkApply_remote_{Guid.NewGuid()}.sqlite");

        try
        {
            var (remote, remoteConn) = await CreateProvider(remoteDbFile);
            var (local, localConn) = await CreateProvider(localDbFile);

            await body(localConn, remoteConn, local, remote);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);
        }
    }

    #endregion

    /// <summary>
    /// Applies a single change set of several thousand inserts and verifies they all land correctly,
    /// then that nothing echoes back. Exercises prepared-command reuse and the single bulk SRC stamp
    /// over a large contiguous change-tracking id range.
    /// </summary>
    [TestMethod]
    public async Task BulkApply_LargeInsert_Sqlite_Sqlite()
    {
        await RunInTempDbs(async (localConn, remoteConn, local, remote) =>
        {
            const int count = 2000;

            var rows = new List<ItemRow>(count);
            for (int i = 1; i <= count; i++)
            {
                rows.Add(new ItemRow(
                    Id: i,
                    Name: $"Item {i}",
                    Description: i % 2 == 0 ? $"Description {i}" : null,
                    Quantity: i,
                    Price: i % 3 == 0 ? (double?)null : i * 1.5));
            }
            await InsertItems(remoteConn, rows);

            await new SyncAgent(local, remote).SynchronizeAsync();

            (await CountItems(localConn)).ShouldBe(count);

            // Spot-check the first, a middle, and the last row (covers both the value and null branches).
            (await GetItem(localConn, 1)).ShouldBe(new ItemRow(1, "Item 1", null, 1, 1.5));
            (await GetItem(localConn, 1000)).ShouldBe(new ItemRow(1000, "Item 1000", "Description 1000", 1000, 1500.0));
            (await GetItem(localConn, 2000)).ShouldBe(new ItemRow(2000, "Item 2000", "Description 2000", 2000, 3000.0));

            await AssertNoPendingChanges(local, remote);
        });
    }

    /// <summary>
    /// Applies a single change set that simultaneously inserts, updates and deletes rows of the same
    /// table. Exercises three distinct cached commands coexisting and the delete path (which has no
    /// value parameters), all stamped by the one bulk SRC update.
    /// </summary>
    [TestMethod]
    public async Task BulkApply_MixedInsertUpdateDelete_Sqlite_Sqlite()
    {
        await RunInTempDbs(async (localConn, remoteConn, local, remote) =>
        {
            // Baseline: 30 rows synced to local.
            var baseline = new List<ItemRow>();
            for (int i = 1; i <= 30; i++)
                baseline.Add(new ItemRow(i, $"Item {i}", $"Description {i}", i, i * 2.0));
            await InsertItems(remoteConn, baseline);

            var agent = new SyncAgent(local, remote);
            await agent.SynchronizeAsync();
            (await CountItems(localConn)).ShouldBe(30);

            // One round of mixed changes on the remote: insert 10, update 10, delete 10.
            var inserts = new List<ItemRow>();
            for (int i = 31; i <= 40; i++)
                inserts.Add(new ItemRow(i, $"Item {i}", i % 2 == 0 ? $"Description {i}" : null, i, i * 2.0));
            await InsertItems(remoteConn, inserts);

            for (int i = 1; i <= 10; i++)
                await UpdateItem(remoteConn, new ItemRow(i, $"Updated {i}", i % 2 == 0 ? null : $"Updated description {i}", i + 100, i * 9.0));

            for (int i = 21; i <= 30; i++)
                await DeleteItem(remoteConn, i);

            // This sync produces a single change set carrying inserts, updates and deletes together.
            await agent.SynchronizeAsync();

            (await CountItems(localConn)).ShouldBe(30); // 30 + 10 inserted - 10 deleted

            // Updates applied.
            (await GetItem(localConn, 5)).ShouldBe(new ItemRow(5, "Updated 5", "Updated description 5", 105, 45.0));
            (await GetItem(localConn, 6)).ShouldBe(new ItemRow(6, "Updated 6", null, 106, 54.0));

            // Untouched baseline rows preserved.
            (await GetItem(localConn, 15)).ShouldBe(new ItemRow(15, "Item 15", "Description 15", 15, 30.0));

            // Deletes applied.
            (await GetItem(localConn, 21)).ShouldBeNull();
            (await GetItem(localConn, 30)).ShouldBeNull();

            // Inserts applied.
            (await GetItem(localConn, 35)).ShouldBe(new ItemRow(35, "Item 35", null, 35, 70.0));
            (await GetItem(localConn, 40)).ShouldBe(new ItemRow(40, "Item 40", "Description 40", 40, 80.0));

            await AssertNoPendingChanges(local, remote);
        });
    }

    /// <summary>
    /// Applies same-shape rows where nullable columns alternate between a value and null, on both the
    /// insert and the update path. Exercises rebinding a reused parameter between an actual value and
    /// DBNull across consecutive items — a failure mode unique to prepared-command reuse.
    /// </summary>
    [TestMethod]
    public async Task BulkApply_NullValueRebinding_Sqlite_Sqlite()
    {
        await RunInTempDbs(async (localConn, remoteConn, local, remote) =>
        {
            const int count = 20;

            // Insert: Description present on even ids, null on odd; Price null on every third id.
            var rows = new List<ItemRow>();
            for (int i = 1; i <= count; i++)
            {
                rows.Add(new ItemRow(
                    Id: i,
                    Name: $"Item {i}",
                    Description: i % 2 == 0 ? $"Description {i}" : null,
                    Quantity: i,
                    Price: i % 3 == 0 ? (double?)null : i * 1.25));
            }
            await InsertItems(remoteConn, rows);

            var agent = new SyncAgent(local, remote);
            await agent.SynchronizeAsync();

            foreach (var expected in rows)
                (await GetItem(localConn, expected.Id)).ShouldBe(expected);

            // Update: flip the nullability of Description and Price the other way, in one change set.
            var flipped = new List<ItemRow>();
            for (int i = 1; i <= count; i++)
            {
                flipped.Add(new ItemRow(
                    Id: i,
                    Name: $"Item {i} v2",
                    Description: i % 2 == 0 ? null : $"Now set {i}",   // was the opposite
                    Quantity: i,
                    Price: i % 3 == 0 ? i * 2.0 : (double?)null));      // was the opposite
                await UpdateItem(remoteConn, flipped[i - 1]);
            }

            await agent.SynchronizeAsync();

            foreach (var expected in flipped)
                (await GetItem(localConn, expected.Id)).ShouldBe(expected);

            await AssertNoPendingChanges(local, remote);
        });
    }
}
