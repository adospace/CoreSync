using System;
using System.Net;
using System.Net.Http;

namespace CoreSync.Http.Client;

/// <summary>
/// Raised by <see cref="ISyncProviderHttpClient"/> when the server answers a synchronization request
/// with a non-success status code, carrying whatever the server said about the failure.
/// </summary>
/// <remarks>
/// <para>
/// It derives from <see cref="HttpRequestException"/> and keeps the message and
/// <see cref="HttpRequestException.StatusCode"/> that <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>
/// produces, so code written against the previous behaviour - typically
/// <c>catch (SynchronizationException ex) when (ex.InnerException is HttpRequestException)</c> - keeps
/// working unchanged. What is new is the classification: <see cref="Kind"/>, <see cref="ErrorCode"/>,
/// <see cref="TableName"/>, <see cref="TraceId"/> and <see cref="ServerMessage"/>, read from the
/// <see cref="SyncHttpHeaders.ErrorCode"/> header and <see cref="SyncErrorDetails"/> body when the
/// server sends them, inferred from the status code when it does not (an older server).
/// </para>
/// <para>
/// A genuine connectivity failure (no route, DNS, TLS, connection reset) is not this exception: it
/// stays the plain <see cref="HttpRequestException"/> that <see cref="HttpClient"/> throws, with no
/// status code, which is how <see cref="SyncAgent"/> tells "the server was never reached" from "the
/// server answered with an error".
/// </para>
/// </remarks>
public class SyncHttpRequestException : HttpRequestException, ISyncFailureInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncHttpRequestException"/> class.
    /// </summary>
    /// <param name="message">
    /// The message; callers pass the one <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>
    /// would have produced, so that it stays identical to the previous behaviour.
    /// </param>
    /// <param name="statusCode">The response status code.</param>
    /// <param name="errorCode">The <see cref="SyncHttpHeaders.ErrorCode"/> header value, or <c>null</c>.</param>
    /// <param name="traceId">The <see cref="SyncHttpHeaders.TraceId"/> header value, or <c>null</c>.</param>
    /// <param name="details">The parsed <see cref="SyncErrorDetails"/> body, or <c>null</c>.</param>
    public SyncHttpRequestException(
        string message,
        HttpStatusCode statusCode,
        string? errorCode,
        string? traceId,
        SyncErrorDetails? details)
        : base(message, null, statusCode)
    {
        ErrorCode = errorCode;
        TraceId = traceId ?? details?.TraceId;
        TableName = details?.TableName;
        ServerMessage = details?.Message;
        Detail = details?.Detail;

        // The header is the server's word and wins. Without it the status code is all there is, and
        // the mapping is deliberately coarse: the point is to tell "the server answered with an error"
        // (so the device is online) from everything else, not to guess at causes.
        Kind = ErrorCode != null
            ? SyncHttpErrorCodes.ToFailureKind(ErrorCode)
            : statusCode switch
            {
                HttpStatusCode.Unauthorized => SyncFailureKind.Unauthorized,
                HttpStatusCode.Forbidden => SyncFailureKind.Unauthorized,
                HttpStatusCode.RequestTimeout => SyncFailureKind.ServerUnavailable,
                HttpStatusCode.TooManyRequests => SyncFailureKind.ServerUnavailable,
                HttpStatusCode.BadGateway => SyncFailureKind.ServerUnavailable,
                HttpStatusCode.ServiceUnavailable => SyncFailureKind.ServerUnavailable,
                HttpStatusCode.GatewayTimeout => SyncFailureKind.ServerUnavailable,
                _ => SyncFailureKind.ServerError
            };

        // Only a server that says "permanent" stops a retry. A server that says nothing (older
        // versions, a proxy's own error page) gets the retries it always got, because those are what
        // rescue a sync over a flaky connection.
        IsTransient = !SyncHttpErrorCodes.IsPermanent(ErrorCode);

        SetData(SynchronizationException.DataKeyKind, Kind.ToString());
        SetData(SynchronizationException.DataKeyErrorCode, ErrorCode);
        SetData(SynchronizationException.DataKeyTableName, TableName);
        SetData(SynchronizationException.DataKeyTraceId, TraceId);
        SetData(SynchronizationException.DataKeyServerMessage, ServerMessage);
        SetData("CoreSync.StatusCode", ((int)statusCode).ToString());
    }

    /// <inheritdoc />
    public SyncFailureKind Kind { get; }

    /// <inheritdoc />
    public string? ErrorCode { get; }

    /// <inheritdoc />
    public string? TableName { get; }

    /// <inheritdoc />
    public string? TraceId { get; }

    /// <inheritdoc />
    public string? ServerMessage { get; }

    /// <summary>
    /// Gets the diagnostic detail the server attached (exception types and messages), when the server
    /// is configured to include it; <c>null</c> otherwise.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Gets a value indicating whether repeating the same request could succeed. <c>false</c> only
    /// when the server marked the failure as permanent through its error code.
    /// </summary>
    public bool IsTransient { get; }

    private void SetData(string key, string? value)
    {
        if (value != null)
        {
            Data[key] = value;
        }
    }
}
