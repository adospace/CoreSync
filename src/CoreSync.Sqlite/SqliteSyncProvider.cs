using System;
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
                connection.Open();

                //1. discover tables
                foreach (var table in Configuration.Tables)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        /*
                        cid         name        type        notnull     dflt_value  pk        
                        ----------  ----------  ----------  ----------  ----------  ----------
                        */
                        while (!await reader.ReadAsync())
                        {
                            var colName = reader.GetString(1);
                            var colType = reader.GetString(2);
                            var pk = reader.GetBoolean(5);

                            table.Columns.Add(new SqliteColumn(colName, colType, pk));
                        }
                    }
                }

                //2. create ct tables
                foreach (var table in Configuration.Tables)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS [{table.Schema}].[{table.Name}_ct] (__op, {string.Join(", ", table.Columns.Select(_ => "[" + _.Name + "] " + _.Type))})";
                    await cmd.ExecuteNonQueryAsync();
                }

                //3. create triggers
                using (var transaction = connection.BeginTransaction())
                {
                    var createTriggerCommand = connection.CreateCommand();
                    createTriggerCommand.Transaction = transaction;

                    foreach (var table in Configuration.Tables)
                    {
                        createTriggerCommand.CommandText = $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-insert__] 
AFTER INSERT ON [{table.Schema}].[{table.Name}]
FOR EACH ROW
BEGIN
    INSERT INTO [{table.Schema}].[{table.Name}_ct] (__op, {string.Join(", ", table.Columns.Select(_ => "[" + _.Name + "]"))}) VALUES ({string.Join(", ", table.Columns.Select(_ => "NEW.[" + _.Name + "]"))});
END";
                        createTriggerCommand.Parameters.Clear();
                    }

                    await createTriggerCommand.ExecuteNonQueryAsync();

                    transaction.Commit();
                }
            }

            _initialized = true;
        }



        public Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null)
        {
            throw new NotImplementedException();
        }

        public Task<SyncChangeSet> GetIncreamentalChangesAsync([NotNull] SyncAnchor anchor)
        {
            throw new NotImplementedException();
        }

        public Task<SyncChangeSet> GetInitialSetAsync()
        {
            throw new NotImplementedException();
        }
    }
}
