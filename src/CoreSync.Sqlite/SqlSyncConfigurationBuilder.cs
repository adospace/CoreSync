using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CoreSync.Sqlite
{
    public class SqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;
        private List<SqliteSyncTable> _tables = new List<SqliteSyncTable>();

        public SqlSyncConfigurationBuilder([NotNull] string connectionString)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            _connectionString = connectionString;
        }

        public SqlSyncConfigurationBuilder Table([NotNull] string name, bool bidirectional = true, string schema = "main")
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();
            if (_tables.Any(_ => String.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException("Table with name '{name}' already added");

            _tables.Add(new SqliteSyncTable(name, bidirectional, schema));
            return this;
        }

        public SqliteSyncConfiguration Configuration
        {
            get { return new SqliteSyncConfiguration(_connectionString, _tables.ToArray()); }
        }
    }
}
