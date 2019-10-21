using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync
{
    public static class SyncAgentExtensions
    {
        public static Task SynchronizeAsync(this SyncAgent syncAgent, ConflictResolution conflictResolutionOnRemoteStore = ConflictResolution.Skip, ConflictResolution conflictResolutionOnLocalStore = ConflictResolution.ForceWrite)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, _ => conflictResolutionOnLocalStore);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc, ConflictResolution conflictResolutionOnLocalStore)
            => syncAgent.SynchronizeAsync(remoteConflictResolutionFunc, _ => conflictResolutionOnLocalStore);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, ConflictResolution conflictResolutionOnRemoteStore, Func<SyncItem, ConflictResolution> localConflictResolutionFunc)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, localConflictResolutionFunc);
    }
}
