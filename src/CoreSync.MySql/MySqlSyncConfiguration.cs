using JetBrains.Annotations;

namespace CoreSync.MySql
{
    public class MySqlSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        internal MySqlSyncConfiguration([NotNull] string connectionString, [NotNull] MySqlSyncTable[] tables)
            : base(tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
        }
    }
}
