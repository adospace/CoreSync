using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Sqlite
{
    public class SqliteSyncProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;

        public SqliteSyncProvider(SqliteSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional)
        {
            Configuration = configuration;
            ProviderMode = providerMode;

            if (configuration.Tables.Any(_ => _.SyncDirection != SyncDirection.UploadAndDownload) &&
                providerMode == ProviderMode.Bidirectional)
            {
                throw new InvalidOperationException("One or more table with sync direction different from Bidirectional: please must specify the provider mode to Local or Remote");
            }
        }

        public SqliteSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null)
        {
            Validate.NotNull(changeSet, nameof(changeSet));

            await InitializeStoreAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
                {
                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                        var version = await cmd.ExecuteLongScalarAsync();

                        cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                        var minVersion = await cmd.ExecuteLongScalarAsync();

                        if (changeSet.SourceAnchor.Version < minVersion - 1)
                            throw new InvalidOperationException($"Unable to apply changes, version of data requested ({changeSet.SourceAnchor.Version}) is too old (min valid version {minVersion})");

                        foreach (var item in changeSet.Items)
                        {
                            var table = (SqliteSyncTable)Configuration.Tables.First(_ => _.Name == item.TableName);

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

                            cmd.Parameters.Add(new SqliteParameter("@last_sync_version", changeSet.TargetAnchor.Version));
                            cmd.Parameters.Add(new SqliteParameter("@sync_force_write", syncForceWrite));
                            //cmd.Parameters.Add(new SqliteParameter("@compoundPrimaryKey", string.Join("-", table.PrimaryColumnNames.Select(_ => item.Values[_].Value.ToString()))));

                            foreach (var valueItem in item.Values)
                                cmd.Parameters.Add(new SqliteParameter("@" + valueItem.Key.Replace(" ", "_"), valueItem.Value.Value ?? DBNull.Value));

                            int affectedRows;

                            try
                            {
                                affectedRows = cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                throw new SynchronizationException($"Unable to {item} item to store for table {table}", ex);
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
                                    cmd.Parameters.Add(new SqliteParameter("@" + table.PrimaryColumnName.Replace(" ", "_"), valueItem.Value ?? DBNull.Value));
                                    if (0 == (long)await cmd.ExecuteScalarAsync())
                                    {
                                        throw new SynchronizationException($"Unable to {item} item to store for table {table} {new SyncAnchor(_storeId, version)}: affected rows was 0");
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

                            cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                            cmd.Parameters.Clear();
                            var currentVersion = await cmd.ExecuteLongScalarAsync();


                            cmd.CommandText = "UPDATE [__CORE_SYNC_CT] SET [SRC] = @sourceId WHERE [ID] = @version";
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@sourceId", changeSet.SourceAnchor.StoreId.ToString());
                            cmd.Parameters.AddWithValue("@version", currentVersion);

                            await cmd.ExecuteNonQueryAsync();
                        }

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
            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
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

        public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncDirection syncDirection)
        {
            long fromVersion = (await GetLastLocalAnchorForStoreAsync(otherStoreId)).Version;

            await InitializeStoreAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
                {
                    var items = new List<SqliteSyncItem>();

                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                        var version = await cmd.ExecuteLongScalarAsync();

                        cmd.CommandText = "SELECT MIN(ID) FROM  __CORE_SYNC_CT";
                        var minVersion = await cmd.ExecuteLongScalarAsync();

                        if (fromVersion < minVersion - 1)
                            throw new InvalidOperationException($"Unable to get changes, version of data requested ({fromVersion}) is too old (min valid version {minVersion})");

                        foreach (var table in Configuration.Tables.Cast<SqliteSyncTable>().Where(_ => _.Columns.Any()))
                        {
                            if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                                table.SyncDirection != syncDirection)
                                continue;


                            cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                            cmd.Parameters.Clear();
                            cmd.Parameters.AddWithValue("@version", fromVersion);
                            cmd.Parameters.AddWithValue("@sourceId", otherStoreId.ToString());

                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                    items.Add(new SqliteSyncItem(table, DetectChangeType(values),
                                        values.Where(_ => _.Key != "__OP").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));
                                }
                            }

                            cmd.CommandText = table.IncrementalDeletesQuery;
                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                    items.Add(new SqliteSyncItem(table, ChangeType.Delete, values));
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
            await InitializeStoreAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [__CORE_SYNC_REMOTE_ANCHOR] WHERE [ID] = @storeId";
                    cmd.Parameters.AddWithValue("@storeId", otherStoreId.ToString());

                    var version = await cmd.ExecuteScalarAsync();

                    if (version == null || version == DBNull.Value)
                        return new SyncAnchor(_storeId, 0);

                    return new SyncAnchor(_storeId, (long)version);
                }
            }
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId)
        {
            //await InitializeAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
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
            await InitializeStoreAsync();

            return _storeId;
        }

        private static ChangeType DetectChangeType(Dictionary<string, object> values)
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

        private static object GetValueFromRecord(SqliteSyncTable table, string columnName, int columnOrdinal, SqliteDataReader r)
        {
            if (r.IsDBNull(columnOrdinal))
                return null;

            if (table.RecordType == null)
                return r.GetValue(columnOrdinal);

            var property = table.RecordType.GetProperty(columnName);
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

        private async Task InitializeStoreAsync()
        {
            if (_initialized)
                return;

            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $@"CREATE TABLE IF NOT EXISTS __CORE_SYNC_CT 
(ID INTEGER PRIMARY KEY, TBL TEXT NOT NULL COLLATE NOCASE, OP CHAR NOT NULL, PK_{SqlitePrimaryColumnType.Int} INTEGER NULL, PK_{SqlitePrimaryColumnType.Text} TEXT NULL COLLATE NOCASE, PK_{SqlitePrimaryColumnType.Blob} BLOB NULL, SRC TEXT NULL COLLATE NOCASE)";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Int}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Int})";
                    await cmd.ExecuteNonQueryAsync();
                    cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Text}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Text})";
                    await cmd.ExecuteNonQueryAsync();
                    cmd.CommandText = $"CREATE INDEX IF NOT EXISTS __CORE_SYNC_CT_PK_{SqlitePrimaryColumnType.Blob}_INDEX ON __CORE_SYNC_CT(PK_{SqlitePrimaryColumnType.Blob})";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __CORE_SYNC_REMOTE_ANCHOR (ID TEXT NOT NULL PRIMARY KEY, LOCAL_VERSION LONG NULL, REMOTE_VERSION LONG NULL)";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS __CORE_SYNC_LOCAL_ID (ID TEXT NOT NULL PRIMARY KEY)";
                    await cmd.ExecuteNonQueryAsync();

                    cmd.CommandText = $"SELECT ID FROM __CORE_SYNC_LOCAL_ID LIMIT 1";
                    var localId = await cmd.ExecuteScalarAsync();
                    if (localId == null)
                    {
                        localId = Guid.NewGuid().ToString();
                        cmd.CommandText = $"INSERT INTO __CORE_SYNC_LOCAL_ID (ID) VALUES (@localId)";
                        cmd.Parameters.Add(new SqliteParameter("@localId", localId));
                        if (1 != await cmd.ExecuteNonQueryAsync())
                        {
                            throw new InvalidOperationException();
                        }
                        cmd.Parameters.Clear();
                    }

                    _storeId = Guid.Parse((string)localId);

                    foreach (SqliteSyncTable table in Configuration.Tables)
                    {
                        cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            /*
                            cid         name        type        notnull     dflt_value  pk
                            ----------  ----------  ----------  ----------  ----------  ----------
                            */
                            while (await reader.ReadAsync())
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

                    foreach (var table in Configuration.Tables.Cast<SqliteSyncTable>())
                    {
                        //var primaryKeyColumns = table.Columns.Where(_ => _.IsPrimaryKey).ToArray();
                        var tableColumns = table.Columns.Select(_ => _.Value).Where(_ => !_.IsPrimaryKey).ToArray();

                        table.InitialSnapshotQuery = $@"SELECT * FROM [{table.Name}]";

                        table.InsertQuery = $@"INSERT OR IGNORE INTO [{table.Name}] ({string.Join(", ", table.Columns.Select(_ => "[" + _.Key + "]"))}) 
            VALUES ({string.Join(", ", table.Columns.Select(_ => "@" + _.Key.Replace(' ', '_')))});";

                        table.SelectExistingQuery = $@"SELECT COUNT(*) FROM [{table.Name}] 
            WHERE [{table.PrimaryColumnName}] = @{table.PrimaryColumnName.Replace(' ', '_')}";

                        table.UpdateQuery = $@"UPDATE [{table.Name}]
            SET {string.Join(", ", tableColumns.Select(_ => "[" + _.Name + "] = @" + _.Name.Replace(' ', '_')))}
            WHERE [{table.Name}].[{table.PrimaryColumnName}] = @{table.PrimaryColumnName.Replace(' ', '_')}
            AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{table.PrimaryColumnType} = @{table.PrimaryColumnName.Replace(' ', '_')} AND TBL = '{table.Name}') <= @last_sync_version)";

                        table.DeleteQuery = $@"DELETE FROM [{table.Name}]
            WHERE [{table.Name}].[{table.PrimaryColumnName}] = @{table.PrimaryColumnName.Replace(' ', '_')}
            AND (@sync_force_write = 1 OR (SELECT MAX(ID) FROM __CORE_SYNC_CT WHERE PK_{table.PrimaryColumnType} = @{table.PrimaryColumnName.Replace(' ', '_')} AND TBL = '{table.Name}') <= @last_sync_version)";


                        if (table.SyncDirection == SyncDirection.UploadAndDownload ||
                            (table.SyncDirection == SyncDirection.UploadOnly && ProviderMode == ProviderMode.Local) ||
                            (table.SyncDirection == SyncDirection.DownloadOnly && ProviderMode == ProviderMode.Remote))
                        {
                            table.IncrementalAddOrUpdatesQuery = $@"SELECT DISTINCT {string.Join(",", table.Columns.Select(_ => "T.[" + _.Key + "]"))}, CT.OP AS __OP 
                                FROM [{table.Name}] AS T INNER JOIN __CORE_SYNC_CT AS CT ON T.[{table.PrimaryColumnName}] = CT.PK_{table.PrimaryColumnType} WHERE CT.ID > @version AND CT.TBL = '{table.Name}' AND (CT.SRC IS NULL OR CT.SRC != @sourceId)";

                            table.IncrementalDeletesQuery = $@"SELECT PK_{table.PrimaryColumnType} AS [{table.PrimaryColumnName}] FROM [__CORE_SYNC_CT] WHERE TBL = '{table.Name}' AND ID > @version AND OP = 'D' AND (SRC IS NULL OR SRC != @sourceId)";
                        }
                    }
                }
            }

            _initialized = true;
        }

        public async Task ApplyProvisionAsync()
        {
            //if (_initialized)
            //    return;

            await InitializeStoreAsync();

            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();

                using (var cmd = connection.CreateCommand())
                {
                    foreach (var table in Configuration.Tables.Cast<SqliteSyncTable>().Where(_ => _.Columns.Any()))
                    {
                        //var primaryKeyColumns = table.Columns.Where(_ => _.IsPrimaryKey).ToArray();
                        //var tableColumns = table.Columns.Where(_ => !_.IsPrimaryKey).ToArray();

                        if (table.SyncDirection == SyncDirection.UploadAndDownload ||
                            (table.SyncDirection == SyncDirection.UploadOnly && ProviderMode == ProviderMode.Local) ||
                            (table.SyncDirection == SyncDirection.DownloadOnly && ProviderMode == ProviderMode.Remote))
                        {
                            await SetupTableForFullChangeDetection(table, cmd);
                        }
                        else
                        {
                            await SetupTableForUpdatesOrDeletesOnly(table, cmd);
                        }
                    }
                }
            }

            //_initialized = true;
        }

        private async Task SetupTableForFullChangeDetection(SqliteSyncTable table, SqliteCommand cmd)
        {
            var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-{op}__]
    AFTER {op} ON [{table.Name}]
    FOR EACH ROW
    BEGIN
    INSERT INTO [__CORE_SYNC_CT] (TBL, OP, PK_{table.PrimaryColumnType}) VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.[{table.PrimaryColumnName}]);
    END");
            cmd.CommandText = commandTextBase("INSERT");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = commandTextBase("UPDATE");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = commandTextBase("DELETE");
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task SetupTableForUpdatesOrDeletesOnly(SqliteSyncTable table, SqliteCommand cmd)
        {
            var commandTextBase = new Func<string, string>((op) => $@"CREATE TRIGGER IF NOT EXISTS [__{table.Name}_ct-{op}__]
    AFTER {op} ON [{table.Name}]
    FOR EACH ROW
    BEGIN
    INSERT INTO [__CORE_SYNC_CT] (TBL, OP, PK_{table.PrimaryColumnType}) VALUES ('{table.Name}', '{op[0]}', {(op == "DELETE" ? "OLD" : "NEW")}.[{table.PrimaryColumnName}]);
    END");

            cmd.CommandText = commandTextBase("UPDATE");
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = commandTextBase("DELETE");
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveProvisionAsync()
        {
            using (var connection = new SqliteConnection(Configuration.ConnectionString))
            {
                await connection.OpenAsync();

                //1. discover tables
                using (var cmd = connection.CreateCommand())
                {
                    var listOfTables = new List<string>();
                    foreach (SqliteSyncTable table in Configuration.Tables)
                    {
                        cmd.CommandText = $"PRAGMA table_info('{table.Name}')";
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            /*
                            cid         name        type        notnull     dflt_value  pk
                            ----------  ----------  ----------  ----------  ----------  ----------
                            */
                            while (await reader.ReadAsync())
                            {
                                var colName = reader.GetString(1);

                                if (string.CompareOrdinal(colName, "__OP") == 0)
                                {
                                    continue;
                                }

                                listOfTables.Add(colName);
                            }
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
        }

        public async Task<SyncChangeSet> GetInitialSnapshotAsync(Guid otherStoreId, SyncDirection syncDirection)
        {
            //await InitializeAsync();

            using (var c = new SqliteConnection(Configuration.ConnectionString))
            {
                await c.OpenAsync();

                using (var cmd = new SqliteCommand())
                {
                    var items = new List<SqliteSyncItem>();

                    using (var tr = c.BeginTransaction())
                    {
                        cmd.Connection = c;
                        cmd.Transaction = tr;

                        cmd.CommandText = "SELECT MAX(ID) FROM  __CORE_SYNC_CT";
                        var version = await cmd.ExecuteLongScalarAsync();

                        foreach (var table in Configuration.Tables
                            .Cast<SqliteSyncTable>()
                            .Where(_ => _.Columns.Any() && !_.SkipInitialSnapshot))
                        {
                            if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                                table.SyncDirection != syncDirection)
                                continue;

                            cmd.CommandText = table.InitialSnapshotQuery;
                            using (var r = await cmd.ExecuteReaderAsync())
                            {
                                while (await r.ReadAsync())
                                {
                                    var values = Enumerable.Range(0, r.FieldCount).ToDictionary(_ => r.GetName(_), _ => GetValueFromRecord(table, r.GetName(_), _, r));
                                    items.Add(new SqliteSyncItem(table, ChangeType.Insert, values));
                                }
                            }
                        }

                        tr.Commit();

                        return new SyncChangeSet(new SyncAnchor(_storeId, version), await GetLastRemoteAnchorForStoreAsync(otherStoreId), items);
                    }
                }
            }
        }
    }
}