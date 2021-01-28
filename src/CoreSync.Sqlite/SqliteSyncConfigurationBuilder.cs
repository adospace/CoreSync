using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace CoreSync.Sqlite
{
    public class SqliteSyncConfigurationBuilder
    {
        private readonly string _connectionString;

        //do not use dictionary because order is important
        private readonly List<SqliteSyncTable> _tables = new List<SqliteSyncTable>();

        public SqliteSyncConfigurationBuilder([NotNull] string connectionString)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            _connectionString = connectionString;
        }

        public SqliteSyncConfigurationBuilder Table(
            [NotNull] string name, 
            Type recordType = null, 
            SyncDirection syncDirection = SyncDirection.UploadAndDownload, 
            bool skipInitialSnapshot = false, 
            string selectIncrementalQuery = null,
            string customSnapshotQuery = null)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();
            if (_tables.Any(_ => string.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException($"Table with name '{name}' already added");

            _tables.Add(new SqliteSyncTable(name, recordType: recordType, syncDirection: syncDirection, skipInitialSnapshot: skipInitialSnapshot, selectIncrementalQuery, customSnapshotQuery));
            return this;
        }

        public SqliteSyncConfigurationBuilder Table<T>(
            string name = null, 
            SyncDirection syncDirection = SyncDirection.UploadAndDownload, 
            bool skipInitialSnapshot = false, 
            string selectIncrementalQuery = null,
            string customSnapshotQuery = null)
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
        /// Specify a custom query to select incremental updates for the table
        /// </summary>
        /// <param name="selectIncrementalQuery">Query to select incremental updates</param>
        /// <returns>The current Sql configuration builder</returns>
        public SqliteSyncConfigurationBuilder SelectIncrementalQuery(string selectIncrementalQuery)
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
        public SqliteSyncConfigurationBuilder CustomSnapshotQuery(string customSnapshotQuery)
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

        public SqliteSyncConfiguration Build() => new SqliteSyncConfiguration(_connectionString, _tables.ToArray());
    }
}