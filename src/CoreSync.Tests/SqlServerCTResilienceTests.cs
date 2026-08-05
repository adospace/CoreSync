using CoreSync.SqlServerCT;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers the failure modes behind the "clients silently stop uploading updates" incident on the
/// SQL Server Change Tracking provider:
/// <list type="bullet">
/// <item><description>an upload whose anchor predates the table's change tracking retention window must
/// fail with a typed, non-retryable <see cref="SyncAnchorTooOldException"/> rather than be masked;</description></item>
/// <item><description>a missing anchor row must read back as <see cref="SyncAnchor.Null"/>, not as version 0
/// (which looks valid but sits permanently below the retention floor);</description></item>
/// <item><description>a genuine constraint violation must surface as an exception instead of being
/// misreported as a write conflict and dropped;</description></item>
/// <item><description>the configured change retention must be applied when provisioning a database that
/// already has change tracking enabled.</description></item>
/// </list>
/// </summary>
[TestClass]
public class SqlServerCTResilienceTests
{
    private static string SqlServerConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
        "Server=localhost;User Id=sa;Password=CoreSync_Test123!;TrustServerCertificate=True";

    #region Helpers

    private static async Task CreateDatabase(string dbName)
    {
        using var conn = new SqlConnection(SqlServerConnectionString + ";Initial Catalog=master");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            IF DB_ID('{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END
            CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabase(string dbName)
    {
        using var conn = new SqlConnection(SqlServerConnectionString + ";Initial Catalog=master");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            IF DB_ID('{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Authors/Articles, with Articles.AuthorId a real foreign key so constraint violations can be
    /// provoked on purpose.
    /// </summary>
    private static async Task CreateSchema(string connectionString)
    {
        await ExecuteNonQuery(connectionString, @"
            CREATE TABLE [dbo].[Authors] (
                [Id] INT NOT NULL PRIMARY KEY,
                [Name] NVARCHAR(100) NOT NULL
            );
            CREATE TABLE [dbo].[Articles] (
                [Id] INT NOT NULL PRIMARY KEY,
                [Title] NVARCHAR(200) NOT NULL,
                [AuthorId] INT NULL REFERENCES [dbo].[Authors]([Id])
            );");
    }

    private static async Task ExecuteNonQuery(string connectionString, string commandText)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteScalar(string connectionString, string commandText)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        var res = await cmd.ExecuteScalarAsync();
        return res == DBNull.Value ? null : res;
    }

    private static async Task<long> GetCurrentVersion(string connectionString)
        => Convert.ToInt64(await ExecuteScalar(connectionString, "SELECT CHANGE_TRACKING_CURRENT_VERSION()"));

    private static async Task<long> GetMinValidVersion(string connectionString, string tableNameWithSchema)
        => Convert.ToInt64(await ExecuteScalar(connectionString, $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{tableNameWithSchema}'))"));

    /// <summary>
    /// Pushes a table's minimum valid version up to the current database version, which is what the
    /// change tracking auto-cleanup task does once the retention window elapses - without having to
    /// wait for it.
    /// </summary>
    private static Task ExpireChangeHistory(string connectionString, string tableNameWithSchema)
        => ExecuteNonQuery(connectionString, $@"
            ALTER TABLE {tableNameWithSchema} DISABLE CHANGE_TRACKING;
            ALTER TABLE {tableNameWithSchema} ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);");

    private static async Task<(int retentionPeriod, int retentionPeriodUnits, bool autoCleanup)> GetChangeTrackingSettings(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT retention_period, retention_period_units, is_auto_cleanup_on
FROM sys.change_tracking_databases WHERE database_id = DB_ID()";
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue("change tracking should be enabled on the database");
        // retention_period_units and is_auto_cleanup_on are tinyint, not int/bit.
        return (Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)), Convert.ToBoolean(reader.GetValue(2)));
    }

    private static SyncItem Item(string tableName, ChangeType changeType, Dictionary<string, object?> values)
        => new(tableName, changeType, values);

    private static SqlException? FindSqlException(Exception? ex)
    {
        while (ex != null)
        {
            if (ex is SqlException sqlException)
                return sqlException;
            ex = ex.InnerException;
        }
        return null;
    }

    private static async Task RunInDatabase(string dbName, Func<string, Task> body)
    {
        try
        {
            await CreateDatabase(dbName);
            await CreateSchema(SqlServerConnectionString + $";Initial Catalog={dbName}");
            await body(SqlServerConnectionString + $";Initial Catalog={dbName}");
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabase(dbName);
        }
    }

    private static SqlServerCTProvider CreateProvider(string connectionString, string label = "SUT")
    {
        var config = new SqlServerCTSyncConfigurationBuilder(connectionString)
            .Table("Authors")
            .Table("Articles")
            .Build();

        return new SqlServerCTProvider(config, logger: new ConsoleLogger(label));
    }

    #endregion

    /// <summary>
    /// A store that holds no anchor row for a peer must report the null sentinel. Version 0 is not a
    /// sentinel: it is a real-looking version permanently below the retention floor, which is what made
    /// every subsequent update from such a peer fail.
    /// </summary>
    [TestMethod]
    public async Task GetChanges_WithNoAnchorRowForPeer_ReportsNullTargetAnchor()
    {
        await RunInDatabase("CoreSyncCT_NullAnchor", async connStr =>
        {
            var provider = CreateProvider(connStr);
            await provider.ApplyProvisionAsync();

            var changeSet = await provider.GetChangesAsync(Guid.NewGuid());

            changeSet.TargetAnchor.IsNull().ShouldBeTrue("a missing anchor row must read back as the null sentinel");
            changeSet.TargetAnchor.Version.ShouldBe(-1);
        });
    }

    /// <summary>
    /// The end of the chain the previous test starts: an update sent with no recorded anchor cannot be
    /// conflict-checked at all, so it must be rejected as "reinitialize this client" rather than be
    /// pushed into CHANGETABLE with a doomed version.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_UpdateWithNullTargetAnchor_ThrowsSyncAnchorTooOld()
    {
        await RunInDatabase("CoreSyncCT_UpdateNullAnchor", async connStr =>
        {
            var provider = CreateProvider(connStr);
            await provider.ApplyProvisionAsync();

            await ExecuteNonQuery(connStr, "INSERT INTO [dbo].[Authors] ([Id], [Name]) VALUES (1, 'Original')");

            var changeSet = new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                SyncAnchor.Null,
                new[]
                {
                    Item("Authors", ChangeType.Update, new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Updated" })
                });

            var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(() => provider.ApplyChangesAsync(changeSet));

            ex.TableName.ShouldContain("Authors");
            ex.RequestedVersion.ShouldBe(-1);
            ex.MinValidVersion.ShouldBeGreaterThanOrEqualTo(0);

            // Nothing was written.
            (await ExecuteScalar(connStr, "SELECT [Name] FROM [dbo].[Authors] WHERE [Id] = 1")).ShouldBe("Original");
        });
    }

    /// <summary>
    /// The production failure itself: an anchor that is a perfectly ordinary looking version number but
    /// has fallen out of the retention window. It used to be handed straight to
    /// CHANGETABLE(CHANGES ..., @last_sync_version) and the resulting error was swallowed.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_UpdateWithAnchorBelowMinValidVersion_ThrowsSyncAnchorTooOld()
    {
        await RunInDatabase("CoreSyncCT_StaleAnchor", async connStr =>
        {
            var provider = CreateProvider(connStr);
            await provider.ApplyProvisionAsync();

            await ExecuteNonQuery(connStr, "INSERT INTO [dbo].[Authors] ([Id], [Name]) VALUES (1, 'Original')");
            await ExecuteNonQuery(connStr, "UPDATE [dbo].[Authors] SET [Name] = 'Second' WHERE [Id] = 1");

            var staleVersion = await GetCurrentVersion(connStr);
            staleVersion.ShouldBeGreaterThan(0);

            await ExpireChangeHistory(connStr, "[dbo].[Authors]");
            (await GetMinValidVersion(connStr, "[dbo].[Authors]")).ShouldBeGreaterThan(0);

            // The peer is still asking for changes relative to a version the store no longer retains.
            var changeSet = new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                new SyncAnchor(await provider.GetStoreIdAsync(), 0),
                new[]
                {
                    Item("Authors", ChangeType.Update, new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Updated" })
                });

            var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(() => provider.ApplyChangesAsync(changeSet));

            ex.TableName.ShouldContain("Authors");
            ex.RequestedVersion.ShouldBe(0);
            ex.MinValidVersion.ShouldBeGreaterThan(0);

            (await ExecuteScalar(connStr, "SELECT [Name] FROM [dbo].[Authors] WHERE [Id] = 1")).ShouldBe("Second");
        });
    }

    /// <summary>
    /// Negative control for the guard above: inserts never touch CHANGETABLE, so a first-time sync -
    /// which legitimately carries a null target anchor and nothing but inserts - must still go through.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_InsertOnlyChangeSetWithNullTargetAnchor_IsApplied()
    {
        await RunInDatabase("CoreSyncCT_FirstSync", async connStr =>
        {
            var provider = CreateProvider(connStr);
            await provider.ApplyProvisionAsync();

            var changeSet = new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                SyncAnchor.Null,
                new[]
                {
                    Item("Authors", ChangeType.Insert, new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "First" }),
                    Item("Articles", ChangeType.Insert, new Dictionary<string, object?> { ["Id"] = 10, ["Title"] = "Hello", ["AuthorId"] = 1 })
                });

            await provider.ApplyChangesAsync(changeSet);

            (await ExecuteScalar(connStr, "SELECT [Name] FROM [dbo].[Authors] WHERE [Id] = 1")).ShouldBe("First");
            (await ExecuteScalar(connStr, "SELECT [Title] FROM [dbo].[Articles] WHERE [Id] = 10")).ShouldBe("Hello");
        });
    }

    /// <summary>
    /// A foreign key violation on an update used to be swallowed by the T-SQL CATCH; the batch then
    /// reported zero affected rows, which the provider read as a write conflict and - with the default
    /// Skip resolution - dropped the item. That is silent data loss. It must throw instead, and the
    /// conflict callback must never see the item.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_UpdateViolatingForeignKey_ThrowsAndIsNotReportedAsConflict()
    {
        await RunInDatabase("CoreSyncCT_FkViolation", async connStr =>
        {
            var provider = CreateProvider(connStr);
            await provider.ApplyProvisionAsync();

            await ExecuteNonQuery(connStr, @"
                INSERT INTO [dbo].[Authors] ([Id], [Name]) VALUES (1, 'Author');
                INSERT INTO [dbo].[Articles] ([Id], [Title], [AuthorId]) VALUES (10, 'Title', 1);");

            // Anchor at the current version so the conflict check passes and the UPDATE actually runs.
            var currentVersion = await GetCurrentVersion(connStr);

            var conflicts = new List<SyncItem>();

            var changeSet = new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                new SyncAnchor(await provider.GetStoreIdAsync(), currentVersion),
                new[]
                {
                    // Author 999 does not exist.
                    Item("Articles", ChangeType.Update, new Dictionary<string, object?> { ["Id"] = 10, ["Title"] = "Retitled", ["AuthorId"] = 999 })
                });

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet, item =>
            {
                conflicts.Add(item);
                return ConflictResolution.Skip;
            }));

            conflicts.ShouldBeEmpty("a constraint violation is not a write conflict and must not reach the conflict handler");

            var sqlException = FindSqlException(ex);
            sqlException.ShouldNotBeNull("the underlying SqlException must survive in the exception chain");
            sqlException.Number.ShouldBe(547, "547 is the FOREIGN KEY / CHECK constraint violation");

            // The whole change set was rolled back.
            (await ExecuteScalar(connStr, "SELECT [Title] FROM [dbo].[Articles] WHERE [Id] = 10")).ShouldBe("Title");
            (await ExecuteScalar(connStr, "SELECT [AuthorId] FROM [dbo].[Articles] WHERE [Id] = 10")).ShouldBe(1);
        });
    }

    /// <summary>
    /// The main regression: <c>ChangeRetention</c> used to reach the database only through the
    /// <c>SET CHANGE_TRACKING = ON (...)</c> statement, which provisioning skips whenever change tracking
    /// is already enabled - so on every real deployment the setting was silently ignored forever.
    /// Provisioning must reconcile it instead.
    /// </summary>
    [TestMethod]
    public async Task ApplyProvision_AppliesChangeRetention_WhenChangeTrackingIsAlreadyEnabled()
    {
        await RunInDatabase("CoreSyncCT_Retention", async connStr =>
        {
            // First provisioning turns change tracking on and sets the retention.
            var initial = new SqlServerCTProvider(
                new SqlServerCTSyncConfigurationBuilder(connStr).Table("Authors").Table("Articles").ChangeRetention(2).Build(),
                logger: new ConsoleLogger("P1"));
            await initial.ApplyProvisionAsync();

            (await GetChangeTrackingSettings(connStr)).ShouldBe((2, (int)ChangeRetentionUnit.Days, true));

            // Second provisioning runs against a database that already has change tracking enabled.
            var reconfigured = new SqlServerCTProvider(
                new SqlServerCTSyncConfigurationBuilder(connStr).Table("Authors").Table("Articles").ChangeRetention(90, autoCleanup: false).Build(),
                logger: new ConsoleLogger("P2"));
            await reconfigured.ApplyProvisionAsync();

            (await GetChangeTrackingSettings(connStr)).ShouldBe((90, (int)ChangeRetentionUnit.Days, false),
                "the configured retention must be applied even when change tracking is already on");
        });
    }

    /// <summary>
    /// Retention is also configurable in units other than days, and a database that already has change
    /// tracking enabled is left alone when the caller never asked for a particular retention.
    /// </summary>
    [TestMethod]
    public async Task ApplyProvision_AppliesChangeRetentionWithExplicitUnit_AndLeavesItAloneWhenUnconfigured()
    {
        await RunInDatabase("CoreSyncCT_RetentionUnit", async connStr =>
        {
            var configured = new SqlServerCTProvider(
                new SqlServerCTSyncConfigurationBuilder(connStr).Table("Authors").Table("Articles")
                    .ChangeRetention(45, ChangeRetentionUnit.Minutes).Build(),
                logger: new ConsoleLogger("P1"));
            await configured.ApplyProvisionAsync();

            (await GetChangeTrackingSettings(connStr)).ShouldBe((45, (int)ChangeRetentionUnit.Minutes, true));

            // A provider that never called ChangeRetention must not silently reset the database to the
            // 7 day default.
            var unconfigured = new SqlServerCTProvider(
                new SqlServerCTSyncConfigurationBuilder(connStr).Table("Authors").Table("Articles").Build(),
                logger: new ConsoleLogger("P2"));
            await unconfigured.ApplyProvisionAsync();

            (await GetChangeTrackingSettings(connStr)).ShouldBe((45, (int)ChangeRetentionUnit.Minutes, true),
                "an unconfigured retention must leave the existing setting untouched");
        });
    }

    /// <summary>
    /// The builder rejects a non-positive retention rather than emitting invalid T-SQL.
    /// </summary>
    [TestMethod]
    public void ChangeRetention_RejectsNonPositiveValues()
    {
        var builder = new SqlServerCTSyncConfigurationBuilder("Server=.;Initial Catalog=x;Integrated Security=true").Table("Authors");

        Should.Throw<ArgumentOutOfRangeException>(() => builder.ChangeRetention(0));
        Should.Throw<ArgumentOutOfRangeException>(() => builder.ChangeRetention(-1, ChangeRetentionUnit.Hours));

        // The days-only overload keeps working and is expressed in days.
        var config = builder.ChangeRetention(14).Build();
        config.ChangeRetention.ShouldBe(14);
        config.ChangeRetentionUnit.ShouldBe(ChangeRetentionUnit.Days);
        config.ChangeRetentionDays.ShouldBe(14);
        config.AutoCleanup.ShouldBeTrue();

        var inMinutes = new SqlServerCTSyncConfigurationBuilder("Server=.;Initial Catalog=x;Integrated Security=true")
            .Table("Authors").ChangeRetention(90, ChangeRetentionUnit.Minutes).Build();
        inMinutes.ChangeRetentionInMinutes.ShouldBe(90);
        inMinutes.ChangeRetentionDays.ShouldBe(1, "rounded up to whole days for the legacy property");
    }
}
