using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    /// <summary>
    /// Provides convenience overloads for <see cref="SyncAgent.SynchronizeAsync"/> that accept
    /// fixed <see cref="ConflictResolution"/> values instead of per-item delegate functions.
    /// </summary>
    public static class SyncAgentExtensions
    {
        /// <summary>
        /// Performs a full bidirectional synchronization using fixed conflict resolution strategies.
        /// </summary>
        /// <param name="syncAgent">The sync agent to use.</param>
        /// <param name="conflictResolutionOnRemoteStore">
        /// The conflict resolution to apply when pushing local changes to the remote store.
        /// Defaults to <see cref="ConflictResolution.Skip"/>.
        /// </param>
        /// <param name="conflictResolutionOnLocalStore">
        /// The conflict resolution to apply when pulling remote changes to the local store.
        /// Defaults to <see cref="ConflictResolution.ForceWrite"/>.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the synchronization operation.</param>
        /// <param name="remoteSyncFilterParameters">Optional filter parameters for remote changes.</param>
        /// <param name="localSyncFilterParameters">Optional filter parameters for local changes.</param>
        /// <returns>A task that represents the asynchronous synchronization operation.</returns>
        public static Task SynchronizeAsync(this SyncAgent syncAgent,
            ConflictResolution conflictResolutionOnRemoteStore = ConflictResolution.Skip,
            ConflictResolution conflictResolutionOnLocalStore = ConflictResolution.ForceWrite,
            CancellationToken cancellationToken = default,
            SyncFilterParameter[]? remoteSyncFilterParameters = null,
            SyncFilterParameter[]? localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, _ => conflictResolutionOnLocalStore,
                cancellationToken: cancellationToken,
                remoteSyncFilterParameters: remoteSyncFilterParameters,
                localSyncFilterParameters: localSyncFilterParameters);

        /// <summary>
        /// Performs a full bidirectional synchronization using a per-item delegate for remote conflicts
        /// and a fixed resolution for local conflicts.
        /// </summary>
        /// <param name="syncAgent">The sync agent to use.</param>
        /// <param name="remoteConflictResolutionFunc">
        /// A function that determines how to resolve each conflict on the remote store.
        /// </param>
        /// <param name="conflictResolutionOnLocalStore">
        /// The fixed conflict resolution to apply when pulling remote changes to the local store.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the synchronization operation.</param>
        /// <param name="remoteSyncFilterParameters">Optional filter parameters for remote changes.</param>
        /// <param name="localSyncFilterParameters">Optional filter parameters for local changes.</param>
        /// <returns>A task that represents the asynchronous synchronization operation.</returns>
        public static Task SynchronizeAsync(this SyncAgent syncAgent,
            Func<SyncItem, ConflictResolution> remoteConflictResolutionFunc,
            ConflictResolution conflictResolutionOnLocalStore,
            CancellationToken cancellationToken = default,
            SyncFilterParameter[]? remoteSyncFilterParameters = null,
            SyncFilterParameter[]? localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(remoteConflictResolutionFunc, _ => conflictResolutionOnLocalStore,
                cancellationToken: cancellationToken,
                remoteSyncFilterParameters: remoteSyncFilterParameters,
                localSyncFilterParameters: localSyncFilterParameters);

        /// <summary>
        /// Performs a full bidirectional synchronization using a fixed resolution for remote conflicts
        /// and a per-item delegate for local conflicts.
        /// </summary>
        /// <param name="syncAgent">The sync agent to use.</param>
        /// <param name="conflictResolutionOnRemoteStore">
        /// The fixed conflict resolution to apply when pushing local changes to the remote store.
        /// </param>
        /// <param name="localConflictResolutionFunc">
        /// A function that determines how to resolve each conflict on the local store.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the synchronization operation.</param>
        /// <param name="remoteSyncFilterParameters">Optional filter parameters for remote changes.</param>
        /// <param name="localSyncFilterParameters">Optional filter parameters for local changes.</param>
        /// <returns>A task that represents the asynchronous synchronization operation.</returns>
        public static Task SynchronizeAsync(this SyncAgent syncAgent,
            ConflictResolution conflictResolutionOnRemoteStore,
            Func<SyncItem, ConflictResolution> localConflictResolutionFunc,
            CancellationToken cancellationToken = default,
            SyncFilterParameter[]? remoteSyncFilterParameters = null,
            SyncFilterParameter[]? localSyncFilterParameters = null)
            => syncAgent.SynchronizeAsync(_ => conflictResolutionOnRemoteStore, localConflictResolutionFunc,
                cancellationToken: cancellationToken,
                remoteSyncFilterParameters: remoteSyncFilterParameters,
                localSyncFilterParameters: localSyncFilterParameters);
    }
}
