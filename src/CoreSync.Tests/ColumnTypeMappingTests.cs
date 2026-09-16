using CoreSync.Http;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers the column types that have no dedicated <see cref="SyncItemValueType"/> and used to make
/// synchronization fail outright with
/// <c>NotSupportedException: Type of value ('System.Byte') is not supported for synchronization</c>
/// as soon as a synchronized table contained a SQL Server <c>tinyint</c> or <c>time</c> column:
/// <list type="bullet">
/// <item><description><c>tinyint</c> is read as <see cref="byte"/> and must travel as an
/// <see cref="SyncItemValueType.Int32"/>, then be narrowed back to a byte when applied;</description></item>
/// <item><description><c>time</c> is read as <see cref="TimeSpan"/> and must travel as an invariant
/// <c>"c"</c> formatted <see cref="SyncItemValueType.String"/> - the very format both
/// Microsoft.Data.Sqlite and EF Core use to store a <see cref="TimeSpan"/> - then be parsed back;</description></item>
/// <item><description>no new <see cref="SyncItemValueType"/> member may appear, or a peer running an
/// older version of the library would throw on the payload.</description></item>
/// </list>
/// The round trips also carry <c>smallint</c>, <c>decimal</c>, <c>uniqueidentifier</c> and
/// <c>datetime2</c> columns, both null and non-null, so that the normalization cannot regress the
/// types that already worked.
/// </summary>
[TestClass]
public class ColumnTypeMappingTests
{
    private static string SqlServerConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
        "Server=localhost;User Id=sa;Password=CoreSync_Test123!;TrustServerCertificate=True";

    private static string SqlServerConnectionStringFor(string databaseName)
        => new SqlConnectionStringBuilder(SqlServerConnectionString) { InitialCatalog = databaseName }.ConnectionString;

    #region Model

    /// <summary>
    /// Mirrors the raw DDL below. Property types are what the mobile side of a real deployment declares,
    /// so the SQLite provider reads every column through its typed reader path.
    /// </summary>
    public class Measurement
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte StartMonth { get; set; }
        public byte? EndMonth { get; set; }
        public short Zone { get; set; }
        public TimeSpan CountTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public decimal Weight { get; set; }
        public Guid ExternalId { get; set; }
        public DateTime Recorded { get; set; }
    }

    private sealed class MeasurementDbContext : DbContext
    {
        private readonly string _connectionString;

        public MeasurementDbContext(string connectionString) => _connectionString = connectionString;

        public DbSet<Measurement> Measurements => Set<Measurement>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Measurement>(entity =>
            {
                entity.ToTable("Measurements");
                entity.HasKey(_ => _.Id);
                entity.Property(_ => _.Id).ValueGeneratedNever();
                entity.Property(_ => _.Weight).HasColumnType("TEXT");
            });
        }
    }

    private const string SqlServerDdl = @"
        CREATE TABLE [dbo].[Measurements] (
            [Id] INT NOT NULL PRIMARY KEY,
            [Name] NVARCHAR(100) NOT NULL,
            [StartMonth] TINYINT NOT NULL,
            [EndMonth] TINYINT NULL,
            [Zone] SMALLINT NOT NULL,
            [CountTime] TIME(0) NOT NULL,
            [Duration] TIME(7) NULL,
            [Weight] DECIMAL(10,3) NOT NULL,
            [ExternalId] UNIQUEIDENTIFIER NOT NULL,
            [Recorded] DATETIME2 NOT NULL
        );";

    // Declared types are the ones EF Core's SQLite provider generates for the model above.
    private const string SqliteDdl = @"
        CREATE TABLE [Measurements] (
            [Id] INTEGER NOT NULL PRIMARY KEY,
            [Name] TEXT NOT NULL,
            [StartMonth] INTEGER NOT NULL,
            [EndMonth] INTEGER NULL,
            [Zone] INTEGER NOT NULL,
            [CountTime] TEXT NOT NULL,
            [Duration] TEXT NULL,
            [Weight] TEXT NOT NULL,
            [ExternalId] TEXT NOT NULL,
            [Recorded] TEXT NOT NULL
        );";

    #endregion

    #region Helpers

    private static async Task ExecuteSqlServerNonQuery(string connectionString, string commandText)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateSqlServerDatabase(string dbName)
    {
        using var conn = new SqlConnection(SqlServerConnectionStringFor("master"));
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
        using var conn = new SqlConnection(SqlServerConnectionStringFor("master"));
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

    private static async Task CreateSqliteDatabase(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqliteDdl;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Measurement?> ReadFromSqlServer(string connectionString, int id)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT [Name],[StartMonth],[EndMonth],[Zone],[CountTime],[Duration],[Weight],[ExternalId],[Recorded]
FROM [dbo].[Measurements] WHERE [Id] = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Measurement
        {
            Id = id,
            Name = reader.GetString(0),
            // GetByte/GetTimeSpan, not GetValue: the column must really be a tinyint/time on arrival,
            // not something SQL Server silently coerced.
            StartMonth = reader.GetByte(1),
            EndMonth = reader.IsDBNull(2) ? null : reader.GetByte(2),
            Zone = reader.GetInt16(3),
            CountTime = reader.GetTimeSpan(4),
            Duration = reader.IsDBNull(5) ? null : reader.GetTimeSpan(5),
            Weight = reader.GetDecimal(6),
            ExternalId = reader.GetGuid(7),
            Recorded = reader.GetDateTime(8)
        };
    }

    private enum Transport
    {
        Direct,
        HttpJson,
        HttpBinary
    }

    /// <summary>
    /// Runs a full SQL Server → SQLite → SQL Server round trip over the requested transport.
    /// </summary>
    private static async Task RunRoundTrip(string testName, Transport transport)
    {
        var dbName = $"CoreSyncTypes_{testName}";
        var sqliteFile = Path.Combine(Path.GetTempPath(), $"CoreSyncTypes_{testName}.sqlite");
        var sqliteConnStr = $"Data Source={sqliteFile}";

        SqliteConnection.ClearAllPools();
        if (File.Exists(sqliteFile)) File.Delete(sqliteFile);

        SyncTestServer? server = null;

        try
        {
            await CreateSqlServerDatabase(dbName);
            var sqlConnStr = SqlServerConnectionStringFor(dbName);
            await ExecuteSqlServerNonQuery(sqlConnStr, SqlServerDdl);
            await CreateSqliteDatabase(sqliteConnStr);

            var remoteProvider = new SqlSyncProvider(
                new SqlSyncConfigurationBuilder(sqlConnStr).Table("Measurements").Build(),
                logger: new ConsoleLogger("REM"));
            await remoteProvider.ApplyProvisionAsync();

            var localProvider = new SqliteSyncProvider(
                new SqliteSyncConfigurationBuilder(sqliteConnStr).Table<Measurement>("Measurements").Build(),
                logger: new ConsoleLogger("LOC"));
            await localProvider.ApplyProvisionAsync();

            ISyncProviderBase remote = remoteProvider;
            if (transport != Transport.Direct)
            {
                server = SyncTestServer.Create(remoteProvider, useBinaryFormat: transport == Transport.HttpBinary);
                remote = server.HttpSyncProvider;
            }

            var agent = new SyncAgent(localProvider, remote);

            // ---------- SQL Server → SQLite ----------

            var downloadedGuid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");
            var downloadedRecorded = new DateTime(2026, 9, 15, 21, 21, 33).AddTicks(1234567);

            await ExecuteSqlServerNonQuery(sqlConnStr, $@"
                INSERT INTO [dbo].[Measurements]
                    ([Id],[Name],[StartMonth],[EndMonth],[Zone],[CountTime],[Duration],[Weight],[ExternalId],[Recorded])
                VALUES
                    (1, N'Spring', 3, 250, -5, '14:30:00', '01:02:03.4567890', 12.345, '{downloadedGuid}', '2026-09-15T21:21:33.1234567'),
                    (2, N'Winter', 0, NULL, 32767, '00:00:00', NULL, 0.001, '{Guid.Empty}', '2026-01-01T00:00:00.0000000');");

            await agent.SynchronizeAsync();

            {
                using var db = new MeasurementDbContext(sqliteConnStr);

                var full = await db.Measurements.SingleAsync(_ => _.Id == 1);
                full.Name.ShouldBe("Spring");
                full.StartMonth.ShouldBe((byte)3);
                // 250 does not fit in an sbyte: it proves the value was widened, not reinterpreted.
                full.EndMonth.ShouldBe((byte)250);
                full.Zone.ShouldBe((short)-5);
                full.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
                full.Duration.ShouldBe(new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890)));
                full.Weight.ShouldBe(12.345m);
                full.ExternalId.ShouldBe(downloadedGuid);
                full.Recorded.ShouldBe(downloadedRecorded);

                var nulls = await db.Measurements.SingleAsync(_ => _.Id == 2);
                nulls.StartMonth.ShouldBe((byte)0);
                nulls.EndMonth.ShouldBeNull();
                nulls.Zone.ShouldBe((short)32767);
                nulls.CountTime.ShouldBe(TimeSpan.Zero);
                nulls.Duration.ShouldBeNull();
                nulls.ExternalId.ShouldBe(Guid.Empty);
            }

            // ---------- SQLite → SQL Server ----------

            var uploadedGuid = new Guid("7c9e6679-7425-40de-944b-e07fc1f90ae7");
            var uploadedRecorded = new DateTime(2026, 2, 3, 4, 5, 6).AddTicks(7654321);
            var uploadedDuration = new TimeSpan(0, 12, 34, 56).Add(TimeSpan.FromTicks(7891234));

            {
                using var db = new MeasurementDbContext(sqliteConnStr);
                db.Measurements.Add(new Measurement
                {
                    Id = 100,
                    Name = "Uploaded",
                    StartMonth = 255,
                    EndMonth = 1,
                    Zone = -32768,
                    CountTime = new TimeSpan(23, 59, 59),
                    Duration = uploadedDuration,
                    Weight = 999.999m,
                    ExternalId = uploadedGuid,
                    Recorded = uploadedRecorded
                });
                db.Measurements.Add(new Measurement
                {
                    Id = 101,
                    Name = "UploadedNulls",
                    StartMonth = 7,
                    EndMonth = null,
                    Zone = 1,
                    CountTime = TimeSpan.Zero,
                    Duration = null,
                    Weight = 0m,
                    ExternalId = Guid.Empty,
                    Recorded = new DateTime(2026, 3, 4, 5, 6, 7)
                });
                await db.SaveChangesAsync();
            }

            await agent.SynchronizeAsync();

            var uploaded = await ReadFromSqlServer(sqlConnStr, 100);
            uploaded.ShouldNotBeNull();
            uploaded.Name.ShouldBe("Uploaded");
            uploaded.StartMonth.ShouldBe((byte)255);
            uploaded.EndMonth.ShouldBe((byte)1);
            uploaded.Zone.ShouldBe((short)-32768);
            uploaded.CountTime.ShouldBe(new TimeSpan(23, 59, 59));
            // time(7) keeps the sub-second part: a "c" formatted string must not lose it on the way in.
            uploaded.Duration.ShouldBe(uploadedDuration);
            uploaded.Weight.ShouldBe(999.999m);
            uploaded.ExternalId.ShouldBe(uploadedGuid);
            uploaded.Recorded.ShouldBe(uploadedRecorded);

            var uploadedNulls = await ReadFromSqlServer(sqlConnStr, 101);
            uploadedNulls.ShouldNotBeNull();
            uploadedNulls.StartMonth.ShouldBe((byte)7);
            uploadedNulls.EndMonth.ShouldBeNull();
            uploadedNulls.CountTime.ShouldBe(TimeSpan.Zero);
            uploadedNulls.Duration.ShouldBeNull();
        }
        finally
        {
            server?.Dispose();
            SqlConnection.ClearAllPools();
            SqliteConnection.ClearAllPools();
            await DropSqlServerDatabase(dbName);
            if (File.Exists(sqliteFile)) File.Delete(sqliteFile);
        }
    }

    #endregion

    #region Value normalization (no database)

    /// <summary>
    /// The types without a dedicated <see cref="SyncItemValueType"/> must be normalized to a value whose
    /// runtime type matches the declared <see cref="SyncItemValue.Type"/>, so that the JSON and the
    /// MessagePack transports (which serialize <see cref="SyncItemValue.Value"/> by its runtime type)
    /// deliver the same thing.
    /// </summary>
    [TestMethod]
    public void SyncItemValue_NormalizesTypesWithoutADedicatedSyncItemValueType()
    {
        var tinyInt = new SyncItemValue((byte)250);
        tinyInt.Type.ShouldBe(SyncItemValueType.Int32);
        tinyInt.Value.ShouldBeOfType<int>().ShouldBe(250);

        var signedByte = new SyncItemValue((sbyte)-3);
        signedByte.Type.ShouldBe(SyncItemValueType.Int32);
        signedByte.Value.ShouldBeOfType<int>().ShouldBe(-3);

        var unsignedShort = new SyncItemValue((ushort)65535);
        unsignedShort.Type.ShouldBe(SyncItemValueType.Int32);
        unsignedShort.Value.ShouldBeOfType<int>().ShouldBe(65535);

        var unsignedInt = new SyncItemValue(4294967295u);
        unsignedInt.Type.ShouldBe(SyncItemValueType.Int64);
        unsignedInt.Value.ShouldBeOfType<long>().ShouldBe(4294967295L);

        var time = new SyncItemValue(new TimeSpan(0, 14, 30, 0));
        time.Type.ShouldBe(SyncItemValueType.String);
        time.Value.ShouldBeOfType<string>().ShouldBe("14:30:00");

        var timeWithFraction = new SyncItemValue(new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890)));
        timeWithFraction.Value.ShouldBeOfType<string>().ShouldBe("01:02:03.4567890");
        TimeSpan.Parse((string)timeWithFraction.Value!, CultureInfo.InvariantCulture)
            .ShouldBe(new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890)));

        var character = new SyncItemValue('U');
        character.Type.ShouldBe(SyncItemValueType.String);
        character.Value.ShouldBeOfType<string>().ShouldBe("U");

        // The types that already worked are untouched.
        new SyncItemValue(42).Type.ShouldBe(SyncItemValueType.Int32);
        new SyncItemValue((short)7).Type.ShouldBe(SyncItemValueType.Int32);
        new SyncItemValue(42L).Type.ShouldBe(SyncItemValueType.Int64);
        new SyncItemValue(1.5m).Type.ShouldBe(SyncItemValueType.Decimal);
        new SyncItemValue(Guid.Empty).Type.ShouldBe(SyncItemValueType.Guid);
        new SyncItemValue(DBNull.Value).Type.ShouldBe(SyncItemValueType.Null);
        new SyncItemValue(DBNull.Value).Value.ShouldBeNull();
        new SyncItemValue(null).Type.ShouldBe(SyncItemValueType.Null);
    }

    /// <summary>
    /// A <see cref="SyncItemValueType"/> member added for these types would be an unknown enum value to a
    /// peer running an older build, and both <c>SyncProviderHttpClient</c> and <c>SyncAgentController</c>
    /// answer an unknown member with <see cref="NotSupportedException"/>. The enum must stay as it is.
    /// </summary>
    [TestMethod]
    public void SyncItemValueType_HasNoNewMembers()
    {
        Enum.GetNames(typeof(SyncItemValueType)).ShouldBe(
            new[] { "Null", "String", "Int32", "Int64", "Float", "Double", "DateTime", "Boolean", "ByteArray", "Guid", "Decimal" },
            ignoreOrder: false);
    }

    /// <summary>
    /// The JSON transport reads a value back with <c>JsonElement.GetInt32()</c>/<c>GetString()</c> chosen
    /// from <see cref="SyncItemValue.Type"/>. A tinyint must therefore be on the wire as a JSON number and
    /// a time as a JSON string.
    /// </summary>
    [TestMethod]
    public void SyncItemValue_SurvivesJsonRoundTrip()
    {
        var item = new SyncItem("Measurements", ChangeType.Insert, new Dictionary<string, object?>
        {
            ["StartMonth"] = (byte)250,
            ["CountTime"] = new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890)),
            ["EndMonth"] = null
        });

        var roundTripped = JsonSerializer.Deserialize<SyncItem>(JsonSerializer.Serialize(item))!;

        roundTripped.Values["StartMonth"].Type.ShouldBe(SyncItemValueType.Int32);
        ((JsonElement)roundTripped.Values["StartMonth"].Value!).GetInt32().ShouldBe(250);

        roundTripped.Values["CountTime"].Type.ShouldBe(SyncItemValueType.String);
        ((JsonElement)roundTripped.Values["CountTime"].Value!).GetString().ShouldBe("01:02:03.4567890");

        roundTripped.Values["EndMonth"].Type.ShouldBe(SyncItemValueType.Null);
        roundTripped.Values["EndMonth"].Value.ShouldBeNull();
    }

    /// <summary>
    /// The binary transport uses MessagePack's typeless resolver, which writes the runtime type of
    /// <see cref="SyncItemValue.Value"/> into the payload. Without normalization a tinyint would arrive
    /// as a <see cref="byte"/> and a time as a <see cref="TimeSpan"/>, which is exactly what the apply
    /// side cannot bind.
    /// </summary>
    [TestMethod]
    public async Task SyncItemValue_SurvivesMessagePackRoundTrip()
    {
        var item = new SyncItem("Measurements", ChangeType.Insert, new Dictionary<string, object?>
        {
            ["StartMonth"] = (byte)250,
            ["CountTime"] = new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890)),
            ["EndMonth"] = null
        });

        using var stream = new MemoryStream(CoreSyncMessagePackSerializer.Serialize(item));
        var roundTripped = await CoreSyncMessagePackSerializer.DeserializeAsync<SyncItem>(stream);

        roundTripped.Values["StartMonth"].Type.ShouldBe(SyncItemValueType.Int32);
        roundTripped.Values["StartMonth"].Value.ShouldBeOfType<int>().ShouldBe(250);

        roundTripped.Values["CountTime"].Type.ShouldBe(SyncItemValueType.String);
        roundTripped.Values["CountTime"].Value.ShouldBeOfType<string>().ShouldBe("01:02:03.4567890");

        roundTripped.Values["EndMonth"].Type.ShouldBe(SyncItemValueType.Null);
        roundTripped.Values["EndMonth"].Value.ShouldBeNull();
    }

    #endregion

    #region Round trips

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_Direct()
        => RunRoundTrip("Direct", Transport.Direct);

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_HttpJson()
        => RunRoundTrip("HttpJson", Transport.HttpJson);

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_HttpBinary()
        => RunRoundTrip("HttpBinary", Transport.HttpBinary);

    #endregion
}
