namespace CoreSync.SqlServerCT
{
    /// <summary>
    /// The database-level change tracking settings as reported by <c>sys.change_tracking_databases</c>.
    /// </summary>
    internal sealed class ChangeTrackingDatabaseOptions
    {
        public ChangeTrackingDatabaseOptions(int retentionPeriod, ChangeRetentionUnit retentionPeriodUnit, bool autoCleanup)
        {
            RetentionPeriod = retentionPeriod;
            RetentionPeriodUnit = retentionPeriodUnit;
            AutoCleanup = autoCleanup;
        }

        public int RetentionPeriod { get; }

        public ChangeRetentionUnit RetentionPeriodUnit { get; }

        public bool AutoCleanup { get; }

        public override string ToString() => $"CHANGE_RETENTION = {RetentionPeriod} {RetentionPeriodUnit}, AUTO_CLEANUP = {(AutoCleanup ? "ON" : "OFF")}";
    }
}
