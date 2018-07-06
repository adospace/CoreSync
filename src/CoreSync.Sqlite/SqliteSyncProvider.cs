using System;
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

                using (var transaction = connection.BeginTransaction())
                {
                    var createTriggerCommand = connection.CreateCommand();
                    createTriggerCommand.Transaction = transaction;

                    foreach (var table in Configuration.Tables)
                    {
                        createTriggerCommand.CommandText = $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-insert__] 
AFTER INSERT ON [{table.Schema}].[{table.Name}]
BEGIN
    INSERT INTO [{table.Schema}].[{table.Name}_ct] (op, ) VALUES ();
END";
                        createTriggerCommand.Parameters.Clear();
                    }


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
