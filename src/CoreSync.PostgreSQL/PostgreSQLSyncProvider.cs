using JetBrains.Annotations;
using Npgsql;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.PostgreSQL
{
    public class PostgreSQLSyncProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;
        private readonly ISyncLogger? _logger;

        public PostgreSQLSyncProvider(PostgreSQLSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional, ISyncLogger? logger = null)
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

        public PostgreSQLSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
        {
            Validate.NotNull(changeSet, nameof(changeSet));
            Validate.NotNull(changeSet.SourceAnchor, nameof(changeSet.SourceAnchor));
            Validate.NotNull(changeSet.TargetAnchor, nameof(changeSet.TargetAnchor));

            await InitializeStoreAsync(cancellationToken);

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin ApplyChanges(source={changeSet.SourceAnchor}, target={changeSet.TargetAnchor}, {changeSet.Items.Count} items)");

            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new NpgsqlCommand();
            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = "SELECT MAX(id) FROM __core_sync_ct";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(id) FROM __core_sync_ct";
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                foreach (var item in changeSet.Items)
                {
                    var table = (PostgreSQLSyncTable)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);
                    if (table == null)
                    {
                        continue;
                    }

                    bool syncForceWrite = false;
                    var itemChangeType = item.ChangeType;

                retryWrite:
                    cmd.Parameters.Clear();

                    table.SetupCommand(cmd, itemChangeType, item.Values);

                    cmd.Parameters.Add(new NpgsqlParameter { Value = syncForceWrite });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.TargetAnchor.Version });

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
                        _logger?.Error($"Unable to {itemChangeType} item {item} to store for table {table}.{Environment.NewLine}{ex}{Environment.NewLine}Generated SQL:{Environment.NewLine}{cmd.CommandText}");
                    }

                    if (affectedRows == 0)
                    {
                        if (itemChangeType == ChangeType.Insert)
                        {
                            cmd.CommandText = table.SelectExistingQuery;
                            cmd.Parameters.Clear();
                            var valueItem = item.Values[table.PrimaryColumnName];
                            cmd.Parameters.Add(new NpgsqlParameter { Value = valueItem.Value ?? DBNull.Value });
                            if (1 == await cmd.ExecuteLongScalarAsync(cancellationToken) && !syncForceWrite)
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
                                    _logger?.Trace($"[{_storeId}] Insert on delete conflict occurred for {item}");
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
                        cmd.CommandText = "SELECT MAX(id) FROM __core_sync_ct";
                        cmd.Parameters.Clear();
                        var currentVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                        cmd.CommandText = "UPDATE __core_sync_ct SET src = $1 WHERE id = $2";
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.SourceAnchor.StoreId.ToString() });
                        cmd.Parameters.Add(new NpgsqlParameter { Value = currentVersion });

                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                cmd.CommandText = $"UPDATE __core_sync_remote_anchor SET remote_version = $1 WHERE id = $2";
                cmd.Parameters.Clear();
                cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.SourceAnchor.Version });
                cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.SourceAnchor.StoreId.ToString() });

                if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                {
                    cmd.CommandText = "INSERT INTO __core_sync_remote_anchor (id, remote_version) VALUES ($1, $2)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.SourceAnchor.StoreId.ToString() });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = changeSet.SourceAnchor.Version });

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
            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new NpgsqlCommand();
            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = $"UPDATE __core_sync_remote_anchor SET local_version = $1 WHERE id = $2";
                cmd.Parameters.Add(new NpgsqlParameter { Value = version });
                cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });

                if (0 == await cmd.ExecuteNonQueryAsync(cancellationToken))
                {
                    cmd.CommandText = "INSERT INTO __core_sync_remote_anchor (id, local_version) VALUES ($1, $2)";
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });
                    cmd.Parameters.Add(new NpgsqlParameter { Value = version });

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

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin GetChanges(from={otherStoreId}, syncDirection={syncDirection}, fromVersion={fromAnchor})");

            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new NpgsqlCommand();
            var items = new List<PostgreSQLSyncItem>();

            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;

            try
            {
                cmd.CommandText = "SELECT MAX(id) FROM __core_sync_ct";
                cmd.Parameters.Clear();
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(id) FROM __core_sync_ct";
                cmd.Parameters.Clear();
                var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                if (!fromAnchor.IsNull() && fromAnchor.Version < minVersion - 1)
                    throw new InvalidOperationException($"Unable to get changes, version of data requested ({fromAnchor}) is too old (min valid version {minVersion})");

                foreach (var table in Configuration.Tables.Cast<PostgreSQLSyncTable>().Where(_ => _.Columns.Any()))
                {
                    if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                        table.SyncDirection != syncDirection)
                        continue;

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
                            cmd.Parameters.Add(new NpgsqlParameter { Value = syncFilterParameter.Value });
                        }

                        using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                        while (await r.ReadAsync(cancellationToken))
                        {
                            var values = Enumerable.Range(0, r.FieldCount)
                                .ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                            items.Add(new PostgreSQLSyncItem(table, ChangeType.Insert, values));
                            _logger?.Trace($"[{_storeId}] Initial snapshot {items.Last()}");
                        }
                    }

                    if (!fromAnchor.IsNull())
                    {
                        cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(new NpgsqlParameter { Value = fromAnchor.Version });
                        cmd.Parameters.Add(new NpgsqlParameter { Value = table.Name });
                        cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });
                        foreach (var syncFilterParameter in syncFilterParameters)
                        {
                            cmd.Parameters.Add(new NpgsqlParameter { Value = syncFilterParameter.Value });
                        }

                        using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));

                                items.Add(new PostgreSQLSyncItem(table, DetectChangeType(values),
                                    values.Where(_ => _.Key != "__op").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));
                                _logger?.Trace($"[{_storeId}] Incremental add or update {items.Last()}");
                            }
                        }

                        cmd.CommandText = table.IncrementalDeletesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.Add(new NpgsqlParameter { Value = table.Name });
                        cmd.Parameters.Add(new NpgsqlParameter { Value = fromAnchor.Version });
                        cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });
                        using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                items.Add(new PostgreSQLSyncItem(table, ChangeType.Delete, values));
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

            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT local_version FROM __core_sync_remote_anchor WHERE id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });

            var version = await cmd.ExecuteScalarAsync(cancellationToken);

            if (version == null || version == DBNull.Value)
                return SyncAnchor.Null;

            return new SyncAnchor(_storeId, (long)version);
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT remote_version FROM __core_sync_remote_anchor WHERE id = $1";
            cmd.Parameters.Add(new NpgsqlParameter { Value = otherStoreId.ToString() });

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
            if (values.TryGetValue("__op", out var syncChangeOperation))
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

        private static object? GetValueFromRecord(PostgreSQLSyncTable table, string columnName, int columnOrdinal, NpgsqlDataReader r)
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

        private static object GetValueFromRecord(NpgsqlDataReader r, int columnOrdinal, Type propertyType)
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

        private async Task InitializeStoreAsync(CancellationToken cancellationToken = default)
        {
            if (_initialized)
                return;

            using (var connection = new NpgsqlConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS __core_sync_ct 
(id BIGSERIAL PRIMARY KEY, tbl TEXT NOT NULL, op CHAR(1) NOT NULL, pk_integer BIGINT NULL, pk_text TEXT NULL, pk_bytea BYTEA NULL, src TEXT NULL)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __core_sync_ct_pk_integer_index ON __core_sync_ct(pk_integer)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __core_sync_ct_pk_text_index ON __core_sync_ct(pk_text)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __core_sync_ct_pk_bytea_index ON __core_sync_ct(pk_bytea)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __core_sync_remote_anchor (id TEXT NOT NULL PRIMARY KEY, local_version BIGINT NULL, remote_version BIGINT NULL)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __core_sync_local_id (id TEXT NOT NULL PRIMARY KEY)";
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                cmd.CommandText = $"SELECT id FROM __core_sync_local_id LIMIT 1";
                var localId = await cmd.ExecuteScalarAsync(cancellationToken);
                if (localId == null)
                {
                    localId = Guid.NewGuid().ToString();
                    cmd.CommandText = $"INSERT INTO __core_sync_local_id (id) VALUES ($1)";
                    cmd.Parameters.Add(new NpgsqlParameter { Value = localId });
                    if (1 != await cmd.ExecuteNonQueryAsync(cancellationToken))
                    {
                        throw new InvalidOperationException();
                    }
                    cmd.Parameters.Clear();
                }

                _storeId = Guid.Parse((string)localId);

                foreach (PostgreSQLSyncTable table in Configuration.Tables)
                {
                    cmd.CommandText = $@"SELECT c.column_name, c.data_type, 
                        CASE WHEN pk.column_name IS NOT NULL THEN true ELSE false END as is_primary_key
                        FROM information_schema.columns c
                        LEFT JOIN (
                            SELECT kcu.column_name 
                            FROM information_schema.table_constraints tc
                            JOIN information_schema.key_column_usage kcu 
                            ON tc.constraint_name = kcu.constraint_name
                            WHERE tc.table_name = $1 AND tc.constraint_type = 'PRIMARY KEY'
                        ) pk ON c.column_name = pk.column_name
                        WHERE c.table_name = $1
                        ORDER BY c.ordinal_position";
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new NpgsqlParameter { Value = table.Name });
                    using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var colName = reader.GetString(0);
                            var colType = reader.GetString(1);
                            var pk = reader.GetBoolean(2);

                            if (string.CompareOrdinal(colName, "__op") == 0)
                            {
                                throw new NotSupportedException($"Unable to synchronize table '{table.Name}': one column has a reserved name '__op'");
                            }

                            table.Columns.Add(colName, new PostgreSQLColumn(colName, colType, pk));
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
            await InitializeStoreAsync(cancellationToken);

            using var connection = new NpgsqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            foreach (var table in Configuration.Tables.Cast<PostgreSQLSyncTable>().Where(_ => _.Columns.Any()))
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

        private async Task SetupTableForFullChangeDetection(PostgreSQLSyncTable table, NpgsqlCommand cmd, CancellationToken cancellationToken = default)
        {
            var createTriggerBase = new Func<string, string>((op) => $@"
DROP TRIGGER IF EXISTS __{table.Name}_ct_{op.ToLower()}__ ON ""{table.Name}"";
CREATE OR REPLACE FUNCTION __{table.Name}_ct_{op.ToLower()}__()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO __core_sync_ct (tbl, op, pk_{table.PrimaryColumnType.ToString().ToLower()}) 
    VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.""{table.PrimaryColumnName}"");
    RETURN {(op == "DELETE" ? "OLD" : "NEW")};
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER __{table.Name}_ct_{op.ToLower()}__
    AFTER {op} ON ""{table.Name}""
    FOR EACH ROW
    EXECUTE FUNCTION __{table.Name}_ct_{op.ToLower()}__();");

            cmd.CommandText = createTriggerBase("INSERT");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = createTriggerBase("UPDATE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = createTriggerBase("DELETE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task SetupTableForUpdatesOrDeletesOnly(PostgreSQLSyncTable table, NpgsqlCommand cmd, CancellationToken cancellationToken = default)
        {
            var createTriggerBase = new Func<string, string>((op) => $@"
DROP TRIGGER IF EXISTS __{table.Name}_ct_{op.ToLower()}__ ON ""{table.Name}"";
CREATE OR REPLACE FUNCTION __{table.Name}_ct_{op.ToLower()}__()
RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO __core_sync_ct (tbl, op, pk_{table.PrimaryColumnType.ToString().ToLower()}) 
    VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.""{table.PrimaryColumnName}"");
    RETURN {(op == "DELETE" ? "OLD" : "NEW")};
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER __{table.Name}_ct_{op.ToLower()}__
    AFTER {op} ON ""{table.Name}""
    FOR EACH ROW
    EXECUTE FUNCTION __{table.Name}_ct_{op.ToLower()}__();");

            cmd.CommandText = createTriggerBase("UPDATE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            cmd.CommandText = createTriggerBase("DELETE");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        {
            using var connection = new NpgsqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();

            //1. drop ct table
            cmd.CommandText = $"DROP TABLE IF EXISTS __core_sync_ct";
            await cmd.ExecuteNonQueryAsync();

            //2. drop remote anchor table
            cmd.CommandText = $"DROP TABLE IF EXISTS __core_sync_remote_anchor";
            await cmd.ExecuteNonQueryAsync();

            //3. drop local anchor table
            cmd.CommandText = $"DROP TABLE IF EXISTS __core_sync_local_id";
            await cmd.ExecuteNonQueryAsync();

            //4. drop triggers and functions
            foreach (var table in Configuration.Tables.Cast<PostgreSQLSyncTable>())
            {
                await DisableChangeTrackingForTable(cmd, table.Name, cancellationToken);
            }
        }
        
        public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            try
            {
                await c.OpenAsync(cancellationToken);

                using var cmd = new NpgsqlCommand();
                using var tr = c.BeginTransaction();
                cmd.Connection = c;
                cmd.Transaction = tr;

                cmd.CommandText = "SELECT MAX(id) FROM __core_sync_ct";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                cmd.CommandText = "SELECT MIN(id) FROM __core_sync_ct";
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

            using var c = new NpgsqlConnection(Configuration.ConnectionString);
            try
            {
                await c.OpenAsync();

                using var cmd = new NpgsqlCommand();
                using var tr = c.BeginTransaction();
                cmd.Connection = c;
                cmd.Transaction = tr;

                try
                {
                    cmd.CommandText = $"DELETE FROM __core_sync_ct WHERE id < $1";
                    cmd.Parameters.Add(new NpgsqlParameter { Value = minVersion });
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

                    cmd.CommandText = "SELECT MAX(id) FROM __core_sync_ct";
                    cmd.Parameters.Clear();
                    var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                    cmd.CommandText = "SELECT MIN(id) FROM __core_sync_ct";
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

            var table = Configuration.Tables.Cast<PostgreSQLSyncTable>().FirstOrDefault(_ => _.Name == name);

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table with name '{name}'");
            }

            using var connection = new NpgsqlConnection(Configuration.ConnectionString);
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

            var table = Configuration.Tables.Cast<PostgreSQLSyncTable>().FirstOrDefault(_ => _.Name == name) ?? throw new InvalidOperationException($"Unable to find table with name '{name}'");
            using var connection = new NpgsqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            await DisableChangeTrackingForTable(cmd, name, cancellationToken);
        }

        private async Task DisableChangeTrackingForTable(NpgsqlCommand cmd, string tableName, CancellationToken cancellationToken)
        { 
            var dropTriggerAndFunctionBase = new Func<string, string>((op) => $@"
DROP TRIGGER IF EXISTS __{tableName}_ct_{op.ToLower()}__ ON ""{tableName}"";
DROP FUNCTION IF EXISTS __{tableName}_ct_{op.ToLower()}__();");

            cmd.CommandText = dropTriggerAndFunctionBase("INSERT");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = dropTriggerAndFunctionBase("UPDATE");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = dropTriggerAndFunctionBase("DELETE");
            await cmd.ExecuteNonQueryAsync();        
        }

    }
} 