using JetBrains.Annotations;

namespace CoreSync.PostgreSQL
{
    public class PostgreSQLSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        internal PostgreSQLSyncConfiguration([NotNull] string connectionString, [NotNull] PostgreSQLSyncTable[] tables)
            : base(tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
        }
    }
} 