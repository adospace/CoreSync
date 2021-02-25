using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.SqlServer
{
    public class SqlSyncProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;
        private readonly ISyncLogger _logger;

        public SqlSyncProvider(SqlSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional, ISyncLogger logger = null)
        {
            Configuration = configuration;
            ProviderMode = providerMode;
            _logger = logger;

            if (configuration.Tables.Any(_ => _.SyncDirection != SyncDirection.UploadAndDownload) &&
                providerMode == ProviderMode.Bidirectional)
            {
                throw new InvalidOperationException("One or more table with sync direction different from Bidirectional: please must specify the provider mode to Local or Remote");
            }
        }

        public SqlSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, Func<SyncItem, ConflictResolution> onConflictFunc = null, CancellationToken cancellationToken = default)
        {
            Validate.NotNull(changeSet, nameof(changeSet));

            await InitializeStoreAsync(cancellationToken);

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin ApplyChanges(source={changeSet.SourceAnchor}, target={changeSet.TargetAnchor}, {changeSet.Items.Count} items)");

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                var messageLog = new List<SqlInfoMessageEventArgs>();

                try
                {
                    c.InfoMessage += (s, e) => messageLog.Add(e);
                    await c.OpenAsync(cancellationToken);

                    //await DisableConstraintsForChangeSetTables(c, changeSet);

                    using (var cmd = new SqlCommand())
                    {
                        using (var tr = c.BeginTransaction())
                        {
                            cmd.Connection = c;
                            cmd.Transaction = tr;

                            try
                            {
                                cmd.CommandText = "SELECT MAX(ID) FROM __CORE_SYNC_CT";
                                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                                cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                                cmd.CommandText = $"DECLARE @session uniqueidentifier; SET @session = @sync_client_id_binary; SET CONTEXT_INFO @session";
                                cmd.Parameters.Add(new SqlParameter("@sync_client_id_binary", changeSet.SourceAnchor.StoreId));
                                await cmd.ExecuteNonQueryAsync(cancellationToken);
                                cmd.Parameters.Clear();

                                cmd.CommandText = $"SELECT CONTEXT_INFO()";
                                var contextInfo = await cmd.ExecuteScalarAsync(cancellationToken);
                                cmd.Parameters.Clear();

                                foreach (var item in changeSet.Items)
                                {
                                    var table = (SqlSyncTable)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);

                                    if (table == null)
                                    {
                                        continue;
                                    }

                                    bool syncForceWrite = false;
                                    var itemChangeType = item.ChangeType;

                                retryWrite:
                                    cmd.Parameters.Clear();

                                    table.SetupCommand(cmd, itemChangeType, item.Values);

                                    cmd.Parameters.Add(new SqlParameter("@last_sync_version", changeSet.TargetAnchor.Version));
                                    cmd.Parameters.Add(new SqlParameter("@sync_force_write", syncForceWrite));

                                    int affectedRows;

                                    try
                                    {
                                        affectedRows = cmd.ExecuteNonQuery();

                                        if (affectedRows > 0)
                                        {
                                            _logger?.Trace($"[{_storeId}] Successfully applied {item}");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new SynchronizationException($"Unable to {itemChangeType} item {item} to store for table {table}", ex);
                                    }

                                    if (affectedRows == 0)
                                    {
                                        if (itemChangeType == ChangeType.Insert)
                                        {
                                            //If we can't apply an insert means that we already
                                            //applied the insert or another record with same values (see primary key)
                                            //is already present in table.
                                            cmd.CommandText = table.SelectExistingQuery;
                                            cmd.Parameters.Clear();
                                            var valueItem = item.Values[table.PrimaryColumnName];
                                            cmd.Parameters.Add(new SqlParameter("@" + table.PrimaryColumnName.Replace(" ", "_"), table.Columns[table.PrimaryColumnName].DbType)
                                            {
                                                Value = Utils.ConvertToSqlType(valueItem, table.Columns[table.PrimaryColumnName].DbType)
                                            });
                                            if (1 == (int)await cmd.ExecuteScalarAsync(cancellationToken) && !syncForceWrite)
                                            {
                                                itemChangeType = ChangeType.Update;
                                                goto retryWrite;
                                                //_logger?.Trace($"[{_storeId}] Existing record for {item}");
                                            }
                                            else
                                            {
                                                _logger?.Warning($"[{_storeId}] Unable to {itemChangeType} item {item} on table {table}. Messages: Messages:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.Select(_ => _.Message))}");
                                            }
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
                                                    _logger?.Trace($"[{_storeId}] Insert on delete conflict occurred for {item}");
                                                }
                                                else
                                                {
                                                    //if user wants to update forcely a deleted record means
                                                    //he wants to actually insert it again in store
                                                    _logger?.Trace($"[{_storeId}] Insert on delete conflict occurred for {item}");
                                                    itemChangeType = ChangeType.Insert;
                                                    goto retryWrite;
                                                }
                                            }
                                            else
                                            {
                                                //conflict detected
                                                var res = onConflictFunc?.Invoke(item);
                                                if (res.HasValue && res.Value == ConflictResolution.ForceWrite)
                                                {
                                                    _logger?.Trace($"[{_storeId}] Force write on conflict occurred for {item}");

                                                    syncForceWrite = true;
                                                    goto retryWrite;
                                                }
                                                else
                                                {
                                                    _logger?.Warning($"[{_storeId}] Skip conflict for {item}");
                                                }
                                            }
                                        }
                                    }
                                }

                                cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [REMOTE_VERSION] = @version WHERE [ID] = @id";
                                cmd.Parameters.Clear();
                                cmd.Parameters.AddWithValue("@id", changeSet.SourceAnchor.StoreId.ToString());
                                cmd.Parameters.AddWithValue("@version", changeSet.SourceAnchor.Version);

                                if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                                {
                                    cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [REMOTE_VERSION]) VALUES (@id, @version)";

                                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                                }

                                tr.Commit();

                                var resAnchor = new SyncAnchor(_storeId, version);

                                _logger?.Info($"[{_storeId}] Completed ApplyChanges(resAnchor={resAnchor}) in {(DateTime.Now - now).TotalMilliseconds}ms");

                                return resAnchor;
                            }
                            catch (Exception)
                            {
                                tr.Rollback();
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var exceptionMessage = $"An exception occurred during synchronization:{Environment.NewLine}Errors:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.SelectMany(_ => _.Errors.Cast<SqlError>()))}{Environment.NewLine}Messages:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.Select(_ => _.Message))}";
                    throw new SyncErrorException(exceptionMessage, ex);
                }
                finally
                {
                    //await RestoreConstraintsForChangeSetTables(c, changeSet);
                }
            }
        }

        private async Task DisableConstraintsForChangeSetTables(SqlConnection connection, SyncChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            using (var cmd = new SqlCommand())
            {
                cmd.Connection = connection;

                foreach (SqlSyncTable table in changeSet.Items
                    .Select(_=> _.TableName)
                    .Distinct()
                    .Select(tableName => Configuration.Tables.First(_ => _.Name == tableName)))
                {
                    cmd.CommandText = $"ALTER TABLE {table.NameWithSchema} NOCHECK CONSTRAINT ALL";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        private async Task RestoreConstraintsForChangeSetTables(SqlConnection connection, SyncChangeSet changeSet, CancellationToken cancellationToken = default)
        {
            using (var cmd = new SqlCommand())
            {
                cmd.Connection = connection;

                foreach (SqlSyncTable table in changeSet.Items
                    .Select(_ => _.TableName)
                    .Distinct()
                    .Select(tableName => Configuration.Tables.First(_ => _.Name == tableName)))
                {
                    cmd.CommandText = $"ALTER TABLE {table.NameWithSchema} WITH CHECK CHECK CONSTRAINT ALL";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
        {
            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqlCommand())
                {
                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;
                        try
                        {
                            cmd.CommandText = $"UPDATE [__CORE_SYNC_REMOTE_ANCHOR] SET [LOCAL_VERSION] = @version WHERE [ID] = @id";
                            cmd.Parameters.AddWithValue("@id", otherStoreId.ToString());
                            cmd.Parameters.AddWithValue("@version", version);

                            if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                            {
                                cmd.CommandText = "INSERT INTO [__CORE_SYNC_REMOTE_ANCHOR] ([ID], [LOCAL_VERSION]) VALUES (@id, @version)";

                                await cmd.ExecuteNonQueryAsync(cancellationToken);
                            }

                            tr.Commit();

                            _logger?.Trace($"[{_storeId}] Save version {version} for store {otherStoreId}");
                        }
                        catch (Exception)
                        {
                            tr.Rollback();
                            throw;
                        }
                    }
                }
            }
        }

        private async Task<SyncAnchor> GetLastLocalAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync(cancellationToken);

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [dbo].[__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
                    cmd.Parameters.AddWithValue("@storeId", otherStoreId);

                    var version = await cmd.ExecuteScalarAsync(cancellationToken);

                    if (version == null || version == DBNull.Value)
                        return SyncAnchor.Null;

                    return new SyncAnchor(_storeId, (long)version);
                }
            }
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync(cancellationToken);

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT [REMOTE_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
                    cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

                    var version = await cmd.ExecuteScalarAsync(cancellationToken);

                    if (version == null || version == DBNull.Value)
                        return new SyncAnchor(otherStoreId, 0);

                    return new SyncAnchor(otherStoreId, (long)version);
                }
            }
        }

        public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            return _storeId;
        }

        private ChangeType DetectChangeType(Dictionary<string, object> values)
        {
            if (values.TryGetValue("__OP", out var syncChangeOperation))
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

        private async Task InitializeStoreAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;

            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                var tableNames = await connection.GetTableNamesAsync(cancellationToken);
                if (!tableNames.Contains("__CORE_SYNC_CT"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_CT](
	[ID] [bigint] IDENTITY(1,1) NOT NULL,
	[TBL] [nvarchar](1024) NOT NULL,
	[OP] [char](1) NOT NULL,
	[PK_Int] [int] SPARSE NULL,
	[PK_String] [nvarchar](1024) SPARSE NULL,
	[PK_Guid] [uniqueidentifier] SPARSE NULL,
	[SRC] [varbinary](128) NULL,
 CONSTRAINT [PK___CORE_SYNC_CT] PRIMARY KEY CLUSTERED 
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);

                        cmd.CommandText = $@"CREATE NONCLUSTERED INDEX [PK_Int-Index] ON [dbo].[__CORE_SYNC_CT]
(
	[PK_Int] ASC
)
INCLUDE([TBL]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);

                        cmd.CommandText = $@"CREATE NONCLUSTERED INDEX [PK_String-Index] ON [dbo].[__CORE_SYNC_CT]
(
	[PK_String] ASC
)
INCLUDE([TBL]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);

                        cmd.CommandText = $@"CREATE NONCLUSTERED INDEX [PK_Guid-Index] ON [dbo].[__CORE_SYNC_CT]
(
	[PK_Guid] ASC
)
INCLUDE([TBL]) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 [ID] FROM [__CORE_SYNC_LOCAL_ID]";
                    var localId = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (localId == null)
                    {
                        localId = Guid.NewGuid();
                        cmd.CommandText = $"INSERT INTO [__CORE_SYNC_LOCAL_ID] ([ID]) VALUES (@id)";
                        cmd.Parameters.Add(new SqlParameter("@id", localId));
                        if (1 != await cmd.ExecuteNonQueryAsync(cancellationToken))
                        {
                            throw new InvalidOperationException();
                        }
                        cmd.Parameters.Clear();
                    }

                    _storeId = (Guid)localId;
                }

                using (var cmd = connection.CreateCommand())
                {
                    foreach (SqlSyncTable table in Configuration.Tables)
                    {
                        var primaryKeyIndexName = (await connection.GetClusteredPrimaryKeyIndexesAsync(table, cancellationToken)).FirstOrDefault();
                        if (primaryKeyIndexName == null)
                        {
                            throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't exist or it doesn't have a primary key");
                        }

                        var primaryKeyColumns = await connection.GetIndexColumnNamesAsync(table, primaryKeyIndexName, cancellationToken);

                        if (primaryKeyColumns.Length != 1)
                        {
                            throw new NotSupportedException($"Table '{table.Name}' has more than one column as primary key");
                        }

                        table.Columns = (await connection.GetTableColumnsAsync(table, cancellationToken)).ToDictionary(_ => _.Item1, _ => new SqlColumn(_.Item1, _.Item2), StringComparer.OrdinalIgnoreCase);
                        table.PrimaryColumnName = primaryKeyColumns[0];
                        table.PrimaryKeyColumns = primaryKeyColumns;

                        if (table.SkipColumns.Any(skipColumn => string.CompareOrdinal(skipColumn, table.PrimaryColumnName) == 0))
                        {
                            throw new InvalidOperationException($"Column to skip in synchronization ('{table.PrimaryColumnName}') can't be the primary column");
                        }

                        var allColumns = table.Columns.Keys.Except(table.SkipColumns);

                        var tableColumns = allColumns.Where(_ => !primaryKeyColumns.Any(kc => kc == _)).ToArray();

                        if (allColumns.Any(_ => string.CompareOrdinal(_, "__OP") == 0))
                        {
                            throw new NotSupportedException($"Table '{table.Name}' has one column with reserved name '__OP'");
                        }

                        cmd.CommandText = $@"SELECT COUNT(*) FROM SYS.IDENTITY_COLUMNS WHERE OBJECT_NAME(OBJECT_ID) = @tablename AND OBJECT_SCHEMA_NAME(object_id) = @schemaname";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@tablename", table.Name);
                        cmd.Parameters.AddWithValue("@schemaname", table.Schema);

                        table.HasTableIdentityColumn = ((int)await cmd.ExecuteScalarAsync(cancellationToken) == 1);
                    }
                }
            }

            _initialized = true;
        }

        public async Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                var tableNames = await connection.GetTableNamesAsync(cancellationToken);
                using (var cmd = connection.CreateCommand())
                {
                    foreach (SqlSyncTable table in Configuration.Tables)
                    {
                        var primaryKeyIndexName = (await connection.GetClusteredPrimaryKeyIndexesAsync(table, cancellationToken)).FirstOrDefault(); //dbTable.Indexes.Cast<Index>().FirstOrDefault(_ => _.IsClustered && _.IndexKeyType == IndexKeyType.DriPrimaryKey);
                        if (primaryKeyIndexName == null)
                        {
                            throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't have a primary key");
                        }

                        var primaryKeyColumns = await connection.GetIndexColumnNamesAsync(table, primaryKeyIndexName, cancellationToken); //primaryKeyIndexName.IndexedColumns.Cast<IndexedColumn>().ToList();
                        var allColumns = (await connection.GetTableColumnsAsync(table, cancellationToken)).Select(_=>_.Item1).ToArray();
                        //var tableColumns = allColumns.Where(_ => !primaryKeyColumns.Any(kc => kc == _)).ToArray();

                        //if (primaryKeyColumns.Length != 1)
                        //{
                        //    throw new NotSupportedException($"Table '{table.Name}' has more than one column as primary key");
                        //}

                        if (allColumns.Any(_ => string.CompareOrdinal(_, "__OP") == 0))
                        {
                            throw new NotSupportedException($"Table '{table.NameWithSchema}' has one column with reserved name '__OP'");
                        }

                        if (table.SyncDirection == SyncDirection.UploadAndDownload ||
                            (table.SyncDirection == SyncDirection.UploadOnly && ProviderMode == ProviderMode.Local) ||
                            (table.SyncDirection == SyncDirection.DownloadOnly && ProviderMode == ProviderMode.Remote))
                        {
                            await SetupTableForFullChangeDetection(table, cmd, cancellationToken);
                        }
                        else
                        {
                            await SetupTableForUpdatesOrDeletesOnly(table, cmd, cancellationToken);
                        }
                    }
                }
            }
        }

        private async Task SetupTableForFullChangeDetection(SqlSyncTable table, SqlCommand cmd, CancellationToken cancellationToken = default)
        {
            var existsTriggerCommand = new Func<string, string>((op) => $@"select COUNT(*) from sys.objects where schema_id=SCHEMA_ID('{table.Schema}') AND type='TR' and name='__{table.NameWithSchemaRaw}_ct-{op}__'");
            var createTriggerCommand = new Func<string, string>((op) => $@"CREATE TRIGGER [__{table.NameWithSchemaRaw}_ct-{op}__]
ON {table.NameWithSchema}
AFTER {op}
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	INSERT INTO [__CORE_SYNC_CT](TBL, OP, PK_{table.PrimaryColumnType}, SRC) 
	SELECT '{table.NameWithSchema}', '{op[0]}' , {(op == "DELETE" ? "DELETED" : "INSERTED")}{$".[{table.PrimaryColumnName}]"}, CONTEXT_INFO()  
	FROM {(op == "DELETE" ? "DELETED" : "INSERTED")}
END");

            cmd.CommandText = existsTriggerCommand("INSERT");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                cmd.CommandText = createTriggerCommand("INSERT");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            cmd.CommandText = existsTriggerCommand("UPDATE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                cmd.CommandText = createTriggerCommand("UPDATE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            cmd.CommandText = existsTriggerCommand("DELETE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                cmd.CommandText = createTriggerCommand("DELETE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task SetupTableForUpdatesOrDeletesOnly(SqlSyncTable table, SqlCommand cmd, CancellationToken cancellationToken = default)
        {
            var existsTriggerCommand = new Func<string, string>((op) => $@"select COUNT(*) from sys.objects where schema_id=SCHEMA_ID('{table.Schema}') AND type='TR' and name='__{table.NameWithSchemaRaw}_ct-{op}__'");
            var createTriggerCommand = new Func<string, string>((op) => $@"CREATE TRIGGER [__{table.NameWithSchemaRaw}_ct-{op}__]
ON {table.NameWithSchema}
AFTER {op}
AS
BEGIN
	-- SET NOCOUNT ON added to prevent extra result sets from
	-- interfering with SELECT statements.
	SET NOCOUNT ON;

	INSERT INTO [__CORE_SYNC_CT](TBL, OP, PK_{table.PrimaryColumnType}, SRC) 
	SELECT '{table.NameWithSchema}', '{op[0]}' , {(op == "DELETE" ? "DELETED" : "INSERTED")}{$".[{table.PrimaryColumnName}]"}, CONTEXT_INFO()  
	FROM {(op == "DELETE" ? "DELETED" : "INSERTED")}
END");

            cmd.CommandText = existsTriggerCommand("UPDATE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                cmd.CommandText = createTriggerCommand("UPDATE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            cmd.CommandText = existsTriggerCommand("DELETE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                cmd.CommandText = createTriggerCommand("DELETE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        {
            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                var tableNames = await connection.GetTableNamesAsync(cancellationToken);
                if (tableNames.Contains("__CORE_SYNC_CT"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"DROP TABLE [dbo].[__CORE_SYNC_CT]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                if (tableNames.Contains("__CORE_SYNC_REMOTE_ANCHOR"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"DROP TABLE [dbo].[__CORE_SYNC_REMOTE_ANCHOR]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                if (tableNames.Contains("__CORE_SYNC_LOCAL_ID"))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = $@"DROP TABLE [dbo].[__CORE_SYNC_LOCAL_ID]";
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                using (var cmd = connection.CreateCommand())
                {
                    foreach (SqlSyncTable table in Configuration.Tables)
                    {
                        await DisableChangeTrackingForTable(cmd, table, cancellationToken);
                    }
                }
            }
        }

        public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[] syncFilterParameters, SyncDirection syncDirection, CancellationToken cancellationToken = default)
        {
            syncFilterParameters = syncFilterParameters ?? new SyncFilterParameter[] { };

            var fromAnchor = (await GetLastLocalAnchorForStoreAsync(otherStoreId, cancellationToken));

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin GetChanges(from={otherStoreId}, syncDirection={syncDirection}, fromVersion={fromAnchor})");


            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync(cancellationToken);

                using (var cmd = new SqlCommand())
                {
                    var items = new List<SqlSyncItem>();

                    using (var tr = c.BeginTransaction())// IsolationLevel.Snapshot))
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        try
                        {
                            cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                            cmd.Parameters.Clear(); 
                            var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                            cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                            cmd.Parameters.Clear(); 
                            var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                            if (!fromAnchor.IsNull() && fromAnchor.Version < minVersion - 1)
                                throw new InvalidOperationException($"Unable to get changes, version of data requested ({fromAnchor}) is too old (min valid version {minVersion})");

                            foreach (SqlSyncTable table in Configuration.Tables)
                            {
                                if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                                    table.SyncDirection != syncDirection)
                                    continue;

                                //var snapshotItems = new HashSet<object>();

                                if (fromAnchor.IsNull() && !table.SkipInitialSnapshot)
                                {
                                    cmd.CommandText = table.InitialSnapshotQuery;
                                    cmd.Parameters.Clear(); 
                                    foreach (var syncFilterParameter in syncFilterParameters)
                                    {
                                        cmd.Parameters.AddWithValue(syncFilterParameter.Name, syncFilterParameter.Value);
                                    }

                                    using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                                    {
                                        while (await r.ReadAsync(cancellationToken))
                                        {
                                            var values = Enumerable.Range(0, r.FieldCount)
                                                .ToDictionary(index => r.GetName(index), index => r.GetValue(index), StringComparer.OrdinalIgnoreCase);
                                            foreach (var skipColumn in table.SkipColumns)
                                            {
                                                if (string.CompareOrdinal(skipColumn, table.PrimaryColumnName) == 0)
                                                    throw new InvalidOperationException($"Column to skip in synchronization ('{skipColumn}') can't be the primary column");
                                            }

                                            items.Add(new SqlSyncItem(table, ChangeType.Insert, values));
                                            //snapshotItems.Add(values[table.PrimaryColumnName]);
                                            _logger?.Trace($"[{_storeId}] Initial snapshot {items.Last()}");
                                        }
                                    }
                                }

                                if (!fromAnchor.IsNull())
                                {
                                    cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                                    cmd.Parameters.AddWithValue("@sourceId", otherStoreId);
                                    foreach (var syncFilterParameter in syncFilterParameters)
                                    {
                                        cmd.Parameters.AddWithValue(syncFilterParameter.Name, syncFilterParameter.Value);
                                    }

                                    using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                                    {
                                        while (await r.ReadAsync(cancellationToken))
                                        {
                                            var values = Enumerable.Range(0, r.FieldCount)
                                                .ToDictionary(index => r.GetName(index), index => r.GetValue(index), StringComparer.OrdinalIgnoreCase);
                                            //if (snapshotItems.Contains(values[table.PrimaryColumnName]))
                                            //    continue;

                                            foreach (var skipColumn in table.SkipColumns)
                                            {
                                                if (string.CompareOrdinal(skipColumn, table.PrimaryColumnName) == 0)
                                                    throw new InvalidOperationException($"Column to skip in synchronization ('{skipColumn}') can't be the primary column");
                                            }

                                            items.Add(new SqlSyncItem(table, DetectChangeType(values),
                                                values.Where(_ => _.Key != "__OP").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));
                                            _logger?.Trace($"[{_storeId}] Incremental add or update {items.Last()}");
                                        }
                                    }

                                    cmd.CommandText = table.IncrementalDeletesQuery;
                                    cmd.Parameters.Clear();
                                    cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                                    cmd.Parameters.AddWithValue("@sourceId", otherStoreId);
                                    using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                                    {
                                        while (await r.ReadAsync(cancellationToken))
                                        {
                                            var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => r.GetValue(_));
                                            items.Add(new SqlSyncItem(table, ChangeType.Delete, values));
                                            _logger?.Trace($"[{_storeId}] Incremental delete {items.Last()}");
                                        }
                                    }
                                }
                            }

                            tr.Commit();

                            var resChangeSet = new SyncChangeSet(new SyncAnchor(_storeId, version), await GetLastRemoteAnchorForStoreAsync(otherStoreId, cancellationToken), items);

                            _logger?.Info($"[{_storeId}] Completed GetChanges(to={version}, {items.Count} items) in {(DateTime.Now - now).TotalMilliseconds}ms");

                            return resChangeSet;
                        }
                        catch (Exception)
                        {
                            tr.Rollback();
                            throw;
                        }
                    }
                }
            }
        }

        public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                try
                {
                    await c.OpenAsync();

                    using (var cmd = new SqlCommand())
                    {
                        using (var tr = c.BeginTransaction())
                        {
                            cmd.Connection = c;
                            cmd.Transaction = tr;

                            cmd.CommandText = "SELECT MAX(ID) FROM __CORE_SYNC_CT";
                            var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                            cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                            var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                            return new SyncVersion(version, minVersion);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Unable to get current/minimum version from store", ex);
                }
            }
        }

        public async Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using (var c = new SqlConnection(Configuration.ConnectionString))
            {
                try
                {
                    await c.OpenAsync(cancellationToken);

                    using (var cmd = new SqlCommand())
                    {
                        using (var tr = c.BeginTransaction())
                        {
                            cmd.Connection = c;
                            cmd.Transaction = tr;

                            try
                            {
                                cmd.CommandText = $"DELETE FROM __CORE_SYNC_CT WHERE ID < {minVersion}";
                                await cmd.ExecuteNonQueryAsync(cancellationToken);

                                cmd.CommandText = "SELECT MAX(ID) FROM __CORE_SYNC_CT";
                                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                                cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                                var newMinVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                                tr.Commit();

                                return new SyncVersion(version, newMinVersion);
                            }
                            catch (Exception)
                            {
                                tr.Rollback();
                                throw;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Unable to apply version {minVersion} to tracking table of the store", ex);
                }
            }
        }

        public async Task EnableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            var table = Configuration.Tables.Cast<SqlSyncTable>().FirstOrDefault(_ => _.NameWithSchema == name);

            if (table == null)
            {
                table = Configuration.Tables.Cast<SqlSyncTable>().FirstOrDefault(_ => _.Name == name);
            }

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table '{name}'");
            }

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var cmd = connection.CreateCommand())
                {
                    if (table.SyncDirection == SyncDirection.UploadAndDownload ||
                        (table.SyncDirection == SyncDirection.UploadOnly && ProviderMode == ProviderMode.Local) ||
                        (table.SyncDirection == SyncDirection.DownloadOnly && ProviderMode == ProviderMode.Remote))
                    {
                        await SetupTableForFullChangeDetection(table, cmd, cancellationToken);
                    }
                    else
                    {
                        await SetupTableForUpdatesOrDeletesOnly(table, cmd, cancellationToken);
                    }
                }
            }
        }

        public async Task DisableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            var table = Configuration.Tables.Cast<SqlSyncTable>().FirstOrDefault(_ => _.NameWithSchema == name);

            if (table == null)
            {
                table = Configuration.Tables.Cast<SqlSyncTable>().FirstOrDefault(_ => _.Name == name);
            }

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table '{name}'");
            }

            using (var connection = new SqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var cmd = connection.CreateCommand())
                {
                    await DisableChangeTrackingForTable(cmd, table, cancellationToken);
                }
            }
        }

        private async Task DisableChangeTrackingForTable(SqlCommand cmd, SqlSyncTable table, CancellationToken cancellationToken)
        {
            var existsTriggerCommand = new Func<string, string>((op) => $@"select COUNT(*) from sys.objects where schema_id=SCHEMA_ID('{table.Schema}') AND type='TR' and name='__{table.NameWithSchemaRaw}_ct-{op}__'");
            var dropTriggerCommand = new Func<string, string>((op) => $@"DROP TRIGGER [__{table.NameWithSchemaRaw}_ct-{op}__]");

            cmd.CommandText = existsTriggerCommand("INSERT");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 1)
            {
                cmd.CommandText = dropTriggerCommand("INSERT");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            cmd.CommandText = existsTriggerCommand("UPDATE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 1)
            {
                cmd.CommandText = dropTriggerCommand("UPDATE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            cmd.CommandText = existsTriggerCommand("DELETE");
            if (((int)await cmd.ExecuteScalarAsync(cancellationToken)) == 1)
            {
                cmd.CommandText = dropTriggerCommand("DELETE");
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}