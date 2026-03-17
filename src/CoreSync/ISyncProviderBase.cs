using JetBrains.Annotations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    /// <summary>
    /// Defines the core operations required for a sync provider to participate in data synchronization.
    /// </summary>
    /// <remarks>
    /// This is the minimal interface that both local database providers and remote HTTP clients implement.
    /// </remarks>
    public interface ISyncProviderBase
    {
        /// <summary>
        /// Gets the names of the tables configured for synchronization on this provider,
        /// or <c>null</c> when the provider does not know its table list (e.g., an HTTP client proxy).
        /// </summary>
        string[]? SyncTableNames { get; }

        /// <summary>
        /// Gets the unique identifier for this sync store.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="Guid"/> that uniquely identifies this store across all sync peers.</returns>
        [NotNull]
        Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists the last synchronized version for a remote store, so that subsequent syncs
        /// only retrieve changes after this version.
        /// </summary>
        /// <param name="otherStoreId">The unique identifier of the remote store.</param>
        /// <param name="version">The version number to record.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies a set of changes from a remote store to this store.
        /// </summary>
        /// <param name="changeSet">The set of changes to apply.</param>
        /// <param name="onConflictFunc">
        /// An optional function invoked when a conflict is detected, returning the desired
        /// <see cref="ConflictResolution"/> for each conflicting item. When <c>null</c>, conflicts are skipped.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="SyncAnchor"/> representing the store's state after applying changes.</returns>
        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all changes from this store that have not yet been synchronized with the specified remote store.
        /// </summary>
        /// <param name="otherStoreId">The unique identifier of the remote store to get changes for.</param>
        /// <param name="syncFilterParameters">Optional filter parameters to narrow the set of returned changes.</param>
        /// <param name="syncDirection">The direction of synchronization to filter applicable tables.</param>
        /// <param name="tables">
        /// An optional list of table names to restrict the sync to. When specified, only tables present in both
        /// this list and the provider's configuration will be included, preserving the configuration-defined order.
        /// All specified table names must exist in the provider's configuration; otherwise an <see cref="ArgumentException"/> is thrown.
        /// When <c>null</c>, all configured tables are included.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="SyncChangeSet"/> containing the pending changes.</returns>
        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string[]? tables = null, CancellationToken cancellationToken = default);
    }
}
