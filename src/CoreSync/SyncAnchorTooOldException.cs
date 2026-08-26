using System;

namespace CoreSync
{
    /// <summary>
    /// Raised when a store is asked to resolve changes relative to a version whose change history
    /// has already been discarded by the store's change retention policy.
    /// </summary>
    /// <remarks>
    /// This is a <b>permanent</b> condition for the store involved: retrying the same operation with
    /// the same anchor can never succeed, because the change information the anchor refers to no
    /// longer exists (for example it was removed by the SQL Server Change Tracking auto-cleanup task).
    /// <para>
    /// Callers should treat it as a signal to reinitialize the affected client from scratch - drop the
    /// local copy and take a fresh initial snapshot - as opposed to a transient failure worth retrying.
    /// Retrying instead leaves the client permanently unable to upload or download changes.
    /// </para>
    /// <para>
    /// It derives from <see cref="InvalidOperationException"/> so that callers written against the
    /// previous, untyped behaviour keep working.
    /// </para>
    /// <para>
    /// Providers raise it directly. <see cref="SyncAgent.SynchronizeAsync"/> wraps it, like every
    /// other failure, in a <see cref="SynchronizationException"/> - so callers at that level branch
    /// on <see cref="Exception.InnerException"/> rather than catching this type directly.
    /// </para>
    /// </remarks>
    public class SyncAnchorTooOldException : InvalidOperationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAnchorTooOldException"/> class.
        /// </summary>
        /// <param name="tableName">The table whose change history no longer covers the requested version.</param>
        /// <param name="requestedVersion">
        /// The version the operation asked for. A negative value means no anchor was recorded at all
        /// (see <see cref="SyncAnchor.Null"/>), which is equally unrecoverable.
        /// </param>
        /// <param name="minValidVersion">The oldest version the store can still resolve changes from.</param>
        public SyncAnchorTooOldException(string tableName, long requestedVersion, long minValidVersion)
            : base(BuildMessage(tableName, requestedVersion, minValidVersion))
        {
            TableName = tableName;
            RequestedVersion = requestedVersion;
            MinValidVersion = minValidVersion;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAnchorTooOldException"/> class with an
        /// inner exception.
        /// </summary>
        /// <param name="tableName">The table whose change history no longer covers the requested version.</param>
        /// <param name="requestedVersion">The version the operation asked for.</param>
        /// <param name="minValidVersion">The oldest version the store can still resolve changes from.</param>
        /// <param name="innerException">The underlying error, when the condition was detected through one.</param>
        public SyncAnchorTooOldException(string tableName, long requestedVersion, long minValidVersion, Exception innerException)
            : base(BuildMessage(tableName, requestedVersion, minValidVersion), innerException)
        {
            TableName = tableName;
            RequestedVersion = requestedVersion;
            MinValidVersion = minValidVersion;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAnchorTooOldException"/> class for a store
        /// that tracks changes in a single shared journal, where no individual table is implicated.
        /// </summary>
        /// <param name="requestedVersion">
        /// The version the operation asked for. A negative value means no anchor was recorded at all
        /// (see <see cref="SyncAnchor.Null"/>), which is equally unrecoverable.
        /// </param>
        /// <param name="minValidVersion">The oldest version the store can still resolve changes from.</param>
        public SyncAnchorTooOldException(long requestedVersion, long minValidVersion)
            : base(BuildMessage(null, requestedVersion, minValidVersion))
        {
            RequestedVersion = requestedVersion;
            MinValidVersion = minValidVersion;
        }

        /// <summary>
        /// Gets the name of the table whose change history no longer covers <see cref="RequestedVersion"/>,
        /// or <c>null</c> when the store keeps a single shared change journal and no individual table is
        /// implicated.
        /// </summary>
        public string? TableName { get; }

        /// <summary>
        /// Gets the version the operation asked for. A negative value means the store holds no anchor
        /// at all for the peer.
        /// </summary>
        public long RequestedVersion { get; }

        /// <summary>
        /// Gets the oldest version the store can still resolve changes from.
        /// </summary>
        public long MinValidVersion { get; }

        private static string BuildMessage(string? tableName, long requestedVersion, long minValidVersion)
        {
            var requested = requestedVersion < 0
                ? "no anchor is recorded for the peer store"
                : $"version {requestedVersion} is older than the retained history";

            var target = tableName == null
                ? "Unable to resolve changes"
                : $"Unable to resolve changes for table '{tableName}'";

            return $"{target}: {requested} " +
                   $"(requested version {requestedVersion}, minimum valid version {minValidVersion}). " +
                   "The peer store must be reinitialized from a fresh snapshot; retrying will not help.";
        }
    }
}
