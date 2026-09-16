using CoreSync.MySql;
using CoreSync.PostgreSQL;
using CoreSync.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;
using Npgsql;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// The MySql and PostgreSQL half of <see cref="ColumnTypeMappingTests"/>: the same tinyint-like and
/// time-like round trips, plus the behaviours that are specific to those two stores.
/// <list type="bullet">
/// <item><description>MySql reports a <c>TINYINT(1)</c> as a <see cref="bool"/>, a <c>TINYINT</c> as an
/// <see cref="sbyte"/> and a <c>TINYINT UNSIGNED</c> as a <see cref="byte"/> - three different CLR types
/// for one SQL type, and only one of them used to be carryable.</description></item>
/// <item><description>A MySql <c>TIME</c> spans -838:59:59 to 838:59:59 and a PostgreSQL <c>interval</c>
/// is unbounded, so both can hold values a SQL Server <c>time</c> column cannot. The <c>"c"</c> wire
/// format carries them; see <c>ApplyChanges_TimeValueOutsideTimeOfDayRange_IsRejectedWithAClearMessage</c>
/// for what happens when one is pushed into SQL Server anyway.</description></item>
/// <item><description>PostgreSQL's default CLR type for <c>time without time zone</c> is
/// <c>TimeOnly</c>, which has no <see cref="SyncItemValueType"/> and cannot even be named from
/// netstandard2.0, so the provider materializes those columns as a <see cref="TimeSpan"/> explicitly.
/// <c>timetz</c> and a month-bearing <c>interval</c> stay unsupported, on purpose.</description></item>
/// </list>
/// </summary>
public partial class ColumnTypeMappingTests
{
    private static string MySqlConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_MYSQL_CONNECTION_STRING") ??
        "Server=localhost;Port=3306;Database=coresync_test;User=root;Password=test123;GuidFormat=Char36";

    private static string PostgreSQLConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_POSTGRESQL_CONNECTION_STRING") ??
        "Host=localhost;Port=5432;Database=coresync_test;Username=coresync;Password=test123";

    private static string MySqlConnectionStringFor(string database)
        => new MySqlConnectionStringBuilder(MySqlConnectionString) { Database = database }.ConnectionString;

    private static string PostgreSQLConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(PostgreSQLConnectionString) { Database = database }.ConnectionString;

    #region Model

    /// <summary>
    /// The MySql/PostgreSQL counterpart of <see cref="Measurement"/>. It carries no <see cref="Guid"/>
    /// column: how a Guid is spelled differs per store (MySql <c>CHAR(36)</c> uppercased, PostgreSQL
    /// <c>uuid</c>) and that is unrelated to the types under test, which
    /// <see cref="SqlServerAndSqlite_RoundTripTinyIntAndTime_Direct"/> already covers.
    /// </summary>
    public class Reading
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte StartMonth { get; set; }
        public byte? EndMonth { get; set; }
        public short Zone { get; set; }
        public TimeSpan CountTime { get; set; }
        public TimeSpan? Duration { get; set; }
        public decimal Weight { get; set; }
        public DateTime Recorded { get; set; }
    }

    /// <summary>A single time-like column, used for the values a time of day cannot express.</summary>
    public class Span
    {
        public int Id { get; set; }
        public TimeSpan Value { get; set; }
    }

    private sealed class ReadingDbContext : DbContext
    {
        private readonly string _connectionString;

        public ReadingDbContext(string connectionString) => _connectionString = connectionString;

        public DbSet<Reading> Readings => Set<Reading>();
        public DbSet<Span> Spans => Set<Span>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Reading>(entity =>
            {
                entity.ToTable("Readings");
                entity.HasKey(_ => _.Id);
                entity.Property(_ => _.Id).ValueGeneratedNever();
                entity.Property(_ => _.Weight).HasColumnType("TEXT");
            });

            modelBuilder.Entity<Span>(entity =>
            {
                entity.ToTable("Spans");
                entity.HasKey(_ => _.Id);
                entity.Property(_ => _.Id).ValueGeneratedNever();
            });
        }
    }

    private const string SqliteReadingsDdl = @"
        CREATE TABLE [Readings] (
            [Id] INTEGER NOT NULL PRIMARY KEY,
            [Name] TEXT NOT NULL,
            [StartMonth] INTEGER NOT NULL,
            [EndMonth] INTEGER NULL,
            [Zone] INTEGER NOT NULL,
            [CountTime] TEXT NOT NULL,
            [Duration] TEXT NULL,
            [Weight] TEXT NOT NULL,
            [Recorded] TEXT NOT NULL
        );
        CREATE TABLE [Spans] (
            [Id] INTEGER NOT NULL PRIMARY KEY,
            [Value] TEXT NOT NULL
        );";

    // MySql resolves TIME/DATETIME to microseconds at most, PostgreSQL likewise, while a TimeSpan tick is
    // 100ns. Every value below is therefore a whole number of microseconds, so a mismatch means a real
    // conversion bug rather than the store's declared resolution.
    private static TimeSpan MicroDuration => new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4567890));
    private static TimeSpan MicroUploadedDuration => new TimeSpan(0, 12, 34, 56).Add(TimeSpan.FromTicks(7891230));
    private static DateTime MicroRecorded => new DateTime(2026, 9, 15, 21, 21, 33).AddTicks(1234560);
    private static DateTime MicroUploadedRecorded => new DateTime(2026, 2, 3, 4, 5, 6).AddTicks(7654320);

    #endregion

    #region Helpers - MySql

    private const string MySqlDdl = @"
        CREATE TABLE `Readings` (
            `Id` INT NOT NULL PRIMARY KEY,
            `Name` VARCHAR(100) NOT NULL,
            `StartMonth` TINYINT UNSIGNED NOT NULL,
            `EndMonth` TINYINT UNSIGNED NULL,
            `Zone` SMALLINT NOT NULL,
            `CountTime` TIME NOT NULL,
            `Duration` TIME(6) NULL,
            `Weight` DECIMAL(10,3) NOT NULL,
            `Recorded` DATETIME(6) NOT NULL
        );
        CREATE TABLE `Spans` (
            `Id` INT NOT NULL PRIMARY KEY,
            `Value` TIME NOT NULL
        );
        CREATE TABLE `TinyIntFlavors` (
            `Id` INT NOT NULL PRIMARY KEY,
            `Flag` TINYINT(1) NOT NULL,
            `Signed` TINYINT NOT NULL,
            `Unsigned` TINYINT UNSIGNED NOT NULL
        );";

    private static async Task ExecuteMySqlNonQuery(string connectionString, string commandText)
    {
        using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateMySqlDatabase(string dbName)
    {
        MySqlConnection.ClearAllPools();
        using var conn = new MySqlConnection(MySqlConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS `{dbName}`; CREATE DATABASE `{dbName}`;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropMySqlDatabase(string dbName)
    {
        MySqlConnection.ClearAllPools();
        using var conn = new MySqlConnection(MySqlConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS `{dbName}`;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Reading?> ReadReadingFromMySql(string connectionString, int id)
    {
        using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT `Name`,`StartMonth`,`EndMonth`,`Zone`,`CountTime`,`Duration`,`Weight`,`Recorded` FROM `Readings` WHERE `Id` = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Reading
        {
            Id = id,
            Name = reader.GetString(0),
            // GetByte/GetTimeSpan: the column must really be a tinyint/time on arrival.
            StartMonth = reader.GetByte(1),
            EndMonth = reader.IsDBNull(2) ? null : reader.GetByte(2),
            Zone = reader.GetInt16(3),
            CountTime = reader.GetTimeSpan(4),
            Duration = reader.IsDBNull(5) ? null : reader.GetTimeSpan(5),
            Weight = reader.GetDecimal(6),
            Recorded = reader.GetDateTime(7)
        };
    }

    private static async Task<TimeSpan> ReadSpanFromMySql(string connectionString, int id)
    {
        using var conn = new MySqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT `Value` FROM `Spans` WHERE `Id` = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue($"row {id} must exist in Spans");
        return reader.GetTimeSpan(0);
    }

    private static MySqlSyncProvider CreateMySqlProvider(string connectionString, params string[] tables)
    {
        var builder = new MySqlSyncConfigurationBuilder(connectionString);
        foreach (var table in tables)
            builder.Table(table);
        return new MySqlSyncProvider(builder.Build(), logger: new ConsoleLogger("MYSQL"));
    }

    #endregion

    #region Helpers - PostgreSQL

    // Identifiers are quoted so PostgreSQL keeps their case: the column names have to match the ones the
    // SQLite peer sends, and an unquoted identifier would be folded to lower case.
    private const string PostgreSQLDdl = @"
        CREATE TABLE ""Readings"" (
            ""Id"" INTEGER NOT NULL PRIMARY KEY,
            ""Name"" TEXT NOT NULL,
            ""StartMonth"" SMALLINT NOT NULL,
            ""EndMonth"" SMALLINT NULL,
            ""Zone"" SMALLINT NOT NULL,
            ""CountTime"" TIME(6) NOT NULL,
            ""Duration"" TIME(6) NULL,
            ""Weight"" NUMERIC(10,3) NOT NULL,
            ""Recorded"" TIMESTAMP NOT NULL
        );
        CREATE TABLE ""Spans"" (
            ""Id"" INTEGER NOT NULL PRIMARY KEY,
            ""Value"" INTERVAL NOT NULL
        );";

    private static async Task ExecutePostgreSQLNonQuery(string connectionString, string commandText)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreatePostgreSQLDatabase(string dbName)
    {
        NpgsqlConnection.ClearAllPools();
        using var conn = new NpgsqlConnection(PostgreSQLConnectionString);
        await conn.OpenAsync();
        using (var drop = conn.CreateCommand())
        {
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)";
            await drop.ExecuteNonQueryAsync();
        }
        using var create = conn.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await create.ExecuteNonQueryAsync();
    }

    private static async Task DropPostgreSQLDatabase(string dbName)
    {
        NpgsqlConnection.ClearAllPools();
        using var conn = new NpgsqlConnection(PostgreSQLConnectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<Reading?> ReadReadingFromPostgreSQL(string connectionString, int id)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""Name"",""StartMonth"",""EndMonth"",""Zone"",""CountTime"",""Duration"",""Weight"",""Recorded"" FROM ""Readings"" WHERE ""Id"" = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new Reading
        {
            Id = id,
            Name = reader.GetString(0),
            StartMonth = (byte)reader.GetInt16(1),
            EndMonth = reader.IsDBNull(2) ? null : (byte)reader.GetInt16(2),
            Zone = reader.GetInt16(3),
            // GetFieldValue<TimeSpan>: a 'time without time zone' column materializes as TimeOnly by
            // default, so asking for a TimeSpan proves the column really is a time.
            CountTime = reader.GetFieldValue<TimeSpan>(4),
            Duration = reader.IsDBNull(5) ? null : reader.GetFieldValue<TimeSpan>(5),
            Weight = reader.GetDecimal(6),
            Recorded = reader.GetDateTime(7)
        };
    }

    private static async Task<TimeSpan> ReadSpanFromPostgreSQL(string connectionString, int id)
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""Value"" FROM ""Spans"" WHERE ""Id"" = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = id });
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue($"row {id} must exist in Spans");
        return reader.GetFieldValue<TimeSpan>(0);
    }

    private static PostgreSQLSyncProvider CreatePostgreSQLProvider(string connectionString, params string[] tables)
    {
        var builder = new PostgreSQLSyncConfigurationBuilder(connectionString);
        foreach (var table in tables)
            builder.Table(table);
        return new PostgreSQLSyncProvider(builder.Build(), logger: new ConsoleLogger("PGSQL"));
    }

    #endregion

    #region Helpers - SQLite side of these tests

    private static SqliteSyncProvider CreateSqliteReadingsProvider(string connectionString)
        => new(new SqliteSyncConfigurationBuilder(connectionString)
                .Table<Reading>("Readings")
                .Table<Span>("Spans")
                .Build(),
            logger: new ConsoleLogger("LOC"));

    #endregion

    #region MySql ⇄ SQLite round trips

    private static async Task RunMySqlRoundTrip(string testName, Transport transport)
    {
        var dbName = $"coresync_types_{testName.ToLowerInvariant()}";
        var sqliteFile = CreateSqliteFile(testName);
        var sqliteConnStr = $"Data Source={sqliteFile}";

        SyncTestServer? server = null;

        try
        {
            await CreateMySqlDatabase(dbName);
            var mysqlConnStr = MySqlConnectionStringFor(dbName);
            await ExecuteMySqlNonQuery(mysqlConnStr, MySqlDdl);
            await ExecuteSqliteNonQuery(sqliteConnStr, SqliteReadingsDdl);

            var remoteProvider = CreateMySqlProvider(mysqlConnStr, "Readings", "Spans");
            await remoteProvider.ApplyProvisionAsync();

            var localProvider = CreateSqliteReadingsProvider(sqliteConnStr);
            await localProvider.ApplyProvisionAsync();

            (var remote, server) = Wrap(remoteProvider, transport);
            var agent = new SyncAgent(localProvider, remote);

            // ---------- MySql → SQLite ----------

            await ExecuteMySqlNonQuery(mysqlConnStr, @"
                INSERT INTO `Readings` (`Id`,`Name`,`StartMonth`,`EndMonth`,`Zone`,`CountTime`,`Duration`,`Weight`,`Recorded`)
                VALUES
                    (1, 'Spring', 3, 250, -5, '14:30:00', '01:02:03.456789', 12.345, '2026-09-15 21:21:33.123456'),
                    (2, 'Winter', 0, NULL, 32767, '00:00:00', NULL, 0.001, '2026-01-01 00:00:00.000000');
                INSERT INTO `Spans` (`Id`,`Value`) VALUES
                    (1, '100:00:00'),
                    (2, '-01:02:03'),
                    (3, '838:59:59');");

            await agent.SynchronizeAsync();

            {
                using var db = new ReadingDbContext(sqliteConnStr);

                var full = await db.Readings.SingleAsync(_ => _.Id == 1);
                full.StartMonth.ShouldBe((byte)3);
                // 250 is out of range for the sbyte MySql reports a signed TINYINT as: it proves the
                // value was widened, not reinterpreted.
                full.EndMonth.ShouldBe((byte)250);
                full.Zone.ShouldBe((short)-5);
                full.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
                full.Duration.ShouldBe(MicroDuration);
                full.Weight.ShouldBe(12.345m);
                full.Recorded.ShouldBe(MicroRecorded);

                var nulls = await db.Readings.SingleAsync(_ => _.Id == 2);
                nulls.EndMonth.ShouldBeNull();
                nulls.Duration.ShouldBeNull();
                nulls.CountTime.ShouldBe(TimeSpan.Zero);

                // A MySql TIME is not a time of day, and the "c" wire format carries all of it.
                (await db.Spans.SingleAsync(_ => _.Id == 1)).Value.ShouldBe(TimeSpan.FromHours(100));
                (await db.Spans.SingleAsync(_ => _.Id == 2)).Value.ShouldBe(new TimeSpan(0, -1, -2, -3));
                (await db.Spans.SingleAsync(_ => _.Id == 3)).Value.ShouldBe(new TimeSpan(838, 59, 59));
            }

            // ---------- SQLite → MySql ----------

            {
                using var db = new ReadingDbContext(sqliteConnStr);
                db.Readings.Add(new Reading
                {
                    Id = 100,
                    Name = "Uploaded",
                    StartMonth = 255,
                    EndMonth = 1,
                    Zone = -32768,
                    CountTime = new TimeSpan(23, 59, 59),
                    Duration = MicroUploadedDuration,
                    Weight = 999.999m,
                    Recorded = MicroUploadedRecorded
                });
                db.Readings.Add(new Reading
                {
                    Id = 101,
                    Name = "UploadedNulls",
                    StartMonth = 0,
                    EndMonth = null,
                    Zone = 1,
                    CountTime = TimeSpan.Zero,
                    Duration = null,
                    Weight = 0m,
                    Recorded = new DateTime(2026, 3, 4, 5, 6, 7)
                });
                db.Spans.Add(new Span { Id = 100, Value = TimeSpan.FromHours(-100) });
                db.Spans.Add(new Span { Id = 101, Value = new TimeSpan(500, 0, 0) });
                await db.SaveChangesAsync();
            }

            await agent.SynchronizeAsync();

            var uploaded = await ReadReadingFromMySql(mysqlConnStr, 100);
            uploaded.ShouldNotBeNull();
            uploaded.StartMonth.ShouldBe((byte)255);
            uploaded.EndMonth.ShouldBe((byte)1);
            uploaded.Zone.ShouldBe((short)-32768);
            uploaded.CountTime.ShouldBe(new TimeSpan(23, 59, 59));
            uploaded.Duration.ShouldBe(MicroUploadedDuration);
            uploaded.Weight.ShouldBe(999.999m);
            uploaded.Recorded.ShouldBe(MicroUploadedRecorded);

            var uploadedNulls = await ReadReadingFromMySql(mysqlConnStr, 101);
            uploadedNulls.ShouldNotBeNull();
            uploadedNulls.StartMonth.ShouldBe((byte)0);
            uploadedNulls.EndMonth.ShouldBeNull();
            uploadedNulls.Duration.ShouldBeNull();

            (await ReadSpanFromMySql(mysqlConnStr, 100)).ShouldBe(TimeSpan.FromHours(-100));
            (await ReadSpanFromMySql(mysqlConnStr, 101)).ShouldBe(new TimeSpan(500, 0, 0));
        }
        finally
        {
            server?.Dispose();
            DeleteSqliteFile(sqliteFile);
            await DropMySqlDatabase(dbName);
        }
    }

    [TestMethod]
    public Task MySqlAndSqlite_RoundTripTinyIntAndTime_Direct()
        => RunMySqlRoundTrip("MySqlDirect", Transport.Direct);

    [TestMethod]
    public Task MySqlAndSqlite_RoundTripTinyIntAndTime_HttpJson()
        => RunMySqlRoundTrip("MySqlHttpJson", Transport.HttpJson);

    [TestMethod]
    public Task MySqlAndSqlite_RoundTripTinyIntAndTime_HttpBinary()
        => RunMySqlRoundTrip("MySqlHttpBinary", Transport.HttpBinary);

    /// <summary>
    /// One SQL type, three CLR types: MySqlConnector reports <c>TINYINT(1)</c> as a <see cref="bool"/>,
    /// <c>TINYINT</c> as an <see cref="sbyte"/> and <c>TINYINT UNSIGNED</c> as a <see cref="byte"/>.
    /// Only the first was carryable before; all three must be now, and each must land on the
    /// <see cref="SyncItemValueType"/> that keeps its value intact.
    /// </summary>
    [TestMethod]
    public async Task MySql_ReportsEveryTinyIntFlavourWithACarryableType()
    {
        var dbName = "coresync_types_tinyintflavors";

        try
        {
            await CreateMySqlDatabase(dbName);
            var connStr = MySqlConnectionStringFor(dbName);
            await ExecuteMySqlNonQuery(connStr, MySqlDdl);

            var provider = CreateMySqlProvider(connStr, "TinyIntFlavors");
            await provider.ApplyProvisionAsync();

            await ExecuteMySqlNonQuery(connStr, @"
                INSERT INTO `TinyIntFlavors` (`Id`,`Flag`,`Signed`,`Unsigned`) VALUES
                    (1, 1, -128, 255),
                    (2, 0, 127, 0);");

            var changes = await provider.GetChangesAsync(Guid.NewGuid());
            changes.Items.Count.ShouldBe(2);

            var first = changes.Items.Single(_ => Convert.ToInt32(_.Values["Id"].Value) == 1).Values;
            first["Flag"].Type.ShouldBe(SyncItemValueType.Boolean, "MySql reports TINYINT(1) as a bool");
            first["Flag"].Value.ShouldBe(true);
            first["Signed"].Type.ShouldBe(SyncItemValueType.Int32, "MySql reports TINYINT as an sbyte");
            first["Signed"].Value.ShouldBeOfType<int>().ShouldBe(-128);
            first["Unsigned"].Type.ShouldBe(SyncItemValueType.Int32, "MySql reports TINYINT UNSIGNED as a byte");
            first["Unsigned"].Value.ShouldBeOfType<int>().ShouldBe(255);

            var second = changes.Items.Single(_ => Convert.ToInt32(_.Values["Id"].Value) == 2).Values;
            second["Flag"].Value.ShouldBe(false);
            second["Signed"].Value.ShouldBe(127);
            second["Unsigned"].Value.ShouldBe(0);
        }
        finally
        {
            await DropMySqlDatabase(dbName);
        }
    }

    #endregion

    #region PostgreSQL ⇄ SQLite round trips

    private static async Task RunPostgreSQLRoundTrip(string testName, Transport transport)
    {
        var dbName = $"coresync_types_{testName.ToLowerInvariant()}";
        var sqliteFile = CreateSqliteFile(testName);
        var sqliteConnStr = $"Data Source={sqliteFile}";

        SyncTestServer? server = null;

        try
        {
            await CreatePostgreSQLDatabase(dbName);
            var pgConnStr = PostgreSQLConnectionStringFor(dbName);
            await ExecutePostgreSQLNonQuery(pgConnStr, PostgreSQLDdl);
            await ExecuteSqliteNonQuery(sqliteConnStr, SqliteReadingsDdl);

            var remoteProvider = CreatePostgreSQLProvider(pgConnStr, "Readings", "Spans");
            await remoteProvider.ApplyProvisionAsync();

            var localProvider = CreateSqliteReadingsProvider(sqliteConnStr);
            await localProvider.ApplyProvisionAsync();

            (var remote, server) = Wrap(remoteProvider, transport);
            var agent = new SyncAgent(localProvider, remote);

            // ---------- PostgreSQL → SQLite ----------

            await ExecutePostgreSQLNonQuery(pgConnStr, @"
                INSERT INTO ""Readings"" (""Id"",""Name"",""StartMonth"",""EndMonth"",""Zone"",""CountTime"",""Duration"",""Weight"",""Recorded"")
                VALUES
                    (1, 'Spring', 3, 250, -5, TIME '14:30:00', TIME '01:02:03.456789', 12.345, TIMESTAMP '2026-09-15 21:21:33.123456'),
                    (2, 'Winter', 0, NULL, 32767, TIME '00:00:00', NULL, 0.001, TIMESTAMP '2026-01-01 00:00:00');
                INSERT INTO ""Spans"" (""Id"",""Value"") VALUES
                    (1, INTERVAL '100 hours'),
                    (2, INTERVAL '-1 hour -2 minutes -3 seconds'),
                    (3, INTERVAL '2 days 3 hours');");

            await agent.SynchronizeAsync();

            {
                using var db = new ReadingDbContext(sqliteConnStr);

                var full = await db.Readings.SingleAsync(_ => _.Id == 1);
                full.StartMonth.ShouldBe((byte)3);
                full.EndMonth.ShouldBe((byte)250);
                full.Zone.ShouldBe((short)-5);
                // PostgreSQL materializes a 'time without time zone' as a TimeOnly by default, which has
                // no SyncItemValueType: the provider has to ask for a TimeSpan explicitly.
                full.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
                full.Duration.ShouldBe(MicroDuration);
                full.Weight.ShouldBe(12.345m);
                full.Recorded.ShouldBe(MicroRecorded);

                var nulls = await db.Readings.SingleAsync(_ => _.Id == 2);
                nulls.EndMonth.ShouldBeNull();
                nulls.Duration.ShouldBeNull();

                (await db.Spans.SingleAsync(_ => _.Id == 1)).Value.ShouldBe(TimeSpan.FromHours(100));
                (await db.Spans.SingleAsync(_ => _.Id == 2)).Value.ShouldBe(new TimeSpan(0, -1, -2, -3));
                (await db.Spans.SingleAsync(_ => _.Id == 3)).Value.ShouldBe(new TimeSpan(2, 3, 0, 0));
            }

            // ---------- SQLite → PostgreSQL ----------

            {
                using var db = new ReadingDbContext(sqliteConnStr);
                db.Readings.Add(new Reading
                {
                    Id = 100,
                    Name = "Uploaded",
                    StartMonth = 255,
                    EndMonth = 1,
                    Zone = -32768,
                    CountTime = new TimeSpan(23, 59, 59),
                    Duration = MicroUploadedDuration,
                    Weight = 999.999m,
                    Recorded = MicroUploadedRecorded
                });
                db.Readings.Add(new Reading
                {
                    Id = 101,
                    Name = "UploadedNulls",
                    StartMonth = 0,
                    EndMonth = null,
                    Zone = 1,
                    CountTime = TimeSpan.Zero,
                    Duration = null,
                    Weight = 0m,
                    Recorded = new DateTime(2026, 3, 4, 5, 6, 7)
                });
                db.Spans.Add(new Span { Id = 100, Value = TimeSpan.FromHours(-100) });
                db.Spans.Add(new Span { Id = 101, Value = new TimeSpan(500, 0, 0) });
                await db.SaveChangesAsync();
            }

            await agent.SynchronizeAsync();

            var uploaded = await ReadReadingFromPostgreSQL(pgConnStr, 100);
            uploaded.ShouldNotBeNull();
            uploaded.StartMonth.ShouldBe((byte)255);
            uploaded.EndMonth.ShouldBe((byte)1);
            uploaded.Zone.ShouldBe((short)-32768);
            uploaded.CountTime.ShouldBe(new TimeSpan(23, 59, 59));
            uploaded.Duration.ShouldBe(MicroUploadedDuration);
            uploaded.Weight.ShouldBe(999.999m);
            uploaded.Recorded.ShouldBe(MicroUploadedRecorded);

            var uploadedNulls = await ReadReadingFromPostgreSQL(pgConnStr, 101);
            uploadedNulls.ShouldNotBeNull();
            uploadedNulls.StartMonth.ShouldBe((byte)0);
            uploadedNulls.EndMonth.ShouldBeNull();
            uploadedNulls.Duration.ShouldBeNull();

            (await ReadSpanFromPostgreSQL(pgConnStr, 100)).ShouldBe(TimeSpan.FromHours(-100));
            (await ReadSpanFromPostgreSQL(pgConnStr, 101)).ShouldBe(new TimeSpan(500, 0, 0));
        }
        finally
        {
            server?.Dispose();
            DeleteSqliteFile(sqliteFile);
            await DropPostgreSQLDatabase(dbName);
        }
    }

    [TestMethod]
    public Task PostgreSQLAndSqlite_RoundTripSmallIntAndTime_Direct()
        => RunPostgreSQLRoundTrip("PgDirect", Transport.Direct);

    [TestMethod]
    public Task PostgreSQLAndSqlite_RoundTripSmallIntAndTime_HttpJson()
        => RunPostgreSQLRoundTrip("PgHttpJson", Transport.HttpJson);

    [TestMethod]
    public Task PostgreSQLAndSqlite_RoundTripSmallIntAndTime_HttpBinary()
        => RunPostgreSQLRoundTrip("PgHttpBinary", Transport.HttpBinary);

    /// <summary>
    /// The two PostgreSQL shapes that stay unsupported, asserted so the boundary is explicit rather than
    /// implied: a <c>timetz</c> materializes as a <see cref="DateTimeOffset"/>, and an interval carrying
    /// months has no fixed length so it cannot become a <see cref="TimeSpan"/> at all.
    /// </summary>
    [TestMethod]
    public async Task PostgreSQL_TimetzAndMonthBearingIntervalsRemainUnsupported()
    {
        var dbName = "coresync_types_pgunsupported";

        try
        {
            await CreatePostgreSQLDatabase(dbName);
            var connStr = PostgreSQLConnectionStringFor(dbName);
            await ExecutePostgreSQLNonQuery(connStr, @"
                CREATE TABLE ""WithTimetz"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY,
                    ""Moment"" TIMETZ NOT NULL
                );
                CREATE TABLE ""Spans"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY,
                    ""Value"" INTERVAL NOT NULL
                );");

            var provider = CreatePostgreSQLProvider(connStr, "WithTimetz", "Spans");
            await provider.ApplyProvisionAsync();

            await ExecutePostgreSQLNonQuery(connStr, @"
                INSERT INTO ""WithTimetz"" (""Id"",""Moment"") VALUES (1, TIMETZ '14:30:00+02');");

            var timetzFailure = await Should.ThrowAsync<Exception>(() => provider.GetChangesAsync(Guid.NewGuid()));
            var notSupported = FindInChain<NotSupportedException>(timetzFailure);
            notSupported.ShouldNotBeNull("a timetz column has no SyncItemValueType and must say so");
            notSupported.Message.ShouldContain("DateTimeOffset");

            await ExecutePostgreSQLNonQuery(connStr, @"DELETE FROM ""WithTimetz""; DELETE FROM __core_sync_ct;");
            await ExecutePostgreSQLNonQuery(connStr, @"INSERT INTO ""Spans"" (""Id"",""Value"") VALUES (1, INTERVAL '1 month');");

            // A month has no fixed length, so Npgsql refuses to hand it over as a TimeSpan. The failure is
            // Npgsql's, not this library's, and it is the correct answer: there is nothing to carry.
            await Should.ThrowAsync<Exception>(() => provider.GetChangesAsync(Guid.NewGuid()));
        }
        finally
        {
            await DropPostgreSQLDatabase(dbName);
        }
    }

    #endregion

    #region Old peer compatibility - MySql and PostgreSQL

    /// <summary>
    /// Backward compatibility, old client → new server, for the two providers that were never able to
    /// carry these columns at all. The payload is the one a released SQLite client produces: a nullable
    /// byte column read with <c>GetValue()</c> arrives as an <see cref="SyncItemValueType.Int64"/> and a
    /// nullable TimeSpan column as the raw stored text.
    /// </summary>
    [TestMethod]
    public async Task OldClientPayload_IsStillAcceptedByMySql()
    {
        var dbName = "coresync_types_oldpeer_mysql";

        try
        {
            await CreateMySqlDatabase(dbName);
            var connStr = MySqlConnectionStringFor(dbName);
            await ExecuteMySqlNonQuery(connStr, MySqlDdl);

            var provider = CreateMySqlProvider(connStr, "Readings", "Spans");
            await provider.ApplyProvisionAsync();

            await provider.ApplyChangesAsync(OldClientReadingChangeSet());

            var applied = await ReadReadingFromMySql(connStr, 1);
            applied.ShouldNotBeNull();
            applied.StartMonth.ShouldBe((byte)3);
            applied.EndMonth.ShouldBe((byte)250);
            applied.Zone.ShouldBe((short)-5);
            applied.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
            applied.Duration.ShouldBe(MicroDuration);
            applied.Weight.ShouldBe(12.345m);
            applied.Recorded.ShouldBe(MicroRecorded);
        }
        finally
        {
            await DropMySqlDatabase(dbName);
        }
    }

    [TestMethod]
    public async Task OldClientPayload_IsStillAcceptedByPostgreSQL()
    {
        var dbName = "coresync_types_oldpeer_pg";

        try
        {
            await CreatePostgreSQLDatabase(dbName);
            var connStr = PostgreSQLConnectionStringFor(dbName);
            await ExecutePostgreSQLNonQuery(connStr, PostgreSQLDdl);

            var provider = CreatePostgreSQLProvider(connStr, "Readings", "Spans");
            await provider.ApplyProvisionAsync();

            await provider.ApplyChangesAsync(OldClientReadingChangeSet());

            var applied = await ReadReadingFromPostgreSQL(connStr, 1);
            applied.ShouldNotBeNull();
            applied.StartMonth.ShouldBe((byte)3);
            applied.EndMonth.ShouldBe((byte)250);
            applied.Zone.ShouldBe((short)-5);
            applied.CountTime.ShouldBe(new TimeSpan(14, 30, 0));
            applied.Duration.ShouldBe(MicroDuration);
            applied.Weight.ShouldBe(12.345m);
            applied.Recorded.ShouldBe(MicroRecorded);
        }
        finally
        {
            await DropPostgreSQLDatabase(dbName);
        }
    }

    private static SyncChangeSet OldClientReadingChangeSet()
        => new(
            new SyncAnchor(Guid.NewGuid(), 1),
            SyncAnchor.Null,
            new[]
            {
                RawItem("Readings", ChangeType.Insert, new Dictionary<string, SyncItemValue>
                {
                    ["Id"] = Raw(SyncItemValueType.Int64, 1L),
                    ["Name"] = Raw(SyncItemValueType.String, "old client"),
                    ["StartMonth"] = Raw(SyncItemValueType.Int64, 3L),
                    ["EndMonth"] = Raw(SyncItemValueType.Int64, 250L),
                    ["Zone"] = Raw(SyncItemValueType.Int64, -5L),
                    ["CountTime"] = Raw(SyncItemValueType.String, "14:30:00"),
                    ["Duration"] = Raw(SyncItemValueType.String, MicroDuration.ToString("c", CultureInfo.InvariantCulture)),
                    ["Weight"] = Raw(SyncItemValueType.Decimal, 12.345m),
                    ["Recorded"] = Raw(SyncItemValueType.DateTime, MicroRecorded)
                })
            });

    #endregion
}
