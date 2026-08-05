using JetBrains.Annotations;
using System;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTSyncConfiguration : SyncConfiguration
    {
        public string ConnectionString { get; }

        /// <summary>
        /// Gets the configured change tracking retention period, expressed in <see cref="ChangeRetentionUnit"/>.
        /// </summary>
        public int ChangeRetention { get; }

        /// <summary>
        /// Gets the unit <see cref="ChangeRetention"/> is expressed in.
        /// </summary>
        public ChangeRetentionUnit ChangeRetentionUnit { get; }

        /// <summary>
        /// Gets the configured retention period expressed in minutes.
        /// </summary>
        public long ChangeRetentionInMinutes
        {
            get
            {
                switch (ChangeRetentionUnit)
                {
                    case ChangeRetentionUnit.Minutes: return ChangeRetention;
                    case ChangeRetentionUnit.Hours: return ChangeRetention * 60L;
                    default: return ChangeRetention * 1440L;
                }
            }
        }

        /// <summary>
        /// Gets the configured retention period expressed in whole days, rounded up.
        /// </summary>
        /// <remarks>
        /// Retained for backward compatibility with configurations built through the
        /// <c>ChangeRetention(int days, bool autoCleanup)</c> overload. When the retention is configured
        /// in hours or minutes this reports the equivalent rounded up to the next whole day, so prefer
        /// <see cref="ChangeRetention"/> together with <see cref="ChangeRetentionUnit"/> for an exact value.
        /// </remarks>
        public int ChangeRetentionDays => ChangeRetentionUnit == ChangeRetentionUnit.Days
            ? ChangeRetention
            : (int)Math.Ceiling(ChangeRetentionInMinutes / 1440.0);

        public bool AutoCleanup { get; }

        /// <summary>
        /// Gets a value indicating whether the retention policy was set explicitly on the builder.
        /// Provisioning only reconciles the retention of an already change-tracked database when it was.
        /// </summary>
        internal bool IsChangeRetentionConfigured { get; }

        internal SqlServerCTSyncConfiguration(
            [NotNull] string connectionString,
            [NotNull] SqlServerCTSyncTable[] tables,
            int changeRetention = 7,
            ChangeRetentionUnit changeRetentionUnit = ChangeRetentionUnit.Days,
            bool autoCleanup = true,
            bool isChangeRetentionConfigured = false)
            : base(tables)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            Validate.NotNullOrEmptyArray(tables, nameof(tables));

            ConnectionString = connectionString;
            ChangeRetention = changeRetention;
            ChangeRetentionUnit = changeRetentionUnit;
            AutoCleanup = autoCleanup;
            IsChangeRetentionConfigured = isChangeRetentionConfigured;
        }
    }
}
