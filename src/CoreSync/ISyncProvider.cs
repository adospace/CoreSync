using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    /// <summary>
    /// Extends <see cref="ISyncProviderBase"/> with provisioning, version management, and per-table
    /// change tracking operations for database-backed sync providers.
    /// </summary>
    public interface ISyncProvider : ISyncProviderBase
    {
        /// <summary>
        /// Creates the internal tables, triggers, and metadata required for change tracking.
        /// This must be called once before the first synchronization.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous provisioning operation.</returns>
        Task ApplyProvisionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes all internal tables, triggers, and metadata created by <see cref="ApplyProvisionAsync"/>.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous removal operation.</returns>
        Task RemoveProvisionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current and minimum sync version for this store.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="SyncVersion"/> containing the current and minimum version numbers.</returns>
        Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a retention policy by purging change tracking data older than the specified minimum version.
        /// </summary>
        /// <param name="minVersion">The minimum version to retain. Changes below this version are deleted.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="SyncVersion"/> reflecting the version state after applying the retention policy.</returns>
        Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables change tracking for the specified table, creating the necessary triggers and metadata.
        /// </summary>
        /// <param name="tableName">The name of the table to enable change tracking on.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task EnableChangeTrackingForTable(
            string tableName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables change tracking for the specified table, removing triggers and metadata.
        /// </summary>
        /// <param name="tableName">The name of the table to disable change tracking on.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DisableChangeTrackingForTable(
            string tableName, CancellationToken cancellationToken = default);
    }
}
