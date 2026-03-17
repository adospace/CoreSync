using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    /// <summary>
    /// Provides convenience extension methods for <see cref="ISyncProviderBase"/>.
    /// </summary>
    public static class SyncProviderExtenstions
    {
        /// <summary>
        /// Applies a set of changes using separate conflict resolution strategies for updates and deletes.
        /// Inserts are always skipped on conflict.
        /// </summary>
        /// <param name="provider">The sync provider to apply changes to.</param>
        /// <param name="changeSet">The set of changes to apply.</param>
        /// <param name="updateResultion">The conflict resolution to use for update conflicts.</param>
        /// <param name="deleteResolution">The conflict resolution to use for delete conflicts.</param>
        /// <returns>A <see cref="SyncAnchor"/> representing the store's state after applying changes.</returns>
        public static Task<SyncAnchor> ApplyChangesAsync(this ISyncProviderBase provider,
            SyncChangeSet changeSet,
            ConflictResolution updateResultion,
            ConflictResolution deleteResolution)
        {
            Validate.NotNull(provider, nameof(provider));

            return provider.ApplyChangesAsync(
                changeSet, new Func<SyncItem, ConflictResolution>((item) =>
                {
                    if (item.ChangeType == ChangeType.Update)
                        return updateResultion;
                    else if (item.ChangeType == ChangeType.Delete)
                        return deleteResolution;

                    return ConflictResolution.Skip;
                }));
        }

        /// <summary>
        /// Retrieves changes from the provider without filter parameters.
        /// </summary>
        /// <param name="provider">The sync provider to get changes from.</param>
        /// <param name="otherStoreId">The unique identifier of the remote store to get changes for.</param>
        /// <param name="syncDirection">The direction of synchronization to filter applicable tables.</param>
        /// <param name="tables">An optional list of table names to restrict the sync to. When <c>null</c>, all configured tables are included.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="SyncChangeSet"/> containing the pending changes.</returns>
        public static Task<SyncChangeSet> GetChangesAsync(this ISyncProviderBase provider,
            Guid otherStoreId,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            string[]? tables = null,
            CancellationToken cancellationToken = default)
            => provider.GetChangesAsync(otherStoreId, null, syncDirection, tables, cancellationToken);

    }
}
