using JetBrains.Annotations;

namespace CoreSync.Sqlite
{
    public class SqliteSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        internal SqliteSyncConfiguration([NotNull] string connectionString, [NotNull] SqliteSyncTable[] tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
            Tables = tables;
        }
    }
}