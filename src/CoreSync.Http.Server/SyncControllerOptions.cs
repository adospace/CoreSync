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

}
