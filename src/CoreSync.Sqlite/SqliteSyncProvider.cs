﻿using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.Sqlite
{
    public class SqliteSyncProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;
        private readonly ISyncLogger? _logger;

        public SqliteSyncProvider(SqliteSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional, ISyncLogger? logger = null)
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

        public SqliteSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
        {
            Validate.NotNull(changeSet, nameof(changeSet));
            Validate.NotNull(changeSet.SourceAnchor, nameof(changeSet.SourceAnchor));
            Validate.NotNull(changeSet.TargetAnchor, nameof(changeSet.TargetAnchor));

            await InitializeStoreAsync(cancellationToken);

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin ApplyChanges(source={changeSet.SourceAnchor}, target={changeSet.TargetAnchor}, {changeSet.Items.Count} items)");

            using var c = new SqliteConnection(Configuration.ConnectionString); // +";Foreign Keys=False"))
            await c.OpenAsync(cancellationToken);

            using var cmd = new SqliteCommand();
            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                foreach (var item in changeSet.Items)
                {
                    var table = (SqliteSyncTable)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);
                    if (table == null)
                    {
                        continue;
                    }

                    bool syncForceWrite = false;
                    var itemChangeType = item.ChangeType;

                retryWrite:
                    cmd.Parameters.Clear();

                    table.SetupCommand(cmd, itemChangeType, item.Values);

                    cmd.Parameters.Add(new SqliteParameter("@last_sync_version", changeSet.TargetAnchor.Version));
                    cmd.Parameters.Add(new SqliteParameter("@sync_force_write", syncForceWrite));

                    int affectedRows = 0;

                    try
                    {
                        affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);

                        if (affectedRows > 0)
                        {
                            _logger?.Trace($"[{_storeId}] Successfully applied {item}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        //throw new SynchronizationException($"Unable to {item} item to store for table {table}", ex);
                        _logger?.Error($"Unable to {itemChangeType} item {item} to store for table {table}.{Environment.NewLine}{ex}{Environment.NewLine}Generated SQL:{Environment.NewLine}{cmd.CommandText}");
                    }

                    if (affectedRows == 0)
                    {
                        if (itemChangeType == ChangeType.Insert)
                        {
                            //If we can't apply an insert means that we already
                            //applied the insert or another record with same values (see primary key)
                            //is already present in table.
                            //In any case we can't proceed
                            cmd.CommandText = table.SelectExistingQuery;
                            cmd.Parameters.Clear();
                            var valueItem = item.Values[table.PrimaryColumnName];
                            cmd.Parameters.Add(new SqliteParameter("@PrimaryColumnParameter", valueItem.Value ?? DBNull.Value));
                            if (1 == (long)await cmd.ExecuteScalarAsync(cancellationToken) && !syncForceWrite)
                            {
                                itemChangeType = ChangeType.Update;
                                goto retryWrite;
                            }
                            else
                            {
                                _logger?.Warning($"Unable to {item}: much probably there is a foreign key constraint issue logged before");
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
                                    //if user wants to update forcely a deleted record means that
                                    //he asctually wants to insert it again in store
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

                    if (affectedRows > 0)
                    {
                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                        cmd.Parameters.Clear();
                        var currentVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                        cmd.CommandText = "UPDATE [__CORE_SYNC_CT] SET [SRC] = @sourceId WHERE [ID] = @version";
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@sourceId", changeSet.SourceAnchor.StoreId.ToString());
                        cmd.Parameters.AddWithValue("@version", currentVersion);

                        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
            catch (Exception ex)
            {
                _logger?.Error($"[{_storeId}] Unable to complete the apply changes:{Environment.NewLine}{ex}");
                tr.Rollback();
                throw;
            }
        }

        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
        {
            using var c = new SqliteConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new SqliteCommand();
            using var tr = c.BeginTransaction();
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

        public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, CancellationToken cancellationToken = default)
        {
            syncFilterParameters ??= [];

            var fromAnchor = (await GetLastLocalAnchorForStoreAsync(otherStoreId, cancellationToken));

            //if fromVersion is 0 means that client needs actually to initialize its local datastore

            //await InitializeStoreAsync();

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin GetChanges(from={otherStoreId}, syncDirection={syncDirection}, fromVersion={fromAnchor})");

            using var c = new SqliteConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new SqliteCommand();
            var items = new List<SqliteSyncItem>();

            using var tr = c.BeginTransaction();
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

                foreach (var table in Configuration.Tables.Cast<SqliteSyncTable>().Where(_ => _.Columns.Any()))
                {
                    if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                        table.SyncDirection != syncDirection)
                        continue;

                    //var snapshotItems = new HashSet<object>();

                    if (fromAnchor.IsNull() && !table.SkipInitialSnapshot)
                    {
                        if (string.IsNullOrWhiteSpace(table.InitialSnapshotQuery))
                        {
                            throw new InvalidOperationException($"InitialSnapshotQuery not specified for table");
                        }

                        cmd.CommandText = table.InitialSnapshotQuery;
                        cmd.Parameters.Clear();
                        foreach (var syncFilterParameter in syncFilterParameters)
                        {
                            cmd.Parameters.AddWithValue(syncFilterParameter.Name, syncFilterParameter.Value);
                        }

                        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                        while (await r.ReadAsync(cancellationToken))
                        {
                            var values = Enumerable.Range(0, r.FieldCount)
                                .ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                            items.Add(new SqliteSyncItem(table, ChangeType.Insert, values));
                            //snapshotItems.Add(values[table.PrimaryColumnName]);
                            _logger?.Trace($"[{_storeId}] Initial snapshot {items.Last()}");
                        }
                    }

                    if (!fromAnchor.IsNull())
                    {
                        cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                        cmd.Parameters.AddWithValue("@sourceId", otherStoreId.ToString());
                        foreach (var syncFilterParameter in syncFilterParameters)
                        {
                            cmd.Parameters.AddWithValue(syncFilterParameter.Name, syncFilterParameter.Value);
                        }

                        using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                //if (snapshotItems.Contains(values[table.PrimaryColumnName]))
                                //    continue;

                                items.Add(new SqliteSyncItem(table, DetectChangeType(values),
                                    values.Where(_ => _.Key != "__OP").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));
                                _logger?.Trace($"[{_storeId}] Incremental add or update {items.Last()}");
                            }
                        }

                        cmd.CommandText = table.IncrementalDeletesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                        cmd.Parameters.AddWithValue("@sourceId", otherStoreId.ToString());
                        using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                items.Add(new SqliteSyncItem(table, ChangeType.Delete, values));
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

        private async Task<SyncAnchor> GetLastLocalAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var c = new SqliteConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
            cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

            var version = await cmd.ExecuteScalarAsync(cancellationToken);

            if (version == null || version == DBNull.Value)
                return SyncAnchor.Null;

            return new SyncAnchor(_storeId, (long)version);
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            //await InitializeAsync();

            using var c = new SqliteConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT [REMOTE_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
            cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

            var version = await cmd.ExecuteScalarAsync(cancellationToken);

            if (version == null || version == DBNull.Value)
                return SyncAnchor.Null;

            return new SyncAnchor(otherStoreId, (long)version);
        }

        public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            return _storeId;
        }

        private static ChangeType DetectChangeType(Dictionary<string, object?> values)
        {
            if (values.TryGetValue("__OP", out var syncChangeOperation))
            {
                switch (syncChangeOperation?.ToString())
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

        private static object? GetValueFromRecord(SqliteSyncTable table, string columnName, int columnOrdinal, SqliteDataReader r)
        {
            if (r.IsDBNull(columnOrdinal))
                return null;

            if (table.RecordType == null)
                return r.GetValue(columnOrdinal);

            var property = table.RecordType.GetProperty(columnName);
            if (property != null)
                return GetValueFromRecord(r, columnOrdinal, property.PropertyType);

            property = table.RecordType.GetProperties().FirstOrDefault(_ =>
            {
                var columnAttribute = _.GetCustomAttribute<ColumnAttribute>(false);
                if (columnAttribute != null)
                {
                    return columnAttribute.Name == columnName;
                }

                return false;
            });

            if (property != null)
                return GetValueFromRecord(r, columnOrdinal, property.PropertyType);

            //fallback to getvalue
            return r.GetValue(columnOrdinal);
        }

        private static object GetValueFromRecord(SqliteDataReader r, int columnOrdinal, Type propertyType)
        {
            if (propertyType == typeof(string))
                return r.GetString(columnOrdinal);
            if (propertyType == typeof(DateTime))
                return r.GetDateTime(columnOrdinal);
            if (propertyType == typeof(int))
                return r.GetInt32(columnOrdinal);
            if (propertyType == typeof(bool))
                return r.GetBoolean(columnOrdinal);
            if (propertyType == typeof(byte))
                return r.GetByte(columnOrdinal);
            if (propertyType == typeof(char))
                return r.GetChar(columnOrdinal);
            if (propertyType == typeof(short))
                return r.GetInt16(columnOrdinal);
            if (propertyType == typeof(long))
                return r.GetInt64(columnOrdinal);
            if (propertyType == typeof(decimal))
                return r.GetDecimal(columnOrdinal);
            if (propertyType == typeof(double))
                return r.GetDouble(columnOrdinal);
            if (propertyType == typeof(float))
                return r.GetFloat(columnOrdinal);

            //fallback to getvalue
            return r.GetValue(columnOrdinal);
        }

        private static string TypeToPrintFormat(string type)
        {
            if (type == "INTEGER")
                return "%d";
            if (type == "TEXT")
                return "%s";

            return "%s";
        }

        private async Task InitializeStoreAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;

            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS __CORE_SYNC_CT 
(ID INTEGER PRIMARY KEY, TBL TEXT NOT NULL COLLATE NOCASE, OP CHAR NOT NULL, PK_{SqlitePrimaryColumnType.Int} INTEGER NULL, PK_{SqlitePrimaryColumnType.Text} TEXT NULL COLLATE NOCASE, PK_{SqlitePrimaryColumnType.Blob} BLOB NULL, SRC TEXT NULL COLLATE NOCASE)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Int}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Int})";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Text}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Text})";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Blob}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Blob})";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __CORE_SYNC_REMOTE_ANCHOR (ID TEXT NOT NULL PRIMARY KEY, LOCAL_VERSION LONG NULL, REMOTE_VERSION LONG NULL)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __CORE_SYNC_LOCAL_ID (ID TEXT NOT NULL PRIMARY KEY)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"SELECT ID FROM __CORE_SYNC_LOCAL_ID LIMIT 1";
                var localId = await cmd.ExecuteScalarAsync(cancellationToken);
                if (localId == null)
                {
                    localId = Guid.NewGuid().ToString();
                    cmd.CommandText = $"INSERT INTO __CORE_SYNC_LOCAL_ID (ID) VALUES (@localId)";
                    cmd.Parameters.Add(new SqliteParameter("@localId", localId));
                    if (1 != await cmd.ExecuteNonQueryAsync(cancellationToken))
                    {
                        throw new InvalidOperationException();
                    }
                    cmd.Parameters.Clear();
                }

                _storeId = Guid.Parse((string)localId);

                foreach (SqliteSyncTable table in Configuration.Tables)
                {
                    cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        /*
                        cid         name        type        notnull     dflt_value  pk
                        ----------  ----------  ----------  ----------  ----------  ----------
                        */
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var colName = reader.GetString(1);
                            var colType = reader.GetString(2);
                            var pk = reader.GetBoolean(5);

                            if (string.CompareOrdinal(colName, "__OP") == 0)
                            {
                                throw new NotSupportedException($"Unable to synchronize table '{table.Name}': one column has a reserved name '__OP'");
                            }

                            table.Columns.Add(colName, new SqliteColumn(colName, colType, pk));
                        }
                    }

                    if (table.Columns.Count == 0)
                    {
                        throw new InvalidOperationException($"Unable to configure table '{table}': does it exist with at least one column?");
                    }

                    if (table.Columns.Count(_ => _.Value.IsPrimaryKey) == 0)
                    {
                        throw new NotSupportedException($"Unable to configure table '{table}': no primary key defined");
                    }

                    if (table.Columns.Count(_ => _.Value.IsPrimaryKey) > 1)
                    {
                        throw new NotSupportedException($"Unable to configure table '{table}': it has more than one column as primary key");
                    }
                }
            }

            _initialized = true;
        }

        public async Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
        {
            //if (_initialized)
            //    return;

            await InitializeStoreAsync(cancellationToken);

            using var connection = new SqliteConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            foreach (var table in Configuration.Tables.Cast<SqliteSyncTable>().Where(_ => _.Columns.Any()))
            {
                //var primaryKeyColumns = table.Columns.Where(_ => _.IsPrimaryKey).ToArray();
                //var tableColumns = table.Columns.Where(_ => !_.IsPrimaryKey).ToArray();

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

            //_initialized = true;
        }

        private async Task SetupTableForFullChangeDetection(SqliteSyncTable table, SqliteCommand cmd, CancellationToken cancellationToken = default)
        {
            var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-{op}__]
    AFTER {op} ON [{table.Name}]
    FOR EACH ROW
    BEGIN
    INSERT INTO [__CORE_SYNC_CT] (TBL, OP, PK_{table.PrimaryColumnType}) VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.[{table.PrimaryColumnName}]);
    END");
            cmd.CommandText = commandTextBase("INSERT");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = commandTextBase("UPDATE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = commandTextBase("DELETE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task SetupTableForUpdatesOrDeletesOnly(SqliteSyncTable table, SqliteCommand cmd, CancellationToken cancellationToken = default)
        {
            var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-{op}__]
    AFTER {op} ON [{table.Name}]
    FOR EACH ROW
    BEGIN
    INSERT INTO [__CORE_SYNC_CT] (TBL, OP, PK_{table.PrimaryColumnType}) VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.[{table.PrimaryColumnName}]);
    END");

            cmd.CommandText = commandTextBase("UPDATE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = commandTextBase("DELETE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            //1. discover tables
            using var cmd = connection.CreateCommand();
            var listOfTables = new List<string>();
            foreach (SqliteSyncTable table in Configuration.Tables)
            {
                cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                /*
                cid         name        type        notnull     dflt_value  pk
                ----------  ----------  ----------  ----------  ----------  ----------
                */
                while (await reader.ReadAsync(cancellationToken))
                {
                    var colName = reader.GetString(1);

                    if (string.CompareOrdinal(colName, "__OP") == 0)
                    {
                        continue;
                    }

                    listOfTables.Add(colName);
                }
            }

            //2. drop ct table
            cmd.CommandText = $"DROP TABLE IF EXISTS __CORE_SYNC_CT";
            await cmd.ExecuteNonQueryAsync();

            //3. drop remote anchor table
            cmd.CommandText = $"DROP TABLE IF EXISTS __CORE_SYNC_REMOTE_ANCHOR";
            await cmd.ExecuteNonQueryAsync();

            //4. drop local anchor table
            cmd.CommandText = $"DROP TABLE IF EXISTS __CORE_SYNC_LOCAL_ID";
            await cmd.ExecuteNonQueryAsync();

            //5. drop triggers
            foreach (var tableName in listOfTables)
            {
                await DisableChangeTrackingForTable(cmd, tableName, cancellationToken);
            }
        }
        
        public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var c = new SqliteConnection(Configuration.ConnectionString);
            try
            {
                await c.OpenAsync(cancellationToken);

                using var cmd = new SqliteCommand();
                using var tr = c.BeginTransaction();
                cmd.Connection = c;
                cmd.Transaction = tr;

                cmd.CommandText = "SELECT MAX(ID) FROM __CORE_SYNC_CT";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                return new SyncVersion(version, minVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to get current/minimum version from store", ex);
            }
        }

        public async Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var c = new SqliteConnection(Configuration.ConnectionString);
            try
            {
                await c.OpenAsync();

                using var cmd = new SqliteCommand();
                using var tr = c.BeginTransaction();
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
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to apply version {minVersion} to tracking table of the store", ex);
            }
        }

        public async Task EnableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            await InitializeStoreAsync(cancellationToken);

            var table = Configuration.Tables.Cast<SqliteSyncTable>().FirstOrDefault(_ => _.Name == name);

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table with name '{name}'");
            }

            using var connection = new SqliteConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();

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
         
        public async Task DisableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            var table = Configuration.Tables.Cast<SqliteSyncTable>().FirstOrDefault(_ => _.Name == name) ?? throw new InvalidOperationException($"Unable to find table with name '{name}'");
            using var connection = new SqliteConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            await DisableChangeTrackingForTable(cmd, name, cancellationToken);
        }

        private async Task DisableChangeTrackingForTable(SqliteCommand cmd, string tableName, CancellationToken cancellationToken)
        { 
            var commandTextBase = new Func<string, string>((op) => $@"DROP TRIGGER IF EXISTS [__{tableName}_ct-{op}__]");
            cmd.CommandText = commandTextBase("INSERT");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = commandTextBase("UPDATE");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = commandTextBase("DELETE");
            await cmd.ExecuteNonQueryAsync();        
        }

    }
}