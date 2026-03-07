using JetBrains.Annotations;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        public int ChangeRetentionDays { get; }

        public bool AutoCleanup { get; }

        internal SqlServerCTSyncConfiguration(
            [NotNull] string connectionString,
            [NotNull] SqlServerCTSyncTable[] tables,
            int changeRetentionDays = 7,
            bool autoCleanup = true)
            : base(tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
            ChangeRetentionDays = changeRetentionDays;
            AutoCleanup = autoCleanup;
        }
    }
}
