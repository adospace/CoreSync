using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;

namespace CoreSync.Sqlite
{
    public class SqliteSyncProvider : ISyncProvider
    {
        public SqliteSyncConfiguration Configuration { get; }
        public SqliteSyncProvider(SqliteSyncConfiguration configuration)
        {
            Configuration = configuration;
        }

        bool _initialized = false;
        private async Task InitializeAsync()
        {
            if (_initialized)
                return;

            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();

                //1. discover tables
                using (var cmd = connection.CreateCommand())
                {
                    foreach (var table in Configuration.Tables)
                    {
                        cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            /*
                            cid         name        type        notnull     dflt_value  pk        
                            ----------  ----------  ----------  ----------  ----------  ----------
                            */
                            while (await reader.ReadAsync())
                            {
                                var colName = reader.GetString(1);
                                var colType = reader.GetString(2);
                                var pk = reader.GetBoolean(5);

                                table.Columns.Add(new SqliteColumn(colName, colType, pk));
                            }
                        }
                    }
                }

                //2. create ct table
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __CORE_SYNC_CT (ID INTEGER PRIMARY KEY, TBL TEXT NOT NULL, OP CHAR NOT NULL, PK TEXT NOT NULL)";
                    await cmd.ExecuteNonQueryAsync();
                }

                //3. create triggers
                using (var cmd = connection.CreateCommand())
                {
                    foreach (var table in Configuration.Tables.Where(_ => _.Columns.Any()))
                    {
                        var primaryKeyColumns = table.Columns.Where(_ => _.PrimaryKey);

                        var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-{op}__] 
AFTER {op} ON [{table.Schema}].[{table.Name}]
FOR EACH ROW
BEGIN
    INSERT INTO [__CORE_SYNC_CT] (TBL, OP, PK) VALUES ('{table.Schema}.{table.Name}', '{op[0]}', printf('{string.Join("", primaryKeyColumns.Select(_ => TypeToPrintFormat(_.Type)))}', {string.Join(", ", primaryKeyColumns.Select(_ => (op == "DELETE" ? "OLD" : "NEW") + ".[" + _.Name + "]"))}));
END");
                        cmd.CommandText = commandTextBase("INSERT");
                        await cmd.ExecuteNonQueryAsync();

                        cmd.CommandText = commandTextBase("UPDATE");
                        await cmd.ExecuteNonQueryAsync();

                        cmd.CommandText = commandTextBase("DELETE");
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }

            _initialized = true;
        }

        private static string TypeToPrintFormat(string type)
        {
            if (type == "INTEGER")
                return "%d";
            if (type == "TEXT")
                return "%s";

            return "%s";
        }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null)
        {
            Validate.NotNull(changeSet, nameof(changeSet));

            if (!(changeSet.Anchor is SqliteSyncAnchor sqlAnchor))
                throw new ArgumentException("Incompatible anchor", nameof(changeSet));

            await InitializeAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
                {
                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;
                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

                        long version = 0;
                        {
                            cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                            var res = await cmd.ExecuteScalarAsync();
                            if (!(res is DBNull))
                                version = (long)res;
                        }

                        bool atLeastOneChangeApplied = false;



                    }
                }
            }

        }

        public async Task<SyncChangeSet> GetIncreamentalChangesAsync([NotNull] SyncAnchor anchor)
        {
            Validate.NotNull(anchor, nameof(anchor));

            var sqliteAnchor = anchor as SqliteSyncAnchor;
            if (sqliteAnchor == null)
                throw new ArgumentException("Incompatible anchor", nameof(anchor));

            await InitializeAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
                {
                    var items = new List<SqliteSyncItem>();

                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";

                        long version = 0;
                        {
                            cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                            var res = await cmd.ExecuteScalarAsync();
                            if (!(res is DBNull))
                                version = (long)res;
                        }

                        long minVersion = 0;
                        {
                            cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                            var res = await cmd.ExecuteScalarAsync();
                            if (!(res is DBNull))
                                minVersion = (long)res;
                        }

                        if (sqliteAnchor.Version < minVersion - 1)
                            throw new InvalidOperationException($"Unable to get changes, version of data requested ({sqliteAnchor.Version}) is too old (min valid version {minVersion})");

                        foreach (var table in Configuration.Tables.Where(_=>_.Columns.Any()))
                        {
                            var primaryKeyColumns = table.Columns.Where(_ => _.PrimaryKey);

                            cmd.CommandText = $@"SELECT {string.Join(",", table.Columns.Select(_ => "T.[" + _.Name + "]"))}, CT.OP FROM [{table.Schema}].[{table.Name}] AS T INNER JOIN __CORE_SYNC_CT AS CT ON printf('{string.Join("", primaryKeyColumns.Select(_ => TypeToPrintFormat(_.Type)))}', {string.Join(", ", primaryKeyColumns.Select(_ => "T.[" + _.Name + "]"))}) = CT.PK WHERE CT.Id > {sqliteAnchor.Version}";

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                    items.Add(new SqliteSyncItem(table, DetectChangeType(values), values));
                                }
                            }
                        }

                        tr.Commit();

                        return new SyncChangeSet(new SqliteSyncAnchor(version), items);

                    }
                }
            }
        }

        public async Task<SyncChangeSet> GetInitialSetAsync()
        {
            await InitializeAsync();

            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();
                using (var cmd = connection.CreateCommand())
                {
                    var items = new List<SqliteSyncItem>();

                    using (var tr = connection.BeginTransaction())
                    {
                        cmd.Transaction = tr;

                        long version = 0;
                        {
                            cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                            var res = await cmd.ExecuteScalarAsync();
                            if (!(res is DBNull))
                                version = (long)res;
                        }


                        foreach (var table in Configuration.Tables)
                        {
                            cmd.CommandText = $@"SELECT {string.Join(", ", table.Columns.Select(_ => "[" + _.Name + "]"))} FROM [{table.Name}]";

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                    items.Add(new SqliteSyncItem(table, DetectChangeType(values), values));
                                }
                            }
                        }
                        tr.Commit();

                        return new SyncChangeSet(new SqliteSyncAnchor(version), items);
                    }
                }
            }
        }

        private ChangeType DetectChangeType(Dictionary<string, object> values)
        {
            switch ((string)values["OP"])
            {
                case "I":
                    return ChangeType.Insert;
                case "U":
                    return ChangeType.Update;
                case "D":
                    return ChangeType.Delete;
            }

            throw new NotSupportedException();
        }
    }
}
