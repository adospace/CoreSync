using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.PostgreSQL
{
    /// <summary>
    /// Provides a fluent API for building a <see cref="PostgreSQLSyncConfiguration"/> that defines which PostgreSQL tables
    /// participate in synchronization and how they behave.
    /// </summary>
    /// <remarks>
    /// Use the builder to register tables, optionally customize incremental and snapshot queries,
    /// then call <see cref="Build"/> to produce the final configuration.
    /// <para>
    /// Example usage:
    /// <code>
    /// var config = new PostgreSQLSyncConfigurationBuilder(connectionString)
    ///     .Table("items")
    ///     .Table&lt;Order&gt;(syncDirection: SyncDirection.DownloadOnly)
    ///     .Build();
    /// </code>
    /// </para>
    /// </remarks>
    public class PostgreSQLSyncConfigurationBuilder
    {
        private readonly string _connectionString;

        //do not use dictionary because order is important
        private readonly List<PostgreSQLSyncTable> _tables = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="PostgreSQLSyncConfigurationBuilder"/> class.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL connection string used to connect to the database.</param>
        /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <c>null</c>, empty, or whitespace.</exception>
        public PostgreSQLSyncConfigurationBuilder([NotNull] string connectionString)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            _connectionString = connectionString;
        }

        /// <summary>
        /// Registers a table for synchronization by name.
        /// </summary>
        /// <param name="name">The name of the PostgreSQL table to synchronize.</param>
        /// <param name="recordType">
        /// An optional CLR type that maps to the table. When specified, the provider can use it
        /// for typed deserialization of sync items.
        /// </param>
        /// <param name="syncDirection">
        /// The direction of synchronization for this table. Defaults to <see cref="SyncDirection.UploadAndDownload"/>.
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
        /// <exception cref="InvalidOperationException">A table with the same name has already been added.</exception>
        public PostgreSQLSyncConfigurationBuilder Table(
            [NotNull] string name,
            Type? recordType = null,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            bool skipInitialSnapshot = false,
            string? selectIncrementalQuery = null,
            string? customSnapshotQuery = null)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();
            if (_tables.Any(_ => string.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException($"Table with name '{name}' already added");

            _tables.Add(new PostgreSQLSyncTable(name, recordType: recordType, syncDirection: syncDirection, skipInitialSnapshot: skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery));
            return this;
        }

        /// <summary>
        /// Registers a table for synchronization using a CLR type. The table name is resolved
        /// from the <see cref="TableAttribute"/> if present, otherwise from the type name.
        /// </summary>
        /// <typeparam name="T">The CLR type that maps to the table.</typeparam>
        /// <param name="name">
        /// An optional explicit table name. When <c>null</c>, the name is resolved from the
        /// <see cref="TableAttribute"/> on <typeparamref name="T"/> or falls back to the type name.
        /// </param>
        /// <param name="syncDirection">
        /// The direction of synchronization for this table. Defaults to <see cref="SyncDirection.UploadAndDownload"/>.
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
        /// <exception cref="InvalidOperationException">A table with the same name has already been added.</exception>
        public PostgreSQLSyncConfigurationBuilder Table<T>(
            string? name = null,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            bool skipInitialSnapshot = false,
            string? selectIncrementalQuery = null,
            string? customSnapshotQuery = null)
        {
            if (name == null)
            {
                var tableAttribute = (TableAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(TableAttribute));
                if (tableAttribute != null)
                    name = tableAttribute.Name;
            }

            if (name == null)
            {
                name = typeof(T).Name;
            }

            if (_tables.Any(_ => string.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException($"Table with name '{name}' already added");

            return Table(name, typeof(T), syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery);
        }

        /// <summary>
        /// Sets a custom SQL query for retrieving incremental changes on the most recently added table.
        /// </summary>
        /// <param name="selectIncrementalQuery">The SQL query to select incremental updates.</param>
        /// <returns>This builder instance for method chaining.</returns>
        /// <exception cref="ArgumentException"><paramref name="selectIncrementalQuery"/> is <c>null</c> or whitespace.</exception>
        /// <exception cref="InvalidOperationException">No table has been added yet.</exception>
        public PostgreSQLSyncConfigurationBuilder SelectIncrementalQuery(string selectIncrementalQuery)
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
        public PostgreSQLSyncConfigurationBuilder CustomSnapshotQuery(string customSnapshotQuery)
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
        /// Builds the <see cref="PostgreSQLSyncConfiguration"/> from the registered tables.
        /// </summary>
        /// <returns>A new <see cref="PostgreSQLSyncConfiguration"/> instance.</returns>
        public PostgreSQLSyncConfiguration Build() => new PostgreSQLSyncConfiguration(_connectionString, _tables.ToArray());
    }
}
