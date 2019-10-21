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

        public async Task SynchronizeAsync(
            Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc = null, 
            Func<SyncItem, ConflictResolution> localConflictResolutionFunc = null)
        {
            try
            {
                var localStoreId = await LocalSyncProvider.GetStoreIdAsync();
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync();

                var localChangeSet = await LocalSyncProvider.GetChangesAsync(remoteStoreId);
                var remoteChangeSet = await RemoteSyncProvider.GetChangesAsync(localStoreId);

                await RemoteSyncProvider.ApplyChangesAsync(localChangeSet, remoteConflictResolutionFunc);
                await LocalSyncProvider.ApplyChangesAsync(remoteChangeSet, localConflictResolutionFunc);
            }
            catch (Exception ex)
            {
                throw new SynchronizationException("Unable to synchronize stores", ex);
            }
        }
    }
}
