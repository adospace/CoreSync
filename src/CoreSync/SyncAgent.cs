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

        //public async Task SynchronizeAsync()
        //{
        //    var initialLocalSet = await LocalSyncProvider.GetInitialSetAsync();
        //    var remoteLocalSet = await RemoteSyncProvider.GetInitialSetAsync();

        //    var remoteLocalSetAfterApplyInitialLocalSet = await RemoteSyncProvider.ApplyChangesAsync(new SyncChangeSet(remoteLocalSet.TargetAnchor, initialLocalSet.Items));

        //    //await RemoteSyncProvider.GetIncreamentalChangesAsync()
            
        //}
    }
}
