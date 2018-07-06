using JetBrains.Annotations;
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

                    var primaryKeyColumns = primaryKeyIndex.IndexedColumns.Cast<IndexedColumn>().ToList();
                    var allColumns = dbTable.Columns.Cast<Column>().ToList();
                    var tableColumns = allColumns.Where(_ => !primaryKeyColumns.Any(kc => kc.Name == _.Name)).ToList();

                    table.InitialDataQuery = $@"SELECT
                {string.Join(", ", dbTable.Columns.Cast<Column>().Select(_ => "[" + _.Name + "]"))}
            FROM
                [{table.Name}]";

                    table.IncrementalDataQuery = $@"SELECT  
                {string.Join(", ", primaryKeyColumns.Select(_ => "CT." + _))} {(tableColumns.Any() ? ", " + string.Join(", ", tableColumns.Select(_ => "T.[" + _.Name + "]")) : string.Empty)},  
                CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_COLUMNS, CT.SYS_CHANGE_CONTEXT  
            FROM  
                [{table.Schema}].[{table.Name}] AS T  
            RIGHT OUTER JOIN  
                CHANGETABLE(CHANGES [{table.Schema}].[{table.Name}], @last_synchronization_version) AS CT  
            ON  
                {string.Join(" AND ", primaryKeyColumns.Select(_ => "T." + _ + " = CT." + _))}";

                    table.InsertQuery = $@"INSERT INTO {table.Schema}.[{table.Name}] ({string.Join(", ", allColumns.Select(_ => "[" + _.Name + "]"))}) SELECT {string.Join(", ", allColumns.Select(_ => "@" + _.Name.Replace(' ', '_')))} EXCEPT
   SELECT {string.Join(", ", allColumns.Select(_ => "[" + _.Name + "]"))} FROM {table.Schema}.[{table.Name}];";

                    table.DeleteQuery = $@"DELETE FROM {table.Schema}.[{table.Name}] 
WHERE 
    {string.Join(" AND ", primaryKeyColumns.Select(_ => "[" + _.Name + "]" + " = @" + _.Name.Replace(' ', '_')))} AND  
    @sync_force_write = 1 OR @last_sync_version >= ISNULL (  
        SELECT CT.SYS_CHANGE_VERSION  
        FROM CHANGETABLE(VERSION {table.Schema}.[{table.Name}],  
            ({string.Join(", ", primaryKeyColumns.Select(_ => "[" + _.Name + "]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "t.[" + _.Name + "]"))}) AS CT),  
        0)";

                    table.UpdateQuery = $@"UPDATE {table.Schema}.[{table.Name}] 
SET  
    {string.Join(", ", tableColumns.Select(_ => "[" + _.Name + "] = @" + _.Name.Replace(" ", "_")))}  
FROM  
    {table.Schema}.[{table.Name}] AS t  
WHERE  
    {string.Join(" AND ", primaryKeyColumns.Select(_ => "[" + _.Name + "]" + " = @" + _.Name.Replace(' ', '_')))} AND  
    @sync_force_write = 1 OR @last_sync_version >= ISNULL (  
        SELECT CT.SYS_CHANGE_VERSION  
        FROM CHANGETABLE(VERSION {table.Schema}.[{table.Name}],  
            ({string.Join(", ", primaryKeyColumns.Select(_ => "[" + _.Name + "]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "t.[" + _.Name + "]"))}) AS CT),  
        0)";
                }
            }

            _initialized = true;
        }

        private ChangeType DetectChangeType(Dictionary<string, object> values)
        {
            switch (values["SYS_CHANGE_OPERATION"].ToString())
            {
                case "I":
                    return ChangeType.Insert;
                case "U":
                    return ChangeType.Update;
                case "D":
                    return ChangeType.Delete;
                default:
                    throw new NotSupportedException();
            }
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
                                    items.Add(new SqlSyncItem(table, DetectChangeType(values), values));
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

                            cmd.CommandText = table.IncrementalDataQuery.Replace("@last_synchronization_version", sqlAnchor.Version.ToString());

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                    items.Add(new SqlSyncItem(table, DetectChangeType(values), values));
                                }
                            }
                        }

                        tr.Commit();

                        return new SqlSyncChangeSet(new SqlSyncAnchor(version), items);
                    }

                }
            }
        }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, Func<SyncItem, ConflictResolution> onConflictFunc = null)
        {
            Validate.NotNull(changeSet, nameof(changeSet));

            if (!(changeSet.Anchor is SqlSyncAnchor sqlAnchor))
                throw new ArgumentException("Incompatible anchor", nameof(changeSet));

            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;
                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

                        long version = (long)await cmd.ExecuteScalarAsync();
                        bool atLeastOneChangeApplied = false;

                        foreach (var item in changeSet.Items)
                        {
                            var table = Configuration.Tables.First(_ => _.Name == item.Table.Name);

                            cmd.Parameters.Clear();
                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

                            if (sqlAnchor.Version < minVersionForTable)
                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({sqlAnchor.Version}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

                            bool syncForceWrite = false;
                            retryWrite:
                            cmd.Parameters.Clear();

                            switch (item.ChangeType)
                            {
                                case ChangeType.Insert:
                                    cmd.CommandText = table.InsertQuery;
                                    break;
                                case ChangeType.Update:
                                    cmd.CommandText = table.UpdateQuery;
                                    break;
                                case ChangeType.Delete:
                                    cmd.CommandText = table.DeleteQuery;
                                    break;
                            }

                            cmd.Parameters.Add(new SqlParameter("@last_sync_version", sqlAnchor.Version));
                            cmd.Parameters.Add(new SqlParameter("@sync_force_write", syncForceWrite));

                            foreach (var valueItem in item.Values)
                                cmd.Parameters.Add(new SqlParameter("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value));

                            var affectedRows = cmd.ExecuteNonQuery();

                            if (affectedRows == 0)
                            {
                                if (item.ChangeType == ChangeType.Insert)
                                {
                                    //If we can't apply an insert means that we already
                                    //applied the insert or another record with same values (see primary key)
                                    //is already present in table.
                                    //In any case we can't proceed
                                    throw new InvalidSyncOperationException(new SqlSyncAnchor(sqlAnchor.Version + 1));
                                }
                                else if (item.ChangeType == ChangeType.Update ||
                                    item.ChangeType == ChangeType.Delete)
                                {
                                    //conflict detected
                                    var res = onConflictFunc?.Invoke(item);
                                    if (res.HasValue && res.Value == ConflictResolution.ForceWrite)
                                    {
                                        syncForceWrite = true;
                                        goto retryWrite;
                                    }
                                }
                            }
                            else
                                atLeastOneChangeApplied = true;
                           
                        }

                        tr.Commit();

                        return new SqlSyncAnchor(version + (atLeastOneChangeApplied ? 1 : 0));
                    }

                }
            }
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
