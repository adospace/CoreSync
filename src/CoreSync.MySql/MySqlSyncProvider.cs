using JetBrains.Annotations;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.MySql
{
    public class MySqlSyncProvider : ISyncProvider
    {
        private bool _initialized;
        private Guid _storeId;
        private readonly ISyncLogger? _logger;

        public MySqlSyncProvider(MySqlSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional, ISyncLogger? logger = null)
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

        public MySqlSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }
        public string[]? SyncTableNames => Configuration.Tables.Select(_ => _.Name).ToArray();

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
        {
            Validate.NotNull(changeSet, nameof(changeSet));
            Validate.NotNullAnchor(changeSet.SourceAnchor, nameof(changeSet), nameof(changeSet.SourceAnchor));
            Validate.NotNull(changeSet.TargetAnchor, nameof(changeSet), nameof(changeSet.TargetAnchor));

            await InitializeStoreAsync(cancellationToken);

            var now = DateTime.Now;
            _logger?.Info($"[{_storeId}] Begin ApplyChanges(source={changeSet.SourceAnchor}, target={changeSet.TargetAnchor}, {changeSet.Items.Count} items)");

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            using var tr = await connection.BeginTransactionAsync(cancellationToken);
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = "SELECT MAX(`id`) FROM `__core_sync_ct`";
                cmd.Parameters.Clear();
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(`id`) FROM `__core_sync_ct`";
                cmd.Parameters.Clear();
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                var remainingItems = changeSet.Items.ToList();
                int pass = 0;

                while (remainingItems.Count > 0)
                {
                    pass++;
                    var failedItems = new List<SyncItem>();
                    int appliedInPass = 0;

                    foreach (var item in remainingItems)
                    {
                        var table = (MySqlSyncTable?)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);
                        if (table == null)
                        {
                            continue;
                        }

                        bool syncForceWrite = false;
                        var itemChangeType = item.ChangeType;
                        bool itemHandled = false;

                    retryWrite:
                        cmd.Parameters.Clear();
                        table.SetupCommand(cmd, itemChangeType, item.Values);

                        int affectedRows = 0;
                        try
                        {
                            if (!syncForceWrite &&
                                (itemChangeType == ChangeType.Update || itemChangeType == ChangeType.Delete) &&
                                await HasConflictAsync(connection, tr, table, item, changeSet.TargetAnchor.Version, cancellationToken))
                            {
                                affectedRows = 0;
                            }
                            else
                            {
                                await ExecuteSavepointCommandAsync(connection, tr, "SAVEPOINT sync_sp", cancellationToken);
                                affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);
                                await ExecuteSavepointCommandAsync(connection, tr, "RELEASE SAVEPOINT sync_sp", cancellationToken);

                                if (affectedRows > 0)
                                {
                                    _logger?.Trace($"[{_storeId}] Successfully applied {item}");
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (MySqlException ex) when (ex.Number == 1451 || ex.Number == 1452)
                        {
                            await ExecuteSavepointCommandAsync(connection, tr, "ROLLBACK TO SAVEPOINT sync_sp", cancellationToken);
                            _logger?.Warning($"[{_storeId}] FK violation applying {itemChangeType} on {item}, skipping: {ex.Message}");
                            affectedRows = 0;
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error($"Unable to {itemChangeType} item {item} to store for table {table}.{Environment.NewLine}{ex}{Environment.NewLine}Generated SQL:{Environment.NewLine}{cmd.CommandText}");
                            throw new SynchronizationException($"Unable to {itemChangeType} item {item} to store for table {table}", ex);
                        }

                        if (affectedRows == 0)
                        {
                            if (itemChangeType == ChangeType.Insert)
                            {
                                cmd.CommandText = table.SelectExistingQuery;
                                cmd.Parameters.Clear();
                                var valueItem = item.Values[table.PrimaryColumnName];
                                cmd.Parameters.Add(new MySqlParameter("@PrimaryColumnParameter", table.ConvertPrimaryKeyValue(valueItem.Value)));
                                if (1 == await cmd.ExecuteLongScalarAsync(cancellationToken) && !syncForceWrite)
                                {
                                    itemChangeType = ChangeType.Update;
                                    goto retryWrite;
                                }

                                _logger?.Warning($"Unable to {item}: much probably there is a foreign key constraint issue logged before (pass {pass})");
                            }
                            else if (itemChangeType == ChangeType.Update || itemChangeType == ChangeType.Delete)
                            {
                                if (syncForceWrite)
                                {
                                    if (itemChangeType == ChangeType.Delete)
                                    {
                                        _logger?.Trace($"[{_storeId}] Insert on delete conflict occurred for {item}");
                                        itemHandled = true;
                                    }
                                    else
                                    {
                                        _logger?.Trace($"[{_storeId}] Insert on delete conflict occurred for {item}");
                                        itemChangeType = ChangeType.Insert;
                                        goto retryWrite;
                                    }
                                }
                                else
                                {
                                    var res = onConflictFunc?.Invoke(item);
                                    if (res.HasValue && res.Value == ConflictResolution.ForceWrite)
                                    {
                                        _logger?.Trace($"[{_storeId}] Force write on conflict occurred for {item}");
                                        syncForceWrite = true;
                                        goto retryWrite;
                                    }

                                    _logger?.Warning($"[{_storeId}] Skip conflict for {item}");
                                    itemHandled = true;
                                }
                            }
                        }

                        if (affectedRows > 0)
                        {
                            itemHandled = true;
                            appliedInPass++;

                            cmd.CommandText = "SELECT MAX(`id`) FROM `__core_sync_ct`";
                            cmd.Parameters.Clear();
                            var currentVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                            cmd.CommandText = "UPDATE `__core_sync_ct` SET `src` = @src WHERE `id` = @id";
                            cmd.Parameters.Clear();
                            cmd.Parameters.Add(new MySqlParameter("@src", changeSet.SourceAnchor.StoreId.ToString()));
                            cmd.Parameters.Add(new MySqlParameter("@id", currentVersion));
                            await cmd.ExecuteNonQueryAsync(cancellationToken);
                        }

                        if (!itemHandled)
                        {
                            failedItems.Add(item);
                        }
                    }

                    if (failedItems.Count == 0 || appliedInPass == 0)
                    {
                        if (failedItems.Count > 0)
                        {
                            _logger?.Warning($"[{_storeId}] {failedItems.Count} item(s) could not be applied after {pass} pass(es) (possible unresolvable foreign key constraint)");
                        }

                        break;
                    }

                    _logger?.Info($"[{_storeId}] Pass {pass}: applied {appliedInPass} item(s), retrying {failedItems.Count} remaining item(s)");
                    remainingItems = failedItems;
                }

                cmd.CommandText = "UPDATE `__core_sync_remote_anchor` SET `remote_version` = @remoteVersion WHERE `id` = @id";
                cmd.Parameters.Clear();
                cmd.Parameters.Add(new MySqlParameter("@remoteVersion", changeSet.SourceAnchor.Version));
                cmd.Parameters.Add(new MySqlParameter("@id", changeSet.SourceAnchor.StoreId.ToString()));

                if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                {
                    cmd.CommandText = "INSERT INTO `__core_sync_remote_anchor` (`id`, `remote_version`) VALUES (@id, @remoteVersion)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new MySqlParameter("@id", changeSet.SourceAnchor.StoreId.ToString()));
                    cmd.Parameters.Add(new MySqlParameter("@remoteVersion", changeSet.SourceAnchor.Version));
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await tr.CommitAsync(cancellationToken);

                var resAnchor = new SyncAnchor(_storeId, version);
                _logger?.Info($"[{_storeId}] Completed ApplyChanges(resAnchor={resAnchor}) in {(DateTime.Now - now).TotalMilliseconds}ms");

                return resAnchor;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[{_storeId}] Unable to complete the apply changes:{Environment.NewLine}{ex}");
                await tr.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            using var tr = await connection.BeginTransactionAsync(cancellationToken);
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = "UPDATE `__core_sync_remote_anchor` SET `local_version` = @version WHERE `id` = @id";
                cmd.Parameters.Add(new MySqlParameter("@version", version));
                cmd.Parameters.Add(new MySqlParameter("@id", otherStoreId.ToString()));

                if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                {
                    cmd.CommandText = "INSERT INTO `__core_sync_remote_anchor` (`id`, `local_version`) VALUES (@id, @version)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new MySqlParameter("@id", otherStoreId.ToString()));
                    cmd.Parameters.Add(new MySqlParameter("@version", version));
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await tr.CommitAsync(cancellationToken);
                _logger?.Trace($"[{_storeId}] Save version {version} for store {otherStoreId}");
            }
            catch
            {
                await tr.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string[]? tables = null, CancellationToken cancellationToken = default)
        {
            syncFilterParameters ??= [];

            var tablesToSync = Configuration.ResolveTableFilter(tables);
            var fromAnchor = await GetLastLocalAnchorForStoreAsync(otherStoreId, cancellationToken);
            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin GetChanges(from={otherStoreId}, syncDirection={syncDirection}, fromVersion={fromAnchor})");

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            using var tr = await connection.BeginTransactionAsync(cancellationToken);
            cmd.Transaction = tr;

            var items = new List<MySqlSyncItem>();

            try
            {
                cmd.CommandText = "SELECT MAX(`id`) FROM `__core_sync_ct`";
                cmd.Parameters.Clear();
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(`id`) FROM `__core_sync_ct`";
                cmd.Parameters.Clear();
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                // MIN(`id`) is the oldest change still journalled, so the oldest anchor that can still be
                // served is the one immediately before it.
                if (!fromAnchor.IsNull() && fromAnchor.Version < minVersion - 1)
                {
                    throw new SyncAnchorTooOldException(fromAnchor.Version, minVersion - 1);
                }

                foreach (var table in tablesToSync.Cast<MySqlSyncTable>().Where(_ => _.Columns.Any()))
                {
                    if (table.SyncDirection != SyncDirection.UploadAndDownload && table.SyncDirection != syncDirection)
                    {
                        continue;
                    }

                    if (fromAnchor.IsNull() && !table.SkipInitialSnapshot)
                    {
                        if (string.IsNullOrWhiteSpace(table.InitialSnapshotQuery))
                        {
                            throw new InvalidOperationException("InitialSnapshotQuery not specified for table");
                        }

                        cmd.CommandText = table.InitialSnapshotQuery;
                        cmd.Parameters.Clear();
                        AddFilterParameters(cmd, syncFilterParameters);

                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var values = Enumerable.Range(0, reader.FieldCount)
                                .ToDictionary(_ => reader.GetName(_), _ => GetValueFromRecord(table, reader.GetName(_), _, reader));
                            items.Add(new MySqlSyncItem(table, ChangeType.Insert, values));
                            _logger?.Trace($"[{_storeId}] Initial snapshot {items.Last()}");
                        }
                    }

                    if (!fromAnchor.IsNull())
                    {
                        cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                        cmd.Parameters.Clear();
                        AddFilterParameters(cmd, syncFilterParameters);
                        cmd.Parameters.Add(new MySqlParameter("@version", fromAnchor.Version));
                        cmd.Parameters.Add(new MySqlParameter("@tableName", table.Name));
                        cmd.Parameters.Add(new MySqlParameter("@sourceId", otherStoreId.ToString()));

                        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, reader.FieldCount)
                                    .ToDictionary(_ => reader.GetName(_), _ => GetValueFromRecord(table, reader.GetName(_), _, reader));

                                items.Add(new MySqlSyncItem(table, DetectChangeType(values),
                                    values.Where(_ => _.Key != "__op").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));
                                _logger?.Trace($"[{_storeId}] Incremental add or update {items.Last()}");
                            }
                        }

                        cmd.CommandText = table.IncrementalDeletesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(new MySqlParameter("@tableName", table.Name));
                        cmd.Parameters.Add(new MySqlParameter("@version", fromAnchor.Version));
                        cmd.Parameters.Add(new MySqlParameter("@sourceId", otherStoreId.ToString()));

                        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, reader.FieldCount)
                                    .ToDictionary(_ => reader.GetName(_), _ => GetValueFromRecord(table, reader.GetName(_), _, reader));
                                items.Add(new MySqlSyncItem(table, ChangeType.Delete, values));
                                _logger?.Trace($"[{_storeId}] Incremental delete {items.Last()}");
                            }
                        }
                    }
                }

                await tr.CommitAsync(cancellationToken);

                var resChangeSet = new SyncChangeSet(new SyncAnchor(_storeId, version), await GetLastRemoteAnchorForStoreAsync(otherStoreId, cancellationToken), items);
                _logger?.Info($"[{_storeId}] Completed GetChanges(to={version}, {items.Count} items) in {(DateTime.Now - now).TotalMilliseconds}ms");

                return resChangeSet;
            }
            catch (Exception ex)
            {
                _logger?.Error($"[{_storeId}] Unable to complete GetChanges(from={otherStoreId}):{Environment.NewLine}{ex}");
                try { await tr.RollbackAsync(cancellationToken); } catch { }
                throw;
            }
        }

        private async Task<SyncAnchor> GetLastLocalAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT `local_version` FROM `__core_sync_remote_anchor` WHERE `id` = @id";
            cmd.Parameters.Add(new MySqlParameter("@id", otherStoreId.ToString()));

            var version = await cmd.ExecuteScalarAsync(cancellationToken);
            if (version == null || version == DBNull.Value)
            {
                return SyncAnchor.Null;
            }

            return new SyncAnchor(_storeId, Convert.ToInt64(version));
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT `remote_version` FROM `__core_sync_remote_anchor` WHERE `id` = @id";
            cmd.Parameters.Add(new MySqlParameter("@id", otherStoreId.ToString()));

            var version = await cmd.ExecuteScalarAsync(cancellationToken);
            if (version == null || version == DBNull.Value)
            {
                return SyncAnchor.Null;
            }

            return new SyncAnchor(otherStoreId, Convert.ToInt64(version));
        }

        public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);
            return _storeId;
        }

        private static ChangeType DetectChangeType(Dictionary<string, object?> values)
        {
            if (values.TryGetValue("__op", out var syncChangeOperation))
            {
                return syncChangeOperation?.ToString() switch
                {
                    "I" => ChangeType.Insert,
                    "U" => ChangeType.Update,
                    "D" => ChangeType.Delete,
                    _ => throw new NotSupportedException(),
                };
            }

            return ChangeType.Insert;
        }

        private static object? GetValueFromRecord(MySqlSyncTable table, string columnName, int columnOrdinal, MySqlDataReader reader)
        {
            if (reader.IsDBNull(columnOrdinal))
            {
                return null;
            }

            if (table.Columns.TryGetValue(columnName, out var column) && column.IsGuidLike)
            {
                var value = reader.GetValue(columnOrdinal);
                if (value is Guid guidValue)
                {
                    return guidValue.ToString("D").ToUpperInvariant();
                }

                if (Guid.TryParse(value.ToString(), out var parsedGuid))
                {
                    return parsedGuid.ToString("D").ToUpperInvariant();
                }
            }

            if (table.RecordType == null)
            {
                return reader.GetValue(columnOrdinal);
            }

            var property = table.RecordType.GetProperty(columnName);
            if (property != null)
            {
                return GetValueFromRecord(reader, columnOrdinal, property.PropertyType);
            }

            property = table.RecordType.GetProperties().FirstOrDefault(_ =>
            {
                var columnAttribute = _.GetCustomAttribute<ColumnAttribute>(false);
                return columnAttribute?.Name == columnName;
            });

            if (property != null)
            {
                return GetValueFromRecord(reader, columnOrdinal, property.PropertyType);
            }

            return reader.GetValue(columnOrdinal);
        }

        private static object GetValueFromRecord(MySqlDataReader reader, int columnOrdinal, Type propertyType)
        {
            if (propertyType == typeof(string))
            {
                return reader.GetString(columnOrdinal);
            }

            if (propertyType == typeof(DateTime))
            {
                var value = reader.GetValue(columnOrdinal);
                return value is DateTime dateTime
                    ? dateTime
                    : DateTime.Parse(value.ToString()!);
            }

            if (propertyType == typeof(int))
            {
                return reader.GetInt32(columnOrdinal);
            }

            if (propertyType == typeof(bool))
            {
                return reader.GetBoolean(columnOrdinal);
            }

            if (propertyType == typeof(byte))
            {
                return reader.GetByte(columnOrdinal);
            }

            if (propertyType == typeof(char))
            {
                return reader.GetChar(columnOrdinal);
            }

            if (propertyType == typeof(short))
            {
                return reader.GetInt16(columnOrdinal);
            }

            if (propertyType == typeof(long))
            {
                return reader.GetInt64(columnOrdinal);
            }

            if (propertyType == typeof(decimal))
            {
                return reader.GetDecimal(columnOrdinal);
            }

            if (propertyType == typeof(double))
            {
                return reader.GetDouble(columnOrdinal);
            }

            if (propertyType == typeof(float))
            {
                return reader.GetFloat(columnOrdinal);
            }

            if (propertyType == typeof(Guid))
            {
                var value = reader.GetValue(columnOrdinal);
                if (value is Guid guidValue)
                {
                    return guidValue;
                }

                return Guid.Parse(value.ToString()!);
            }

            return reader.GetValue(columnOrdinal);
        }

        private async Task InitializeStoreAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
            {
                return;
            }

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `__core_sync_ct` (
`id` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
`tbl` TEXT NOT NULL,
`op` CHAR(1) NOT NULL,
`pk_integer` BIGINT NULL,
`pk_text` TEXT NULL,
`pk_blob` BLOB NULL,
`src` TEXT NULL
)";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            await CreateIndexIfMissingAsync(connection, "__core_sync_ct_pk_integer_index", "CREATE INDEX `__core_sync_ct_pk_integer_index` ON `__core_sync_ct`(`pk_integer`)", cancellationToken);
            await CreateIndexIfMissingAsync(connection, "__core_sync_ct_pk_text_index", "CREATE INDEX `__core_sync_ct_pk_text_index` ON `__core_sync_ct`(`pk_text`(255))", cancellationToken);
            await CreateIndexIfMissingAsync(connection, "__core_sync_ct_pk_blob_index", "CREATE INDEX `__core_sync_ct_pk_blob_index` ON `__core_sync_ct`(`pk_blob`(255))", cancellationToken);

            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `__core_sync_remote_anchor` (
`id` CHAR(36) NOT NULL PRIMARY KEY,
`local_version` BIGINT NULL,
`remote_version` BIGINT NULL
)";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS `__core_sync_local_id` (
`id` CHAR(36) NOT NULL PRIMARY KEY
)";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "SELECT `id` FROM `__core_sync_local_id` LIMIT 1";
            var localId = await cmd.ExecuteScalarAsync(cancellationToken);
            if (localId == null)
            {
                localId = Guid.NewGuid().ToString();
                cmd.CommandText = "INSERT INTO `__core_sync_local_id` (`id`) VALUES (@id)";
                cmd.Parameters.Clear();
                cmd.Parameters.Add(new MySqlParameter("@id", localId));
                if (1 != await cmd.ExecuteNonQueryAsync(cancellationToken))
                {
                    throw new InvalidOperationException();
                }

                cmd.Parameters.Clear();
            }

            _storeId = Guid.Parse(localId.ToString()!);

            foreach (var table in Configuration.Tables.Cast<MySqlSyncTable>())
            {
                table.Columns.Clear();

                cmd.CommandText = @"
SELECT c.COLUMN_NAME, c.DATA_TYPE, c.COLUMN_TYPE,
       CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN TRUE ELSE FALSE END AS IS_PRIMARY_KEY
FROM INFORMATION_SCHEMA.COLUMNS c
LEFT JOIN (
    SELECT kcu.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
      ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
     AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
     AND tc.TABLE_NAME = kcu.TABLE_NAME
    WHERE tc.TABLE_SCHEMA = DATABASE()
      AND tc.TABLE_NAME = @tableName
      AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
WHERE c.TABLE_SCHEMA = DATABASE()
  AND c.TABLE_NAME = @tableName
ORDER BY c.ORDINAL_POSITION";
                cmd.Parameters.Clear();
                cmd.Parameters.Add(new MySqlParameter("@tableName", table.Name));

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var colName = reader.GetString(0);
                    var dataType = reader.GetString(1);
                    var columnType = reader.GetString(2);
                    var pk = reader.GetBoolean(3);

                    if (string.CompareOrdinal(colName, "__op") == 0)
                    {
                        throw new NotSupportedException($"Unable to synchronize table '{table.Name}': one column has a reserved name '__op'");
                    }

                    table.Columns[colName] = new MySqlColumn(colName, dataType, columnType, pk);
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

            _initialized = true;
        }

        public async Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            foreach (var table in Configuration.Tables.Cast<MySqlSyncTable>().Where(_ => _.Columns.Any()))
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

        private async Task SetupTableForFullChangeDetection(MySqlSyncTable table, MySqlCommand cmd, CancellationToken cancellationToken = default)
        {
            foreach (var op in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                await CreateTriggerAsync(table, cmd, op, cancellationToken);
            }
        }

        private async Task SetupTableForUpdatesOrDeletesOnly(MySqlSyncTable table, MySqlCommand cmd, CancellationToken cancellationToken = default)
        {
            foreach (var op in new[] { "UPDATE", "DELETE" })
            {
                await CreateTriggerAsync(table, cmd, op, cancellationToken);
            }
        }

        private async Task CreateTriggerAsync(MySqlSyncTable table, MySqlCommand cmd, string op, CancellationToken cancellationToken)
        {
            var triggerName = $"__{table.Name}_ct_{op.ToLowerInvariant()}__";
            var rowReference = op == "DELETE" ? "OLD" : "NEW";

            cmd.CommandText = $"DROP TRIGGER IF EXISTS `{triggerName}`";
            cmd.Parameters.Clear();
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText =
                $@"CREATE TRIGGER `{triggerName}`
AFTER {op} ON `{table.Name}`
FOR EACH ROW
INSERT INTO `__core_sync_ct` (`tbl`, `op`, `pk_{table.PrimaryColumnType.ToString().ToLowerInvariant()}`)
VALUES ('{table.Name}', '{op[0]}', {rowReference}.`{table.PrimaryColumnName}`)";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS `__core_sync_ct`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "DROP TABLE IF EXISTS `__core_sync_remote_anchor`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = "DROP TABLE IF EXISTS `__core_sync_local_id`";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            foreach (var table in Configuration.Tables.Cast<MySqlSyncTable>())
            {
                await DisableChangeTrackingForTable(cmd, table.Name, cancellationToken);
            }
        }

        public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);

                using var cmd = connection.CreateCommand();
                using var tr = await connection.BeginTransactionAsync(cancellationToken);
                cmd.Transaction = tr;

                cmd.CommandText = "SELECT MAX(`id`) FROM `__core_sync_ct`";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(`id`) FROM `__core_sync_ct`";
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                return new SyncVersion(version, minVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to get current/minimum version from store", ex);
            }
        }

        public async Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);

                using var cmd = connection.CreateCommand();
                using var tr = await connection.BeginTransactionAsync(cancellationToken);
                cmd.Transaction = tr;

                try
                {
                    cmd.CommandText = "DELETE FROM `__core_sync_ct` WHERE `id` < @minVersion";
                    cmd.Parameters.Add(new MySqlParameter("@minVersion", minVersion));
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    cmd.CommandText = "SELECT MAX(`id`) FROM `__core_sync_ct`";
                    cmd.Parameters.Clear();
                    var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                    cmd.CommandText = "SELECT MIN(`id`) FROM `__core_sync_ct`";
                    var newMinVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                    await tr.CommitAsync(cancellationToken);
                    return new SyncVersion(version, newMinVersion);
                }
                catch
                {
                    await tr.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unable to apply retention policy to store", ex);
            }
        }

        public async Task EnableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            await InitializeStoreAsync(cancellationToken);

            var table = Configuration.Tables.Cast<MySqlSyncTable>().FirstOrDefault(_ => _.Name == name);
            if (table == null)
            {
                throw new InvalidOperationException($"Table '{name}' is not in sync configuration");
            }

            using var connection = new MySqlConnection(Configuration.ConnectionString);
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
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            using var connection = new MySqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            await DisableChangeTrackingForTable(cmd, name, cancellationToken);
        }

        private async Task DisableChangeTrackingForTable(MySqlCommand cmd, string tableName, CancellationToken cancellationToken)
        {
            foreach (var op in new[] { "insert", "update", "delete" })
            {
                cmd.CommandText = $"DROP TRIGGER IF EXISTS `__{tableName}_ct_{op}__`";
                cmd.Parameters.Clear();
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private static void AddFilterParameters(MySqlCommand cmd, SyncFilterParameter[] syncFilterParameters)
        {
            foreach (var syncFilterParameter in syncFilterParameters)
            {
                cmd.Parameters.Add(new MySqlParameter(syncFilterParameter.Name, syncFilterParameter.Value));
            }
        }

        private static async Task ExecuteSavepointCommandAsync(MySqlConnection connection, MySqlTransaction transaction, string commandText, CancellationToken cancellationToken)
        {
            using var savepointCmd = connection.CreateCommand();
            savepointCmd.Transaction = transaction;
            savepointCmd.CommandText = commandText;
            await savepointCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<bool> HasConflictAsync(MySqlConnection connection, MySqlTransaction transaction, MySqlSyncTable table, SyncItem item, long targetVersion, CancellationToken cancellationToken)
        {
            if (!item.Values.TryGetValue(table.PrimaryColumnName, out var primaryValue))
            {
                return false;
            }

            using var conflictCmd = connection.CreateCommand();
            conflictCmd.Transaction = transaction;
            conflictCmd.CommandText =
                $@"SELECT MAX(`id`) FROM `__core_sync_ct`
WHERE `pk_{table.PrimaryColumnType.ToString().ToLowerInvariant()}` = @primaryKey
  AND `tbl` = @tableName";
            conflictCmd.Parameters.Add(new MySqlParameter("@primaryKey", table.ConvertPrimaryKeyValue(primaryValue.Value)));
            conflictCmd.Parameters.Add(new MySqlParameter("@tableName", table.Name));

            return await conflictCmd.ExecuteLongScalarAsync(cancellationToken) > targetVersion;
        }

        private static async Task CreateIndexIfMissingAsync(MySqlConnection connection, string indexName, string createSql, CancellationToken cancellationToken)
        {
            using var existsCmd = connection.CreateCommand();
            existsCmd.CommandText = @"
SELECT COUNT(*)
FROM INFORMATION_SCHEMA.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = '__core_sync_ct'
  AND INDEX_NAME = @indexName";
            existsCmd.Parameters.Add(new MySqlParameter("@indexName", indexName));
            if (await existsCmd.ExecuteLongScalarAsync(cancellationToken) > 0)
            {
                return;
            }

            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = createSql;
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
