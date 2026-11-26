using CoreSync.SqlServerCT;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers <see cref="SqlServerCTSyncConfigurationBuilder.DiscardOrphanedChanges"/>: a peer that worked
/// offline uploads rows created under a parent that was deleted on the server in the meantime.
/// <para>
/// Schema: Surveys (INT key) &lt;- Samples (GUID key) &lt;- Catches (GUID key), every foreign key NOT NULL
/// and ON DELETE CASCADE, the way a parent delete on the server removes its subtree.
/// </para>
/// </summary>
[TestClass]
public class SqlServerCTOrphanedChangesTests
{
    private static string SqlServerConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
        "Server=localhost;User Id=sa;Password=CoreSync_Test123!;TrustServerCertificate=True";

    private static readonly Guid PeerStoreId = Guid.NewGuid();

    #region Helpers

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

    private static async Task<int> Count(string connectionString, string table)
        => Convert.ToInt32(await ExecuteScalar(connectionString, $"SELECT COUNT(*) FROM {table}"));

    private static async Task<long> GetCurrentVersion(string connectionString)
        => Convert.ToInt64(await ExecuteScalar(connectionString, "SELECT CHANGE_TRACKING_CURRENT_VERSION()"));

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

    private static async Task RunInDatabase(string dbName, Func<string, Task> body)
    {
        var connectionString = SqlServerConnectionString + $";Initial Catalog={dbName}";
        try
        {
            await DropDatabase(dbName);
            await ExecuteNonQuery(SqlServerConnectionString + ";Initial Catalog=master", $"CREATE DATABASE [{dbName}]");
            await ExecuteNonQuery(connectionString, @"
                CREATE TABLE [dbo].[Surveys] (
                    [Id] INT NOT NULL PRIMARY KEY,
                    [Name] NVARCHAR(100) NOT NULL
                );
                CREATE TABLE [dbo].[Samples] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [SurveyId] INT NOT NULL CONSTRAINT [FK_Samples_Surveys] REFERENCES [dbo].[Surveys]([Id]) ON DELETE CASCADE,
                    [Depth] INT NULL CONSTRAINT [CK_Samples_Depth] CHECK ([Depth] IS NULL OR [Depth] >= 0)
                );
                CREATE TABLE [dbo].[Catches] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [SampleId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [FK_Catches_Samples] REFERENCES [dbo].[Samples]([Id]) ON DELETE CASCADE,
                    [Species] NVARCHAR(50) NOT NULL
                );");
            await body(connectionString);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropDatabase(dbName);
        }
    }

    private static async Task<(SqlServerCTProvider Provider, RecordingLogger Logger)> CreateProvisionedProvider(string connectionString, bool discardOrphanedChanges)
    {
        var builder = new SqlServerCTSyncConfigurationBuilder(connectionString)
            .Table("Surveys")
            .Table("Samples")
            .Table("Catches");

        if (discardOrphanedChanges)
        {
            builder.DiscardOrphanedChanges();
        }

        var logger = new RecordingLogger();
        var provider = new SqlServerCTProvider(builder.Build(), logger: logger);
        await provider.ApplyProvisionAsync();
        return (provider, logger);
    }

    /// <summary>
    /// Inserts survey 1 with one sample, as the server holds them when the peer last syncs, and returns
    /// the version the peer is up to date with.
    /// </summary>
    private static async Task<long> SeedSurveyThePeerHasSeen(string connectionString)
    {
        await ExecuteNonQuery(connectionString, @"
            INSERT INTO [dbo].[Surveys] ([Id], [Name]) VALUES (1, 'Survey 1');
            INSERT INTO [dbo].[Samples] ([Id], [SurveyId]) VALUES ('11111111-1111-1111-1111-111111111111', 1);");
        return await GetCurrentVersion(connectionString);
    }

    private static SyncItem Insert(string tableName, Dictionary<string, object?> values)
        => new(tableName, ChangeType.Insert, values);

    /// <summary>
    /// What the peer uploads after working offline under survey 1: a new sample and a catch in it.
    /// The catch comes first, so the change set is not dependency ordered.
    /// </summary>
    private static List<SyncItem> OfflineSampleWithCatch(int surveyId, Guid sampleId) => new()
    {
        Insert("Catches", new() { ["Id"] = Guid.NewGuid(), ["SampleId"] = sampleId, ["Species"] = "Walleye" }),
        Insert("Samples", new() { ["Id"] = sampleId, ["SurveyId"] = surveyId, ["Depth"] = 3 }),
    };

    private static async Task<SyncChangeSet> ChangeSet(SqlServerCTProvider provider, long peerKnownVersion, IEnumerable<SyncItem> items)
        => new(new SyncAnchor(PeerStoreId, 1), new SyncAnchor(await provider.GetStoreIdAsync(), peerKnownVersion), items.ToList());

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

    private sealed class RecordingLogger : ISyncLogger
    {
        public List<string> Warnings { get; } = new();
        public void Trace(string message) { }
        public void Info(string message) => Console.WriteLine($"[INFO] {message}");
        public void Warning(string message) { Warnings.Add(message); Console.WriteLine($"[WARN] {message}"); }
        public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
    }

    #endregion

    /// <summary>
    /// The incident: the server deletes survey 1 while the peer, offline, adds a sample and a catch under it.
    /// Off by default, the upload keeps failing on the foreign key, exactly as before the option existed.
    /// </summary>
    [TestMethod]
    public async Task OptionOff_ChildrenOfParentDeletedAfterPeerAnchor_FailTheChangeSet()
    {
        await RunInDatabase("CoreSyncCT_Orphans_Off", async connStr =>
        {
            var (provider, _) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: false);
            var peerKnownVersion = await SeedSurveyThePeerHasSeen(connStr);
            await ExecuteNonQuery(connStr, "DELETE FROM [dbo].[Surveys] WHERE [Id] = 1");

            var changeSet = await ChangeSet(provider, peerKnownVersion, OfflineSampleWithCatch(1, Guid.NewGuid()));

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);
        });
    }

    /// <summary>
    /// With the option on, the orphaned subtree (the sample and, through it, the catch) is discarded and the
    /// rest of the change set commits, so the peer can go on to download the survey's delete.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_ChildrenOfParentDeletedAfterPeerAnchor_AreDiscardedAndTheRestApplies()
    {
        await RunInDatabase("CoreSyncCT_Orphans_On", async connStr =>
        {
            var (provider, logger) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            var peerKnownVersion = await SeedSurveyThePeerHasSeen(connStr);
            await ExecuteNonQuery(connStr, "DELETE FROM [dbo].[Surveys] WHERE [Id] = 1");

            var items = OfflineSampleWithCatch(1, Guid.NewGuid());
            // Unrelated work from the same session must still land.
            items.Add(Insert("Surveys", new() { ["Id"] = 2, ["Name"] = "Survey 2" }));
            items.AddRange(OfflineSampleWithCatch(2, Guid.NewGuid()));

            await provider.ApplyChangesAsync(await ChangeSet(provider, peerKnownVersion, items));

            (await Count(connStr, "[dbo].[Surveys]")).ShouldBe(1);
            (await Count(connStr, "[dbo].[Samples]")).ShouldBe(1);
            (await Count(connStr, "[dbo].[Catches]")).ShouldBe(1);
            (await ExecuteScalar(connStr, "SELECT [SurveyId] FROM [dbo].[Samples]")).ShouldBe(2);

            // The peer's anchor moved on: the next upload does not resend the discarded rows.
            (await ExecuteScalar(connStr, $"SELECT [REMOTE_VERSION] FROM [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR] WHERE [ID] = '{PeerStoreId}'")).ShouldBe(1L);

            // Each discarded row is logged with its values.
            logger.Warnings.Count(_ => _.Contains("Discarding Insert on Samples") && _.Contains("FK_Samples_Surveys")).ShouldBe(1);
            logger.Warnings.Count(_ => _.Contains("Discarding Insert on Catches") && _.Contains("FK_Catches_Samples")).ShouldBe(1);
            logger.Warnings.ShouldContain(_ => _.Contains("Walleye"));
        });
    }

    /// <summary>
    /// A parent that this store never had is a real bug on the peer, not a delete it missed: still fails.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_ParentThatNeverExisted_StillFails()
    {
        await RunInDatabase("CoreSyncCT_Orphans_NeverExisted", async connStr =>
        {
            var (provider, logger) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            var peerKnownVersion = await SeedSurveyThePeerHasSeen(connStr);

            var changeSet = await ChangeSet(provider, peerKnownVersion, OfflineSampleWithCatch(99, Guid.NewGuid()));

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);
            logger.Warnings.ShouldNotContain(_ => _.Contains("Discarding"));
        });
    }

    /// <summary>
    /// A delete the peer had already synchronized past is one it should have applied: a child under that
    /// parent means something else is wrong, so it still fails.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_ParentDeletedBeforePeerAnchor_StillFails()
    {
        await RunInDatabase("CoreSyncCT_Orphans_OldDelete", async connStr =>
        {
            var (provider, _) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            await SeedSurveyThePeerHasSeen(connStr);
            await ExecuteNonQuery(connStr, "DELETE FROM [dbo].[Surveys] WHERE [Id] = 1");
            var peerKnownVersion = await GetCurrentVersion(connStr);

            var changeSet = await ChangeSet(provider, peerKnownVersion, OfflineSampleWithCatch(1, Guid.NewGuid()));

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);
        });
    }

    /// <summary>
    /// One unexplained failure in the change set fails all of it: nothing is discarded or applied.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_OrphanMixedWithGenuineFailure_FailsAndAppliesNothing()
    {
        await RunInDatabase("CoreSyncCT_Orphans_Mixed", async connStr =>
        {
            var (provider, logger) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            var peerKnownVersion = await SeedSurveyThePeerHasSeen(connStr);
            await ExecuteNonQuery(connStr, "DELETE FROM [dbo].[Surveys] WHERE [Id] = 1");

            var items = OfflineSampleWithCatch(1, Guid.NewGuid());
            items.AddRange(OfflineSampleWithCatch(99, Guid.NewGuid()));
            items.Add(Insert("Surveys", new() { ["Id"] = 2, ["Name"] = "Survey 2" }));

            var changeSet = await ChangeSet(provider, peerKnownVersion, items);

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);

            (await Count(connStr, "[dbo].[Surveys]")).ShouldBe(0, "the whole change set rolls back");
            logger.Warnings.ShouldNotContain(_ => _.Contains("Discarding"));
        });
    }

    /// <summary>
    /// A CHECK constraint also raises error 547, but no foreign key is broken: still fails.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_CheckConstraintViolation_StillFails()
    {
        await RunInDatabase("CoreSyncCT_Orphans_Check", async connStr =>
        {
            var (provider, _) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            var peerKnownVersion = await SeedSurveyThePeerHasSeen(connStr);

            var changeSet = await ChangeSet(provider, peerKnownVersion, new[]
            {
                Insert("Samples", new() { ["Id"] = Guid.NewGuid(), ["SurveyId"] = 1, ["Depth"] = -5 }),
            });

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);
        });
    }

    /// <summary>
    /// A peer that never synchronized has no anchor, so there is no history to prove a delete: still fails.
    /// </summary>
    [TestMethod]
    public async Task OptionOn_PeerWithNullAnchor_StillFails()
    {
        await RunInDatabase("CoreSyncCT_Orphans_NullAnchor", async connStr =>
        {
            var (provider, _) = await CreateProvisionedProvider(connStr, discardOrphanedChanges: true);
            await SeedSurveyThePeerHasSeen(connStr);
            await ExecuteNonQuery(connStr, "DELETE FROM [dbo].[Surveys] WHERE [Id] = 1");

            var changeSet = new SyncChangeSet(new SyncAnchor(PeerStoreId, 1), SyncAnchor.Null, OfflineSampleWithCatch(1, Guid.NewGuid()));

            var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
            FindSqlException(ex)!.Number.ShouldBe(547);
        });
    }
}
