using System;

namespace CoreSync.Http.Client;

/// <summary>
/// A sync provider that communicates with a remote CoreSync server over HTTP.
/// Extends <see cref="ISyncProviderBase"/> with progress reporting.
/// </summary>
public interface ISyncProviderHttpClient : ISyncProviderBase
{
    /// <summary>
    /// Occurs when the synchronization progresses through different stages, allowing UI updates
    /// or progress reporting.
    /// </summary>
    event EventHandler<SyncProgressEventArgs> SyncProgress;
}

/// <summary>
/// Provides data for the <see cref="ISyncProviderHttpClient.SyncProgress"/> event.
/// </summary>
public class SyncProgressEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncProgressEventArgs"/> class.
    /// </summary>
    /// <param name="stage">The current synchronization stage.</param>
    /// <param name="progress">
    /// An optional progress value between 0.0 and 1.0, or <c>null</c> if progress is indeterminate.
    /// </param>
    public SyncProgressEventArgs(SyncStage stage, double? progress = null)
    {
        Stage = stage;
        Progress = progress;
    }

    /// <summary>
    /// Gets the current synchronization stage.
    /// </summary>
    public SyncStage Stage { get; }

    /// <summary>
    /// Gets the progress value (0.0 to 1.0), or <c>null</c> if progress is indeterminate.
    /// </summary>
    public double? Progress { get; }
}

/// <summary>
/// Identifies the current stage of an HTTP-based synchronization operation.
/// </summary>
public enum SyncStage
{
    /// <summary>
    /// Performing the initial snapshot transfer for a first-time sync.
    /// </summary>
    InitialSnapshot,

    /// <summary>
    /// Computing local changes to upload to the server.
    /// </summary>
    ComputingLocalChanges,

    /// <summary>
    /// Applying local changes to the remote server.
    /// </summary>
    ApplyChanges,

    /// <summary>
    /// Computing remote changes to download from the server.
    /// </summary>
    ComputingRemotingChanges,

    /// <summary>
    /// Downloading remote changes from the server.
    /// </summary>
    GetChanges,

    /// <summary>
    /// Applying downloaded remote changes to the local store.
    /// </summary>
    ApplyChangesLocally
}
