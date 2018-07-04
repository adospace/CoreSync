using JetBrains.Annotations;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
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
        private List<SqlSyncTable> _tables = new List<SqlSyncTable>();

        public SqlSyncConfigurationBuilder(string connectionString)
        {
            _connectionString = connectionString;
        }

        public SqlSyncConfigurationBuilder Table([NotNull] string name, bool bidirectional = true, string schema = "dbo")
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            name = name.Trim();
            if (_tables.Any(_ => String.CompareOrdinal(_.Name, name) == 0))
                throw new InvalidOperationException("Table with name '{name}' already added");

            _tables.Add(new SqlSyncTable(name, bidirectional, schema));
            return this;
        }

        public SqlSyncConfiguration Configuration
        {
            get { return new SqlSyncConfiguration(_connectionString, _tables.ToArray()); }
        }
    }
}
