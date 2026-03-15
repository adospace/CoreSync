using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.SqlServer
{
    /// <summary>
    /// Provides a fluent API for building a <see cref="SqlSyncConfiguration"/> that defines which SQL Server tables
    /// participate in synchronization and how they behave using trigger-based change tracking.
    /// </summary>
    /// <remarks>
    /// Use the builder to set a default schema, register tables with various options, then call <see cref="Build"/>
    /// to produce the final configuration.
    /// <para>
    /// Example usage:
    /// <code>
    /// var config = new SqlSyncConfigurationBuilder(connectionString)
    ///     .Schema("dbo")
    ///     .Table("Items")
    ///         .SkipColumns("LastModified")
    ///     .Table&lt;Order&gt;(syncDirection: SyncDirection.UploadOnly)
    ///         .IdentityInsert(IdentityInsertMode.On)
    ///     .Build();
    /// </code>
    /// </para>
    /// </remarks>
    public class SqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;

        //do not use dictionary because order is important
        private readonly List<SqlSyncTable> _tables = new List<SqlSyncTable>();

        private string _schema = "dbo";

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlSyncConfigurationBuilder"/> class.
        /// </summary>
        /// <param name="connectionString">The SQL Server connection string used to connect to the database.</param>
        public SqlSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Sets the default schema for subsequently added tables that do not specify one explicitly.
        /// </summary>
        /// <param name="schema">The default SQL Server schema name (e.g., "dbo").</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="schema"/> is <c>null</c>, empty, or whitespace.</exception>
        public SqlSyncConfigurationBuilder Schema(string schema)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            _schema = schema;
            return this;
        }

        /// <summary>
        /// Registers a table for synchronization by name.
        /// </summary>
        /// <param name="name">The name of the SQL Server table to synchronize.</param>
        /// <param name="syncDirection">
        /// The direction of synchronization for this table. Defaults to <see cref="SyncDirection.UploadAndDownload"/>.
        /// </param>
        /// <param name="schema">
        /// The schema that contains the table. When <c>null</c>, the default schema set via <see cref="Schema"/> is used.
        /// </param>
        /// <param name="skipInitialSnapshot">
        /// When <c>true</c>, the table is not included in the initial snapshot sent to new peers.
        /// Defaults to <c>false</c>.
        /// </param>
        /// <param name="selectIncrementalQuery">
        /// An optional custom SQL query used to retrieve incremental changes for this table.
        /// </param>
        /// <param name="customSnapshotQuery">
        /// An optional custom SQL query used to retrieve the initial snapshot for this table.
        /// </param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c>, empty, or whitespace.</exception>
        /// <exception cref="InvalidOperationException">A table with the same schema-qualified name has already been added.</exception>
        public SqlSyncConfigurationBuilder Table(
            [NotNull] string name,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            string? schema = null,
            bool skipInitialSnapshot = false,
            string? selectIncrementalQuery = null,
            string? customSnapshotQuery = null)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();

            var nameWithSchema = $"{(schema == null ? string.Empty : "[" + schema + "].")}[{name}]";

            if (_tables.Any(_ => string.CompareOrdinal(_.NameWithSchema, nameWithSchema) == 0))
                throw new InvalidOperationException($"Table with name '{nameWithSchema}' already added");

            _tables.Add(new SqlSyncTable(name, syncDirection, schema ?? _schema, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery));
            return this;
        }

        /// <summary>
        /// Registers a table for synchronization using a CLR type. The table name and schema are resolved
        /// from the <see cref="TableAttribute"/> if present, otherwise from the type name and default schema.
        /// </summary>
        /// <typeparam name="T">The CLR type that maps to the table.</typeparam>
        /// <param name="syncDirection">
        /// The direction of synchronization for this table. Defaults to <see cref="SyncDirection.UploadAndDownload"/>.
        /// </param>
        /// <param name="schema">
        /// The schema that contains the table. When <c>null</c>, resolved from <see cref="TableAttribute.Schema"/>
        /// or the default schema set via <see cref="Schema"/>.
        /// </param>
        /// <param name="skipInitialSnapshot">
        /// When <c>true</c>, the table is not included in the initial snapshot sent to new peers.
        /// Defaults to <c>false</c>.
        /// </param>
        /// <param name="selectIncrementalQuery">
        /// An optional custom SQL query used to retrieve incremental changes for this table.
        /// </param>
        /// <param name="customSnapshotQuery">
        /// An optional custom SQL query used to retrieve the initial snapshot for this table.
        /// </param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">A table with the same schema-qualified name has already been added.</exception>
        public SqlSyncConfigurationBuilder Table<T>(
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            string? schema = null,
            bool skipInitialSnapshot = false,
            string? selectIncrementalQuery = null,
            string? customSnapshotQuery = null)
        {
            var name = typeof(T).Name;
            var tableAttribute = (TableAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(TableAttribute));
            if (tableAttribute != null)
            {
                name = tableAttribute.Name;
                schema = tableAttribute.Schema;
            }

            var nameWithSchema = $"{(schema == null ? string.Empty : "[" + schema + "].")}[{name}]";

            if (_tables.Any(_ => string.CompareOrdinal(_.NameWithSchema, nameWithSchema) == 0))
                throw new InvalidOperationException($"Table with name '{nameWithSchema}' already added");

            return Table(name, syncDirection, schema ?? _schema, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery);
        }

        /// <summary>
        /// Specifies columns to exclude entirely from synchronization for the most recently added table.
        /// These columns will not be read or written during sync operations.
        /// </summary>
        /// <param name="columnNames">The column names to skip during synchronization.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="columnNames"/> is <c>null</c>.</exception>
        public SqlSyncConfigurationBuilder SkipColumns(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumns requires a table");

            //remove duplicates

            lastTable.SkipColumns = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        /// <summary>
        /// Specifies columns to exclude from insert and update operations for the most recently added table.
        /// Unlike <see cref="SkipColumns"/>, these columns are still read during change detection but are
        /// not written when applying changes.
        /// </summary>
        /// <param name="columnNames">The column names to skip during insert and update operations.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="columnNames"/> is <c>null</c>.</exception>
        public SqlSyncConfigurationBuilder SkipColumnsOnInsertOrUpdate(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumnsOnInsertOrUpdate requires a table");

            //remove duplicates

            lastTable.SkipColumnsOnInsertOrUpdate = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        /// <summary>
        /// Forces the provider to reload inserted records after applying them for the most recently added table.
        /// This is useful when the table has triggers that modify data on insert or has auto-increment identity
        /// columns other than the primary key.
        /// </summary>
        /// <param name="forceReloadInsertedRecords">
        /// <c>true</c> to reload inserted records after insert; <c>false</c> to skip reloading. Defaults to <c>true</c>.
        /// </param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        public SqlSyncConfigurationBuilder ForceReloadInsertedRecords(bool forceReloadInsertedRecords = true)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("ForceReloadInsertedRecords requires a table");
            lastTable.ForceReloadInsertedRecords = forceReloadInsertedRecords;
            return this;
        }

        /// <summary>
        /// Sets a custom SQL query for retrieving incremental changes on the most recently added table.
        /// </summary>
        /// <param name="selectIncrementalQuery">The SQL query to select incremental updates.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="selectIncrementalQuery"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        public SqlSyncConfigurationBuilder SelectIncrementalQuery(string selectIncrementalQuery)
        {
            if (string.IsNullOrWhiteSpace(selectIncrementalQuery))
            {
                throw new ArgumentException($"'{nameof(selectIncrementalQuery)}' cannot be null or whitespace", nameof(selectIncrementalQuery));
            }

            var lastTable = _tables.LastOrDefault();
            if (lastTable == null)
            {
                throw new InvalidOperationException("SelectIncrementalQuery requires a table");
            }

            lastTable.SelectIncrementalQuery = selectIncrementalQuery;
            return this;
        }

        /// <summary>
        /// Sets a custom SQL query for retrieving the initial snapshot on the most recently added table.
        /// </summary>
        /// <param name="customSnapshotQuery">The SQL query to select initial snapshot records.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="customSnapshotQuery"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        public SqlSyncConfigurationBuilder CustomSnapshotQuery(string customSnapshotQuery)
        {
            if (string.IsNullOrWhiteSpace(customSnapshotQuery))
            {
                throw new ArgumentException($"'{nameof(customSnapshotQuery)}' cannot be null or whitespace", nameof(customSnapshotQuery));
            }

            var lastTable = _tables.LastOrDefault();
            if (lastTable == null)
            {
                throw new InvalidOperationException("CustomSnapshotQuery requires a table");
            }

            lastTable.CustomSnapshotQuery = customSnapshotQuery;
            return this;
        }


        /// <summary>
        /// Configures the <c>IDENTITY_INSERT</c> behavior for the most recently added table.
        /// Controls whether <c>SET IDENTITY_INSERT ON/OFF</c> is issued before insert operations.
        /// </summary>
        /// <param name="mode">The <see cref="IdentityInsertMode"/> to apply.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        public SqlSyncConfigurationBuilder IdentityInsert(IdentityInsertMode mode)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("IdentityInsert requires a table");

            //remove duplicates

            lastTable.IdentityInsert = mode;

            return this;
        }

        /// <summary>
        /// Builds the <see cref="SqlSyncConfiguration"/> from the registered tables.
        /// </summary>
        /// <returns>A new <see cref="SqlSyncConfiguration"/> instance.</returns>
        public SqlSyncConfiguration Build() => new SqlSyncConfiguration(_connectionString, _tables.ToArray());
    }
}
