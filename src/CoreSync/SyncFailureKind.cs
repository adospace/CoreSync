namespace CoreSync
{
    /// <summary>
    /// Broad classification of why a synchronization failed, meant to drive what an application tells
    /// the user and whether retrying makes sense.
    /// </summary>
    /// <remarks>
    /// The value is best effort: it is derived from whatever the failing component could tell
    /// (a typed exception, an <c>X-CoreSync-Error</c> header from a server that emits it, or the HTTP
    /// status code and exception type when talking to an older server). <see cref="Unknown"/> means
    /// nothing better than "it failed" is known, which is exactly what callers had before this
    /// classification existed.
    /// </remarks>
    public enum SyncFailureKind
    {
        /// <summary>
        /// No classification is available.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The remote store could not be reached or the connection dropped or timed out before a
        /// response arrived. Typically the device is offline; retrying later is appropriate.
        /// </summary>
        Network,

        /// <summary>
        /// The server refused the credentials (HTTP 401/403). Retrying without re-authenticating
        /// cannot succeed.
        /// </summary>
        Unauthorized,

        /// <summary>
        /// The caller cancelled the operation through its <see cref="System.Threading.CancellationToken"/>.
        /// </summary>
        Cancelled,

        /// <summary>
        /// One of the stores can no longer resolve changes from the other's anchor
        /// (<see cref="SyncAnchorTooOldException"/>). The affected store must be reinitialized from a
        /// fresh snapshot; retrying cannot help.
        /// </summary>
        AnchorTooOld,

        /// <summary>
        /// The server no longer holds the upload or download session this synchronization was using
        /// (it was evicted, the server restarted, or the client took too long). Nothing is wrong with
        /// the data: starting a new synchronization is the fix.
        /// </summary>
        SessionExpired,

        /// <summary>
        /// The server was reached but cannot serve requests right now (HTTP 502/503/504, or the
        /// server's data store agent is offline). Retrying later is appropriate.
        /// </summary>
        ServerUnavailable,

        /// <summary>
        /// A store refused the changes: a constraint violation, a missing table or column, a value
        /// that could not be converted. Retrying the same changes cannot succeed; the problem needs
        /// someone to look at the data or the schema.
        /// </summary>
        DataError,

        /// <summary>
        /// The synchronization endpoint or store is not configured in a way that can be served (the
        /// endpoint does not exist or is unpublished, a table has no primary key, ...). Needs an
        /// administrator; retrying cannot help.
        /// </summary>
        ConfigurationError,

        /// <summary>
        /// The server answered with an error it did not classify further (an unexpected HTTP 5xx, or
        /// a 4xx from a server that does not emit <c>X-CoreSync-Error</c>). The device is online and
        /// the problem is on the server side.
        /// </summary>
        ServerError
    }
}
