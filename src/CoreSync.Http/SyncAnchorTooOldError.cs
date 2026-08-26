namespace CoreSync.Http;

/// <summary>
/// Body of the HTTP 410 Gone response returned when a client's sync anchor has fallen outside the
/// store's change retention window.
/// </summary>
/// <remarks>
/// The wire form of <c>CoreSync.SyncAnchorTooOldException</c>: the client rebuilds that exception
/// from these values, so the condition reaches application code typed rather than as a generic
/// transport failure.
/// </remarks>
public class SyncAnchorTooOldError
{
    /// <summary>
    /// Gets or sets the table whose change history no longer covers <see cref="RequestedVersion"/>,
    /// or <c>null</c> when the store keeps a single shared change journal.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the version the client asked for. A negative value means the server holds no
    /// anchor at all for the client.
    /// </summary>
    public long RequestedVersion { get; set; }

    /// <summary>
    /// Gets or sets the oldest version the server can still resolve changes from.
    /// </summary>
    public long MinValidVersion { get; set; }
}
