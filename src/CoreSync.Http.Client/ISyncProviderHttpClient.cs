using System;

namespace CoreSync.Http.Client;

public interface ISyncProviderHttpClient : ISyncProviderBase
{
    event EventHandler<SyncProgressEventArgs> SyncProgress;
}

public class SyncProgressEventArgs : EventArgs
{
    public SyncProgressEventArgs(SyncStage stage, double? progress = null)
    {
        Stage = stage;
        Progress = progress;
    }

    public SyncStage Stage { get; }
    public double? Progress { get; }
}

public enum SyncStage
{
    InitialSnapshot,

    ComputingLocalChanges,

    ApplyChanges,

    ComputingRemotingChanges,

    GetChanges,

    ApplyChangesLocally
}

