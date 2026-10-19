namespace CoreSync
{
    /// <summary>
    /// The step of <see cref="SyncAgent.SynchronizeAsync"/> that was executing when a synchronization
    /// failed. Reported through <see cref="SynchronizationException.Stage"/>.
    /// </summary>
    /// <remarks>
    /// Named <c>SynchronizationStage</c> rather than <c>SyncStage</c> on purpose: <c>CoreSync.Http.Client</c>
    /// already has a <c>SyncStage</c> progress enum, and applications commonly import both namespaces.
    /// </remarks>
    public enum SynchronizationStage
    {
        /// <summary>
        /// The stage is not known (the failure was raised outside <see cref="SyncAgent"/>, or before
        /// the first step started).
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Reading the local store's identifier.
        /// </summary>
        LocalStoreId,

        /// <summary>
        /// Reading the remote store's identifier. Over HTTP this is the first request of a
        /// synchronization, so connectivity and authentication problems usually surface here.
        /// </summary>
        RemoteStoreId,

        /// <summary>
        /// Computing the local changes to upload.
        /// </summary>
        LocalChanges,

        /// <summary>
        /// Applying the local changes to the remote store (upload).
        /// </summary>
        UploadChanges,

        /// <summary>
        /// Recording on the local store the version that has been uploaded.
        /// </summary>
        SaveLocalVersion,

        /// <summary>
        /// Retrieving the remote changes (download).
        /// </summary>
        DownloadChanges,

        /// <summary>
        /// Applying the downloaded changes to the local store.
        /// </summary>
        ApplyChangesLocally,

        /// <summary>
        /// Recording on the remote store the version that has been downloaded.
        /// </summary>
        SaveRemoteVersion
    }
}
