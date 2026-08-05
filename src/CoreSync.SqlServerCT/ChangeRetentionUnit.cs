namespace CoreSync.SqlServerCT
{
    /// <summary>
    /// The time unit a SQL Server Change Tracking retention period is expressed in.
    /// </summary>
    /// <remarks>
    /// The numeric values match the <c>retention_period_units</c> column of
    /// <c>sys.change_tracking_databases</c>, so a value read from that catalog view can be cast
    /// directly to this enum.
    /// </remarks>
    public enum ChangeRetentionUnit
    {
        /// <summary>
        /// The retention period is expressed in minutes (<c>CHANGE_RETENTION = n MINUTES</c>).
        /// </summary>
        Minutes = 1,

        /// <summary>
        /// The retention period is expressed in hours (<c>CHANGE_RETENTION = n HOURS</c>).
        /// </summary>
        Hours = 2,

        /// <summary>
        /// The retention period is expressed in days (<c>CHANGE_RETENTION = n DAYS</c>).
        /// </summary>
        Days = 3
    }
}
