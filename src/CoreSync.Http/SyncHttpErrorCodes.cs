namespace CoreSync.Http;

/// <summary>
/// Machine-readable values carried by the <see cref="SyncHttpHeaders.ErrorCode"/> response header.
/// </summary>
/// <remarks>
/// <para>
/// The header lets a client branch on a failure without parsing the response body or matching on
/// message text, and stays readable even when the body is a message pack payload the client did not
/// expect, or an HTML error page produced by the host.
/// </para>
/// <para>
/// The header is additive to the status code, never a replacement: a client that does not know a
/// code still sees the same non-success status it always did. A client that does know it can tell a
/// permanent failure (reinitialize, fix the data, fix the configuration) from one worth retrying.
/// </para>
/// </remarks>
public static class SyncHttpErrorCodes
{
    /// <summary>
    /// The client asked for changes relative to a version the server can no longer resolve, because
    /// the change history covering it has been discarded by the store's retention policy.
    /// Accompanied by HTTP 410 Gone and a <see cref="SyncAnchorTooOldError"/> body.
    /// </summary>
    /// <remarks>
    /// This is permanent for the client involved: retrying with the same anchor can never succeed.
    /// The client has to be reinitialized from a fresh snapshot.
    /// </remarks>
    public const string AnchorTooOld = "anchor-too-old";

    /// <summary>
    /// The upload or download session the request refers to is no longer known to the server: it was
    /// evicted from the cache, the server restarted, or the client took too long between requests.
    /// Accompanied by a <see cref="SyncErrorDetails"/> body.
    /// </summary>
    /// <remarks>
    /// Permanent for the current synchronization, harmless for the data: retrying the same request
    /// cannot succeed, but starting a new synchronization will.
    /// </remarks>
    public const string SessionExpired = "session-expired";

    /// <summary>
    /// The synchronization endpoint addressed by the request does not exist or is not published.
    /// Accompanied by a <see cref="SyncErrorDetails"/> body.
    /// </summary>
    /// <remarks>
    /// Permanent until an administrator changes the server configuration or the client its address.
    /// </remarks>
    public const string EndpointNotFound = "endpoint-not-found";

    /// <summary>
    /// The server is reachable but the component that owns the data store (for example an on-premise
    /// agent) is not connected or did not answer in time. Accompanied by a
    /// <see cref="SyncErrorDetails"/> body.
    /// </summary>
    /// <remarks>
    /// Transient: retrying later is appropriate.
    /// </remarks>
    public const string AgentUnavailable = "agent-unavailable";

    /// <summary>
    /// The store is configured in a way the server cannot synchronize (a table without a primary key,
    /// a reserved column name, a table missing from the configuration...). Accompanied by a
    /// <see cref="SyncErrorDetails"/> body.
    /// </summary>
    /// <remarks>
    /// Permanent until an administrator fixes the configuration.
    /// </remarks>
    public const string ConfigurationError = "configuration-error";

    /// <summary>
    /// The server's store refused the changes or could not produce them: a constraint violation, a
    /// missing table or column, a value that could not be converted. Accompanied by a
    /// <see cref="SyncErrorDetails"/> body naming the table when it is known.
    /// </summary>
    /// <remarks>
    /// Permanent for the changes involved: retrying the same request cannot succeed.
    /// </remarks>
    public const string DataError = "data-error";

    /// <summary>
    /// The server failed for a reason it did not classify further.
    /// </summary>
    /// <remarks>
    /// Treated as transient by the client, which is how an unclassified failure has always been
    /// treated: the request is retried where the client retried before.
    /// </remarks>
    public const string ServerError = "server-error";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="code"/> identifies a failure that retrying the same
    /// request cannot fix.
    /// </summary>
    /// <remarks>
    /// Unknown codes (from a newer server) and <c>null</c> (no header at all) are reported as not
    /// permanent, so a client never stops retrying something it used to retry just because the server
    /// learned a new word.
    /// </remarks>
    public static bool IsPermanent(string? code)
    {
        switch (code)
        {
            case AnchorTooOld:
            case SessionExpired:
            case EndpointNotFound:
            case ConfigurationError:
            case DataError:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Maps an error code to the <see cref="SyncFailureKind"/> an application should see.
    /// </summary>
    /// <param name="code">The value of the <see cref="SyncHttpHeaders.ErrorCode"/> header, or <c>null</c>.</param>
    /// <returns>
    /// The matching kind, <see cref="SyncFailureKind.ServerError"/> for a code this version does not
    /// know, and <see cref="SyncFailureKind.Unknown"/> for <c>null</c>.
    /// </returns>
    public static SyncFailureKind ToFailureKind(string? code)
    {
        switch (code)
        {
            case null:
                return SyncFailureKind.Unknown;
            case AnchorTooOld:
                return SyncFailureKind.AnchorTooOld;
            case SessionExpired:
                return SyncFailureKind.SessionExpired;
            case EndpointNotFound:
            case ConfigurationError:
                return SyncFailureKind.ConfigurationError;
            case AgentUnavailable:
                return SyncFailureKind.ServerUnavailable;
            case DataError:
                return SyncFailureKind.DataError;
            default:
                return SyncFailureKind.ServerError;
        }
    }
}
