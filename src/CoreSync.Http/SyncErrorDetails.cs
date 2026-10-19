namespace CoreSync.Http;

/// <summary>
/// JSON body a CoreSync server attaches to an unsuccessful response when it recognises the failure,
/// alongside the <see cref="SyncHttpHeaders.ErrorCode"/> header carrying the same <see cref="Code"/>.
/// </summary>
/// <remarks>
/// <para>
/// The header is the contract and this body is the detail: a client that cannot read the body (an
/// HTML error page from the host, a truncated response) still classifies the failure from the header,
/// and a client that predates both ignores them and sees the status code it always saw.
/// </para>
/// <para>
/// Nothing in here is meant to leak what the server knows: <see cref="Message"/> is a short sentence
/// safe to show a user, <see cref="TableName"/> names the table involved when the server knows it, and
/// <see cref="TraceId"/> is the handle support uses to find the full story in the server's logs.
/// <see cref="Detail"/> (exception types and messages) is only filled when the server operator opts in.
/// </para>
/// <para>
/// The 410 anchor-too-old response keeps its own body (<see cref="SyncAnchorTooOldError"/>), which
/// existing clients already parse.
/// </para>
/// </remarks>
public class SyncErrorDetails
{
    /// <summary>
    /// Gets or sets the machine-readable error code; one of the <see cref="SyncHttpErrorCodes"/> values.
    /// </summary>
    public string Code { get; set; } = SyncHttpErrorCodes.ServerError;

    /// <summary>
    /// Gets or sets a short, user-safe description of the failure.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the table involved in the failure, when the server knows it.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Gets or sets the identifier under which the server logged the failure
    /// (its request trace identifier), so that support can correlate a user report with the server
    /// side. Also sent as the <see cref="SyncHttpHeaders.TraceId"/> header.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Gets or sets diagnostic detail (exception types and messages). Only present when the server is
    /// configured to include it; never contains it by default because provider messages can carry
    /// SQL text and schema details.
    /// </summary>
    public string? Detail { get; set; }
}
