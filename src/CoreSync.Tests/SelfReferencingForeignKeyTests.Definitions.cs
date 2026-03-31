using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using CoreSync.PostgreSQL;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using Shouldly;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CoreSync.Tests;

public partial class SelfReferencingForeignKeyTests
{
    private static string SqlServerConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
        "Server=localhost;User Id=sa;Password=CoreSync_Test123!;TrustServerCertificate=True";

    private static string PostgreSQLConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_POSTGRESQL_CONNECTION_STRING") ??
        "Host=localhost;Port=5432;Database=coresync_test;Username=coresync;Password=test123";

    #region SQLite helpers

    private static async Task CreateSqliteCommentsTable(string connectionString, bool withForeignKey = true)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        var fkClause = withForeignKey ? "REFERENCES [Comments]([Id])" : "";
        cmd.CommandText = $@"
            CREATE TABLE [Comments] (
                [Id] INTEGER PRIMARY KEY,
                [Content] TEXT NOT NULL,
                [ParentId] INTEGER NULL {fkClause}
            )";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(ISyncProvider provider, string connStr)> CreateSqliteProvider(
        string dbFile, string label, bool foreignKeys = true)
    {
        var connStr = $"Data Source={dbFile}" + (foreignKeys ? ";Foreign Keys=True" : "");
        await CreateSqliteCommentsTable(connStr, withForeignKey: foreignKeys);

        var config = new SqliteSyncConfigurationBuilder(connStr)
            .Table("Comments")
            .Build();
        var provider = new SqliteSyncProvider(config, logger: new ConsoleLogger(label));
        await provider.ApplyProvisionAsync();

        return (provider, connStr);
    }

    private static async Task InsertSqliteComments(string connectionString, params (int id, string content, int? parentId)[] records)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        foreach (var (id, content, parentId) in records)
        {
            cmd.CommandText = "INSERT INTO [Comments] ([Id], [Content], [ParentId]) VALUES (@id, @content, @parentId)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@parentId", parentId.HasValue ? parentId.Value : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    #endregion

    #region SQL Server helpers

    private static async Task CreateSqlServerDatabase(string dbName)
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

    private static async Task DropSqlServerDatabase(string dbName)
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

    private static async Task CreateSqlServerCommentsTable(string connectionString, bool withForeignKey = true)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        var fkClause = withForeignKey ? "REFERENCES [Comments]([Id])" : "";
        cmd.CommandText = $@"
            CREATE TABLE [Comments] (
                [Id] INT PRIMARY KEY,
                [Content] NVARCHAR(MAX) NOT NULL,
                [ParentId] INT NULL {fkClause}
            )";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertSqlServerComments(string connectionString, params (int id, string content, int? parentId)[] records)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        foreach (var (id, content, parentId) in records)
        {
            cmd.CommandText = "INSERT INTO [Comments] ([Id], [Content], [ParentId]) VALUES (@id, @content, @parentId)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@content", content);
            cmd.Parameters.AddWithValue("@parentId", parentId.HasValue ? (object)parentId.Value : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> GetSqlServerCommentCount(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string content, int? parentId)> GetSqlServerComment(string connectionString, int id)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [Content], [ParentId] FROM [Comments] WHERE [Id] = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var content = reader.GetString(0);
        var parentId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return (content, parentId);
    }

    #endregion

    #region PostgreSQL helpers

    private static async Task CreatePostgreSQLDatabase(string dbName)
    {
        // Connect to default database to create/drop the target
        var builder = new NpgsqlConnectionStringBuilder(PostgreSQLConnectionString);
        var defaultDb = builder.Database;
        builder.Database = "postgres";
        var adminConnStr = builder.ToString();

        using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        // Terminate existing connections
        cmd.CommandText = $@"
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = '{dbName}' AND pid <> pg_backend_pid()";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropPostgreSQLDatabase(string dbName)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgreSQLConnectionString);
        builder.Database = "postgres";
        var adminConnStr = builder.ToString();

        using var conn = new NpgsqlConnection(adminConnStr);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = $@"
            SELECT pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = '{dbName}' AND pid <> pg_backend_pid()";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetPostgreSQLConnectionString(string dbName)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgreSQLConnectionString)
        {
            Database = dbName
        };
        return builder.ToString();
    }

    private static async Task CreatePostgreSQLCommentsTable(string connectionString, bool withForeignKey = true)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        var fkClause = withForeignKey ? "REFERENCES \"Comments\"(\"Id\")" : "";
        cmd.CommandText = $@"
            CREATE TABLE ""Comments"" (
                ""Id"" INTEGER PRIMARY KEY,
                ""Content"" TEXT NOT NULL,
                ""ParentId"" INTEGER NULL {fkClause}
            )";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertPostgreSQLComments(string connectionString, params (int id, string content, int? parentId)[] records)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();

        foreach (var (id, content, parentId) in records)
        {
            cmd.CommandText = "INSERT INTO \"Comments\" (\"Id\", \"Content\", \"ParentId\") VALUES ($1, $2, $3)";
            cmd.Parameters.Clear();
            cmd.Parameters.Add(new NpgsqlParameter { Value = id });
            cmd.Parameters.Add(new NpgsqlParameter { Value = content });
            cmd.Parameters.Add(new NpgsqlParameter { Value = parentId.HasValue ? (object)parentId.Value : DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> GetPostgreSQLCommentCount(string connectionString)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM \"Comments\"";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string content, int? parentId)> GetPostgreSQLComment(string connectionString, int id)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Content\", \"ParentId\" FROM \"Comments\" WHERE \"Id\" = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var content = reader.GetString(0);
        var parentId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return (content, parentId);
    }

    #endregion

    #region Sqlite -> Sqlite

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_Sqlite_Sqlite()
    {
        var remoteDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_remote_{Guid.NewGuid()}.sqlite");
        var localDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_local_{Guid.NewGuid()}.sqlite");

        try
        {
            var (remoteSyncProvider, remoteConnStr) = await CreateSqliteProvider(remoteDbFile, "REM");
            var (localSyncProvider, localConnStr) = await CreateSqliteProvider(localDbFile, "LOC");

            await InsertSqliteComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                using var conn = new SqliteConnection(localConnStr);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(2);

                cmd.CommandText = "SELECT [Content] FROM [Comments] WHERE [Id] = 1";
                ((string)(await cmd.ExecuteScalarAsync())!).ShouldBe("Parent comment");

                cmd.CommandText = "SELECT [Content] FROM [Comments] WHERE [Id] = 2";
                ((string)(await cmd.ExecuteScalarAsync())!).ShouldBe("Child comment");

                cmd.CommandText = "SELECT [ParentId] FROM [Comments] WHERE [Id] = 2";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(1);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
        }
    }

    [TestMethod]
    public async Task SelfReferencingFK_UnresolvableReference_Sqlite_Sqlite()
    {
        var remoteDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_unresolvable_remote_{Guid.NewGuid()}.sqlite");
        var localDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_unresolvable_local_{Guid.NewGuid()}.sqlite");

        try
        {
            // Remote without FK so we can insert orphan data
            var (remoteSyncProvider, remoteConnStr) = await CreateSqliteProvider(remoteDbFile, "REM", foreignKeys: false);
            var (localSyncProvider, localConnStr) = await CreateSqliteProvider(localDbFile, "LOC");

            await InsertSqliteComments(remoteConnStr, (1, "Orphan comment", 999));

            await TestUnresolvableReference(remoteSyncProvider, localSyncProvider, async () =>
            {
                using var conn = new SqliteConnection(localConnStr);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(0);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
        }
    }

    [TestMethod]
    public async Task SelfReferencingFK_DeepChain_Sqlite_Sqlite()
    {
        var remoteDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_deep_remote_{Guid.NewGuid()}.sqlite");
        var localDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_deep_local_{Guid.NewGuid()}.sqlite");

        try
        {
            var (remoteSyncProvider, remoteConnStr) = await CreateSqliteProvider(remoteDbFile, "REM");
            var (localSyncProvider, localConnStr) = await CreateSqliteProvider(localDbFile, "LOC");

            await InsertSqliteComments(remoteConnStr,
                (1, "Root", null),
                (2, "Child", 1),
                (3, "Grandchild", 2));

            await TestDeepChain(remoteSyncProvider, localSyncProvider, async () =>
            {
                using var conn = new SqliteConnection(localConnStr);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(3);

                cmd.CommandText = "SELECT [ParentId] FROM [Comments] WHERE [Id] = 3";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(2);

                cmd.CommandText = "SELECT [ParentId] FROM [Comments] WHERE [Id] = 2";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(1);
            });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
        }
    }

    #endregion

    #region SqlServer -> SqlServer (trigger-based)

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_SqlServer_SqlServer()
    {
        var remoteDb = "SelfRefFK_SqlServer_Remote";
        var localDb = "SelfRefFK_SqlServer_Local";

        try
        {
            await CreateSqlServerDatabase(remoteDb);
            await CreateSqlServerDatabase(localDb);

            var remoteConnStr = SqlServerConnectionString + $";Initial Catalog={remoteDb}";
            var localConnStr = SqlServerConnectionString + $";Initial Catalog={localDb}";

            await CreateSqlServerCommentsTable(remoteConnStr);
            await CreateSqlServerCommentsTable(localConnStr);

            var remoteConfig = new SqlSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new SqlSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new SqlSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new SqlSyncProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertSqlServerComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetSqlServerCommentCount(localConnStr)).ShouldBe(2);
                var (content1, _) = await GetSqlServerComment(localConnStr, 1);
                content1.ShouldBe("Parent comment");
                var (content2, parentId2) = await GetSqlServerComment(localConnStr, 2);
                content2.ShouldBe("Child comment");
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropSqlServerDatabase(remoteDb);
            await DropSqlServerDatabase(localDb);
        }
    }

    [TestMethod]
    public async Task SelfReferencingFK_DeepChain_SqlServer_SqlServer()
    {
        var remoteDb = "SelfRefFK_SqlServer_Deep_Remote";
        var localDb = "SelfRefFK_SqlServer_Deep_Local";

        try
        {
            await CreateSqlServerDatabase(remoteDb);
            await CreateSqlServerDatabase(localDb);

            var remoteConnStr = SqlServerConnectionString + $";Initial Catalog={remoteDb}";
            var localConnStr = SqlServerConnectionString + $";Initial Catalog={localDb}";

            await CreateSqlServerCommentsTable(remoteConnStr);
            await CreateSqlServerCommentsTable(localConnStr);

            var remoteConfig = new SqlSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new SqlSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new SqlSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new SqlSyncProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertSqlServerComments(remoteConnStr,
                (1, "Root", null),
                (2, "Child", 1),
                (3, "Grandchild", 2));

            await TestDeepChain(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetSqlServerCommentCount(localConnStr)).ShouldBe(3);
                var (_, parentId3) = await GetSqlServerComment(localConnStr, 3);
                parentId3.ShouldBe(2);
                var (_, parentId2) = await GetSqlServerComment(localConnStr, 2);
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropSqlServerDatabase(remoteDb);
            await DropSqlServerDatabase(localDb);
        }
    }

    #endregion

    #region SqlServerCT -> SqlServerCT (Change Tracking)

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_SqlServerCT_SqlServerCT()
    {
        var remoteDb = "SelfRefFK_SqlServerCT_Remote";
        var localDb = "SelfRefFK_SqlServerCT_Local";

        try
        {
            await CreateSqlServerDatabase(remoteDb);
            await CreateSqlServerDatabase(localDb);

            var remoteConnStr = SqlServerConnectionString + $";Initial Catalog={remoteDb}";
            var localConnStr = SqlServerConnectionString + $";Initial Catalog={localDb}";

            await CreateSqlServerCommentsTable(remoteConnStr);
            await CreateSqlServerCommentsTable(localConnStr);

            var remoteConfig = new SqlServerCTSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new SqlServerCTProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new SqlServerCTSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new SqlServerCTProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertSqlServerComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetSqlServerCommentCount(localConnStr)).ShouldBe(2);
                var (content1, _) = await GetSqlServerComment(localConnStr, 1);
                content1.ShouldBe("Parent comment");
                var (content2, parentId2) = await GetSqlServerComment(localConnStr, 2);
                content2.ShouldBe("Child comment");
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropSqlServerDatabase(remoteDb);
            await DropSqlServerDatabase(localDb);
        }
    }

    [TestMethod]
    public async Task SelfReferencingFK_DeepChain_SqlServerCT_SqlServerCT()
    {
        var remoteDb = "SelfRefFK_SqlServerCT_Deep_Remote";
        var localDb = "SelfRefFK_SqlServerCT_Deep_Local";

        try
        {
            await CreateSqlServerDatabase(remoteDb);
            await CreateSqlServerDatabase(localDb);

            var remoteConnStr = SqlServerConnectionString + $";Initial Catalog={remoteDb}";
            var localConnStr = SqlServerConnectionString + $";Initial Catalog={localDb}";

            await CreateSqlServerCommentsTable(remoteConnStr);
            await CreateSqlServerCommentsTable(localConnStr);

            var remoteConfig = new SqlServerCTSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new SqlServerCTProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new SqlServerCTSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new SqlServerCTProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertSqlServerComments(remoteConnStr,
                (1, "Root", null),
                (2, "Child", 1),
                (3, "Grandchild", 2));

            await TestDeepChain(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetSqlServerCommentCount(localConnStr)).ShouldBe(3);
                var (_, parentId3) = await GetSqlServerComment(localConnStr, 3);
                parentId3.ShouldBe(2);
                var (_, parentId2) = await GetSqlServerComment(localConnStr, 2);
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropSqlServerDatabase(remoteDb);
            await DropSqlServerDatabase(localDb);
        }
    }

    #endregion

    #region PostgreSQL -> PostgreSQL

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_PostgreSQL_PostgreSQL()
    {
        var remoteDb = "coresync_selfrefk_pg_remote";
        var localDb = "coresync_selfrefk_pg_local";

        try
        {
            await CreatePostgreSQLDatabase(remoteDb);
            await CreatePostgreSQLDatabase(localDb);

            var remoteConnStr = GetPostgreSQLConnectionString(remoteDb);
            var localConnStr = GetPostgreSQLConnectionString(localDb);

            await CreatePostgreSQLCommentsTable(remoteConnStr);
            await CreatePostgreSQLCommentsTable(localConnStr);

            var remoteConfig = new PostgreSQLSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new PostgreSQLSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new PostgreSQLSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new PostgreSQLSyncProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertPostgreSQLComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetPostgreSQLCommentCount(localConnStr)).ShouldBe(2);
                var (content1, _) = await GetPostgreSQLComment(localConnStr, 1);
                content1.ShouldBe("Parent comment");
                var (content2, parentId2) = await GetPostgreSQLComment(localConnStr, 2);
                content2.ShouldBe("Child comment");
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropPostgreSQLDatabase(remoteDb);
            await DropPostgreSQLDatabase(localDb);
        }
    }

    [TestMethod]
    public async Task SelfReferencingFK_DeepChain_PostgreSQL_PostgreSQL()
    {
        var remoteDb = "coresync_selfrefk_pg_deep_remote";
        var localDb = "coresync_selfrefk_pg_deep_local";

        try
        {
            await CreatePostgreSQLDatabase(remoteDb);
            await CreatePostgreSQLDatabase(localDb);

            var remoteConnStr = GetPostgreSQLConnectionString(remoteDb);
            var localConnStr = GetPostgreSQLConnectionString(localDb);

            await CreatePostgreSQLCommentsTable(remoteConnStr);
            await CreatePostgreSQLCommentsTable(localConnStr);

            var remoteConfig = new PostgreSQLSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new PostgreSQLSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var localConfig = new PostgreSQLSyncConfigurationBuilder(localConnStr).Table("Comments").Build();
            ISyncProvider localSyncProvider = new PostgreSQLSyncProvider(localConfig, logger: new ConsoleLogger("LOC"));
            await localSyncProvider.ApplyProvisionAsync();

            await InsertPostgreSQLComments(remoteConnStr,
                (1, "Root", null),
                (2, "Child", 1),
                (3, "Grandchild", 2));

            await TestDeepChain(remoteSyncProvider, localSyncProvider, async () =>
            {
                (await GetPostgreSQLCommentCount(localConnStr)).ShouldBe(3);
                var (_, parentId3) = await GetPostgreSQLComment(localConnStr, 3);
                parentId3.ShouldBe(2);
                var (_, parentId2) = await GetPostgreSQLComment(localConnStr, 2);
                parentId2.ShouldBe(1);
            });
        }
        finally
        {
            await DropPostgreSQLDatabase(remoteDb);
            await DropPostgreSQLDatabase(localDb);
        }
    }

    #endregion

    #region Mixed: SqlServer remote -> Sqlite local

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_SqlServer_Sqlite()
    {
        var remoteDb = "SelfRefFK_SqlServer_Sqlite_Remote";
        var localDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_SqlServer_Sqlite_local_{Guid.NewGuid()}.sqlite");

        try
        {
            await CreateSqlServerDatabase(remoteDb);

            var remoteConnStr = SqlServerConnectionString + $";Initial Catalog={remoteDb}";
            await CreateSqlServerCommentsTable(remoteConnStr);

            var remoteConfig = new SqlSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new SqlSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var (localSyncProvider, localConnStr) = await CreateSqliteProvider(localDbFile, "LOC");

            await InsertSqlServerComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                using var conn = new SqliteConnection(localConnStr);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(2);

                cmd.CommandText = "SELECT [ParentId] FROM [Comments] WHERE [Id] = 2";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(1);
            });
        }
        finally
        {
            await DropSqlServerDatabase(remoteDb);
            SqliteConnection.ClearAllPools();
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
        }
    }

    #endregion

    #region Mixed: PostgreSQL remote -> Sqlite local

    [TestMethod]
    public async Task SelfReferencingFK_ChildBeforeParent_PostgreSQL_Sqlite()
    {
        var remoteDb = "coresync_selfrefk_pg_sqlite_remote";
        var localDbFile = Path.Combine(Path.GetTempPath(), $"SelfRefFK_PG_Sqlite_local_{Guid.NewGuid()}.sqlite");

        try
        {
            await CreatePostgreSQLDatabase(remoteDb);

            var remoteConnStr = GetPostgreSQLConnectionString(remoteDb);
            await CreatePostgreSQLCommentsTable(remoteConnStr);

            var remoteConfig = new PostgreSQLSyncConfigurationBuilder(remoteConnStr).Table("Comments").Build();
            ISyncProvider remoteSyncProvider = new PostgreSQLSyncProvider(remoteConfig, logger: new ConsoleLogger("REM"));
            await remoteSyncProvider.ApplyProvisionAsync();

            var (localSyncProvider, localConnStr) = await CreateSqliteProvider(localDbFile, "LOC");

            await InsertPostgreSQLComments(remoteConnStr,
                (1, "Parent comment", null),
                (2, "Child comment", 1));

            await TestChildBeforeParent(remoteSyncProvider, localSyncProvider, async () =>
            {
                using var conn = new SqliteConnection(localConnStr);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM [Comments]";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(2);

                cmd.CommandText = "SELECT [ParentId] FROM [Comments] WHERE [Id] = 2";
                ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(1);
            });
        }
        finally
        {
            await DropPostgreSQLDatabase(remoteDb);
            SqliteConnection.ClearAllPools();
            if (File.Exists(localDbFile)) File.Delete(localDbFile);
        }
    }

    #endregion
}
