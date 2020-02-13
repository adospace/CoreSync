using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncConfigurationBuilder
    {
        private readonly string _connectionString;
        private readonly List<SqlSyncTable> _tables = new List<SqlSyncTable>();

        public SqlSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqlSyncConfigurationBuilder Table([NotNull] string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo")
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();
            if (_tables.Any(_ => String.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException($"Table with name '{name}' already added");

            _tables.Add(new SqlSyncTable(name, syncDirection, schema));
            return this;
        }

        public SqlSyncConfigurationBuilder Table<T>(SyncDirection syncDirection = SyncDirection.UploadAndDownload, string schema = "dbo")
        {
            return Table(typeof(T).Name, syncDirection, schema);
        }

        public SqlSyncConfiguration Build() => new SqlSyncConfiguration(_connectionString, _tables.ToArray());
    }
}
