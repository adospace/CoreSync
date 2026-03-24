using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.PostgreSQL;
using CoreSync.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Threading.Tasks;

namespace CoreSync.Tests;

public partial class IntegrationTests
{
    // Note: The following tests are excluded from HTTP because they rely on conflict
    // resolution behavior (per-item callbacks or Skip semantics) that the HTTP transport
    // does not support. The HTTP server always uses ForceWrite for updates and Skip for deletes.
    // Excluded: Test1, Test2, TestSyncAgent (conflict scenario), TestSyncAgentWithInitialData.

    #region TestSyncAgent Multiple Records over HTTP — Sqlite_Sqlite

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpJson_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpJson_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpBinary_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_MultipleRecordsSameTable_HttpBinary_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region TestSyncAgent Multiple Records over HTTP — Sqlite_SqlServer

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpJson");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable_HttpBinary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region TestSyncAgent Multiple Records over HTTP — Sqlite_PostgreSQL

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_PostgreSQL_MultipleRecordsSameTable_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_PostgreSQL_MultipleRecordsSameTable_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_multi_json");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_PostgreSQL_MultipleRecordsSameTable_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_PostgreSQL_MultipleRecordsSameTable_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_multi_binary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region TestSyncAgent UpdatedRemoteDeletedLocal over HTTP — Sqlite_Sqlite

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpJson_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpJson_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpBinary_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_UpdatedRemoteDeletedLocal_HttpBinary_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region TestSyncAgent UpdatedRemoteDeletedLocal over HTTP — Sqlite_SqlServer

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpJson");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_UpdatedRemoteDeletedLocal_HttpBinary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region TestSyncAgent UpdatedRemoteDeletedLocal over HTTP — Sqlite_PostgreSQL

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_PostgreSQL_UpdatedRemoteDeletedLocal_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_PostgreSQL_UpdatedRemoteDeletedLocal_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_urdl_json");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task TestSyncAgent_Sqlite_PostgreSQL_UpdatedRemoteDeletedLocal_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_PostgreSQL_UpdatedRemoteDeletedLocal_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_urdl_binary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteWithForeignKeys over HTTP — Sqlite_Sqlite

    [TestMethod]
    public async Task Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpJson_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpJson_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpBinary_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteWithForeignKeys_HttpBinary_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteWithForeignKeys over HTTP — Sqlite_SqlServer

    [TestMethod]
    public async Task Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpJson");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test_Sqlite_SqlServer_DeleteWithForeignKeys_HttpBinary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteWithForeignKeys over HTTP — Sqlite_PostgreSQL

    [TestMethod]
    public async Task Test_Sqlite_PostgreSQL_DeleteWithForeignKeys_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_PostgreSQL_DeleteWithForeignKeys_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_delfk_json");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_PostgreSQL_DeleteWithForeignKeys_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_PostgreSQL_DeleteWithForeignKeys_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_delfk_binary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table<User>("Users").Table<Post>("Posts").Table<Comment>("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteWithForeignKeys(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteParentRecordInRelatedTables over HTTP — Sqlite_Sqlite

    [TestMethod]
    public async Task Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpJson_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpJson_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        remoteDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpBinary_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DeleteParentRecordInRelatedTables_HttpBinary_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}");

        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        remoteDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteParentRecordInRelatedTables over HTTP — Sqlite_SqlServer

    [TestMethod]
    public async Task Test_Sqlite_SqlServer_DeleteParentRecordInRelatedTables_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_SqlServer_DeleteParentRecordInRelatedTables_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test_Sqlite_SqlServer_DelParent_HttpJson");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_SqlServer_DeleteParentRecordInRelatedTables_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_SqlServer_DeleteParentRecordInRelatedTables_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test_Sqlite_SqlServer_DelParent_HttpBin");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new SqlSyncProvider(
            new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion

    #region DeleteParentRecordInRelatedTables over HTTP — Sqlite_PostgreSQL

    [TestMethod]
    public async Task Test_Sqlite_PostgreSQL_DeleteParentRecordInRelatedTables_HttpJson()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_PostgreSQL_DeleteParentRecordInRelatedTables_HttpJson.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_delparent_json");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: false);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    [TestMethod]
    public async Task Test_Sqlite_PostgreSQL_DeleteParentRecordInRelatedTables_HttpBinary()
    {
        var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_PostgreSQL_DeleteParentRecordInRelatedTables_HttpBinary.sqlite";
        if (File.Exists(localDbFile)) File.Delete(localDbFile);

        using var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}");
        using var remoteDb = new PostgreSQLBlogDbContext(PostgreSQLConnectionString + ";Database=coresync_http_delparent_binary");
        await localDb.Database.EnsureDeletedAsync();
        await remoteDb.Database.EnsureDeletedAsync();

        await localDb.Database.MigrateAsync();
        await remoteDb.Database.MigrateAsync();

        localDb.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var remoteSyncProvider = new PostgreSQLSyncProvider(
            new PostgreSQLSyncConfigurationBuilder(remoteDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Remote, logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localSyncProvider = new SqliteSyncProvider(
            new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                .Table("Users").Table("Posts").Table("Comments").Build(),
            ProviderMode.Local, logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        using var server = SyncTestServer.Create(remoteSyncProvider, useBinaryFormat: true);
        await TestSyncAgentDeleteParentRecordInRelatedTables(localDb, localSyncProvider, remoteDb, server.HttpSyncProvider);
    }

    #endregion
}
