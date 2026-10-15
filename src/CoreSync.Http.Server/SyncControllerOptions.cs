using Microsoft.AspNetCore.Builder;
using System;

namespace CoreSync.Http.Server;

/// <summary>
/// Provides per-endpoint customization hooks for the CoreSync HTTP server endpoints.
/// Each property allows configuring the corresponding endpoint (e.g., adding authorization, rate limiting, or CORS).
/// </summary>
public class SyncControllerOptions
{
    /// <summary>
    /// Gets or sets an action applied to all sync endpoints. Use this to add shared middleware
    /// such as authorization or CORS policies.
    /// </summary>
    public Action<IEndpointConventionBuilder>? AllEndpoints { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the GET store-id endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? GetStoreIdEndpoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the GET bulk change set endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? GetBulkChangeSetAsyncEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the GET bulk change set item (JSON) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? GetBulkChangeSetItemEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the GET bulk change set item (binary/MessagePack) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? GetBulkChangeSetItemBinaryEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST begin-apply-bulk-changes endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostBeginApplyBulkChangesEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST apply-bulk-changes-item (JSON) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostApplyBulkChangesItemEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST apply-bulk-changes-item (binary/MessagePack) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostApplyBulkChangesItemBinaryEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST complete-apply-bulk-changes (JSON) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostCompleteApplyBulkChangesAsyncEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST complete-apply-bulk-changes (binary/MessagePack) endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostCompleteApplyBulkChangesBinaryAsyncEndPoint { get; set; }

    /// <summary>
    /// Gets or sets an action to configure the POST save-version-for-store endpoint.
    /// </summary>
    public Action<IEndpointConventionBuilder>? PostSaveVersionForStoreAsyncEndPoint { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sync endpoints answer unexpected exceptions
    /// themselves, with HTTP 500 and a <see cref="CoreSync.Http.SyncErrorDetails"/> JSON body, instead
    /// of letting them propagate to the host's exception handling. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// With the default, an unexpected exception reaches the host exactly as before (its exception
    /// handler, its logging and its error reporting all see it); the endpoints only add the
    /// <see cref="CoreSync.Http.SyncHttpHeaders.ErrorCode"/> and <see cref="CoreSync.Http.SyncHttpHeaders.TraceId"/>
    /// headers to whatever response the host produces, so a client can still classify the failure.
    /// Set to <c>true</c> for a host that prefers a JSON body over its error page; the exception is then
    /// logged at Error level through <see cref="Microsoft.Extensions.Logging.ILogger"/> and does not
    /// reach the host's exception handler.
    /// </remarks>
    public bool HandleUnexpectedExceptions { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="CoreSync.Http.SyncErrorDetails.Detail"/>
    /// (the exception types and messages behind a failure) is sent to clients. Defaults to <c>false</c>:
    /// provider messages can carry SQL text and schema details that do not belong on a device.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }
}
