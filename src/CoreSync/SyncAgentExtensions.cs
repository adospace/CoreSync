using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public static class SyncAgentExtensions
    {
        public static Task SynchronizeAsync(this SyncAgent syncAgent, 
            ConflictResolution conflictResolutionOnRemoteStore = ConflictResolution.Skip, 
            ConflictResolution conflictResolutionOnLocalStore = ConflictResolution.ForceWrite, 
            CancellationToken cancellationToken = default,
            SyncFilterParameter[] remoteSyncFilterParameters = null,
            SyncFilterParameter[] localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, _ => conflictResolutionOnLocalStore, 
                cancellationToken: cancellationToken, 
                remoteSyncFilterParameters: remoteSyncFilterParameters, 
                localSyncFilterParameters: localSyncFilterParameters);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, 
            Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc, 
            ConflictResolution conflictResolutionOnLocalStore, 
            CancellationToken cancellationToken = default,
            SyncFilterParameter[] remoteSyncFilterParameters = null,
            SyncFilterParameter[] localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(remoteConflictResolutionFunc, _ => conflictResolutionOnLocalStore, 
                cancellationToken: cancellationToken, 
                remoteSyncFilterParameters: remoteSyncFilterParameters, 
                localSyncFilterParameters: localSyncFilterParameters);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, 
            ConflictResolution conflictResolutionOnRemoteStore, 
            Func<SyncItem, ConflictResolution> localConflictResolutionFunc, 
            CancellationToken cancellationToken = default,
            SyncFilterParameter[] remoteSyncFilterParameters = null,
            SyncFilterParameter[] localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, localConflictResolutionFunc, 
                cancellationToken: cancellationToken, 
                remoteSyncFilterParameters: remoteSyncFilterParameters, 
                localSyncFilterParameters: localSyncFilterParameters);
    }
}
