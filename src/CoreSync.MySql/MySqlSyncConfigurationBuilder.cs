using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.MySql
{
    /// <summary>
    /// Provides a fluent API for building a <see cref="MySqlSyncConfiguration"/> that defines which MySQL tables
    /// participate in synchronization and how they behave.
    /// </summary>
    public class MySqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;
        private readonly List<MySqlSyncTable> _tables = [];

        public MySqlSyncConfigurationBuilder([NotNull] string connectionString)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            _connectionString = connectionString;
        }

        public MySqlSyncConfigurationBuilder Table(
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
            {
                throw new InvalidOperationException($"Table with name '{name}' already added");
            }

            _tables.Add(new MySqlSyncTable(name, recordType, syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery));
            return this;
        }

        public MySqlSyncConfigurationBuilder Table<T>(
            string? name = null,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            bool skipInitialSnapshot = false,
            string? selectIncrementalQuery = null,
            string? customSnapshotQuery = null)
        {
            if (name == null)
            {
                var tableAttribute = (TableAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(TableAttribute));
                if (tableAttribute != null)
                {
                    name = tableAttribute.Name;
                }
            }

            name ??= typeof(T).Name;

            if (_tables.Any(_ => string.CompareOrdinal(_.Name, name) == 0))
            {
                throw new InvalidOperationException($"Table with name '{name}' already added");
            }

            return Table(name, typeof(T), syncDirection, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery);
        }

        public MySqlSyncConfigurationBuilder SkipColumns(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumns requires a table");

            lastTable.SkipColumns = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
            return this;
        }

        public MySqlSyncConfigurationBuilder SkipColumnsOnInsertOrUpdate(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumnsOnInsertOrUpdate requires a table");

            lastTable.SkipColumnsOnInsertOrUpdate = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
            return this;
        }

        public MySqlSyncConfigurationBuilder SelectIncrementalQuery(string selectIncrementalQuery)
        {
            if (string.IsNullOrWhiteSpace(selectIncrementalQuery))
            {
                throw new ArgumentException($"'{nameof(selectIncrementalQuery)}' cannot be null or whitespace", nameof(selectIncrementalQuery));
            }

            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SelectIncrementalQuery requires a table");

            lastTable.SelectIncrementalQuery = selectIncrementalQuery;
            return this;
        }

        public MySqlSyncConfigurationBuilder CustomSnapshotQuery(string customSnapshotQuery)
        {
            if (string.IsNullOrWhiteSpace(customSnapshotQuery))
            {
                throw new ArgumentException($"'{nameof(customSnapshotQuery)}' cannot be null or whitespace", nameof(customSnapshotQuery));
            }

            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("CustomSnapshotQuery requires a table");

            lastTable.CustomSnapshotQuery = customSnapshotQuery;
            return this;
        }

        public MySqlSyncConfiguration Build() => new MySqlSyncConfiguration(_connectionString, _tables.ToArray());
    }
}
