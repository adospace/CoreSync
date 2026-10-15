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
        /// <exception cref="SynchronizationException">
        /// Thrown when synchronization fails, with the underlying failure as
        /// <see cref="Exception.InnerException"/>.
        /// <para>
        /// <see cref="SynchronizationException.Kind"/> says why it failed as far as the failing component
        /// could tell (offline, unauthorized, server-side data error, ...) and
        /// <see cref="SynchronizationException.Stage"/> says which step was running. Both are additive:
        /// the message and the inner exception are the same as before they existed.
        /// <code>
        /// catch (SynchronizationException ex) when (ex.Kind == SyncFailureKind.Network)
        /// {
        ///     // the device is offline: ask the user to check connectivity and try again
        /// }
        /// catch (SynchronizationException ex)
        /// {
        ///     // ex.Kind, ex.Stage, ex.TableName, ex.ErrorCode, ex.TraceId, ex.ServerMessage
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// An inner <see cref="SyncAnchorTooOldException"/> (<see cref="SyncFailureKind.AnchorTooOld"/>)
        /// means one of the stores can no longer resolve changes from the other's anchor, because the
        /// change history covering it has been discarded by its retention policy. That is permanent:
        /// the affected store has to be reinitialized from a fresh snapshot, and retrying will not help.
        /// <code>
        /// catch (SynchronizationException ex)
        ///     when (ex.InnerException is SyncAnchorTooOldException tooOld)
        /// {
        ///     // tooOld.TableName, tooOld.RequestedVersion, tooOld.MinValidVersion
        /// }
        /// </code>
        /// </para>
        /// </exception>
        public async Task SynchronizeAsync(
            Func<SyncItem, ConflictResolution>? remoteConflictResolutionFunc = null,
            Func<SyncItem, ConflictResolution>? localConflictResolutionFunc = null,
            CancellationToken cancellationToken = default,
            SyncFilterParameter[]? remoteSyncFilterParameters = null,
            SyncFilterParameter[]? localSyncFilterParameters = null)
        {
            var stage = SynchronizationStage.Unknown;

            try
            {
                remoteConflictResolutionFunc ??= ((_) => ConflictResolution.Skip);
                localConflictResolutionFunc ??= ((_) => ConflictResolution.ForceWrite);

                stage = SynchronizationStage.LocalStoreId;
                var localStoreId = await LocalSyncProvider.GetStoreIdAsync(cancellationToken);

                stage = SynchronizationStage.RemoteStoreId;
                var remoteStoreId = await RemoteSyncProvider.GetStoreIdAsync(cancellationToken);

                stage = SynchronizationStage.LocalChanges;
                var localChangeSet = await LocalSyncProvider.GetChangesAsync(remoteStoreId, localSyncFilterParameters, SyncDirection.UploadOnly, RemoteSyncProvider.SyncTableNames, cancellationToken: cancellationToken);

                stage = SynchronizationStage.UploadChanges;
                await RemoteSyncProvider.ApplyChangesAsync(localChangeSet, remoteConflictResolutionFunc, cancellationToken: cancellationToken);

                stage = SynchronizationStage.SaveLocalVersion;
                await LocalSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                stage = SynchronizationStage.DownloadChanges;
                var remoteChangeSet = await RemoteSyncProvider.GetChangesAsync(localStoreId, remoteSyncFilterParameters, SyncDirection.DownloadOnly, LocalSyncProvider.SyncTableNames, cancellationToken: cancellationToken);

                stage = SynchronizationStage.ApplyChangesLocally;
                await LocalSyncProvider.ApplyChangesAsync(remoteChangeSet, localConflictResolutionFunc, cancellationToken: cancellationToken);

                stage = SynchronizationStage.SaveRemoteVersion;
                await RemoteSyncProvider.SaveVersionForStoreAsync(localStoreId, remoteChangeSet.SourceAnchor.Version, cancellationToken: cancellationToken);

                //await LocalSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
                //await RemoteSyncProvider.ApplyProvisionAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // Every failure leaves here as a SynchronizationException, including
                // SyncAnchorTooOldException: callers have caught this one type around
                // SynchronizeAsync since before the typed exception existed, and letting one case
                // escape unwrapped would break them at runtime with nothing to catch it at compile
                // time. The typed exception stays reachable as the inner exception, which is what
                // callers branch on to tell "reinitialize this client" from a transient failure.
                // The message and the inner exception are unchanged; the classification is additive.
                throw Classify(new SynchronizationException("Unable to synchronize stores", ex), ex, stage, cancellationToken);
            }
        }

        /// <summary>
        /// Fills the classification properties of <paramref name="result"/> from what the failing
        /// component was able to say about <paramref name="failure"/>.
        /// </summary>
        /// <remarks>
        /// Best effort by design: anything implementing <see cref="ISyncFailureInfo"/> in the inner
        /// exception chain wins, because it was produced by the component that saw the failure (the
        /// HTTP client reading the server's error code, a provider naming the table it could not
        /// write). Without one, the exception types that reliably mean one thing are mapped - a
        /// cancelled token, an unreachable server, refused credentials - and everything else stays
        /// <see cref="SyncFailureKind.Unknown"/>, which is what callers had before.
        /// </remarks>
        private static SynchronizationException Classify(
            SynchronizationException result,
            Exception failure,
            SynchronizationStage stage,
            CancellationToken cancellationToken)
        {
            result.Stage = stage;

            ISyncFailureInfo? info = null;
            var sawProviderFailure = false;
            var sawCancellation = false;
            var sawHttpFailure = false;
            var sawUnauthorized = false;

            foreach (var inner in Flatten(failure))
            {
                if (info == null && inner is ISyncFailureInfo candidate && candidate.Kind != SyncFailureKind.Unknown)
                {
                    info = candidate;
                }

                // A provider raises SynchronizationException for a change it could not apply (constraint
                // violation, missing table...). Older provider packages do not set Kind on it, so the type
                // alone has to count as a data error.
                sawProviderFailure |= inner is SynchronizationException;
                sawCancellation |= inner is OperationCanceledException;
                sawHttpFailure |= inner is System.Net.Http.HttpRequestException;
                sawUnauthorized |= inner is UnauthorizedAccessException;

                if (result.TableName == null && inner is ISyncFailureInfo withTable && withTable.TableName != null)
                {
                    result.TableName = withTable.TableName;
                }
            }

            if (info != null)
            {
                result.Kind = info.Kind;
                result.ErrorCode = info.ErrorCode;
                result.TraceId = info.TraceId;
                result.ServerMessage = info.ServerMessage;
                return result;
            }

            if (sawCancellation)
            {
                // The caller asked to stop. Otherwise a cancellation nobody requested is a timeout:
                // HttpClient reports an expired request timeout as TaskCanceledException.
                result.Kind = cancellationToken.IsCancellationRequested
                    ? SyncFailureKind.Cancelled
                    : IsRemoteStage(stage) ? SyncFailureKind.Network : SyncFailureKind.Unknown;
            }
            else if (sawUnauthorized)
            {
                result.Kind = SyncFailureKind.Unauthorized;
            }
            else if (sawHttpFailure)
            {
                // An HttpRequestException that does not implement ISyncFailureInfo never carried a
                // response: the connection failed, the name did not resolve, TLS failed. The server
                // was not reached.
                result.Kind = SyncFailureKind.Network;
            }
            else if (sawProviderFailure)
            {
                result.Kind = SyncFailureKind.DataError;
            }

            return result;
        }

        private static bool IsRemoteStage(SynchronizationStage stage)
            => stage == SynchronizationStage.RemoteStoreId
            || stage == SynchronizationStage.UploadChanges
            || stage == SynchronizationStage.DownloadChanges
            || stage == SynchronizationStage.SaveRemoteVersion;

        private static IEnumerable<Exception> Flatten(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        foreach (var nested in Flatten(inner))
                        {
                            yield return nested;
                        }
                    }

                    yield break;
                }

                yield return current;
                current = current.InnerException;
            }
        }
    }
}
