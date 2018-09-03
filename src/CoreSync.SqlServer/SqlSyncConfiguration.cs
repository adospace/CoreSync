using JetBrains.Annotations;

namespace CoreSync.SqlServer
{
    public class SqlSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        internal SqlSyncConfiguration([NotNull] string connectionString, [NotNull] SqlSyncTable[] tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
            Tables = tables;
        }
    }
}