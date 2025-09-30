using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.SqlServer
{
    public class SqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;

        //do not use dictionary because order is important
        private readonly List<SqlSyncTable> _tables = new List<SqlSyncTable>();

        private string _schema = "dbo";

        public SqlSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqlSyncConfigurationBuilder Schema(string schema)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(schema, nameof(schema));

            _schema = schema;
            return this;
        }

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
        /// Specify which column should be skipped when synchronizing
        /// </summary>
        /// <param name="columnNames">Array of columns to skip when synchronizing</param>
        /// <returns>The current Sql configuration builder</returns>
        public SqlSyncConfigurationBuilder SkipColumns(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault() 
                ?? throw new InvalidOperationException("SkipColumns requires a table");

            //remove duplicates

            lastTable.SkipColumns = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        /// <summary>
        /// Specify which column should be skipped when inserting or updating
        /// </summary>
        /// <param name="columnNames">Array of columns to skip when inserting or updating records</param>
        /// <returns>The current Sql configuration builder</returns>
        public SqlSyncConfigurationBuilder SkipColumnsOnInsertOrUpdate(params string[] columnNames)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("SkipColumnsOnInsertOrUpdate requires a table");

            //remove duplicates

            lastTable.SkipColumnsOnInsertOrUpdate = columnNames ?? throw new ArgumentNullException();
            return this;
        }

        /// <summary>
        /// Force reloading of inserted records (useful when there are triggers that modify data on insert or indenty auto-increment columns other than primary key)
        /// </summary>
        public SqlSyncConfigurationBuilder ForceReloadInsertedRecords(bool forceReloadInsertedRecords = true)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("ForceReloadInsertedRecords requires a table");
            lastTable.ForceReloadInsertedRecords = forceReloadInsertedRecords;
            return this;
        }

        /// <summary>
        /// Specify a custom query to select incremental updates for the table
        /// </summary>
        /// <param name="selectIncrementalQuery">Query to select incremental updates</param>
        /// <returns>The current Sql configuration builder</returns>
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
        /// Specify a custom snapshot query to select initial records to synchronize
        /// </summary>
        /// <param name="customSnapshotQuery">Query to select initial records</param>
        /// <returns>The current Sql configuration builder</returns>
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
        /// Specify an IDENTITY_INSERT ON/OFF before issuing an insert command to SqlServer
        /// </summary>
        /// <param name="mode">Mode to set IDENTITY_INSERT</param>
        /// <returns>The current Sql configuration builder</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public SqlSyncConfigurationBuilder IdentityInsert(IdentityInsertMode mode)
        {
            var lastTable = _tables.LastOrDefault()
                ?? throw new InvalidOperationException("IdentityInsert requires a table");

            //remove duplicates

            lastTable.IdentityInsert = mode;

            return this;
        }

        public SqlSyncConfiguration Build() => new SqlSyncConfiguration(_connectionString, _tables.ToArray());
    }
}