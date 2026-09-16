using CoreSync.Http;
using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.SqlServerCT;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers the column types that have no dedicated <see cref="SyncItemValueType"/> and used to make
/// synchronization fail outright with
/// <c>NotSupportedException: Type of value ('System.Byte') is not supported for synchronization</c>
/// as soon as a synchronized table contained a SQL Server <c>tinyint</c> or <c>time</c> column:
/// <list type="bullet">
/// <item><description>a tinyint-like column (SQL Server/MySql <c>tinyint</c>) is read as a
/// <see cref="byte"/>/<see cref="sbyte"/>/<see cref="bool"/> and must travel as an
/// <see cref="SyncItemValueType.Int32"/>, then be narrowed back when applied;</description></item>
/// <item><description>a time-like column (SQL Server/MySql <c>time</c>, PostgreSQL <c>time</c> and
/// <c>interval</c>) is read as a <see cref="TimeSpan"/> and must travel as an invariant <c>"c"</c>
/// formatted <see cref="SyncItemValueType.String"/> - the very format both Microsoft.Data.Sqlite and
/// EF Core use to store a <see cref="TimeSpan"/> - then be parsed back;</description></item>
/// <item><description>no new <see cref="SyncItemValueType"/> member may appear, or a peer running an
/// older version of the library would throw on the payload.</description></item>
/// </list>
/// The round trips also carry <c>smallint</c>, <c>decimal</c>, <c>uniqueidentifier</c> and
/// <c>datetime2</c> columns, both null and non-null, so that the normalization cannot regress the
/// types that already worked. The MySql and PostgreSQL halves live in
/// <c>ColumnTypeMappingTests.MySqlPostgreSQL.cs</c>.
/// </summary>
[TestClass]
public partial class ColumnTypeMappingTests
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

    /// <summary>
    /// One nullable column per CLR type the SQLite provider now reads through a typed getter, so that
    /// extending typed reads to nullable columns cannot regress a type that used to go through
    /// <c>GetValue()</c>.
    /// </summary>
    public class NullableProbe
    {
        public int Id { get; set; }
        public DateTime? Moment { get; set; }
        public decimal? Amount { get; set; }
        public Guid? Reference { get; set; }
        public bool? Flag { get; set; }
        public double? Ratio { get; set; }
        public float? Single { get; set; }
        public int? Counter { get; set; }
        public long? Big { get; set; }
        public short? Tiny { get; set; }
        public byte? Small { get; set; }
        public TimeSpan? Elapsed { get; set; }
        public string? Label { get; set; }
    }

    private sealed class MeasurementDbContext : DbContext
    {
        private readonly string _connectionString;

        public MeasurementDbContext(string connectionString) => _connectionString = connectionString;

        public DbSet<Measurement> Measurements => Set<Measurement>();
        public DbSet<NullableProbe> NullableProbes => Set<NullableProbe>();

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

            modelBuilder.Entity<NullableProbe>(entity =>
            {
                entity.ToTable("NullableProbes");
                entity.HasKey(_ => _.Id);
                entity.Property(_ => _.Id).ValueGeneratedNever();
                entity.Property(_ => _.Amount).HasColumnType("TEXT");
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
        );
        CREATE TABLE [dbo].[NullableProbes] (
            [Id] INT NOT NULL PRIMARY KEY,
            [Moment] DATETIME2 NULL,
            [Amount] DECIMAL(18,4) NULL,
            [Reference] UNIQUEIDENTIFIER NULL,
            [Flag] BIT NULL,
            [Ratio] FLOAT NULL,
            [Single] REAL NULL,
            [Counter] INT NULL,
            [Big] BIGINT NULL,
            [Tiny] SMALLINT NULL,
            [Small] TINYINT NULL,
            [Elapsed] TIME(7) NULL,
            [Label] NVARCHAR(50) NULL
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
        );
        CREATE TABLE [NullableProbes] (
            [Id] INTEGER NOT NULL PRIMARY KEY,
            [Moment] TEXT NULL,
            [Amount] TEXT NULL,
            [Reference] TEXT NULL,
            [Flag] INTEGER NULL,
            [Ratio] REAL NULL,
            [Single] REAL NULL,
            [Counter] INTEGER NULL,
            [Big] INTEGER NULL,
            [Tiny] INTEGER NULL,
            [Small] INTEGER NULL,
            [Elapsed] TEXT NULL,
            [Label] TEXT NULL
        );";

    #endregion

    #region Shared values

    private static readonly Guid DownloadedGuid = new("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid UploadedGuid = new("7c9e6679-7425-40de-944b-e07fc1f90ae7");

    /// <summary>01:02:03.4567890 - seven fractional digits, the full resolution of a TimeSpan tick.</summary>
    private static TimeSpan TickPreciseDuration => new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890));

    /// <summary>12:34:56.7891234 - the value uploaded back from SQLite.</summary>
    private static TimeSpan UploadedDuration => new TimeSpan(0, 12, 34, 56).Add(TimeSpan.FromTicks(7891234));

    private static DateTime DownloadedRecorded => new DateTime(2026, 9, 15, 21, 21, 33).AddTicks(1234567);
    private static DateTime UploadedRecorded => new DateTime(2026, 2, 3, 4, 5, 6).AddTicks(7654321);

    #endregion

    #region Helpers - SQL Server

    private static async Task ExecuteSqlServerNonQuery(string connectionString, string commandText)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ExecuteSqlServerScalar(string connectionString, string commandText)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        var res = await cmd.ExecuteScalarAsync();
        return res == DBNull.Value ? null : res;
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

    private static async Task<Measurement?> ReadMeasurementFromSqlServer(string connectionString, int id)
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

    private static async Task<NullableProbe?> ReadProbeFromSqlServer(string connectionString, int id)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT [Moment],[Amount],[Reference],[Flag],[Ratio],[Single],[Counter],[Big],[Tiny],[Small],[Elapsed],[Label]
FROM [dbo].[NullableProbes] WHERE [Id] = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new NullableProbe
        {
            Id = id,
            Moment = reader.IsDBNull(0) ? null : reader.GetDateTime(0),
            Amount = reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            Reference = reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Flag = reader.IsDBNull(3) ? null : reader.GetBoolean(3),
            Ratio = reader.IsDBNull(4) ? null : reader.GetDouble(4),
            Single = reader.IsDBNull(5) ? null : reader.GetFloat(5),
            Counter = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            Big = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            Tiny = reader.IsDBNull(8) ? null : reader.GetInt16(8),
            Small = reader.IsDBNull(9) ? null : reader.GetByte(9),
            Elapsed = reader.IsDBNull(10) ? null : reader.GetTimeSpan(10),
            Label = reader.IsDBNull(11) ? null : reader.GetString(11)
        };
    }

    #endregion

    #region Helpers - SQLite

    private static async Task ExecuteSqliteNonQuery(string connectionString, string commandText)
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static string CreateSqliteFile(string testName)
    {
        var file = Path.Combine(Path.GetTempPath(), $"CoreSyncTypes_{testName}.sqlite");
        SqliteConnection.ClearAllPools();
        if (File.Exists(file)) File.Delete(file);
        return file;
    }

    private static void DeleteSqliteFile(string file)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(file)) File.Delete(file);
    }

    private static SqliteSyncProvider CreateSqliteProvider(string connectionString)
        => new(new SqliteSyncConfigurationBuilder(connectionString)
                .Table<Measurement>("Measurements")
                .Table<NullableProbe>("NullableProbes")
                .Build(),
            logger: new ConsoleLogger("LOC"));

    #endregion

    #region Helpers - transports

    internal enum Transport
    {
        Direct,
        HttpJson,
        HttpBinary
    }

    /// <summary>
    /// Wraps <paramref name="remote"/> in the requested transport, so every assertion runs identically
    /// against the provider itself, against the JSON HTTP pipeline and against the MessagePack one.
    /// </summary>
    private static (ISyncProviderBase remote, SyncTestServer? server) Wrap(ISyncProvider remote, Transport transport)
    {
        if (transport == Transport.Direct)
            return (remote, null);

        var server = SyncTestServer.Create(remote, useBinaryFormat: transport == Transport.HttpBinary);
        return (server.HttpSyncProvider, server);
    }

    private static SyncItem Item(string tableName, ChangeType changeType, Dictionary<string, object?> values)
        => new(tableName, changeType, values);

    /// <summary>
    /// Builds a change set the way an older build of the library would have sent it, by assigning
    /// <see cref="SyncItemValue.Type"/> and <see cref="SyncItemValue.Value"/> directly instead of
    /// letting the constructor normalize them.
    /// </summary>
    private static SyncItem RawItem(string tableName, ChangeType changeType, Dictionary<string, SyncItemValue> values)
        => new() { TableName = tableName, ChangeType = changeType, Values = values };

    private static SyncItemValue Raw(SyncItemValueType type, object? value)
        => new() { Type = type, Value = value };

    private static T? FindInChain<T>(Exception? ex) where T : Exception
    {
        while (ex != null)
        {
            if (ex is T match)
                return match;
            ex = ex.InnerException;
        }
        return null;
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

        var timeWithFraction = new SyncItemValue(TickPreciseDuration);
        timeWithFraction.Value.ShouldBeOfType<string>().ShouldBe("01:02:03.4567890");
        TimeSpan.Parse((string)timeWithFraction.Value!, CultureInfo.InvariantCulture).ShouldBe(TickPreciseDuration);

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
    /// The <c>"c"</c> format is signed and carries whole days, so it can express a MySql <c>TIME</c>
    /// (-838:59:59 to 838:59:59) and a PostgreSQL <c>interval</c> as faithfully as a SQL Server
    /// <c>time</c>. This is the wire-level half of the range question; the provider-level half - a
    /// SQL Server <c>time</c> column refusing what it cannot hold - is asserted by
    /// <see cref="ApplyChanges_TimeValueOutsideTimeOfDayRange_IsRejectedWithAClearMessage"/>.
    /// </summary>
    [TestMethod]
    public void SyncItemValue_FormatsTimeSpansOutsideATimeOfDay()
    {
        var negative = new SyncItemValue(new TimeSpan(0, -1, -2, -3));
        negative.Type.ShouldBe(SyncItemValueType.String);
        negative.Value.ShouldBe("-01:02:03");
        TimeSpan.Parse((string)negative.Value!, CultureInfo.InvariantCulture).ShouldBe(new TimeSpan(0, -1, -2, -3));

        // 100 hours: legal in MySql and PostgreSQL, impossible in a SQL Server time column.
        var hundredHours = new SyncItemValue(TimeSpan.FromHours(100));
        hundredHours.Value.ShouldBe("4.04:00:00");
        TimeSpan.Parse((string)hundredHours.Value!, CultureInfo.InvariantCulture).ShouldBe(TimeSpan.FromHours(100));

        var mostNegativeMySqlTime = new SyncItemValue(new TimeSpan(-838, -59, -59));
        mostNegativeMySqlTime.Value.ShouldBe("-34.22:59:59");
        TimeSpan.Parse((string)mostNegativeMySqlTime.Value!, CultureInfo.InvariantCulture).ShouldBe(new TimeSpan(-838, -59, -59));
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
    /// Types that still have no representation stay unsupported, and say so. <see cref="DateTimeOffset"/>
    /// is what Npgsql materializes a PostgreSQL <c>timetz</c> as, and what SQL Server returns for a
    /// <c>datetimeoffset</c> column.
    /// </summary>
    [TestMethod]
    public void SyncItemValue_StillRejectsTypesWithNoRepresentation()
    {
        Should.Throw<NotSupportedException>(() => new SyncItemValue(DateTimeOffset.UtcNow))
            .Message.ShouldContain("DateTimeOffset");

        Should.Throw<NotSupportedException>(() => new SyncItemValue(new object()));
    }

    /// <summary>
    /// The JSON transport reads a value back with <c>JsonElement.GetInt32()</c>/<c>GetString()</c> chosen
    /// from <see cref="SyncItemValue.Type"/>. A tinyint must therefore be on the wire as a JSON number and
    /// a time as a JSON string.
    /// </summary>
    [TestMethod]
    public void SyncItemValue_SurvivesJsonRoundTrip()
    {
        var item = Item("Measurements", ChangeType.Insert, new Dictionary<string, object?>
        {
            ["StartMonth"] = (byte)250,
            ["CountTime"] = TickPreciseDuration,
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
        var item = Item("Measurements", ChangeType.Insert, new Dictionary<string, object?>
        {
            ["StartMonth"] = (byte)250,
            ["CountTime"] = TickPreciseDuration,
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

    /// <summary>
    /// Backward compatibility, new server → old client: an older <c>SyncProviderHttpClient</c> decodes a
    /// JSON payload with a switch over the eleven <see cref="SyncItemValueType"/> members it knows and
    /// throws <see cref="NotSupportedException"/> on anything else. This replays that exact switch over a
    /// payload produced by the current code, so a tinyint/time column cannot break a client that has not
    /// been updated.
    /// </summary>
    [TestMethod]
    public void NewPayload_IsDecodableByTheOldClientConversionSwitch()
    {
        var item = Item("Measurements", ChangeType.Insert, new Dictionary<string, object?>
        {
            ["Id"] = 1,
            ["StartMonth"] = (byte)250,
            ["Signed"] = (sbyte)-3,
            ["Zone"] = (short)-5,
            ["CountTime"] = TickPreciseDuration,
            ["Negative"] = new TimeSpan(0, -1, -2, -3),
            ["Letter"] = 'U',
            ["Weight"] = 12.345m,
            ["Recorded"] = DownloadedRecorded,
            ["ExternalId"] = DownloadedGuid,
            ["EndMonth"] = null
        });

        var roundTripped = JsonSerializer.Deserialize<SyncItem>(JsonSerializer.Serialize(item))!;

        foreach (var entry in roundTripped.Values)
        {
            var decoded = OldClientConvertJsonValueToNetObject((JsonElement?)entry.Value.Value, entry.Value.Type);

            if (entry.Value.Type == SyncItemValueType.Null)
                decoded.ShouldBeNull();
            else
                decoded.ShouldNotBeNull($"the old client must be able to decode '{entry.Key}'");
        }

        // and the values survive that decoding unchanged
        OldClientConvertJsonValueToNetObject((JsonElement?)roundTripped.Values["StartMonth"].Value, SyncItemValueType.Int32).ShouldBe(250);
        OldClientConvertJsonValueToNetObject((JsonElement?)roundTripped.Values["CountTime"].Value, SyncItemValueType.String).ShouldBe("01:02:03.4567890");
        OldClientConvertJsonValueToNetObject((JsonElement?)roundTripped.Values["Negative"].Value, SyncItemValueType.String).ShouldBe("-01:02:03");
        OldClientConvertJsonValueToNetObject((JsonElement?)roundTripped.Values["Letter"].Value, SyncItemValueType.String).ShouldBe("U");
    }

    /// <summary>
    /// A verbatim copy of the <c>ConvertJsonValueToNetObject</c> switch shipped in released versions of
    /// CoreSync.Http.Client. Do not extend it: its whole point is to be frozen at what an old peer knows.
    /// </summary>
    private static object? OldClientConvertJsonValueToNetObject(JsonElement? value, SyncItemValueType targetType)
    {
        if (value == null)
            return null;

        return targetType switch
        {
            SyncItemValueType.Null => null,
            SyncItemValueType.String => value.Value.GetString(),
            SyncItemValueType.Int32 => value.Value.GetInt32(),
            SyncItemValueType.Float => value.Value.GetSingle(),
            SyncItemValueType.Double => value.Value.GetDouble(),
            SyncItemValueType.DateTime => value.Value.GetDateTime(),
            SyncItemValueType.Boolean => value.Value.GetBoolean(),
            SyncItemValueType.ByteArray => value.Value.GetBytesFromBase64(),
            SyncItemValueType.Guid => value.Value.GetGuid(),
            SyncItemValueType.Int64 => value.Value.GetInt64(),
            SyncItemValueType.Decimal => value.Value.GetDecimal(),
            _ => throw new NotSupportedException(),
        };
    }

    #endregion

    #region SQL Server ⇄ SQLite round trips

    /// <summary>
    /// Runs a full SQL Server → SQLite → SQL Server round trip over the requested transport.
    /// </summary>
    private static async Task RunSqlServerRoundTrip(string testName, Transport transport, bool useChangeTrackingProvider = false)
    {
        var dbName = $"CoreSyncTypes_{testName}";
        var sqliteFile = CreateSqliteFile(testName);
        var sqliteConnStr = $"Data Source={sqliteFile}";

        SyncTestServer? server = null;

        try
        {
            await CreateSqlServerDatabase(dbName);
            var sqlConnStr = SqlServerConnectionStringFor(dbName);
            await ExecuteSqlServerNonQuery(sqlConnStr, SqlServerDdl);
            await ExecuteSqliteNonQuery(sqliteConnStr, SqliteDdl);

            ISyncProvider remoteProvider = useChangeTrackingProvider
                ? new SqlServerCTProvider(
                    new SqlServerCTSyncConfigurationBuilder(sqlConnStr).Table("Measurements").Table("NullableProbes").Build(),
                    logger: new ConsoleLogger("REM-CT"))
                : new SqlSyncProvider(
                    new SqlSyncConfigurationBuilder(sqlConnStr).Table("Measurements").Table("NullableProbes").Build(),
                    logger: new ConsoleLogger("REM"));
            await remoteProvider.ApplyProvisionAsync();

            var localProvider = CreateSqliteProvider(sqliteConnStr);
            await localProvider.ApplyProvisionAsync();

            (var remote, server) = Wrap(remoteProvider, transport);
            var agent = new SyncAgent(localProvider, remote);

            // ---------- SQL Server → SQLite ----------

            await ExecuteSqlServerNonQuery(sqlConnStr, $@"
                INSERT INTO [dbo].[Measurements]
                    ([Id],[Name],[StartMonth],[EndMonth],[Zone],[CountTime],[Duration],[Weight],[ExternalId],[Recorded])
                VALUES
                    (1, N'Spring', 3, 250, -5, '14:30:00', '01:02:03.4567890', 12.345, '{DownloadedGuid}', '2026-09-15T21:21:33.1234567'),
                    (2, N'Winter', 0, NULL, 32767, '00:00:00', NULL, 0.001, '{Guid.Empty}', '2026-01-01T00:00:00.0000000');
                INSERT INTO [dbo].[NullableProbes]
                    ([Id],[Moment],[Amount],[Reference],[Flag],[Ratio],[Single],[Counter],[Big],[Tiny],[Small],[Elapsed],[Label])
                VALUES
                    (1, '2026-09-15T21:21:33.1234567', 1234.5678, '{DownloadedGuid}', 1, 2.5, 1.5, -2147483648, 9223372036854775807, -32768, 255, '01:02:03.4567890', N'set'),
                    (2, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL);");

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
                full.Duration.ShouldBe(TickPreciseDuration);
                full.Weight.ShouldBe(12.345m);
                full.ExternalId.ShouldBe(DownloadedGuid);
                full.Recorded.ShouldBe(DownloadedRecorded);

                var nulls = await db.Measurements.SingleAsync(_ => _.Id == 2);
                nulls.StartMonth.ShouldBe((byte)0);
                nulls.EndMonth.ShouldBeNull();
                nulls.Zone.ShouldBe((short)32767);
                nulls.CountTime.ShouldBe(TimeSpan.Zero);
                nulls.Duration.ShouldBeNull();
                nulls.ExternalId.ShouldBe(Guid.Empty);

                var probe = await db.NullableProbes.SingleAsync(_ => _.Id == 1);
                probe.Moment.ShouldBe(DownloadedRecorded);
                probe.Amount.ShouldBe(1234.5678m);
                probe.Reference.ShouldBe(DownloadedGuid);
                probe.Flag.ShouldBe(true);
                probe.Ratio.ShouldBe(2.5);
                probe.Single.ShouldBe(1.5f);
                probe.Counter.ShouldBe(int.MinValue);
                probe.Big.ShouldBe(long.MaxValue);
                probe.Tiny.ShouldBe(short.MinValue);
                probe.Small.ShouldBe((byte)255);
                probe.Elapsed.ShouldBe(TickPreciseDuration);
                probe.Label.ShouldBe("set");

                var emptyProbe = await db.NullableProbes.SingleAsync(_ => _.Id == 2);
                emptyProbe.Moment.ShouldBeNull();
                emptyProbe.Amount.ShouldBeNull();
                emptyProbe.Reference.ShouldBeNull();
                emptyProbe.Flag.ShouldBeNull();
                emptyProbe.Ratio.ShouldBeNull();
                emptyProbe.Single.ShouldBeNull();
                emptyProbe.Counter.ShouldBeNull();
                emptyProbe.Big.ShouldBeNull();
                emptyProbe.Tiny.ShouldBeNull();
                emptyProbe.Small.ShouldBeNull();
                emptyProbe.Elapsed.ShouldBeNull();
                emptyProbe.Label.ShouldBeNull();
            }

            // ---------- SQLite → SQL Server ----------

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
                    Duration = UploadedDuration,
                    Weight = 999.999m,
                    ExternalId = UploadedGuid,
                    Recorded = UploadedRecorded
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
                db.NullableProbes.Add(new NullableProbe
                {
                    Id = 100,
                    Moment = UploadedRecorded,
                    Amount = -8765.4321m,
                    Reference = UploadedGuid,
                    Flag = false,
                    Ratio = -0.125,
                    Single = 0.5f,
                    Counter = 2147483647,
                    Big = long.MinValue,
                    Tiny = 32767,
                    Small = 0,
                    Elapsed = UploadedDuration,
                    Label = "uploaded"
                });
                db.NullableProbes.Add(new NullableProbe { Id = 101 });
                await db.SaveChangesAsync();
            }

            await agent.SynchronizeAsync();

            var uploaded = await ReadMeasurementFromSqlServer(sqlConnStr, 100);
            uploaded.ShouldNotBeNull();
            uploaded.Name.ShouldBe("Uploaded");
            uploaded.StartMonth.ShouldBe((byte)255);
            uploaded.EndMonth.ShouldBe((byte)1);
            uploaded.Zone.ShouldBe((short)-32768);
            uploaded.CountTime.ShouldBe(new TimeSpan(23, 59, 59));
            // time(7) keeps the sub-second part: a "c" formatted string must not lose it on the way in.
            uploaded.Duration.ShouldBe(UploadedDuration);
            uploaded.Weight.ShouldBe(999.999m);
            uploaded.ExternalId.ShouldBe(UploadedGuid);
            uploaded.Recorded.ShouldBe(UploadedRecorded);

            var uploadedNulls = await ReadMeasurementFromSqlServer(sqlConnStr, 101);
            uploadedNulls.ShouldNotBeNull();
            uploadedNulls.StartMonth.ShouldBe((byte)7);
            uploadedNulls.EndMonth.ShouldBeNull();
            uploadedNulls.CountTime.ShouldBe(TimeSpan.Zero);
            uploadedNulls.Duration.ShouldBeNull();

            var uploadedProbe = await ReadProbeFromSqlServer(sqlConnStr, 100);
            uploadedProbe.ShouldNotBeNull();
            uploadedProbe.Moment.ShouldBe(UploadedRecorded);
            uploadedProbe.Amount.ShouldBe(-8765.4321m);
            uploadedProbe.Reference.ShouldBe(UploadedGuid);
            uploadedProbe.Flag.ShouldBe(false);
            uploadedProbe.Ratio.ShouldBe(-0.125);
            uploadedProbe.Single.ShouldBe(0.5f);
            uploadedProbe.Counter.ShouldBe(int.MaxValue);
            uploadedProbe.Big.ShouldBe(long.MinValue);
            uploadedProbe.Tiny.ShouldBe((short)32767);
            uploadedProbe.Small.ShouldBe((byte)0);
            uploadedProbe.Elapsed.ShouldBe(UploadedDuration);
            uploadedProbe.Label.ShouldBe("uploaded");

            var uploadedEmptyProbe = await ReadProbeFromSqlServer(sqlConnStr, 101);
            uploadedEmptyProbe.ShouldNotBeNull();
            uploadedEmptyProbe.Moment.ShouldBeNull();
            uploadedEmptyProbe.Amount.ShouldBeNull();
            uploadedEmptyProbe.Reference.ShouldBeNull();
            uploadedEmptyProbe.Flag.ShouldBeNull();
            uploadedEmptyProbe.Small.ShouldBeNull();
            uploadedEmptyProbe.Elapsed.ShouldBeNull();
        }
        finally
        {
            server?.Dispose();
            SqlConnection.ClearAllPools();
            DeleteSqliteFile(sqliteFile);
            await DropSqlServerDatabase(dbName);
        }
    }

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_Direct()
        => RunSqlServerRoundTrip("Direct", Transport.Direct);

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_HttpJson()
        => RunSqlServerRoundTrip("HttpJson", Transport.HttpJson);

    [TestMethod]
    public Task SqlServerAndSqlite_RoundTripTinyIntAndTime_HttpBinary()
        => RunSqlServerRoundTrip("HttpBinary", Transport.HttpBinary);

    /// <summary>
    /// The change tracking provider has its own copy of <c>Utils.ConvertToSqlType</c>, so it needs its
    /// own proof that a tinyint/time column round-trips.
    /// </summary>
    [TestMethod]
    public Task SqlServerCTAndSqlite_RoundTripTinyIntAndTime_Direct()
        => RunSqlServerRoundTrip("CtDirect", Transport.Direct, useChangeTrackingProvider: true);

    [TestMethod]
    public Task SqlServerCTAndSqlite_RoundTripTinyIntAndTime_HttpJson()
        => RunSqlServerRoundTrip("CtHttpJson", Transport.HttpJson, useChangeTrackingProvider: true);

    [TestMethod]
    public Task SqlServerCTAndSqlite_RoundTripTinyIntAndTime_HttpBinary()
        => RunSqlServerRoundTrip("CtHttpBinary", Transport.HttpBinary, useChangeTrackingProvider: true);

    #endregion

    #region SQL Server time range and precision

    private static async Task RunInSqlServerDatabase(string dbName, Func<string, Task> body)
    {
        try
        {
            await CreateSqlServerDatabase(dbName);
            var connStr = SqlServerConnectionStringFor(dbName);
            await ExecuteSqlServerNonQuery(connStr, SqlServerDdl);
            await body(connStr);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await DropSqlServerDatabase(dbName);
        }
    }

    /// <summary>
    /// A SQL Server <c>time</c> column holds a time of day only. MySql and PostgreSQL are wider, so a
    /// value arriving from one of them can be unrepresentable here - and truncating it silently would be
    /// data loss. The apply must fail with a message that names the offending value.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_TimeValueOutsideTimeOfDayRange_IsRejectedWithAClearMessage()
    {
        await RunInSqlServerDatabase("CoreSyncTypes_TimeRange", async connStr =>
        {
            var provider = new SqlSyncProvider(
                new SqlSyncConfigurationBuilder(connStr).Table("Measurements").Table("NullableProbes").Build(),
                logger: new ConsoleLogger("SUT"));
            await provider.ApplyProvisionAsync();

            foreach (var (label, wireValue) in new[]
            {
                ("100 hours", "4.04:00:00"),
                ("the widest MySql TIME", "34.22:59:59"),
                ("a negative interval", "-01:02:03")
            })
            {
                var changeSet = new SyncChangeSet(
                    new SyncAnchor(Guid.NewGuid(), 1),
                    SyncAnchor.Null,
                    new[]
                    {
                        RawItem("Measurements", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                        {
                            ["Id"] = Raw(SyncItemValueType.Int32, 1),
                            ["Name"] = Raw(SyncItemValueType.String, label),
                            ["StartMonth"] = Raw(SyncItemValueType.Int32, 1),
                            ["EndMonth"] = Raw(SyncItemValueType.Null, null),
                            ["Zone"] = Raw(SyncItemValueType.Int32, (short)1),
                            ["CountTime"] = Raw(SyncItemValueType.String, wireValue),
                            ["Duration"] = Raw(SyncItemValueType.Null, null),
                            ["Weight"] = Raw(SyncItemValueType.Decimal, 1m),
                            ["ExternalId"] = Raw(SyncItemValueType.Guid, Guid.Empty),
                            ["Recorded"] = Raw(SyncItemValueType.DateTime, new DateTime(2026, 1, 1))
                        })
                    });

                var ex = await Should.ThrowAsync<Exception>(() => provider.ApplyChangesAsync(changeSet));
                var notSupported = FindInChain<NotSupportedException>(ex);
                notSupported.ShouldNotBeNull($"{label} must be rejected, not silently truncated");
                notSupported.Message.ShouldContain(wireValue);
                notSupported.Message.ShouldContain("time of day");

                // Nothing was written.
                (await ExecuteSqlServerScalar(connStr, "SELECT COUNT(*) FROM [dbo].[Measurements]")).ShouldBe(0);
            }
        });
    }

    /// <summary>
    /// A time of day at the very edge of the range is accepted: the guard must not be off by one tick.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_TimeValueAtTheEdgeOfTheRange_IsAccepted()
    {
        await RunInSqlServerDatabase("CoreSyncTypes_TimeEdge", async connStr =>
        {
            var provider = new SqlSyncProvider(
                new SqlSyncConfigurationBuilder(connStr).Table("Measurements").Table("NullableProbes").Build(),
                logger: new ConsoleLogger("SUT"));
            await provider.ApplyProvisionAsync();

            var lastTick = TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(1)); // 23:59:59.9999999

            await provider.ApplyChangesAsync(new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                SyncAnchor.Null,
                new[]
                {
                    RawItem("NullableProbes", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                    {
                        ["Id"] = Raw(SyncItemValueType.Int32, 1),
                        ["Elapsed"] = Raw(SyncItemValueType.String, TimeSpan.Zero.ToString("c", CultureInfo.InvariantCulture))
                    }),
                    RawItem("NullableProbes", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                    {
                        ["Id"] = Raw(SyncItemValueType.Int32, 2),
                        ["Elapsed"] = Raw(SyncItemValueType.String, lastTick.ToString("c", CultureInfo.InvariantCulture))
                    })
                }));

            (await ReadProbeFromSqlServer(connStr, 1))!.Elapsed.ShouldBe(TimeSpan.Zero);
            (await ReadProbeFromSqlServer(connStr, 2))!.Elapsed.ShouldBe(lastTick);
        });
    }

    /// <summary>
    /// A <c>time(0)</c> target cannot hold sub-second precision. Document what actually reaches the
    /// column - SQL Server rounds the value to the column's scale rather than truncating it - by
    /// asserting the stored result, not the value that was sent.
    /// </summary>
    [TestMethod]
    public async Task ApplyChanges_TimeValueWithMorePrecisionThanTheColumn_IsRoundedByTheStore()
    {
        await RunInSqlServerDatabase("CoreSyncTypes_TimeScale", async connStr =>
        {
            var provider = new SqlSyncProvider(
                new SqlSyncConfigurationBuilder(connStr).Table("Measurements").Table("NullableProbes").Build(),
                logger: new ConsoleLogger("SUT"));
            await provider.ApplyProvisionAsync();

            // CountTime is TIME(0); Duration is TIME(7) and keeps everything.
            async Task Insert(int id, TimeSpan countTime)
            {
                await provider.ApplyChangesAsync(new SyncChangeSet(
                    new SyncAnchor(Guid.NewGuid(), 1),
                    SyncAnchor.Null,
                    new[]
                    {
                        RawItem("Measurements", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                        {
                            ["Id"] = Raw(SyncItemValueType.Int32, id),
                            ["Name"] = Raw(SyncItemValueType.String, "scale"),
                            ["StartMonth"] = Raw(SyncItemValueType.Int32, 1),
                            ["Zone"] = Raw(SyncItemValueType.Int32, (short)1),
                            ["CountTime"] = Raw(SyncItemValueType.String, countTime.ToString("c", CultureInfo.InvariantCulture)),
                            ["Duration"] = Raw(SyncItemValueType.String, countTime.ToString("c", CultureInfo.InvariantCulture)),
                            ["Weight"] = Raw(SyncItemValueType.Decimal, 1m),
                            ["ExternalId"] = Raw(SyncItemValueType.Guid, Guid.Empty),
                            ["Recorded"] = Raw(SyncItemValueType.DateTime, new DateTime(2026, 1, 1))
                        })
                    }));
            }

            await Insert(1, new TimeSpan(14, 30, 0).Add(TimeSpan.FromMilliseconds(600)));
            await Insert(2, new TimeSpan(14, 30, 0).Add(TimeSpan.FromMilliseconds(400)));

            var roundedUp = await ReadMeasurementFromSqlServer(connStr, 1);
            roundedUp.ShouldNotBeNull();
            roundedUp.CountTime.ShouldBe(new TimeSpan(14, 30, 1), "SQL Server rounds to the column scale");
            roundedUp.Duration.ShouldBe(new TimeSpan(14, 30, 0).Add(TimeSpan.FromMilliseconds(600)), "time(7) keeps it");

            var roundedDown = await ReadMeasurementFromSqlServer(connStr, 2);
            roundedDown.ShouldNotBeNull();
            roundedDown.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
        });
    }

    #endregion

    #region Old peer compatibility against a live store

    /// <summary>
    /// Backward compatibility, old client → new server. This is the payload a released SQLite client
    /// actually produces for these columns today: a nullable byte column falls through to
    /// <c>GetValue()</c> and arrives as an <see cref="SyncItemValueType.Int64"/>, a nullable TimeSpan
    /// column arrives as the raw text SQLite stored. The upgraded server must keep accepting both.
    /// </summary>
    [TestMethod]
    public async Task OldClientPayload_IsStillAcceptedBySqlServer()
        => await RunOldClientPayloadTest("CoreSyncTypes_OldPeer", useChangeTrackingProvider: false);

    [TestMethod]
    public async Task OldClientPayload_IsStillAcceptedBySqlServerCT()
        => await RunOldClientPayloadTest("CoreSyncTypes_OldPeerCT", useChangeTrackingProvider: true);

    private static async Task RunOldClientPayloadTest(string dbName, bool useChangeTrackingProvider)
    {
        await RunInSqlServerDatabase(dbName, async connStr =>
        {
            ISyncProvider provider = useChangeTrackingProvider
                ? new SqlServerCTProvider(
                    new SqlServerCTSyncConfigurationBuilder(connStr).Table("Measurements").Table("NullableProbes").Build(),
                    logger: new ConsoleLogger("SUT-CT"))
                : new SqlSyncProvider(
                    new SqlSyncConfigurationBuilder(connStr).Table("Measurements").Table("NullableProbes").Build(),
                    logger: new ConsoleLogger("SUT"));
            await provider.ApplyProvisionAsync();

            await provider.ApplyChangesAsync(new SyncChangeSet(
                new SyncAnchor(Guid.NewGuid(), 1),
                SyncAnchor.Null,
                new[]
                {
                    RawItem("Measurements", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                    {
                        ["Id"] = Raw(SyncItemValueType.Int64, 1L),
                        ["Name"] = Raw(SyncItemValueType.String, "old client"),
                        // an old SQLite client read a byte? column with GetValue() -> Int64
                        ["StartMonth"] = Raw(SyncItemValueType.Int64, 3L),
                        ["EndMonth"] = Raw(SyncItemValueType.Int64, 250L),
                        ["Zone"] = Raw(SyncItemValueType.Int64, -5L),
                        // ...and a TimeSpan? column as the raw stored text
                        ["CountTime"] = Raw(SyncItemValueType.String, "14:30:00"),
                        ["Duration"] = Raw(SyncItemValueType.String, "01:02:03.4567890"),
                        // decimals used to arrive as text too
                        ["Weight"] = Raw(SyncItemValueType.String, "12.345"),
                        ["ExternalId"] = Raw(SyncItemValueType.String, DownloadedGuid.ToString()),
                        ["Recorded"] = Raw(SyncItemValueType.String, "2026-09-15T21:21:33.1234567")
                    })
                }));

            var applied = await ReadMeasurementFromSqlServer(connStr, 1);
            applied.ShouldNotBeNull();
            applied.StartMonth.ShouldBe((byte)3);
            applied.EndMonth.ShouldBe((byte)250);
            applied.Zone.ShouldBe((short)-5);
            applied.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
            applied.Duration.ShouldBe(TickPreciseDuration);
            applied.Weight.ShouldBe(12.345m);
            applied.ExternalId.ShouldBe(DownloadedGuid);
            applied.Recorded.ShouldBe(DownloadedRecorded);
        });
    }

    #endregion

    #region SQLite typed reads on legacy stores

    /// <summary>
    /// Nullable columns now go through the same typed getters as their non-nullable counterparts. A store
    /// written by something else can hold text those getters refuse - SQLite has affinities, not types -
    /// and such a store must stay synchronizable: the reader falls back to the raw stored value, which is
    /// exactly what it returned before typed reads were extended to nullable columns.
    /// </summary>
    [TestMethod]
    public async Task Sqlite_ReadingALegacyValueFallsBackToTheRawStoredValue()
    {
        var sqliteFile = CreateSqliteFile("LegacyValues");
        var connStr = $"Data Source={sqliteFile}";

        try
        {
            await ExecuteSqliteNonQuery(connStr, SqliteDdl);

            var provider = CreateSqliteProvider(connStr);
            await provider.ApplyProvisionAsync();

            // A date in a non-invariant format and a boolean spelled out as text.
            await ExecuteSqliteNonQuery(connStr, @"
                INSERT INTO [NullableProbes] ([Id],[Moment],[Flag],[Label])
                VALUES (1, '15/09/2026 21:21:33', 'yes', 'legacy')");

            var changes = await provider.GetChangesAsync(Guid.NewGuid());

            var item = changes.Items.Single(_ => _.TableName == "NullableProbes");

            // GetDateTime parses text and throws on a format it doesn't recognize: the reader falls back
            // to the raw stored value instead of failing the whole synchronization.
            item.Values["Moment"].Type.ShouldBe(SyncItemValueType.String);
            item.Values["Moment"].Value.ShouldBe("15/09/2026 21:21:33");

            // GetBoolean (like the integer and floating point getters) does not parse text: SQLite converts
            // non-numeric text to 0, so it reads false without throwing. That is exactly what EF Core
            // returns to the application for this row, and what a non-nullable bool column has always
            // synchronized, so no fallback applies - sending the raw 'yes' to a server bit column would
            // only fail when the change is applied.
            item.Values["Flag"].Type.ShouldBe(SyncItemValueType.Boolean);
            item.Values["Flag"].Value.ShouldBe(false);

            item.Values["Label"].Value.ShouldBe("legacy");
        }
        finally
        {
            DeleteSqliteFile(sqliteFile);
        }
    }

    /// <summary>
    /// The companion of the test above: a store holding well-formed values must be read through the typed
    /// getters, so a nullable column produces the same <see cref="SyncItemValueType"/> as the non-nullable
    /// column of the same type - and the same one the server sends down for it.
    /// </summary>
    [TestMethod]
    public async Task Sqlite_ReadsNullableColumnsThroughTheirUnderlyingType()
    {
        var sqliteFile = CreateSqliteFile("NullableTypes");
        var connStr = $"Data Source={sqliteFile}";

        try
        {
            await ExecuteSqliteNonQuery(connStr, SqliteDdl);

            var provider = CreateSqliteProvider(connStr);
            await provider.ApplyProvisionAsync();

            using (var db = new MeasurementDbContext(connStr))
            {
                db.NullableProbes.Add(new NullableProbe
                {
                    Id = 1,
                    Moment = UploadedRecorded,
                    Amount = 1234.5678m,
                    Reference = UploadedGuid,
                    Flag = true,
                    Ratio = 2.5,
                    Single = 1.5f,
                    Counter = 7,
                    Big = long.MaxValue,
                    Tiny = -32768,
                    Small = 255,
                    Elapsed = TickPreciseDuration,
                    Label = "typed"
                });
                await db.SaveChangesAsync();
            }

            var changes = await provider.GetChangesAsync(Guid.NewGuid());
            var values = changes.Items.Single(_ => _.TableName == "NullableProbes").Values;

            values["Moment"].Type.ShouldBe(SyncItemValueType.DateTime);
            values["Moment"].Value.ShouldBe(UploadedRecorded);
            values["Amount"].Type.ShouldBe(SyncItemValueType.Decimal);
            values["Amount"].Value.ShouldBe(1234.5678m);
            values["Flag"].Type.ShouldBe(SyncItemValueType.Boolean);
            values["Flag"].Value.ShouldBe(true);
            values["Ratio"].Type.ShouldBe(SyncItemValueType.Double);
            values["Ratio"].Value.ShouldBe(2.5);
            values["Single"].Type.ShouldBe(SyncItemValueType.Float);
            values["Single"].Value.ShouldBe(1.5f);
            values["Counter"].Type.ShouldBe(SyncItemValueType.Int32);
            values["Counter"].Value.ShouldBe(7);
            values["Big"].Type.ShouldBe(SyncItemValueType.Int64);
            values["Big"].Value.ShouldBe(long.MaxValue);
            values["Tiny"].Type.ShouldBe(SyncItemValueType.Int32);
            values["Small"].Type.ShouldBe(SyncItemValueType.Int32);
            values["Small"].Value.ShouldBe(255);
            values["Elapsed"].Type.ShouldBe(SyncItemValueType.String);
            values["Elapsed"].Value.ShouldBe("01:02:03.4567890");
            // Guid? has no typed getter and keeps falling through to the raw stored value
            values["Reference"].Value.ShouldNotBeNull();
        }
        finally
        {
            DeleteSqliteFile(sqliteFile);
        }
    }

    #endregion
}
