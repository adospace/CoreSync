using MessagePack.AspNetCoreMvcFormatter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Http.Server;

public static class WebApplicationExtensions
{
    public static void UseCoreSyncHttpServer(this WebApplication webApplication, string route = "api/sync-agent", Action<SyncControllerOptions>? optionsConfigure = null)
    {
        webApplication.MapCoreSyncHttpServerEndpoints(route, optionsConfigure);
    }

    public static void MapCoreSyncHttpServerEndpoints(this IEndpointRouteBuilder webApplication, string route = "api/sync-agent", Action<SyncControllerOptions>? optionsConfigure = null)
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

        var getStoreEndPoint = webApplication.MapGet($"/{route}/store-id", 
            ([FromServices] SyncAgentController controller) 
            => controller.GetStoreIdAsync());
        ConfigureEndpoint(getStoreEndPoint, options.GetStoreIdEndpoint);

        var getBulkChangeSetAsyncEndPoint = webApplication.MapGet($"/{route}/changes-bulk/{{storeId}}", 
            (string storeId, [FromServices] SyncAgentController controller)
            => controller.GetBulkChangeSetAsync(Guid.Parse(storeId)));
        ConfigureEndpoint(getBulkChangeSetAsyncEndPoint, options.GetBulkChangeSetAsyncEndPoint);

        var getBulkChangeSetItemEndPoint = webApplication.MapGet(
            $"/{route}/changes-bulk-item/{{sessionId}}/{{skip}}/{{take}}", 
            (string sessionId, int skip, int take, [FromServices] SyncAgentController controller)
                => controller.GetBulkChangeSetItem(new BulkChangeSetDownloadItem
                {
                    SessionId = Guid.Parse(sessionId),
                    Skip = skip,
                    Take = take
                }));
        ConfigureEndpoint(getBulkChangeSetItemEndPoint, options.GetBulkChangeSetItemEndPoint);

        var getBulkChangeSetItemBinaryEndPoint = webApplication
            .MapGet(
                $"/{route}/changes-bulk-item-binary/{{sessionId}}/{{skip}}/{{take}}",
                (string sessionId, int skip, int take, [FromServices] SyncAgentController controller)
                    => Results.File(controller.GetBulkChangeSetItemBinary(new BulkChangeSetDownloadItem
                    {
                        SessionId = Guid.Parse(sessionId),
                        Skip = skip,
                        Take = take
                    }), "application/x-msgpack"))
            .AddMessagePackEndpointFilter();

        ConfigureEndpoint(getBulkChangeSetItemBinaryEndPoint, options.GetBulkChangeSetItemBinaryEndPoint);

        var postBeginApplyBulkChangesEndPoint = webApplication.MapPost(
            $"/{route}/changes-bulk-begin", 
            ([FromBody] BulkSyncChangeSet bulkChangeSet, [FromServices] SyncAgentController controller) 
            => controller.BeginApplyBulkChanges(bulkChangeSet));

        ConfigureEndpoint(postBeginApplyBulkChangesEndPoint, options.PostBeginApplyBulkChangesEndPoint);

        var postApplyBulkChangesItemEndPoint = webApplication.MapPost(
            $"/{route}/changes-bulk-item", 
            ([FromBody] BulkChangeSetUploadItem bulkUploadItem, [FromServices] SyncAgentController controller)
            => controller.ApplyBulkChangesItem(bulkUploadItem));
        ConfigureEndpoint(postApplyBulkChangesItemEndPoint, options.PostApplyBulkChangesItemEndPoint);

        var postApplyBulkChangesItemBinaryEndPoint = webApplication.MapPost(
            $"/{route}/changes-bulk-item-binary",
            (HttpRequest request, [FromServices] SyncAgentController controller)
            => controller.ApplyBulkChangesItemBinary(request))
            .AddMessagePackEndpointFilter();
        ConfigureEndpoint(postApplyBulkChangesItemBinaryEndPoint, options.PostApplyBulkChangesItemBinaryEndPoint);

        var postCompleteApplyBulkChangesAsyncEndPoint = webApplication.MapPost(
            $"/{route}/changes-bulk-complete/{{sessionId}}", 
            (string sessionId, [FromServices] SyncAgentController controller) 
            => controller.CompleteApplyBulkChangesAsync(Guid.Parse(sessionId)));
        ConfigureEndpoint(postCompleteApplyBulkChangesAsyncEndPoint, options.PostCompleteApplyBulkChangesAsyncEndPoint);

        var postCompleteApplyBulkChangesBinaryAsyncEndPoint = webApplication.MapPost(
            $"/{route}/changes-bulk-complete-binary/{{sessionId}}",
            (string sessionId, [FromServices] SyncAgentController controller)
            => controller.CompleteApplyBulkChangesBinaryAsync(Guid.Parse(sessionId)));
        ConfigureEndpoint(postCompleteApplyBulkChangesBinaryAsyncEndPoint, options.PostCompleteApplyBulkChangesBinaryAsyncEndPoint);

        var postSaveVersionForStoreAsyncEndPoint = webApplication.MapPost(
            $"/{route}/save-version/{{storeId}}/{{version}}", 
            (string storeId, long version, [FromServices] SyncAgentController controller) 
            => controller.SaveVersionForStoreAsync(Guid.Parse(storeId), version));
        ConfigureEndpoint(postSaveVersionForStoreAsyncEndPoint, options.PostSaveVersionForStoreAsyncEndPoint);
    }

}


internal static class EndpointMessagePackFilterEndpointExtensions
{
    public static TBuilder AddMessagePackEndpointFilter<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            {
                context.HttpContext.Request.Headers.TryGetValue("Accept", out StringValues acceptHeader);
                if (acceptHeader.Contains("application/x-msgpack"))
                {
                    context.HttpContext.Request.Headers.Accept = "application/x-msgpack";
                }
            }

            var result = await next(context);

            if (result is ResultExecutingContext resultExecutingContext)
            {
                context.HttpContext.Request.Headers.TryGetValue("Accept", out StringValues acceptHeader);
                if (acceptHeader.Contains("application/x-msgpack"))
                {
                    context.HttpContext.Response.ContentType = "application/x-msgpack";
                    if (resultExecutingContext.Result is ObjectResult objectResult)
                    {
                        var messagePackFormatter = new MessagePackOutputFormatter();
                        await messagePackFormatter.WriteAsync(new OutputFormatterWriteContext(
                            context.HttpContext,
                            (stream, encoding) => new StreamWriter(stream, encoding),
                            objectResult.DeclaredType,
                            objectResult.Value));
                        return objectResult;
                    }
                }
            }

            return result;
        });

        return builder;
    }

}