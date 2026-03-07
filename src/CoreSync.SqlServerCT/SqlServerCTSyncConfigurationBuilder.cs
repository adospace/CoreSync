using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTSyncConfigurationBuilder
    {
        private readonly string _connectionString;
        private readonly List<SqlServerCTSyncTable> _tables = new List<SqlServerCTSyncTable>();
        private string _schema = "dbo";
        private int _changeRetentionDays = 7;
        private bool _autoCleanup = true;

        public SqlServerCTSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqlServerCTSyncConfigurationBuilder Schema(string schema)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));
            _schema = schema;
            return this;
        }

        public SqlServerCTSyncConfigurationBuilder ChangeRetention(int days, bool autoCleanup = true)
        {
            _changeRetentionDays = days;
            _autoCleanup = autoCleanup;
            return this;
        }

        public SqlServerCTSyncConfigurationBuilder Table(
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

            _tables.Add(new SqlServerCTSyncTable(name, syncDirection, schema ?? _schema, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery));
            return this;
        }

        public SqlServerCTSyncConfigurationBuilder Table<T>(
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

            return Table(name, syncDirection, schema ?? _schema, skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery);
        }

        public SqlServerCTSyncConfigurationBuilder SkipColumns(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumns requires a table");

            lastTable.SkipColumns = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        public SqlServerCTSyncConfigurationBuilder SkipColumnsOnInsertOrUpdate(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumnsOnInsertOrUpdate requires a table");

            lastTable.SkipColumnsOnInsertOrUpdate = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        public SqlServerCTSyncConfigurationBuilder SelectIncrementalQuery(string selectIncrementalQuery)
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

        public SqlServerCTSyncConfigurationBuilder CustomSnapshotQuery(string customSnapshotQuery)
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

        public SqlServerCTSyncConfigurationBuilder IdentityInsert(IdentityInsertMode mode)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("IdentityInsert requires a table");

            lastTable.IdentityInsert = mode;
            return this;
        }

        public SqlServerCTSyncConfiguration Build() => new SqlServerCTSyncConfiguration(_connectionString, _tables.ToArray(), _changeRetentionDays, _autoCleanup);
    }
}
