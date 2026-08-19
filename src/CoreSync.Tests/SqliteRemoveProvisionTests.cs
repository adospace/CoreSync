using CoreSync.Sqlite;
using CoreSync.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers <see cref="SqliteSyncProvider.RemoveProvisionAsync"/>: after a de-provision the database
/// must be left exactly as it was before <see cref="SqliteSyncProvider.ApplyProvisionAsync"/> ran,
/// i.e. no change-tracking triggers and no __CORE_SYNC_* tables.
/// </summary>
[TestClass]
public class SqliteRemoveProvisionTests
{
    private static string CreateDatabase(string testName)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"{testName}_{Guid.NewGuid():N}.sqlite");

        using (var db = new SQLiteConnection(dbFile))
        {
            db.CreateTable<Stock>();
            db.CreateTable<Valuation>();
        }

        SqliteConnection.ClearAllPools();

        return dbFile;
    }

    private static SqliteSyncProvider CreateProvider(string dbFile)
        => new(new SqliteSyncConfigurationBuilder($"Data Source={dbFile}")
            .Table<Stock>()
            .Table<Valuation>()
            .Build(), logger: new ConsoleLogger("LOC"));

    private static List<string> GetObjectNames(string dbFile, string type)
    {
        var names = new List<string>();

        using var connection = new SqliteConnection($"Data Source={dbFile}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT name FROM sqlite_master WHERE type = '{type}' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [TestMethod]
    public async Task RemoveProvisionAsync_RemovesChangeTrackingTriggersAndTables()
    {
        var dbFile = CreateDatabase(nameof(RemoveProvisionAsync_RemovesChangeTrackingTriggersAndTables));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.ApplyProvisionAsync();

        //provisioning creates 3 triggers per table (INSERT/UPDATE/DELETE) plus the sync tables
        GetObjectNames(dbFile, "trigger").ShouldBe(
        [
            "__Stock_ct-DELETE__",
            "__Stock_ct-INSERT__",
            "__Stock_ct-UPDATE__",
            "__Valuation_ct-DELETE__",
            "__Valuation_ct-INSERT__",
            "__Valuation_ct-UPDATE__",
        ]);
        GetObjectNames(dbFile, "table").ShouldContain("__CORE_SYNC_CT");

        await syncProvider.RemoveProvisionAsync();

        GetObjectNames(dbFile, "trigger").ShouldBeEmpty();

        var tables = GetObjectNames(dbFile, "table");
        tables.ShouldNotContain("__CORE_SYNC_CT");
        tables.ShouldNotContain("__CORE_SYNC_REMOTE_ANCHOR");
        tables.ShouldNotContain("__CORE_SYNC_LOCAL_ID");

        //user tables must survive the de-provision
        tables.ShouldContain("Stock");
        tables.ShouldContain("Valuation");
    }

    [TestMethod]
    public async Task RemoveProvisionAsync_LeavesDatabaseWritable()
    {
        var dbFile = CreateDatabase(nameof(RemoveProvisionAsync_LeavesDatabaseWritable));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.ApplyProvisionAsync();
        await syncProvider.RemoveProvisionAsync();

        //triggers left behind by a partial de-provision still target the (now dropped) __CORE_SYNC_CT
        //table, so any write to a tracked table would fail with "no such table: main.__CORE_SYNC_CT"
        using var db = new SQLiteConnection(dbFile);
        db.Insert(new Stock { Id = Guid.NewGuid(), Symbol = "MY_SYMBOL" });

        db.Table<Stock>().Count().ShouldBe(1);
    }

    [TestMethod]
    public async Task RemoveProvisionAsync_CanBeCalledOnANotProvisionedDatabase()
    {
        var dbFile = CreateDatabase(nameof(RemoveProvisionAsync_CanBeCalledOnANotProvisionedDatabase));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.RemoveProvisionAsync();

        GetObjectNames(dbFile, "trigger").ShouldBeEmpty();
    }

    [TestMethod]
    public async Task RemoveProvisionAsync_RemovesTriggersOfATableMissingFromTheDatabase()
    {
        var dbFile = CreateDatabase(nameof(RemoveProvisionAsync_RemovesTriggersOfATableMissingFromTheDatabase));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.ApplyProvisionAsync();

        //simulate a table dropped outside of CoreSync: its triggers go away with it, but the
        //de-provision must not choke on the missing table nor skip the remaining ones
        using (var connection = new SqliteConnection($"Data Source={dbFile}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE [Valuation]";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        await syncProvider.RemoveProvisionAsync();

        GetObjectNames(dbFile, "trigger").ShouldBeEmpty();
    }
}
