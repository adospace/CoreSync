using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using System;

namespace CoreSync.Http.Server;

public static class WebApplicationExtensions
{
    public static void UseCoreSyncHttpServer(this WebApplication webApplication, string route = "api/sync-agent", Action<SyncControllerOptions>? optionsConfigure = null)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            throw new ArgumentException("Parameter can't be null or empty", nameof(route));
        }

        var options = new SyncControllerOptions();
        optionsConfigure?.Invoke(options);

        void ConfigureEndpoint<T>(T endpoint, Action<T>? specificAction = null) where T : IEndpointConventionBuilder
        {
            options.AllEndpoints?.Invoke(endpoint);
            specificAction?.Invoke(endpoint);
        }

        var getStoreEndPoint = webApplication.MapGet($"/{route}/store-id", async (SyncAgentController controller) => await controller.GetStoreIdAsync());
        ConfigureEndpoint(getStoreEndPoint, options.GetStoreIdEndpoint);

        var getBulkChangeSetAsyncEndPoint = webApplication.MapGet($"/{route}/changes-bulk/{{storeId}}", async (string storeId, SyncAgentController controller) => await controller.GetBulkChangeSetAsync(Guid.Parse(storeId)));
        ConfigureEndpoint(getBulkChangeSetAsyncEndPoint, options.GetBulkChangeSetAsyncEndPoint);

        var getBulkChangeSetItemEndPoint = webApplication.MapGet($"/{route}/changes-bulk-item/{{sessionId}}/{{skip}}/{{take}}", (string sessionId, int skip, int take, SyncAgentController controller) 
                => controller.GetBulkChangeSetItem(new BulkChangeSetDownloadItem
                { 
                    SessionId = Guid.Parse(sessionId),
                    Skip = skip,
                    Take = take
                }));
        ConfigureEndpoint(getBulkChangeSetItemEndPoint, options.GetBulkChangeSetItemEndPoint);

        var postBeginApplyBulkChangesEndPoint = webApplication.MapPost($"/{route}/changes-bulk-begin", (BulkSyncChangeSet bulkChangeSet, SyncAgentController controller) => controller.BeginApplyBulkChanges(bulkChangeSet));
        ConfigureEndpoint(postBeginApplyBulkChangesEndPoint, options.PostBeginApplyBulkChangesEndPoint);

        var postApplyBulkChangesItemEndPoint = webApplication.MapPost($"/{route}/changes-bulk-item", (BulkChangeSetUploadItem bulkUploadItem, SyncAgentController controller) => controller.ApplyBulkChangesItem(bulkUploadItem));
        ConfigureEndpoint(postApplyBulkChangesItemEndPoint, options.PostApplyBulkChangesItemEndPoint);

        var postCompleteApplyBulkChangesAsyncEndPoint = webApplication.MapPost($"/{route}/changes-bulk-complete/{{sessionId}}", async (string sessionId, SyncAgentController controller) => await controller.CompleteApplyBulkChangesAsync(Guid.Parse(sessionId)));
        ConfigureEndpoint(postCompleteApplyBulkChangesAsyncEndPoint, options.PostCompleteApplyBulkChangesAsyncEndPoint);

        var postSaveVersionForStoreAsyncEndPoint = webApplication.MapPost($"/{route}/save-version/{{storeId}}/{{version}}", async (string storeId, long version, SyncAgentController controller) => await controller.SaveVersionForStoreAsync(Guid.Parse(storeId), version));
        ConfigureEndpoint(postSaveVersionForStoreAsyncEndPoint, options.PostSaveVersionForStoreAsyncEndPoint);
    }
}
