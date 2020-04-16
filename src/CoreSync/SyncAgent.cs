using System;
using System.Collections.Generic;
using System.Text;
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

        public async Task InitializeAsync()
        {
            try
            {
                var localStoreId = await LocalSyncProvider.GetStoreIdAsync();
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync();

                var initalLocalChangeSet = await LocalSyncProvider.GetInitialSnapshotAsync(remoteStoreId, SyncDirection.UploadOnly);
                await RemoteSyncProvider.ApplyChangesAsync(initalLocalChangeSet, (item) => throw new InvalidOperationException($"Conflit on insert initial item on remote store: {item}"));
                await LocalSyncProvider.SaveVersionForStoreAsync(remoteStoreId, initalLocalChangeSet.SourceAnchor.Version);

                var initialRemoteChangeSet = await RemoteSyncProvider.GetInitialSnapshotAsync(localStoreId, SyncDirection.DownloadOnly);
                await LocalSyncProvider.ApplyChangesAsync(initialRemoteChangeSet, (item) => throw new InvalidOperationException($"Conflit on insert initial item on local store: {item}"));
                await RemoteSyncProvider.SaveVersionForStoreAsync(localStoreId, initialRemoteChangeSet.SourceAnchor.Version);

                await LocalSyncProvider.ApplyProvisionAsync();
                await RemoteSyncProvider.ApplyProvisionAsync();

            }
            catch (Exception ex)
            {
                throw new SynchronizationException("Unable to initialize stores", ex);
            }
        }

        public async Task SynchronizeAsync(
            Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc = null, 
            Func<SyncItem, ConflictResolution> localConflictResolutionFunc = null)
        {
            try
            {
                remoteConflictResolutionFunc = remoteConflictResolutionFunc ?? ((_) => ConflictResolution.Skip);
                localConflictResolutionFunc = localConflictResolutionFunc ?? ((_) => ConflictResolution.ForceWrite);

                var localStoreId = await LocalSyncProvider.GetStoreIdAsync();
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync();

                var localChangeSet = await LocalSyncProvider.GetChangesAsync(remoteStoreId, SyncDirection.UploadOnly);
                await RemoteSyncProvider.ApplyChangesAsync(localChangeSet, remoteConflictResolutionFunc);
                await LocalSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);


                var remoteChangeSet = await RemoteSyncProvider.GetChangesAsync(localStoreId, SyncDirection.DownloadOnly);
                await LocalSyncProvider.ApplyChangesAsync(remoteChangeSet, localConflictResolutionFunc);
                await RemoteSyncProvider.SaveVersionForStoreAsync(localStoreId, remoteChangeSet.SourceAnchor.Version);
            }
            catch (Exception ex)
            {
                throw new SynchronizationException("Unable to synchronize stores", ex);
            }
        }
    }
}
