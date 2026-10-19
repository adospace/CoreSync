namespace CoreSync
{
    /// <summary>
    /// Implemented by exceptions that know why a synchronization step failed, so that
    /// <see cref="SyncAgent"/> can copy the classification onto the <see cref="SynchronizationException"/>
    /// it raises without depending on the transport or provider that detected the failure.
    /// </summary>
    /// <remarks>
    /// <see cref="SyncAgent.SynchronizeAsync"/> walks the inner exception chain of whatever it catches
    /// and uses the first implementation it finds. Transport packages implement it on their own
    /// exception types (for example the HTTP client's <c>SyncHttpRequestException</c>) so the agent,
    /// which targets .NET Standard 2.0, never has to inspect HTTP status codes itself.
    /// </remarks>
    public interface ISyncFailureInfo
    {
        /// <summary>
        /// Gets the classification of the failure.
        /// </summary>
        SyncFailureKind Kind { get; }

        /// <summary>
        /// Gets the machine-readable error code reported by the remote side, when it reported one
        /// (see <c>CoreSync.Http.SyncHttpErrorCodes</c>), otherwise <c>null</c>.
        /// </summary>
        string? ErrorCode { get; }

        /// <summary>
        /// Gets the table involved in the failure, when known, otherwise <c>null</c>.
        /// </summary>
        string? TableName { get; }

        /// <summary>
        /// Gets an identifier the remote side attached to the failure so that it can be found in its
        /// logs, when it attached one, otherwise <c>null</c>.
        /// </summary>
        string? TraceId { get; }

        /// <summary>
        /// Gets a short, user-safe message the remote side attached to the failure, when it attached
        /// one, otherwise <c>null</c>.
        /// </summary>
        string? ServerMessage { get; }
    }
}
