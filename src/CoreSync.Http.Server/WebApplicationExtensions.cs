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

/// <summary>
/// Extension methods for mapping CoreSync HTTP server endpoints in an ASP.NET Core application.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps all CoreSync synchronization endpoints on the specified <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="webApplication">The web application to add endpoints to.</param>
    /// <param name="route">The route prefix for all sync endpoints. Defaults to <c>"api/sync-agent"</c>.</param>
    /// <param name="optionsConfigure">An optional action to configure per-endpoint options via <see cref="SyncControllerOptions"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="route"/> is <c>null</c>, empty, or whitespace.</exception>
    public static void UseCoreSyncHttpServer(this WebApplication webApplication, string route = "api/sync-agent", Action<SyncControllerOptions>? optionsConfigure = null)
    {
        webApplication.MapCoreSyncHttpServerEndpoints(route, optionsConfigure);
    }

    /// <summary>
    /// Maps all CoreSync synchronization endpoints on the specified <see cref="IEndpointRouteBuilder"/>.
    /// Use this overload when configuring endpoints outside of <see cref="WebApplication"/> (e.g., inside route groups).
    /// </summary>
    /// <param name="webApplication">The endpoint route builder to add endpoints to.</param>
    /// <param name="route">The route prefix for all sync endpoints. Defaults to <c>"api/sync-agent"</c>.</param>
    /// <param name="optionsConfigure">An optional action to configure per-endpoint options via <see cref="SyncControllerOptions"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="route"/> is <c>null</c>, empty, or whitespace.</exception>
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
            (string storeId, HttpRequest request, [FromServices] SyncAgentController controller)
            => controller.GetBulkChangeSetAsync(Guid.Parse(storeId), request));
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
