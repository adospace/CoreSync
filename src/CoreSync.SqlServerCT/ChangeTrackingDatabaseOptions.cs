namespace CoreSync.SqlServerCT
{
    /// <summary>
    /// The database-level change tracking settings as reported by <c>sys.change_tracking_databases</c>.
    /// </summary>
    public sealed class ChangeTrackingDatabaseOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChangeTrackingDatabaseOptions"/> class.
        /// </summary>
        /// <param name="retentionPeriod">The retention period, expressed in <paramref name="retentionPeriodUnit"/>.</param>
        /// <param name="retentionPeriodUnit">The unit <paramref name="retentionPeriod"/> is expressed in.</param>
        /// <param name="autoCleanup">Whether SQL Server automatically removes expired change tracking data.</param>
        public ChangeTrackingDatabaseOptions(int retentionPeriod, ChangeRetentionUnit retentionPeriodUnit, bool autoCleanup)
        {
            RetentionPeriod = retentionPeriod;
            RetentionPeriodUnit = retentionPeriodUnit;
            AutoCleanup = autoCleanup;
        }

        /// <summary>
        /// Gets how long change history is kept, expressed in <see cref="RetentionPeriodUnit"/>.
        /// </summary>
        public int RetentionPeriod { get; }

        /// <summary>
        /// Gets the unit <see cref="RetentionPeriod"/> is expressed in.
        /// </summary>
        public ChangeRetentionUnit RetentionPeriodUnit { get; }

        /// <summary>
        /// Gets a value indicating whether SQL Server automatically removes expired change tracking data.
        /// </summary>
        public bool AutoCleanup { get; }

        /// <inheritdoc />
        public override string ToString() => $"CHANGE_RETENTION = {RetentionPeriod} {RetentionPeriodUnit}, AUTO_CLEANUP = {(AutoCleanup ? "ON" : "OFF")}";
    }
}
