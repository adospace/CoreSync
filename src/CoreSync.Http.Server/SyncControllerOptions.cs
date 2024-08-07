using Microsoft.AspNetCore.Builder;
using System;

namespace CoreSync.Http.Server;

public class SyncControllerOptions
{
    public Action<IEndpointConventionBuilder>? AllEndpoints { get; set; }

    public Action<IEndpointConventionBuilder>? GetStoreIdEndpoint { get; set; }

    public Action<IEndpointConventionBuilder>? GetBulkChangeSetAsyncEndPoint { get; set; }

    public Action<IEndpointConventionBuilder>? GetBulkChangeSetItemEndPoint { get; set; }

    public Action<IEndpointConventionBuilder>? PostBeginApplyBulkChangesEndPoint { get; set; }

    public Action<IEndpointConventionBuilder>? PostApplyBulkChangesItemEndPoint { get; set; }

    public Action<IEndpointConventionBuilder>? PostCompleteApplyBulkChangesAsyncEndPoint { get; set; }

    public Action<IEndpointConventionBuilder>? PostSaveVersionForStoreAsyncEndPoint { get; set; }

}
