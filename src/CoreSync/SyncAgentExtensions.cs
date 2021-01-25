using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public static class SyncAgentExtensions
    {
        public static Task SynchronizeAsync(this SyncAgent syncAgent, ConflictResolution conflictResolutionOnRemoteStore = ConflictResolution.Skip, ConflictResolution conflictResolutionOnLocalStore = ConflictResolution.ForceWrite, CancellationToken cancellationToken = default)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, _ => conflictResolutionOnLocalStore, cancellationToken: cancellationToken);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc, ConflictResolution conflictResolutionOnLocalStore, CancellationToken cancellationToken = default)
            => syncAgent.SynchronizeAsync(remoteConflictResolutionFunc, _ => conflictResolutionOnLocalStore, cancellationToken: cancellationToken);

        public static Task SynchronizeAsync(this SyncAgent syncAgent, ConflictResolution conflictResolutionOnRemoteStore, Func<SyncItem, ConflictResolution> localConflictResolutionFunc, CancellationToken cancellationToken = default)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, localConflictResolutionFunc, cancellationToken: cancellationToken);
    }
}
