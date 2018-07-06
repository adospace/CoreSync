using JetBrains.Annotations;

namespace CoreSync.Sqlite
{
    public class SqliteSyncConfiguration
    {
        public string ConnectionString { get; }
        public SqliteSyncTable[] Tables { get; }

        internal SqliteSyncConfiguration([NotNull] string connectionString, [NotNull] SqliteSyncTable[] tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
            Tables = tables;
        }
    }
}