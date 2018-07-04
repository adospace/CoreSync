using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.SqlServer
{
    public class SqlSyncProvider : ISyncProvider
    {
        public SqlSyncConfiguration Configuration { get; }
        public SqlSyncProvider(SqlSyncConfiguration configuration)
        {
            Configuration = configuration;
        }

        bool _initialized = false;
        private async Task InitializeAsync()
        {
            if (_initialized)
                return;

            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string");

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                var server = new Server(new ServerConnection(c));
                var database = server.Databases.Cast<Database>().FirstOrDefault(_=>_.Name == connStringBuilder.InitialCatalog);

                if (database == null)
                    throw new InvalidOperationException($"Unable to find database '{connStringBuilder.InitialCatalog}'");

                if (!database.ChangeTrackingEnabled)
                {
                    database.ChangeTrackingEnabled = true;
                    database.Alter();
                }

                if (database.SnapshotIsolationState == SnapshotIsolationState.Disabled ||
                    database.SnapshotIsolationState == SnapshotIsolationState.PendingOff)
                {
                    database.SetSnapshotIsolation(true);
                    database.Alter();
                }

                foreach (var table in Configuration.Tables)
                {
                    var dbTable = database.Tables[table.Name];
                    if (!dbTable.ChangeTrackingEnabled)
                    {
                        dbTable.ChangeTrackingEnabled = true;
                        dbTable.Alter();
                    }


                    var primaryKeyIndex = dbTable.Indexes.Cast<Index>().FirstOrDefault(_ => _.IsClustered && _.IndexKeyType == IndexKeyType.DriPrimaryKey);
                    if (primaryKeyIndex == null)
                        throw new InvalidOperationException($"Table '{table.Name}' doesn't have a primary key");

                    var primaryKeyColumns = primaryKeyIndex.IndexedColumns.Cast<IndexedColumn>();
                    var tableColumns = dbTable.Columns.Cast<Column>().Where(_ => !primaryKeyColumns.Any(kc => kc.Name == _.Name)).ToArray();

                    table.InitialDataQuery = $@"SELECT
                {string.Join(", ", dbTable.Columns.Cast<Column>().Select(_ => "[" + _.Name + "]"))}
            FROM
                [{table.Name}]";

                    table.IncrementalInsertQuery = $@"SELECT  
                {string.Join(", ", primaryKeyColumns.Select(_ => "CT." + _))} {(tableColumns.Any() ? ", " + string.Join(", ", tableColumns.Select(_ => "T.[" + _.Name + "]")) : string.Empty)},  
                CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_COLUMNS, CT.SYS_CHANGE_CONTEXT  
            FROM  
                [{table.Schema}].[{table.Name}] AS T  
            RIGHT OUTER JOIN  
                CHANGETABLE(CHANGES [{table.Schema}].[{table.Name}], @last_synchronization_version) AS CT  
            ON  
                {string.Join(" AND ", primaryKeyColumns.Select(_ => "T." + _ + " = CT." + _))}";

                }
            }

            _initialized = true;
        }

        public async Task<SyncChangeSet> GetInitialSetAsync()
        {
            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    var items = new List<SqlSyncItem>();

                    using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

                        long version = (long)await cmd.ExecuteScalarAsync();

                        foreach (var table in Configuration.Tables)
                        {
                            cmd.CommandText = table.InitialDataQuery;

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                    items.Add(new SqlSyncItem(table, values));
                                }
                            }
                        }

                        tr.Commit();

                        return new SqlSyncChangeSet(new SqlSyncAnchor(version), items);
                    }

                }
            }
        }


        public async Task<SyncChangeSet> GetIncreamentalChangesAsync(SyncAnchor anchor)
        {
            Validate.NotNull(anchor, nameof(anchor));

            var sqlAnchor = anchor as SqlSyncAnchor;
            if (sqlAnchor == null)
                throw new ArgumentException("Incompatible anchor", nameof(anchor));

            await InitializeAsync();

            //CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('SalesLT.Product'))

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    var items = new List<SqlSyncItem>();

                    using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

                        long version = (long)await cmd.ExecuteScalarAsync();

                        foreach (var table in Configuration.Tables)
                        {
                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

                            if (sqlAnchor.Version < minVersionForTable)
                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({sqlAnchor.Version}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

                            cmd.CommandText = table.IncrementalInsertQuery.Replace("@last_synchronization_version", sqlAnchor.Version.ToString());

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                    items.Add(new SqlSyncItem(table, values));
                                }
                            }
                        }

                        tr.Commit();

                        return new SqlSyncChangeSet(new SqlSyncAnchor(version), items);
                    }

                }
            }
        }

        public Task<SyncAnchor> ApplyChangesAsync(SyncChangeSet changeSet)
        {
            throw new NotImplementedException();
        }

        public async Task ApplyProvisionAsync()
        {
            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                var server = new Server(new ServerConnection(c));
                var database = server.Databases[0];

                if (!database.ChangeTrackingEnabled)
                {
                    database.ChangeTrackingEnabled = true;
                    database.Alter();
                }
            }
        }

        public async Task RemoveProvisionAsync()
        {
            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                var server = new Server(new ServerConnection(c));
                var database = server.Databases[0];

                if (database.ChangeTrackingEnabled)
                {
                    database.ChangeTrackingEnabled = false;
                    database.Alter();
                }
            }
        }
    }
}
