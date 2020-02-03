using JetBrains.Annotations;
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

            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    using (var tr = c.BeginTransaction())// IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM __CORE_SYNC_CT";
                        var version = await cmd.ExecuteLongScalarAsync();

                        cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                        var minVersion = await cmd.ExecuteLongScalarAsync();

                        cmd.CommandText = $"DECLARE @session uniqueidentifier; SET @session = @sync_client_id_binary; SET CONTEXT_INFO @session";
                        cmd.Parameters.Add(new SqlParameter("@sync_client_id_binary", changeSet.SourceAnchor.StoreId));
                        await cmd.ExecuteNonQueryAsync();
                        cmd.Parameters.Clear();

                        cmd.CommandText = $"SELECT CONTEXT_INFO()";
                        var contextInfo = await cmd.ExecuteScalarAsync();
                        cmd.Parameters.Clear();


                        if (changeSet.SourceAnchor.Version < minVersion - 1)
                            throw new InvalidOperationException($"Unable to apply changes, version of data requested ({changeSet.SourceAnchor.Version}) is too old (min valid version {minVersion})");

                        foreach (var item in changeSet.Items)
                        {
                            var table = (SqlSyncTable)Configuration.Tables.First(_ => _.Name == item.TableName);

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

                            //cmd.Parameters.Add(new SqlParameter("@sync_client_id_binary", changeSet.SourceAnchor.StoreId.ToByteArray()));
                            cmd.Parameters.Add(new SqlParameter("@last_sync_version", changeSet.TargetAnchor.Version));
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
                                    throw new InvalidSyncOperationException(new SyncAnchor(_storeId, version + 1));
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
                                            //if user wants to update forcely a deletes record means
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
                            //else
                            //    atLeastOneChangeApplied = true;
                        }

                        //cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
                        //cmd.Parameters.Clear();
                        //version = (long)await cmd.ExecuteScalarAsync() + (atLeastOneChangeApplied ? 1 : 0);
                        cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [REMOTE_VERSION] = @version WHERE [ID] = @id";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@id", changeSet.SourceAnchor.StoreId.ToString());
                        cmd.Parameters.AddWithValue("@version", changeSet.SourceAnchor.Version);

                        if (0 == await cmd.ExecuteNonQueryAsync())
                        {
                            cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [REMOTE_VERSION]) VALUES (@id, @version)";

                            await cmd.ExecuteNonQueryAsync();
                        }

                        tr.Commit();

                        return new SyncAnchor(_storeId, version);
                    }
                }
            }
        }

        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version)
        {
            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;
                        cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [LOCAL_VERSION] = @version WHERE [ID] = @id";
                        cmd.Parameters.AddWithValue("@id", otherStoreId.ToString());
                        cmd.Parameters.AddWithValue("@version", version);

                        if (0 == await cmd.ExecuteNonQueryAsync())
                        {
                            cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [LOCAL_VERSION]) VALUES (@id, @version)";

                            await cmd.ExecuteNonQueryAsync();
                        }

                        tr.Commit();
                    }
                }
            }
        }

        public async Task<SyncChangeSet> GetChangesForStoreAsync(Guid otherStoreId)
        {
            //Validate.NotNull(otherStoreAnchor, nameof(otherStoreAnchor));

            long fromVersion = (await GetLastLocalAnchorForStoreAsync(otherStoreId)).Version;

            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    var items = new List<SqlSyncItem>();

                    using (var tr = c.BeginTransaction())// IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                        var version = await cmd.ExecuteLongScalarAsync();

                        cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                        var minVersion = await cmd.ExecuteLongScalarAsync();

                        if (fromVersion < minVersion - 1)
                            throw new InvalidOperationException($"Unable to get changes, version of data requested ({fromVersion}) is too old (min valid version {minVersion})");

                        foreach (SqlSyncTable table in Configuration.Tables)
                        {
                            cmd.CommandText = table.IncrementalDataQuery;//.Replace("@last_synchronization_version", fromVersion.ToString());
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@version", fromVersion);
                            cmd.Parameters.AddWithValue("@sourceId", otherStoreId);

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

                        return new SyncChangeSet(new SyncAnchor(_storeId, version), await GetLastRemoteAnchorForStoreAsync(otherStoreId), items);
                    }
                }
            }
        }

        private async Task<SyncAnchor> GetLastLocalAnchorForStoreAsync(Guid otherStoreId)
        {
            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [dbo].[__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
                    cmd.Parameters.AddWithValue("@storeId", otherStoreId);

                    var version = await cmd.ExecuteScalarAsync();

                    if (version == null || version == DBNull.Value)
                        return new SyncAnchor(_storeId, 0);

                    return new SyncAnchor(_storeId, (long)version);
                }
            }
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId)
        {
            await InitializeAsync();

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT [REMOTE_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
                    cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

                    var version = await cmd.ExecuteScalarAsync();

                    if (version == null || version == DBNull.Value)
                        return new SyncAnchor(otherStoreId, 0);

                    return new SyncAnchor(otherStoreId, (long)version);
                }
            }
        }

        public async Task<Guid> GetStoreIdAsync()
        {
            await InitializeAsync();

            return _storeId;
        }

        private ChangeType DetectChangeType(Dictionary<string, object> values)
        {
            if (values.TryGetValue("OP", out var syncChangeOperation))
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

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();

                var tableNames = await connection.GetTableNamesAsync();
                if (!tableNames.Contains("__CORE_SYNC_CT"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_CT](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[TBL] [nvarchar](1024) NOT NULL,
	[OP] [char](1) NOT NULL,
	[PK] [nvarchar](1024) NOT NULL,
	[SRC] [varbinary](16) NULL,
 CONSTRAINT [PK___CORE_SYNC_CT] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (!tableNames.Contains("__CORE_SYNC_REMOTE_ANCHOR"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_REMOTE_ANCHOR](
	[ID] [uniqueidentifier] NOT NULL,
	[LOCAL_VERSION] [BIGINT] NULL,
    [REMOTE_VERSION] [BIGINT] NULL
 CONSTRAINT [PK___CORE_SYNC_REMOTE_ANCHOR] PRIMARY KEY CLUSTERED
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                if (!tableNames.Contains("__CORE_SYNC_LOCAL_ID"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_LOCAL_ID](
	[ID] [uniqueidentifier] NOT NULL
 CONSTRAINT [PK___CORE_SYNC_LOCAL_ID] PRIMARY KEY CLUSTERED
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 [ID] FROM [__CORE_SYNC_LOCAL_ID]";
                    var localId = await cmd.ExecuteScalarAsync();
                    if (localId == null)
                    {
                        localId = Guid.NewGuid();
                        cmd.CommandText = $"INSERT INTO [__CORE_SYNC_LOCAL_ID] ([ID]) VALUES (@id)";
                        cmd.Parameters.Add(new SqlParameter("@id", localId));
                        if (1 != await cmd.ExecuteNonQueryAsync())
                        {
                            throw new InvalidOperationException();
                        }
                        cmd.Parameters.Clear();
                    }

                    _storeId = (Guid)localId;

                    foreach (SqlSyncTable table in Configuration.Tables)
                    {
                        var primaryKeyIndexName = (await connection.GetClusteredPrimaryKeyIndexesAsync(table)).FirstOrDefault(); //dbTable.Indexes.Cast<Index>().FirstOrDefault(_ => _.IsClustered && _.IndexKeyType == IndexKeyType.DriPrimaryKey);
                        if (primaryKeyIndexName == null)
                        {
                            throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't have a primary key");
                        }

                        var primaryKeyColumns = await connection.GetIndexColumnNamesAsync(table, primaryKeyIndexName); //primaryKeyIndexName.IndexedColumns.Cast<IndexedColumn>().ToList();
                        var allColumns = await connection.GetTableColumnNamesAsync(table); //dbTable.Columns.Cast<Column>().ToList();
                        var tableColumns = allColumns.Where(_ => !primaryKeyColumns.Any(kc => kc == _)).ToList();

                        if (primaryKeyColumns.Length != 1)
                        {
                            throw new NotSupportedException();
                        }

                        var existsTriggerCommand = new Func<string, string>((op) => $@"select COUNT(*) from sys.objects where schema_id=SCHEMA_ID('dbo') AND type='TR' and name='__{table.Name}_ct-{op}__'");
                        var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER [__{table.Name}_ct-{op}__]
ON {table.NameWithSchema}
AFTER {op}
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	INSERT INTO [__CORE_SYNC_CT](TBL, OP, PK, SRC) 
	SELECT '{table.NameWithSchema}', '{op[0]}' , {(op == "DELETE" ? "DELETED" : "INSERTED")}.{primaryKeyColumns[0]}, CONTEXT_INFO()  
	FROM {(op == "DELETE" ? "DELETED" : "INSERTED")}
END");

                        cmd.CommandText = existsTriggerCommand("INSERT");
                        if (((int)await cmd.ExecuteScalarAsync()) == 0)
                        {
                            cmd.CommandText = commandTextBase("INSERT");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        cmd.CommandText = existsTriggerCommand("UPDATE");
                        if (((int)await cmd.ExecuteScalarAsync()) == 0)
                        {
                            cmd.CommandText = commandTextBase("UPDATE");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        cmd.CommandText = existsTriggerCommand("DELETE");
                        if (((int)await cmd.ExecuteScalarAsync()) == 0)
                        {
                            cmd.CommandText = commandTextBase("DELETE");
                            await cmd.ExecuteNonQueryAsync();
                        }

                        table.IncrementalDataQuery = $@"SELECT DISTINCT { string.Join(",", allColumns.Select(_ => "T.[" + _ + "]"))}, CT.OP AS OP FROM [{ table.Name}] AS T 
INNER JOIN __CORE_SYNC_CT AS CT ON CONVERT(nvarchar(1024), T.[{primaryKeyColumns[0]}]) = CT.[PK] WHERE CT.Id > @version AND(CT.SRC IS NULL OR CT.SRC != @sourceId)";

                        //SET CONTEXT_INFO @sync_client_id_binary;
                        table.InsertQuery = $@" 
BEGIN TRY 
INSERT INTO {table.NameWithSchema} ({string.Join(", ", allColumns.Select(_ => "[" + _ + "]"))}) 
VALUES ({string.Join(", ", allColumns.Select(_ => "@" + _.Replace(' ', '_')))});
END TRY  
BEGIN CATCH  
END CATCH";

                        //SET CONTEXT_INFO @sync_client_id_binary; 
                        table.DeleteQuery = $@"
DELETE FROM {table.Name}
WHERE ({string.Join(", ", primaryKeyColumns.Select(_ => $"[{table.Name}].[{_}] = @{_.Replace(' ', '_')}"))})
AND (@sync_force_write = 1 OR (SELECT MAX(CT.ID) FROM {table.NameWithSchema} AS T INNER JOIN __CORE_SYNC_CT AS CT ON CONVERT(nvarchar(1024), T.[{primaryKeyColumns[0]}]) = CT.[PK]) <= @last_sync_version)";

                        //SET CONTEXT_INFO @sync_client_id_binary; 
                        table.UpdateQuery = $@"
UPDATE {table.NameWithSchema}
SET {string.Join(", ", tableColumns.Select(_ => "[" + _ + "] = @" + _.Replace(' ', '_')))}
WHERE ({string.Join(", ", primaryKeyColumns.Select(_ => $"{table.NameWithSchema}.[{_}] = @{_.Replace(' ', '_')}"))})
AND (@sync_force_write = 1 OR (SELECT MAX(CT.ID) FROM {table.NameWithSchema} AS T INNER JOIN __CORE_SYNC_CT AS CT ON CONVERT(nvarchar(1024), T.[{primaryKeyColumns[0]}]) = CT.[PK]) <= @last_sync_version)";


                    }
                }
            }

            _initialized = true;
        }
    }


    //    public class SqlSyncProvider : ISyncProvider
    //    {
    //        private bool _initialized = false;
    //        private Guid _storeId;

    //        public SqlSyncProvider(SqlSyncConfiguration configuration)
    //        {
    //            Configuration = configuration;
    //        }

    //        public SqlSyncConfiguration Configuration { get; }

    //        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, Func<SyncItem, ConflictResolution> onConflictFunc = null)
    //        {
    //            Validate.NotNull(changeSet, nameof(changeSet));

    //            await InitializeAsync();

    //            //if (changeSet.Anchor.StoreId != _storeId)
    //            //{
    //            //    throw new InvalidOperationException("ChangeSet doesn't target this store");
    //            //}

    //            using (var c = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await c.OpenAsync();

    //                using (var cmd = new SqlCommand())
    //                {
    //                    using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
    //                    {
    //                        cmd.Connection = c;
    //                        cmd.Transaction = tr;
    //                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

    //                        long version = (long)await cmd.ExecuteScalarAsync();
    //                        //bool atLeastOneChangeApplied = false;

    //                        foreach (var item in changeSet.Items)
    //                        {
    //                            var table = (SqlSyncTable)Configuration.Tables.First(_ => _.Name == item.TableName);

    //                            cmd.Parameters.Clear();
    //                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

    //                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

    //                            if (changeSet.SourceAnchor.Version < minVersionForTable)
    //                                throw new InvalidOperationException($"Unable to apply changes, version of data to apply ({changeSet.SourceAnchor.Version}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

    //                            bool syncForceWrite = false;
    //                            var itemChangeType = item.ChangeType;

    //                        retryWrite:
    //                            cmd.Parameters.Clear();

    //                            switch (itemChangeType)
    //                            {
    //                                case ChangeType.Insert:
    //                                    cmd.CommandText = table.InsertQuery;
    //                                    break;

    //                                case ChangeType.Update:
    //                                    cmd.CommandText = table.UpdateQuery;
    //                                    break;

    //                                case ChangeType.Delete:
    //                                    cmd.CommandText = table.DeleteQuery;
    //                                    break;
    //                            }

    //                            cmd.Parameters.Add(new SqlParameter("@sync_client_id_binary", changeSet.SourceAnchor.StoreId.ToByteArray()));
    //                            cmd.Parameters.Add(new SqlParameter("@last_sync_version", changeSet.TargetAnchor.Version));
    //                            cmd.Parameters.Add(new SqlParameter("@sync_force_write", syncForceWrite));

    //                            foreach (var valueItem in item.Values)
    //                                cmd.Parameters.Add(new SqlParameter("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value ?? DBNull.Value));

    //                            var affectedRows = cmd.ExecuteNonQuery();

    //                            if (affectedRows == 0)
    //                            {
    //                                if (itemChangeType == ChangeType.Insert)
    //                                {
    //                                    //If we can't apply an insert means that we already
    //                                    //applied the insert or another record with same values (see primary key)
    //                                    //is already present in table.
    //                                    //In any case we can't proceed
    //                                    throw new InvalidSyncOperationException(new SyncAnchor(_storeId, version + 1));
    //                                }
    //                                else if (itemChangeType == ChangeType.Update ||
    //                                    itemChangeType == ChangeType.Delete)
    //                                {
    //                                    if (syncForceWrite)
    //                                    {
    //                                        if (itemChangeType == ChangeType.Delete)
    //                                        {
    //                                            //item is already deleted in data store
    //                                            //so this means that we're going to delete a already deleted record
    //                                            //i.e. nothing to do
    //                                        }
    //                                        else
    //                                        {
    //                                            //if user wants to update forcely a deletes record means
    //                                            //he wants to actually insert it again in store
    //                                            itemChangeType = ChangeType.Insert;
    //                                            goto retryWrite;
    //                                        }
    //                                    }
    //                                    //conflict detected
    //                                    var res = onConflictFunc?.Invoke(item);
    //                                    if (res.HasValue && res.Value == ConflictResolution.ForceWrite)
    //                                    {
    //                                        syncForceWrite = true;
    //                                        goto retryWrite;
    //                                    }
    //                                }
    //                            }
    //                            //else
    //                            //    atLeastOneChangeApplied = true;
    //                        }

    //                        //cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
    //                        //cmd.Parameters.Clear();
    //                        //version = (long)await cmd.ExecuteScalarAsync() + (atLeastOneChangeApplied ? 1 : 0);
    //                        cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [REMOTE_VERSION] = @version WHERE [ID] = @id";
    //                        cmd.Parameters.Clear();
    //                        cmd.Parameters.AddWithValue("@id", changeSet.SourceAnchor.StoreId.ToString());
    //                        cmd.Parameters.AddWithValue("@version", changeSet.SourceAnchor.Version);

    //                        if (0 == await cmd.ExecuteNonQueryAsync())
    //                        {
    //                            cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [REMOTE_VERSION]) VALUES (@id, @version)";

    //                            await cmd.ExecuteNonQueryAsync();
    //                        }

    //                        tr.Commit();

    //                        return new SyncAnchor(_storeId, version);
    //                    }
    //                }
    //            }
    //        }

    //        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version)
    //        {
    //            await InitializeAsync();

    //            using (var c = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await c.OpenAsync();

    //                using (var cmd = new SqlCommand())
    //                {
    //                    using (var tr = c.BeginTransaction())
    //                    {
    //                        cmd.Connection = c;
    //                        cmd.Transaction = tr;
    //                        cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [LOCAL_VERSION] = @version WHERE [ID] = @id";
    //                        cmd.Parameters.AddWithValue("@id", otherStoreId.ToString());
    //                        cmd.Parameters.AddWithValue("@version", version);

    //                        if (0 == await cmd.ExecuteNonQueryAsync())
    //                        {
    //                            cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [LOCAL_VERSION]) VALUES (@id, @version)";

    //                            await cmd.ExecuteNonQueryAsync();
    //                        }

    //                        tr.Commit();
    //                    }
    //                }
    //            }
    //        }

    //        //private async Task ApplyProvisionAsync()
    //        //{
    //        //    using (var c = new SqlConnection(Configuration.ConnectionString))
    //        //    {
    //        //        await c.OpenAsync();

    //        //        await c.EnableChangeTrakingAsync();
    //        //    }
    //        //}

    //        public async Task<SyncChangeSet> GetChangesForStoreAsync(Guid otherStoreId)
    //        {
    //            //Validate.NotNull(otherStoreAnchor, nameof(otherStoreAnchor));

    //            long fromVersion = (await GetLastLocalAnchorForStoreAsync(otherStoreId)).Version;

    //            await InitializeAsync();

    //            using (var c = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await c.OpenAsync();

    //                using (var cmd = new SqlCommand())
    //                {
    //                    var items = new List<SqlSyncItem>();

    //                    using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
    //                    {
    //                        cmd.Connection = c;
    //                        cmd.Transaction = tr;

    //                        cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

    //                        long version = (long)await cmd.ExecuteScalarAsync();

    //                        foreach (SqlSyncTable table in Configuration.Tables)
    //                        {
    //                            cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.Schema}.[{table.Name}]'))";

    //                            long minVersionForTable = (long)await cmd.ExecuteScalarAsync();

    //                            if (fromVersion < minVersionForTable)
    //                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({fromVersion}) for table '{table.Schema}.[{table.Name}]' is too old (min valid version {minVersionForTable})");

    //                            cmd.CommandText = table.IncrementalDataQuery;//.Replace("@last_synchronization_version", fromVersion.ToString());
    //                            cmd.Parameters.Clear();
    //                            cmd.Parameters.AddWithValue("@last_sync_version", fromVersion);
    //                            cmd.Parameters.AddWithValue("@sync_client_id_binary", otherStoreId.ToByteArray());

    //                            using (var r = await cmd.ExecuteReaderAsync())
    //                            {
    //                                while (await r.ReadAsync())
    //                                {
    //                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
    //                                    items.Add(new SqlSyncItem(table, DetectChangeType(values), values));
    //                                }
    //                            }
    //                        }

    //                        tr.Commit();

    //                        return new SyncChangeSet(new SyncAnchor(_storeId, version), await GetLastRemoteAnchorForStoreAsync(otherStoreId), items);
    //                    }
    //                }
    //            }
    //        }

    //        //private async Task<SyncChangeSet> GetInitialSetAsync(Guid otherStoreId)
    //        //{
    //        //    await InitializeAsync();

    //        //    using (var c = new SqlConnection(Configuration.ConnectionString))
    //        //    {
    //        //        await c.OpenAsync();

    //        //        using (var cmd = new SqlCommand())
    //        //        {
    //        //            var items = new List<SqlSyncItem>();

    //        //            using (var tr = c.BeginTransaction(IsolationLevel.Snapshot))
    //        //            {
    //        //                cmd.Connection = c;
    //        //                cmd.Transaction = tr;

    //        //                cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";

    //        //                long version = (long)await cmd.ExecuteScalarAsync();

    //        //                foreach (SqlSyncTable table in Configuration.Tables)
    //        //                {
    //        //                    cmd.CommandText = table.InitialDataQuery;

    //        //                    using (var r = await cmd.ExecuteReaderAsync())
    //        //                    {
    //        //                        while (await r.ReadAsync())
    //        //                        {
    //        //                            var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
    //        //                            items.Add(new SqlSyncItem(table, DetectChangeType(values), values));
    //        //                        }
    //        //                    }
    //        //                }

    //        //                tr.Commit();

    //        //                return new SyncChangeSet(new SyncAnchor(_storeId, version), items);
    //        //            }
    //        //        }
    //        //    }
    //        //}

    //        private async Task<SyncAnchor> GetLastLocalAnchorForStoreAsync(Guid otherStoreId)
    //        {
    //            await InitializeAsync();

    //            using (var c = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await c.OpenAsync();

    //                using (var cmd = c.CreateCommand())
    //                {
    //                    cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [dbo].[__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
    //                    cmd.Parameters.AddWithValue("@storeId", otherStoreId);

    //                    var version = await cmd.ExecuteScalarAsync();

    //                    if (version == null || version == DBNull.Value)
    //                        return new SyncAnchor(_storeId, 0);

    //                    return new SyncAnchor(_storeId, (long)version);
    //                }
    //            }
    //        }

    //        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId)
    //        {
    //            await InitializeAsync();

    //            using (var c = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await c.OpenAsync();

    //                using (var cmd = c.CreateCommand())
    //                {
    //                    cmd.CommandText = "SELECT [REMOTE_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
    //                    cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

    //                    var version = await cmd.ExecuteScalarAsync();

    //                    if (version == null || version == DBNull.Value)
    //                        return new SyncAnchor(otherStoreId, 0);

    //                    return new SyncAnchor(otherStoreId, (long)version);
    //                }
    //            }
    //        }

    //        public async Task<Guid> GetStoreIdAsync()
    //        {
    //            await InitializeAsync();

    //            return _storeId;
    //        }

    //        //private async Task RemoveProvisionAsync()
    //        //{
    //        //    using (var c = new SqlConnection(Configuration.ConnectionString))
    //        //    {
    //        //        await c.OpenAsync();

    //        //        await c.DisableChangeTrakingAsync();
    //        //    }
    //        //}

    //        private ChangeType DetectChangeType(Dictionary<string, object> values)
    //        {
    //            if (values.TryGetValue("SYS_CHANGE_OPERATION", out var syncChangeOperation))
    //            {
    //                switch (syncChangeOperation.ToString())
    //                {
    //                    case "I":
    //                        return ChangeType.Insert;

    //                    case "U":
    //                        return ChangeType.Update;

    //                    case "D":
    //                        return ChangeType.Delete;

    //                    default:
    //                        throw new NotSupportedException();
    //                }
    //            }

    //            return ChangeType.Insert;
    //        }

    //        private async Task InitializeAsync()
    //        {
    //            if (_initialized)
    //                return;

    //            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
    //            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
    //                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

    //            using (var connection = new SqlConnection(Configuration.ConnectionString))
    //            {
    //                await connection.OpenAsync();

    //                if (!await connection.GetIsChangeTrakingEnabledAsync())
    //                {
    //                    await connection.EnableChangeTrakingAsync();
    //                }

    //                if (!await connection.SetSnapshotIsolationAsync())
    //                {
    //                    await connection.SetSnapshotIsolationAsync(true);
    //                }

    //                var tableNames = await connection.GetTableNamesAsync();
    //                if (!tableNames.Contains("__CORE_SYNC_REMOTE_ANCHOR"))
    //                {
    //                    using (var cmd = connection.CreateCommand())
    //                    {
    //                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_REMOTE_ANCHOR](
    //	[ID] [uniqueidentifier] NOT NULL,
    //	[LOCAL_VERSION] [BIGINT] NULL,
    //    [REMOTE_VERSION] [BIGINT] NULL
    // CONSTRAINT [PK___CORE_SYNC_REMOTE_ANCHOR] PRIMARY KEY CLUSTERED
    //(
    //	[ID] ASC
    //)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    //) ON [PRIMARY]";
    //                        await cmd.ExecuteNonQueryAsync();
    //                    }
    //                }

    //                if (!tableNames.Contains("__CORE_SYNC_LOCAL_ID"))
    //                {
    //                    using (var cmd = connection.CreateCommand())
    //                    {
    //                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_LOCAL_ID](
    //	[ID] [uniqueidentifier] NOT NULL
    // CONSTRAINT [PK___CORE_SYNC_LOCAL_ID] PRIMARY KEY CLUSTERED
    //(
    //	[ID] ASC
    //)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    //) ON [PRIMARY]";
    //                        await cmd.ExecuteNonQueryAsync();
    //                    }
    //                }

    //                using (var cmd = connection.CreateCommand())
    //                {
    //                    cmd.CommandText = "SELECT TOP 1 [ID] FROM [__CORE_SYNC_LOCAL_ID]";
    //                    var localId = await cmd.ExecuteScalarAsync();
    //                    if (localId == null)
    //                    {
    //                        localId = Guid.NewGuid();
    //                        cmd.CommandText = $"INSERT INTO [__CORE_SYNC_LOCAL_ID] ([ID]) VALUES (@id)";
    //                        cmd.Parameters.Add(new SqlParameter("@id", localId));
    //                        if (1 != await cmd.ExecuteNonQueryAsync())
    //                        {
    //                            throw new InvalidOperationException();
    //                        }
    //                        cmd.Parameters.Clear();
    //                    }

    //                    _storeId = (Guid)localId;
    //                }

    //                foreach (SqlSyncTable table in Configuration.Tables)
    //                {
    //                    if (!await connection.GetIsChangeTrakingEnabledAsync(table))
    //                    {
    //                        await connection.EnableChangeTrakingAsync(table);
    //                    }

    //                    var primaryKeyIndexName = (await connection.GetClusteredPrimaryKeyIndexesAsync(table)).FirstOrDefault(); //dbTable.Indexes.Cast<Index>().FirstOrDefault(_ => _.IsClustered && _.IndexKeyType == IndexKeyType.DriPrimaryKey);
    //                    if (primaryKeyIndexName == null)
    //                    {
    //                        throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't have a primary key");
    //                    }

    //                    var primaryKeyColumns = await connection.GetIndexColumnNamesAsync(table, primaryKeyIndexName); //primaryKeyIndexName.IndexedColumns.Cast<IndexedColumn>().ToList();
    //                    var allColumns = await connection.GetTableColumnNamesAsync(table); //dbTable.Columns.Cast<Column>().ToList();
    //                    var tableColumns = allColumns.Where(_ => !primaryKeyColumns.Any(kc => kc == _)).ToList();

    //                    table.InitialDataQuery = $@"SELECT
    //                {string.Join(", ", allColumns.Select(_ => "[" + _ + "]"))}
    //            FROM
    //                {table.NameWithSchema}";

    //                    table.IncrementalDataQuery = $@"SELECT
    //                {string.Join(", ", primaryKeyColumns.Select(_ => "CT.[" + _ + "]"))} {(tableColumns.Any() ? ", " + string.Join(", ", tableColumns.Select(_ => "T.[" + _ + "]")) : string.Empty)},
    //                CT.SYS_CHANGE_OPERATION, CT.SYS_CHANGE_COLUMNS, CT.SYS_CHANGE_CONTEXT
    //            FROM
    //                {table.NameWithSchema} AS T
    //            RIGHT OUTER JOIN
    //                CHANGETABLE(CHANGES {table.NameWithSchema}, @last_sync_version) AS CT
    //            ON
    //                {string.Join(" AND ", primaryKeyColumns.Select(_ => $"T.[{_}] = CT.[{_}]"))}
    //            WHERE (CT.SYS_CHANGE_CONTEXT IS NULL OR CT.SYS_CHANGE_CONTEXT <> @sync_client_id_binary)";

    //                    //                 table.InsertQuery = $@"WITH CHANGE_TRACKING_CONTEXT (@sync_client_id_binary) INSERT INTO {table.NameWithSchema} ({string.Join(", ", allColumns.Select(_ => $"[{_}]"))}) SELECT {string.Join(", ", allColumns.Select(_ => "@" + _.Replace(' ', '_')))} EXCEPT
    //                    //SELECT {string.Join(", ", allColumns.Select(_ => $"[{_}]"))} FROM {table.NameWithSchema};";
    //            table.InsertQuery = $@"BEGIN TRY  
    //WITH CHANGE_TRACKING_CONTEXT (@sync_client_id_binary) 
    //INSERT INTO {table.NameWithSchema} ({string.Join(", ", allColumns.Select(_ => $"[{_}]"))}) VALUES ({string.Join(", ", allColumns.Select(_ => "@" + _.Replace(' ', '_')))})
    //END TRY  
    //BEGIN CATCH  
    //END CATCH";

    //                    table.DeleteQuery = $@"WITH CHANGE_TRACKING_CONTEXT (@sync_client_id_binary) DELETE FROM {table.NameWithSchema}
    //FROM
    //    {table.NameWithSchema}
    //JOIN CHANGETABLE(VERSION {table.NameWithSchema}, ({string.Join(", ", primaryKeyColumns.Select(_ => "[" + _ + "]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "@" + _.Replace(' ', '_')))})) CT  ON {string.Join(" AND ", primaryKeyColumns.Select(_ => $"CT.[{_}] = {table.Schema}.[{table.Name}].[{_}]"))}
    //WHERE
    //    @sync_force_write = 1 OR @last_sync_version >= ISNULL(CT.SYS_CHANGE_VERSION, 0)";

    //                    table.UpdateQuery = $@"WITH CHANGE_TRACKING_CONTEXT (@sync_client_id_binary) UPDATE {table.NameWithSchema}
    //SET
    //    {string.Join(", ", tableColumns.Select(_ => "[" + _ + "] = @" + _.Replace(" ", "_")))}
    //FROM
    //    {table.NameWithSchema}
    //JOIN CHANGETABLE(VERSION {table.NameWithSchema}, ({string.Join(", ", primaryKeyColumns.Select(_ => $"[{_}]"))}), ({string.Join(", ", primaryKeyColumns.Select(_ => "@" + _.Replace(' ', '_')))})) CT ON {string.Join(" AND ", primaryKeyColumns.Select(_ => $"CT.[{_}] = {table.Schema}.[{table.Name}].[{_}]"))}
    //WHERE
    //    @sync_force_write = 1 OR @last_sync_version >= ISNULL(CT.SYS_CHANGE_VERSION, 0)";
    //                }
    //            }

    //            _initialized = true;
    //        }
    //    }
}