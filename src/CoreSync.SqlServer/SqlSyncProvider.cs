using JetBrains.Annotations;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.SqlServer
{
    public class SqlSyncProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;

        public SqlSyncProvider(SqlSyncConfiguration configuration)
        {
            Configuration = configuration;
        }

        public SqlSyncConfiguration Configuration { get; }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, Func<SyncItem, ConflictResolution> onConflictFunc = null)
        {
            Validate.NotNull(changeSet, nameof(changeSet));

            if (changeSet.Anchor.StoreId != _storeId)
            {
                throw new ArgumentException("Invalid anchor store id");
            }

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
                            var table = (SqlSyncTable)Configuration.Tables.First(_ => _.Name == item.Table.Name);

                            cmd.Parameters.Clear();
                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

                            if (changeSet.Anchor.Version < minVersionForTable)
                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({changeSet.Anchor.Version}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

                            bool syncForceWrite = false;
                            var itemChangeType = item.ChangeType;

                        retryWrite:
                            cmd.Parameters.Clear();

                            switch (itemChangeType)
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

                            cmd.Parameters.Add(new SqlParameter("@last_sync_version", changeSet.Anchor.Version));
                            cmd.Parameters.Add(new SqlParameter("@sync_force_write", syncForceWrite));

                            foreach (var valueItem in item.Values)
                                cmd.Parameters.Add(new SqlParameter("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value ?? DBNull.Value));

                            var affectedRows = cmd.ExecuteNonQuery();

                            if (affectedRows == 0)
                            {
                                if (itemChangeType == ChangeType.Insert)
                                {
                                    //If we can't apply an insert means that we already
                                    //applied the insert or another record with same values (see primary key)
                                    //is already present in table.
                                    //In any case we can't proceed
                                    throw new InvalidSyncOperationException(new SyncAnchor(_storeId, changeSet.Anchor.Version + 1));
                                }
                                else if (itemChangeType == ChangeType.Update ||
                                    itemChangeType == ChangeType.Delete)
                                {
                                    if (syncForceWrite)
                                    {
                                        if (itemChangeType == ChangeType.Delete)
                                        {
                                            //item is already deleted in data store
                                            //so this means that we're going to delete a already deleted record
                                            //i.e. nothing to do
                                        }
                                        else
                                        {
                                            //if user wants to update forcely a delete record means
                                            //he wants to actually insert it again in store
                                            itemChangeType = ChangeType.Insert;
                                            goto retryWrite;
                                        }
                                    }
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

                        var newAnchor = new SyncAnchor(_storeId, version + (atLeastOneChangeApplied ? 1 : 0));

                        cmd.Parameters.Clear();
                        cmd.CommandText = "UPDATE __CORE_SYNC_LOCAL_ANCHOR SET LOCAL_ANCHOR = @localAnchor WHERE LOCAL_ID = @localId";
                        cmd.Parameters.AddWithValue("@localId", _storeId);
                        cmd.Parameters.AddWithValue("@localAnchor", newAnchor.Version);

                        if (1 != await cmd.ExecuteNonQueryAsync())
                        {
                            throw new InvalidOperationException("Unable to update LocalAnchor table");
                        }

                        tr.Commit();

                        return newAnchor;
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

        public async Task<SyncChangeSet> GetIncreamentalChangesAsync(SyncAnchor anchor)
        {
            Validate.NotNull(anchor, nameof(anchor));

            if (anchor.StoreId != _storeId)
            {
                throw new ArgumentException("Invalid anchor store id");
            }

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

                        foreach (SqlSyncTable table in Configuration.Tables)
                        {
                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

                            if (anchor.Version < minVersionForTable)
                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({anchor.Version}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

                            cmd.CommandText = table.IncrementalDataQuery.Replace("@last_synchronization_version", anchor.Version.ToString());

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

                        return new SyncChangeSet(new SyncAnchor(_storeId, version), items);
                    }
                }
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

                        foreach (SqlSyncTable table in Configuration.Tables)
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

                        return new SyncChangeSet(new SyncAnchor(_storeId, version), items);
                    }
                }
            }
        }

        public Task<SyncAnchor> GetLastAnchorForRemoteStoreAsync(Guid storeId)
        {
            throw new NotImplementedException();
        }

        public Task<SyncAnchor> GetLocalAnchorAsync()
        {
            throw new NotImplementedException();
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

        private ChangeType DetectChangeType(Dictionary<string, object> values)
        {
            if (values.TryGetValue("SYS_CHANGE_OPERATION", out var syncChangeOperation))
            {
                switch (syncChangeOperation.ToString())
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

            return ChangeType.Insert;
        }

        private async Task InitializeAsync()
        {
            if (_initialized)
                return;

            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                var server = new Server(new ServerConnection(c));
                var database = server.Databases.Cast<Database>().FirstOrDefault(_ => _.Name == connStringBuilder.InitialCatalog);

                if (database == null)
                    throw new InvalidOperationException($"Unable to find database '{connStringBuilder.InitialCatalog}' in server '{connStringBuilder.DataSource}'");

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

                if (!database.Tables.Contains("__CORE_SYNC_REMOTE_ANCHOR"))
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_REMOTE_ANCHOR](
	[REMOTE_ID] [uniqueidentifier] NOT NULL,
	[REMOTE_ANCHOR] [BIGINT] NOT NULL
 CONSTRAINT [PK___CORE_SYNC_REMOTE_ANCHOR] PRIMARY KEY CLUSTERED
(
	[REMOTE_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (!database.Tables.Contains("__CORE_SYNC_LOCAL_ANCHOR"))
                {
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_LOCAL_ANCHOR](
	[LOCAL_ID] [uniqueidentifier] NOT NULL,
	[LOCAL_ANCHOR] [BIGINT] NOT NULL
 CONSTRAINT [PK___CORE_SYNC_LOCAL_ANCHOR] PRIMARY KEY CLUSTERED
(
	[LOCAL_ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 LOCAL_ID FROM __CORE_SYNC_LOCAL_ANCHOR";
                    var localId = await cmd.ExecuteScalarAsync();
                    if (localId == null)
                    {
                        localId = Guid.NewGuid().ToString();
                        cmd.CommandText = $"INSERT INTO __CORE_SYNC_LOCAL_ANCHOR (LOCAL_ID, LOCAL_ANCHOR) VALUES (@localId, 0)";
                        cmd.Parameters.Add(new SqlParameter("@localId", localId));
                        if (1 != await cmd.ExecuteNonQueryAsync())
                        {
                            throw new InvalidOperationException();
                        }
                        cmd.Parameters.Clear();
                    }

                    _storeId = Guid.Parse((string)localId);
                }

                foreach (SqlSyncTable table in Configuration.Tables)
                {
                    var dbTable = database.Tables[table.Name];
                    if (!dbTable.ChangeTrackingEnabled)
                    {
                        dbTable.ChangeTrackingEnabled = true;
                        dbTable.Alter();
                    }

                    var primaryKeyIndex = dbTable.Indexes.Cast<Index>().FirstOrDefault(_ => _.IsClustered && _.IndexKeyType == IndexKeyType.DriPrimaryKey);
                    if (primaryKeyIndex == null)
                    {
                        throw new InvalidOperationException($"Table '{table.Name}' doesn't have a primary key");
                    }

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
FROM
    {table.Schema}.[{table.Name}]
JOIN CHANGETABLE(VERSION {table.Schema}.[{table.Name}], ({string.Join(", ", primaryKeyColumns.Select(_ => "[" + _.Name + "]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "@" + _.Name.Replace(' ', '_')))})) CT  ON {string.Join(" AND ", primaryKeyColumns.Select(_ => $"CT.[{_.Name}] = {table.Schema}.[{table.Name}].[{_.Name}]"))}
WHERE
    @sync_force_write = 1 OR @last_sync_version >= ISNULL(CT.SYS_CHANGE_VERSION, 0)";

                    table.UpdateQuery = $@"UPDATE {table.Schema}.[{table.Name}]
SET
    {string.Join(", ", tableColumns.Select(_ => "[" + _.Name + "] = @" + _.Name.Replace(" ", "_")))}
FROM
    {table.Schema}.[{table.Name}]
JOIN CHANGETABLE(VERSION {table.Schema}.[{table.Name}], ({string.Join(", ", primaryKeyColumns.Select(_ => "[" + _.Name + "]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "@" + _.Name.Replace(' ', '_')))})) CT ON {string.Join(" AND ", primaryKeyColumns.Select(_ => $"CT.[{_.Name}] = {table.Schema}.[{table.Name}].[{_.Name}]"))}
WHERE
    @sync_force_write = 1 OR @last_sync_version >= ISNULL(CT.SYS_CHANGE_VERSION, 0)";
                }
            }

            _initialized = true;
        }
    }
}