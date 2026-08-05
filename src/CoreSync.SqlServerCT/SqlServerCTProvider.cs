using JetBrains.Annotations;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTProvider : ISyncProvider
    {
        private bool _initialized = false;
        private Guid _storeId;
        private readonly ISyncLogger? _logger;

        public SqlServerCTProvider(SqlServerCTSyncConfiguration configuration, ProviderMode providerMode = ProviderMode.Bidirectional, ISyncLogger? logger = null)
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

        public SqlServerCTSyncConfiguration Configuration { get; }
        public ProviderMode ProviderMode { get; }
        public string[]? SyncTableNames => Configuration.Tables.Select(_ => _.Name).ToArray();

        public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
        {
            Validate.NotNull(changeSet, nameof(changeSet));
            Validate.NotNullAnchor(changeSet.SourceAnchor, nameof(changeSet), nameof(changeSet.SourceAnchor));
            Validate.NotNull(changeSet.TargetAnchor, nameof(changeSet), nameof(changeSet.TargetAnchor));

            await InitializeStoreAsync(cancellationToken);

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin ApplyChanges(source={changeSet.SourceAnchor}, target={changeSet.TargetAnchor}, {changeSet.Items.Count} items)");

            using var c = new SqlConnection(Configuration.ConnectionString);
            var messageLog = new List<SqlInfoMessageEventArgs>();

            try
            {
                c.InfoMessage += (s, e) => messageLog.Add(e);
                await c.OpenAsync(cancellationToken);

                using var cmd = new SqlCommand();
                using var tr = c.BeginTransaction();
                cmd.Connection = c;
                cmd.Transaction = tr;

                try
                {
                    // Get current CT version
                    cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
                    var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                    // Update and Delete resolve conflicts through CHANGETABLE(CHANGES <table>,
                    // @last_sync_version), which SQL Server rejects outright once the requested version
                    // falls out of the table's retention window. Validate the anchor up front so a stale
                    // client gets a typed "reinitialize me" signal instead of a masked failure.
                    // Inserts never touch CHANGETABLE, so a first-time sync - which legitimately carries
                    // a null target anchor and nothing but inserts - must not be validated here.
                    var minValidVersions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in changeSet.Items)
                    {
                        if (item.ChangeType != ChangeType.Update && item.ChangeType != ChangeType.Delete)
                            continue;

                        var tableToValidate = (SqlServerCTSyncTable?)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);
                        if (tableToValidate == null)
                            continue;

                        await EnsureAnchorNotTooOldAsync(cmd, tableToValidate, changeSet.TargetAnchor, minValidVersions, cancellationToken);
                    }

                    var remainingItems = changeSet.Items.ToList();
                    int pass = 0;
                    while (remainingItems.Count > 0)
                    {
                        pass++;
                        var failedItems = new List<SyncItem>();
                        int appliedInPass = 0;
                        SqlException? lastFailure = null;

                        foreach (var item in remainingItems)
                        {
                            var table = (SqlServerCTSyncTable)Configuration.Tables.FirstOrDefault(_ => _.Name == item.TableName);

                            if (table == null)
                            {
                                continue;
                            }

                            bool syncForceWrite = false;
                            var itemChangeType = item.ChangeType;
                            bool itemHandled = false;
                            SqlException? itemFailure = null;

                        retryWrite:
                            cmd.Parameters.Clear();

                            // messageLog accumulates for the lifetime of the connection; reset it so a
                            // failure reports only the messages this statement produced.
                            messageLog.Clear();

                            table.SetupCommand(cmd, itemChangeType, item.Values);

                            cmd.Parameters.Add(new SqlParameter("@last_sync_version", changeSet.TargetAnchor.Version));
                            cmd.Parameters.Add(new SqlParameter("@sync_force_write", syncForceWrite));
                            cmd.Parameters.Add(new SqlParameter("@sync_client_id", SqlDbType.VarBinary, 128)
                            {
                                Value = changeSet.SourceAnchor.StoreId.ToByteArray()
                            });

                            int affectedRows;

                            try
                            {
                                affectedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);

                                if (affectedRows > 0)
                                {
                                    _logger?.Trace($"[{_storeId}] Successfully applied {item}");
                                }
                            }
                            catch (SqlException ex) when (itemChangeType == ChangeType.Insert && IsDuplicateKeyViolation(ex))
                            {
                                // The row is already there. Report zero rows affected and let the handling
                                // below confirm it exists and retry the item as an update - the behaviour
                                // the swallowed T-SQL error used to produce, now driven by the real error.
                                _logger?.Trace($"[{_storeId}] {item} already exists on table {table}: {ex.Message}");
                                itemFailure = ex;
                                affectedRows = 0;
                            }
                            catch (SqlException ex) when (ex.Number == SqlConstraintViolationErrorNumber)
                            {
                                // A referenced row may simply not have been applied yet (the change set
                                // is not dependency ordered). Defer the item and let the retry loop have
                                // another go once the rest of the pass has landed. A constraint violation
                                // is statement-scoped, so the transaction stays usable.
                                // If the loop stops making progress the error is rethrown below rather
                                // than dropped.
                                _logger?.Trace($"[{_storeId}] Deferring {itemChangeType} of {item} on table {table} (pass {pass}): {ex.Message}");
                                lastFailure = ex;
                                failedItems.Add(item);
                                continue;
                            }
                            catch (Exception ex)
                            {
                                _logger?.Error(DescribeItemFailure(itemChangeType, item, table, cmd.CommandText, messageLog, ex));
                                throw new SynchronizationException($"Unable to {itemChangeType} item {item} to store for table {table}", ex);
                            }

                            if (affectedRows == 0)
                            {
                                if (itemChangeType == ChangeType.Insert)
                                {
                                    var executedCommand = cmd.CommandText;

                                    cmd.CommandText = table.SelectExistingQuery;
                                    cmd.Parameters.Clear();
                                    var valueItem = item.Values[table.PrimaryColumnName];

                                    cmd.Parameters.Add(table.Columns[table.PrimaryColumnName].CreateParameter("@PrimaryColumnParameter", valueItem));

                                    if (1 == (int)await cmd.ExecuteScalarAsync(cancellationToken) && !syncForceWrite)
                                    {
                                        // The row is already there, so this insert becomes an update -
                                        // which does query CHANGETABLE. Validate the anchor before the
                                        // statement runs, since the up-front pass skipped this table.
                                        await EnsureAnchorNotTooOldAsync(cmd, table, changeSet.TargetAnchor, minValidVersions, cancellationToken);

                                        itemChangeType = ChangeType.Update;
                                        goto retryWrite;
                                    }
                                    else
                                    {
                                        _logger?.Warning($"[{_storeId}] Unable to {itemChangeType} item {item} on table {table} (pass {pass}). Messages:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.Select(_ => _.Message))}{Environment.NewLine}Generated SQL:{Environment.NewLine}{executedCommand}");
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
                                        else
                                        {
                                            _logger?.Warning($"[{_storeId}] Skip conflict for {item}");
                                            itemHandled = true;
                                        }
                                    }
                                }
                            }

                            if (affectedRows > 0)
                            {
                                itemHandled = true;
                                appliedInPass++;
                            }

                            if (!itemHandled)
                            {
                                failedItems.Add(item);

                                if (itemFailure != null)
                                {
                                    lastFailure = itemFailure;
                                }
                            }
                        }

                        if (failedItems.Count == 0 || appliedInPass == 0)
                        {
                            if (failedItems.Count > 0)
                            {
                                if (lastFailure != null)
                                {
                                    // The retry loop has stopped making progress and the remaining items
                                    // still fail against the database. Surface it: dropping them here
                                    // would be silent data loss.
                                    _logger?.Error($"[{_storeId}] {failedItems.Count} item(s) could not be applied after {pass} pass(es): {lastFailure.Message}");
                                    throw new SynchronizationException(
                                        $"Unable to apply {failedItems.Count} item(s) after {pass} pass(es); the change set does not resolve the last failure",
                                        lastFailure);
                                }

                                _logger?.Warning($"[{_storeId}] {failedItems.Count} item(s) could not be applied after {pass} pass(es)");
                            }
                            break;
                        }

                        _logger?.Info($"[{_storeId}] Pass {pass}: applied {appliedInPass} item(s), retrying {failedItems.Count} remaining item(s)");
                        remainingItems = failedItems;
                    }

                    cmd.CommandText = UpsertRemoteVersionCommandText;
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = changeSet.SourceAnchor.StoreId });
                    cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = changeSet.SourceAnchor.Version });
                    await cmd.ExecuteNonQueryAsync(cancellationToken);

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
            catch (SyncAnchorTooOldException)
            {
                // Deliberately not wrapped: callers have to be able to tell "reinitialize this client"
                // apart from a transient error worth retrying.
                throw;
            }
            catch (Exception ex)
            {
                var exceptionMessage = $"An exception occurred during synchronization: {ex.Message}{Environment.NewLine}Errors:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.SelectMany(_ => _.Errors.Cast<SqlError>()))}{Environment.NewLine}Messages:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.Select(_ => _.Message))}";
                throw new SyncErrorException(exceptionMessage, ex);
            }
        }

        /// <summary>
        /// SQL Server error raised when a statement conflicts with a FOREIGN KEY or CHECK constraint.
        /// </summary>
        private const int SqlConstraintViolationErrorNumber = 547;

        /// <summary>
        /// Returns whether <paramref name="ex"/> reports an attempt to insert a row that is already
        /// there - either a PRIMARY KEY / UNIQUE constraint or a unique index violation.
        /// </summary>
        private static bool IsDuplicateKeyViolation(SqlException ex)
            => ex.Number == 2627 || ex.Number == 2601;

        /// <summary>
        /// Retention windows shorter than this are legal but risky: a client offline for longer can no
        /// longer resume an incremental sync.
        /// </summary>
        private const int ShortChangeRetentionWarningThresholdInMinutes = 7 * 1440;

        private const string UpsertRemoteVersionCommandText = @"MERGE [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR] WITH (HOLDLOCK) AS T
USING (SELECT @id AS [ID]) AS S ON T.[ID] = S.[ID]
WHEN MATCHED THEN UPDATE SET [REMOTE_VERSION] = @version
WHEN NOT MATCHED THEN INSERT ([ID], [REMOTE_VERSION]) VALUES (@id, @version);";

        private const string UpsertLocalVersionCommandText = @"MERGE [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR] WITH (HOLDLOCK) AS T
USING (SELECT @id AS [ID]) AS S ON T.[ID] = S.[ID]
WHEN MATCHED THEN UPDATE SET [LOCAL_VERSION] = @version
WHEN NOT MATCHED THEN INSERT ([ID], [LOCAL_VERSION]) VALUES (@id, @version);";

        /// <summary>
        /// Reads (and memoizes) the minimum version <paramref name="table"/> can still resolve changes
        /// from, and throws <see cref="SyncAnchorTooOldException"/> when the anchor predates it.
        /// </summary>
        private static async Task EnsureAnchorNotTooOldAsync(SqlCommand cmd, SqlServerCTSyncTable table, SyncAnchor anchor, IDictionary<string, long> minValidVersions, CancellationToken cancellationToken)
        {
            if (!minValidVersions.TryGetValue(table.NameWithSchema, out var minValidVersion))
            {
                cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.NameWithSchema}'))";
                cmd.Parameters.Clear();
                minValidVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);
                minValidVersions[table.NameWithSchema] = minValidVersion;
            }

            // A null anchor (version -1) means no anchor was ever recorded for the peer store, which is
            // just as unrecoverable as one that fell out of the retention window.
            if (anchor.Version < minValidVersion)
            {
                throw new SyncAnchorTooOldException(table.NameWithSchema, anchor.Version, minValidVersion);
            }
        }

        private static string DescribeItemFailure(ChangeType changeType, SyncItem item, SqlServerCTSyncTable table, string commandText, List<SqlInfoMessageEventArgs> messageLog, Exception ex)
        {
            return $"Unable to {changeType} item {item} to store for table {table}: {ex.Message}" +
                $"{Environment.NewLine}Errors:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.SelectMany(_ => _.Errors.Cast<SqlError>()))}" +
                $"{Environment.NewLine}Messages:{Environment.NewLine}{string.Join(Environment.NewLine, messageLog.Select(_ => _.Message))}" +
                $"{Environment.NewLine}Generated SQL:{Environment.NewLine}{commandText}" +
                $"{Environment.NewLine}Exception:{Environment.NewLine}{ex}";
        }

        public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
        {
            using var c = new SqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new SqlCommand();
            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;
            try
            {
                cmd.CommandText = UpsertLocalVersionCommandText;
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = otherStoreId });
                cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = version });
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                tr.Commit();

                _logger?.Trace($"[{_storeId}] Save version {version} for store {otherStoreId}");
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

            using var c = new SqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT [LOCAL_VERSION] FROM [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR] WHERE [ID] = @storeId";
            cmd.Parameters.Add(new SqlParameter("@storeId", SqlDbType.UniqueIdentifier) { Value = otherStoreId });

            var version = await cmd.ExecuteScalarAsync(cancellationToken);

            if (version == null || version == DBNull.Value)
                return SyncAnchor.Null;

            return new SyncAnchor(_storeId, (long)version);
        }

        private async Task<SyncAnchor> GetLastRemoteAnchorForStoreAsync(Guid otherStoreId, CancellationToken cancellationToken = default)
        {
            using var c = new SqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT [REMOTE_VERSION] FROM [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR] WHERE [ID] = @storeId";
            cmd.Parameters.Add(new SqlParameter("@storeId", SqlDbType.UniqueIdentifier) { Value = otherStoreId });

            var version = await cmd.ExecuteScalarAsync(cancellationToken);

            // Version 0 would not be a sentinel: it is a real-looking version permanently below the
            // change tracking retention floor, so every Update/Delete sent against it is doomed to fail.
            // Report the missing anchor as null and let ApplyChangesAsync turn it into a typed
            // "reinitialize this client" signal.
            if (version == null || version == DBNull.Value)
                return SyncAnchor.Null;

            return new SyncAnchor(otherStoreId, (long)version);
        }

        public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);
            return _storeId;
        }

        private ChangeType DetectChangeType(Dictionary<string, object?> values)
        {
            if (values.TryGetValue("__OP", out var syncChangeOperation))
            {
                return (syncChangeOperation?.ToString()) switch
                {
                    "I" => ChangeType.Insert,
                    "U" => ChangeType.Update,
                    "D" => ChangeType.Delete,
                    _ => throw new NotSupportedException(),
                };
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

                // Ensure the anchor and local ID tables exist (using CT-specific table names to avoid conflicts)
                var tableNames = await connection.GetTableNamesAsync(cancellationToken);

                if (!tableNames.Contains("__CORE_SYNC_CT_REMOTE_ANCHOR"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR](
	[ID] [uniqueidentifier] NOT NULL,
	[LOCAL_VERSION] [BIGINT] NULL,
    [REMOTE_VERSION] [BIGINT] NULL,
 CONSTRAINT [PK___CORE_SYNC_CT_REMOTE_ANCHOR] PRIMARY KEY CLUSTERED
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                if (!tableNames.Contains("__CORE_SYNC_CT_LOCAL_ID"))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $@"CREATE TABLE [dbo].[__CORE_SYNC_CT_LOCAL_ID](
	[ID] [uniqueidentifier] NOT NULL
 CONSTRAINT [PK___CORE_SYNC_CT_LOCAL_ID] PRIMARY KEY CLUSTERED
(
	[ID] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT TOP 1 [ID] FROM [__CORE_SYNC_CT_LOCAL_ID]";
                    var localId = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (localId == null)
                    {
                        localId = Guid.NewGuid();
                        cmd.CommandText = $"INSERT INTO [__CORE_SYNC_CT_LOCAL_ID] ([ID]) VALUES (@id)";
                        cmd.Parameters.Add(new SqlParameter("@id", localId));
                        if (1 != await cmd.ExecuteNonQueryAsync(cancellationToken))
                        {
                            throw new InvalidOperationException();
                        }
                        cmd.Parameters.Clear();
                    }

                    _storeId = (Guid)localId;
                }

                // Discover table metadata
                using (var cmd = connection.CreateCommand())
                {
                    foreach (SqlServerCTSyncTable table in Configuration.Tables.Cast<SqlServerCTSyncTable>())
                    {
                        var primaryKeyIndexName = (await connection.GetPrimaryKeyIndexesAsync(table, cancellationToken)).FirstOrDefault();
                        if (primaryKeyIndexName == null)
                        {
                            throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't exist or it doesn't have a primary key");
                        }

                        var primaryKeyColumns = await connection.GetIndexColumnNamesAsync(table, primaryKeyIndexName, cancellationToken);

                        if (primaryKeyColumns.Length != 1)
                        {
                            throw new NotSupportedException($"Table '{table.Name}' has more than one column as primary key");
                        }

                        table.Columns = (await connection.GetTableColumnsAsync(table, cancellationToken))
                            .ToDictionary(_ => _.Name, _ => _, StringComparer.OrdinalIgnoreCase);
                        table.PrimaryColumnName = primaryKeyColumns[0];
                        table.PrimaryKeyColumns = primaryKeyColumns;

                        if (table.SkipColumns.Any(skipColumn => string.CompareOrdinal(skipColumn, table.PrimaryColumnName) == 0))
                        {
                            throw new InvalidOperationException($"Column to skip in synchronization ('{table.PrimaryColumnName}') can't be the primary column");
                        }

                        var allColumns = table.Columns.Keys.Except(table.SkipColumns);

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

            using var connection = new SqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await ReconcileChangeTrackingRetentionAsync(connection, cancellationToken);

            // Enable change tracking per table
            foreach (SqlServerCTSyncTable table in Configuration.Tables.Cast<SqlServerCTSyncTable>())
            {
                var primaryKeyIndexName = (await connection.GetPrimaryKeyIndexesAsync(table, cancellationToken)).FirstOrDefault();
                if (primaryKeyIndexName == null)
                {
                    throw new InvalidOperationException($"Table '{table.NameWithSchema}' doesn't have a primary key");
                }

                var allColumns = (await connection.GetTableColumnsAsync(table, cancellationToken)).Select(_ => _.Name).ToArray();

                if (allColumns.Any(_ => string.CompareOrdinal(_, "__OP") == 0))
                {
                    throw new NotSupportedException($"Table '{table.NameWithSchema}' has one column with reserved name '__OP'");
                }

                if (!await connection.GetIsChangeTrackingEnabledForTableAsync(table, cancellationToken))
                {
                    await connection.EnableChangeTrackingForTableAsync(table, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Brings the database-level change tracking retention in line with the configuration.
        /// </summary>
        /// <remarks>
        /// Enabling change tracking is only half of it: on a database that already has it on, the
        /// <c>= ON (...)</c> form is rejected, so the configured retention used to be silently ignored
        /// forever. Here the current settings are read back from <c>sys.change_tracking_databases</c>
        /// and, when they differ, applied through the modify form of the statement.
        /// <para>
        /// A database that already has change tracking enabled is only touched when the retention was
        /// set explicitly on the builder, so provisioning with a low privilege account against a
        /// pre-configured database keeps working.
        /// </para>
        /// </remarks>
        private async Task ReconcileChangeTrackingRetentionAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            if (Configuration.ChangeRetentionInMinutes < ShortChangeRetentionWarningThresholdInMinutes)
            {
                _logger?.Warning($"[{_storeId}] Change tracking retention is configured to {Configuration.ChangeRetention} {Configuration.ChangeRetentionUnit}, " +
                    $"which is shorter than {ShortChangeRetentionWarningThresholdInMinutes / 1440} days. Any client that stays offline longer than the retention window " +
                    "cannot resume an incremental sync and has to be reinitialized from a fresh snapshot.");
            }

            var isEnabled = await connection.GetIsChangeTrackingEnabledAsync(cancellationToken);

            try
            {
                if (!isEnabled)
                {
                    await connection.EnableChangeTrackingAsync(Configuration.ChangeRetention, Configuration.ChangeRetentionUnit, Configuration.AutoCleanup, cancellationToken);
                    return;
                }

                if (!Configuration.IsChangeRetentionConfigured)
                {
                    return;
                }

                var current = await connection.GetChangeTrackingOptionsAsync(cancellationToken);

                if (current != null &&
                    current.RetentionPeriod == Configuration.ChangeRetention &&
                    current.RetentionPeriodUnit == Configuration.ChangeRetentionUnit &&
                    current.AutoCleanup == Configuration.AutoCleanup)
                {
                    return;
                }

                _logger?.Info($"[{_storeId}] Change tracking retention is '{current?.ToString() ?? "unknown"}', applying configured " +
                    $"'CHANGE_RETENTION = {Configuration.ChangeRetention} {Configuration.ChangeRetentionUnit}, AUTO_CLEANUP = {(Configuration.AutoCleanup ? "ON" : "OFF")}'");

                await connection.AlterChangeTrackingRetentionAsync(Configuration.ChangeRetention, Configuration.ChangeRetentionUnit, Configuration.AutoCleanup, cancellationToken);
            }
            catch (SqlException ex)
            {
                var action = isEnabled ? "update the change tracking retention policy of" : "enable change tracking on";
                var hint = IsPermissionError(ex)
                    ? " The account used by the connection string needs the ALTER DATABASE permission (or membership of db_owner)."
                    : string.Empty;

                throw new InvalidOperationException(
                    $"Unable to {action} database '{connection.Database}': {ex.Message}.{hint}", ex);
            }
        }

        private static bool IsPermissionError(SqlException ex)
        {
            switch (ex.Number)
            {
                case 262:   // ALTER DATABASE permission denied in database '%.*ls'
                case 300:   // VIEW SERVER STATE / generic permission denied on object
                case 5011:  // User does not have permission to alter database '%.*ls'
                case 15151: // Cannot alter the database '%.*ls', because it does not exist or you do not have permission
                    return true;
                default:
                    return false;
            }
        }

        public async Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
        {
            var connStringBuilder = new SqlConnectionStringBuilder(Configuration.ConnectionString);
            if (string.IsNullOrWhiteSpace(connStringBuilder.InitialCatalog))
                throw new InvalidOperationException("Invalid connection string: InitialCatalog property is missing");

            using var connection = new SqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Disable change tracking on all tables first
            foreach (SqlServerCTSyncTable table in Configuration.Tables.Cast<SqlServerCTSyncTable>())
            {
                if (await connection.GetIsChangeTrackingEnabledForTableAsync(table, cancellationToken))
                {
                    await connection.DisableChangeTrackingForTableAsync(table, cancellationToken);
                }
            }

            // Drop our infrastructure tables
            var tableNames = await connection.GetTableNamesAsync(cancellationToken);

            if (tableNames.Contains("__CORE_SYNC_CT_REMOTE_ANCHOR"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"DROP TABLE [dbo].[__CORE_SYNC_CT_REMOTE_ANCHOR]";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (tableNames.Contains("__CORE_SYNC_CT_LOCAL_ID"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $@"DROP TABLE [dbo].[__CORE_SYNC_CT_LOCAL_ID]";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Optionally disable CT at DB level (only if no other tables use it)
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM sys.change_tracking_tables";
                if ((int)await cmd.ExecuteScalarAsync(cancellationToken) == 0)
                {
                    if (await connection.GetIsChangeTrackingEnabledAsync(cancellationToken))
                    {
                        await connection.DisableChangeTrackingAsync(cancellationToken);
                    }
                }
            }
        }

        public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters, SyncDirection syncDirection, string[]? tables = null, CancellationToken cancellationToken = default)
        {
            syncFilterParameters ??= Array.Empty<SyncFilterParameter>();

            var tablesToSync = Configuration.ResolveTableFilter(tables);

            var fromAnchor = await GetLastLocalAnchorForStoreAsync(otherStoreId, cancellationToken);

            var now = DateTime.Now;

            _logger?.Info($"[{_storeId}] Begin GetChanges(from={otherStoreId}, syncDirection={syncDirection}, fromVersion={fromAnchor})");

            using var c = new SqlConnection(Configuration.ConnectionString);
            await c.OpenAsync(cancellationToken);

            using var cmd = new SqlCommand();
            var items = new List<SqlServerCTSyncItem>();

            using var tr = c.BeginTransaction();
            cmd.Connection = c;
            cmd.Transaction = tr;

            try
            {
                // Get current CT version
                cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
                cmd.Parameters.Clear();
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                // Validate that the requested version is still valid
                if (!fromAnchor.IsNull())
                {
                    foreach (SqlServerCTSyncTable table in tablesToSync.Cast<SqlServerCTSyncTable>())
                    {
                        cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.NameWithSchema}'))";
                        cmd.Parameters.Clear();
                        var minVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);

                        if (fromAnchor.Version < minVersion)
                            throw new SyncAnchorTooOldException(table.NameWithSchema, fromAnchor.Version, minVersion);
                    }
                }

                foreach (SqlServerCTSyncTable table in tablesToSync.Cast<SqlServerCTSyncTable>())
                {
                    if (table.SyncDirection != SyncDirection.UploadAndDownload &&
                        table.SyncDirection != syncDirection)
                        continue;

                    _logger?.Info($"Getting changes for table {table.NameWithSchema}");

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

                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                            while (await r.ReadAsync(cancellationToken))
                            {
                                try
                                {
                                    var values = Enumerable.Range(0, r.FieldCount)
                                        .ToDictionary(index => r.GetName(index), index =>
                                        {
                                            var dbValue = r.GetValue(index);
                                            if (dbValue == DBNull.Value)
                                            {
                                                return null;
                                            }
                                            return dbValue;
                                        }, StringComparer.OrdinalIgnoreCase);

                                    items.Add(new SqlServerCTSyncItem(table, ChangeType.Insert, values));
                                    _logger?.Trace($"[{_storeId}] Initial snapshot {items.Last()}");
                                }
                                catch (ArgumentException)
                                {
                                    var duplicateColumns = Enumerable.Range(0, r.FieldCount).Select(index => r.GetName(index)).GroupBy(_ => _).Where(_ => _.Count() > 1).Select(_ => _.Key).ToArray();
                                    _logger?.Error($"Duplicate columns found in table {table.NameWithSchema}: {string.Join(",", duplicateColumns)}");
                                    throw;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            _logger?.Error($"InitialSnapshotQuery: {cmd.CommandText}");
                            throw;
                        }
                    }

                    if (!fromAnchor.IsNull())
                    {
                        // Get inserts and updates via CHANGETABLE
                        cmd.CommandText = table.IncrementalAddOrUpdatesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                        cmd.Parameters.Add(new SqlParameter("@sourceId", SqlDbType.VarBinary, 128)
                        {
                            Value = otherStoreId.ToByteArray()
                        });
                        foreach (var syncFilterParameter in syncFilterParameters)
                        {
                            cmd.Parameters.AddWithValue(syncFilterParameter.Name, syncFilterParameter.Value);
                        }

                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount)
                                    .ToDictionary(index => r.GetName(index), index =>
                                    {
                                        var dbValue = r.GetValue(index);
                                        if (dbValue == DBNull.Value)
                                        {
                                            return null;
                                        }
                                        return dbValue;
                                    }, StringComparer.OrdinalIgnoreCase);

                                items.Add(new SqlServerCTSyncItem(table, DetectChangeType(values),
                                    values.Where(_ => _.Key != "__OP").ToDictionary(_ => _.Key, _ => _.Value == DBNull.Value ? null : _.Value)));

                                _logger?.Trace($"[{_storeId}] Incremental add or update {items.Last()}");
                            }
                        }
                        catch (Exception)
                        {
                            _logger?.Error($"IncrementalAddOrUpdatesQuery: {cmd.CommandText}");
                            throw;
                        }

                        // Get deletes via CHANGETABLE
                        cmd.CommandText = table.IncrementalDeletesQuery;
                        cmd.Parameters.Clear();
                        cmd.Parameters.AddWithValue("@version", fromAnchor.Version);
                        cmd.Parameters.Add(new SqlParameter("@sourceId", SqlDbType.VarBinary, 128)
                        {
                            Value = otherStoreId.ToByteArray()
                        });

                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                            while (await r.ReadAsync(cancellationToken))
                            {
                                var values = Enumerable.Range(0, r.FieldCount)
                                    .ToDictionary(_ => r.GetName(_), _ =>
                                    {
                                        var dbValue = r.GetValue(_);
                                        if (dbValue == DBNull.Value)
                                        {
                                            return null;
                                        }
                                        return dbValue;
                                    });
                                items.Add(new SqlServerCTSyncItem(table, ChangeType.Delete, values));
                                _logger?.Trace($"[{_storeId}] Incremental delete {items.Last()}");
                            }
                        }
                        catch (Exception)
                        {
                            _logger?.Error($"IncrementalDeletesQuery: {cmd.CommandText}");
                            throw;
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

        public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
        {
            await InitializeStoreAsync(cancellationToken);

            using var c = new SqlConnection(Configuration.ConnectionString);
            try
            {
                await c.OpenAsync(cancellationToken);

                using var cmd = c.CreateCommand();

                cmd.CommandText = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
                var version = await cmd.ExecuteLongScalarAsync(cancellationToken);

                // Get the minimum valid version across all tracked tables
                long minVersion = long.MaxValue;
                foreach (SqlServerCTSyncTable table in Configuration.Tables.Cast<SqlServerCTSyncTable>())
                {
                    cmd.CommandText = $"SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('{table.NameWithSchema}'))";
                    var tableMinVersion = await cmd.ExecuteLongScalarAsync(cancellationToken);
                    if (tableMinVersion < minVersion)
                        minVersion = tableMinVersion;
                }

                if (minVersion == long.MaxValue)
                    minVersion = 0;

                return new SyncVersion(version, minVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to get current/minimum version from store", ex);
            }
        }

        public async Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
        {
            // SQL Server Change Tracking handles retention automatically via CHANGE_RETENTION setting.
            // This method returns the current version info as a no-op.
            await InitializeStoreAsync(cancellationToken);

            _logger?.Info($"[{_storeId}] ApplyRetentionPolicyAsync is a no-op for Change Tracking provider (retention is managed by SQL Server)");

            return await GetSyncVersionAsync(cancellationToken);
        }

        public async Task EnableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            await InitializeStoreAsync(cancellationToken);

            var table = Configuration.Tables.Cast<SqlServerCTSyncTable>().FirstOrDefault(_ => _.NameWithSchema == name);
            table ??= Configuration.Tables.Cast<SqlServerCTSyncTable>().FirstOrDefault(_ => _.Name == name);

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table '{name}'");
            }

            using var connection = new SqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            if (!await connection.GetIsChangeTrackingEnabledForTableAsync(table, cancellationToken))
            {
                await connection.EnableChangeTrackingForTableAsync(table, cancellationToken);
            }
        }

        public async Task DisableChangeTrackingForTable(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace", nameof(name));
            }

            var table = Configuration.Tables.Cast<SqlServerCTSyncTable>().FirstOrDefault(_ => _.NameWithSchema == name);
            table ??= Configuration.Tables.Cast<SqlServerCTSyncTable>().FirstOrDefault(_ => _.Name == name);

            if (table == null)
            {
                throw new InvalidOperationException($"Unable to find table '{name}'");
            }

            using var connection = new SqlConnection(Configuration.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            if (await connection.GetIsChangeTrackingEnabledForTableAsync(table, cancellationToken))
            {
                await connection.DisableChangeTrackingForTableAsync(table, cancellationToken);
            }
        }
    }
}
