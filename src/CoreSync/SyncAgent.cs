using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    /// <summary>
    /// Orchestrates bidirectional synchronization between a local and a remote sync provider.
    /// </summary>
    /// <remarks>
    /// The sync agent performs a two-phase synchronization:
    /// <list type="number">
    /// <item><description>Upload: local changes are sent to the remote provider.</description></item>
    /// <item><description>Download: remote changes are applied to the local provider.</description></item>
    /// </list>
    /// </remarks>
    public class SyncAgent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAgent"/> class.
        /// </summary>
        /// <param name="localSyncProvider">The local sync provider (e.g., a SQLite database on the client).</param>
        /// <param name="remoteSyncProvider">The remote sync provider (e.g., a SQL Server database or an HTTP client).</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="localSyncProvider"/> or <paramref name="remoteSyncProvider"/> is <c>null</c>.
        /// </exception>
        public SyncAgent(ISyncProviderBase localSyncProvider, ISyncProviderBase remoteSyncProvider)
        {
            LocalSyncProvider = localSyncProvider ?? throw new ArgumentNullException(nameof(localSyncProvider));
            RemoteSyncProvider = remoteSyncProvider ?? throw new ArgumentNullException(nameof(remoteSyncProvider));
        }

        /// <summary>
        /// Gets the local sync provider.
        /// </summary>
        public ISyncProviderBase LocalSyncProvider { get; }

        /// <summary>
        /// Gets the remote sync provider.
        /// </summary>
        public ISyncProviderBase RemoteSyncProvider { get; }

        /// <summary>
        /// Performs a full bidirectional synchronization between the local and remote providers.
        /// </summary>
        /// <param name="remoteConflictResolutionFunc">
        /// A function that determines how to resolve conflicts when applying local changes to the remote store.
        /// Defaults to <see cref="ConflictResolution.Skip"/> when <c>null</c>.
        /// </param>
        /// <param name="localConflictResolutionFunc">
        /// A function that determines how to resolve conflicts when applying remote changes to the local store.
        /// Defaults to <see cref="ConflictResolution.ForceWrite"/> when <c>null</c>.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the synchronization operation.</param>
        /// <param name="remoteSyncFilterParameters">
        /// Optional filter parameters applied when retrieving changes from the remote provider.
        /// </param>
        /// <param name="localSyncFilterParameters">
        /// Optional filter parameters applied when retrieving changes from the local provider.
        /// </param>
        /// <returns>A task that represents the asynchronous synchronization operation.</returns>
        /// <exception cref="SynchronizationException">Thrown when synchronization fails.</exception>
        public async Task SynchronizeAsync(
            Func<SyncItem, ConflictResolution>? remoteConflictResolutionFunc = null,
            Func<SyncItem, ConflictResolution>? localConflictResolutionFunc = null,
            CancellationToken cancellationToken = default,
            SyncFilterParameter[]? remoteSyncFilterParameters = null,
            SyncFilterParameter[]? localSyncFilterParameters = null)
        {
            try
            {
                remoteConflictResolutionFunc ??= ((_) => ConflictResolution.Skip);
                localConflictResolutionFunc ??= ((_) => ConflictResolution.ForceWrite);

                var localStoreId = await LocalSyncProvider.GetStoreIdAsync(cancellationToken);
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync(cancellationToken);

                var localChangeSet = await LocalSyncProvider.GetChangesAsync(remoteStoreId, localSyncFilterParameters, SyncDirection.UploadOnly, RemoteSyncProvider.SyncTableNames, cancellationToken: cancellationToken);
                await RemoteSyncProvider.ApplyChangesAsync(localChangeSet, remoteConflictResolutionFunc, cancellationToken: cancellationToken);
                await LocalSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                var remoteChangeSet = await RemoteSyncProvider.GetChangesAsync(localStoreId, remoteSyncFilterParameters, SyncDirection.DownloadOnly, LocalSyncProvider.SyncTableNames, cancellationToken: cancellationToken);
                await LocalSyncProvider.ApplyChangesAsync(remoteChangeSet, localConflictResolutionFunc, cancellationToken: cancellationToken);
                await RemoteSyncProvider.SaveVersionForStoreAsync(localStoreId, remoteChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                //await LocalSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
                //await RemoteSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw new SynchronizationException("Unable to synchronize stores", ex);
            }
        }
    }
}
