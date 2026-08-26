namespace CoreSync.Http;

/// <summary>
/// Machine-readable values carried by the <see cref="SyncHttpHeaders.ErrorCode"/> response header.
/// </summary>
/// <remarks>
/// The header lets a client branch on a failure without parsing the response body or matching on
/// message text, and stays readable even when the body is a message pack payload the client did not
/// expect.
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
}
