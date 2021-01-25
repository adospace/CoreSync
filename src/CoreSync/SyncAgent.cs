using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public class SyncAgent
    {
        public SyncAgent(ISyncProvider localSyncProvider, ISyncProvider remoteSyncProvider)
        {
            LocalSyncProvider = localSyncProvider ?? throw new ArgumentNullException(nameof(localSyncProvider));
            RemoteSyncProvider = remoteSyncProvider ?? throw new ArgumentNullException(nameof(remoteSyncProvider));
        }

        public ISyncProvider LocalSyncProvider { get; }
        public ISyncProvider RemoteSyncProvider { get; }

        public async Task SynchronizeAsync(
            Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc = null, 
            Func<SyncItem, ConflictResolution> localConflictResolutionFunc = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                remoteConflictResolutionFunc = remoteConflictResolutionFunc ?? ((_) => ConflictResolution.Skip);
                localConflictResolutionFunc = localConflictResolutionFunc ?? ((_) => ConflictResolution.ForceWrite);

                var localStoreId = await LocalSyncProvider.GetStoreIdAsync(cancellationToken);
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync(cancellationToken);

                var localChangeSet = await LocalSyncProvider.GetChangesAsync(remoteStoreId, SyncDirection.UploadOnly, cancellationToken: cancellationToken);
                await RemoteSyncProvider.ApplyChangesAsync(localChangeSet, remoteConflictResolutionFunc, cancellationToken: cancellationToken);
                await LocalSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                var remoteChangeSet = await RemoteSyncProvider.GetChangesAsync(localStoreId, SyncDirection.DownloadOnly, cancellationToken: cancellationToken);
                await LocalSyncProvider.ApplyChangesAsync(remoteChangeSet, localConflictResolutionFunc, cancellationToken: cancellationToken);
                await RemoteSyncProvider.SaveVersionForStoreAsync(localStoreId, remoteChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                await LocalSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
                await RemoteSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw new SynchronizationException("Unable to synchronize stores", ex);
            }
        }
    }
}
