using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Base class representing a table registered for synchronization, containing shared configuration
    /// such as sync direction and custom queries.
    /// </summary>
    public abstract class SyncTable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncTable"/> class.
        /// </summary>
        /// <param name="name">The name of the database table.</param>
        /// <param name="syncDirection">The direction of synchronization for this table.</param>
        /// <param name="skipInitialSnapshot">Whether to skip this table during the initial snapshot.</param>
        /// <param name="selectIncrementalQuery">An optional custom query for retrieving incremental changes.</param>
        /// <param name="customSnapshotQuery">An optional custom query for retrieving the initial snapshot.</param>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <c>null</c>, empty, or whitespace.</exception>
        protected SyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false, string? selectIncrementalQuery = null, string? customSnapshotQuery = null)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            Name = name;
            SyncDirection = syncDirection;
            SkipInitialSnapshot = skipInitialSnapshot;
            SelectIncrementalQuery = selectIncrementalQuery;
            CustomSnapshotQuery = customSnapshotQuery;
        }

        /// <summary>
        /// Gets the name of the database table.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets or sets the direction of synchronization for this table.
        /// </summary>
        public SyncDirection SyncDirection { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this table is excluded from the initial snapshot.
        /// </summary>
        public bool SkipInitialSnapshot { get; set; }

        /// <summary>
        /// Gets or sets a custom SQL query used to retrieve the initial snapshot for this table.
        /// When <c>null</c>, the default query is used.
        /// </summary>
        public string? CustomSnapshotQuery { get; set; }

        /// <summary>
        /// Gets or sets a custom SQL query used to retrieve incremental changes for this table.
        /// When <c>null</c>, the default query is used.
        /// </summary>
        public string? SelectIncrementalQuery { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Name}";
        }
    }
}
