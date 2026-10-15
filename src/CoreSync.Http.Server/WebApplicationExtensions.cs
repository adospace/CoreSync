using MessagePack.AspNetCoreMvcFormatter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            // Added before the caller's own conventions so that a recognised failure (an aged-out
            // anchor, an expired session) reaches the client as a documented status with an error
            // code rather than whatever the host makes of an unhandled exception.
            endpoint.AddSyncErrorEndpointFilter(options);
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


internal static class SyncErrorFilterEndpointExtensions
{
    /// <summary>
    /// Translates the failures a sync endpoint can raise into responses a client can classify:
    /// a status code, the <see cref="SyncHttpHeaders.ErrorCode"/> and <see cref="SyncHttpHeaders.TraceId"/>
    /// headers, and a JSON body where the endpoint produces the response itself.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="SyncAnchorTooOldException"/>: 410 Gone, <see cref="SyncHttpErrorCodes.AnchorTooOld"/>,
    /// <see cref="SyncAnchorTooOldError"/> body. 410 is the honest code: the change history the client
    /// asked for existed once and is permanently gone, so unlike a 5xx there is nothing to retry.
    /// </description></item>
    /// <item><description>
    /// <see cref="SyncSessionNotFoundException"/>: 410 Gone, <see cref="SyncHttpErrorCodes.SessionExpired"/>,
    /// <see cref="SyncErrorDetails"/> body. Before it had a name this was an unhandled
    /// <see cref="InvalidOperationException"/>, i.e. a 500 that clients retried for nothing.
    /// </description></item>
    /// <item><description>
    /// Anything else keeps propagating to the host exactly as before - same status, same error page,
    /// same logging and error reporting - with the two headers added to the host's response through
    /// <see cref="HttpResponse.OnStarting"/>, which survives the <c>Response.Clear()</c> the exception
    /// handler middleware performs. <see cref="SyncControllerOptions.HandleUnexpectedExceptions"/>
    /// switches this to a 500 with a <see cref="SyncErrorDetails"/> body produced here.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static TBuilder AddSyncErrorEndpointFilter<TBuilder>(this TBuilder builder, SyncControllerOptions options) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var response = context.HttpContext.Response;

            try
            {
                return await next(context);
            }
            catch (SyncAnchorTooOldException ex)
            {
                response.Headers[SyncHttpHeaders.ErrorCode] = SyncHttpErrorCodes.AnchorTooOld;
                response.Headers[SyncHttpHeaders.TraceId] = context.HttpContext.TraceIdentifier;

                return Results.Json(
                    new SyncAnchorTooOldError
                    {
                        TableName = ex.TableName,
                        RequestedVersion = ex.RequestedVersion,
                        MinValidVersion = ex.MinValidVersion
                    },
                    statusCode: StatusCodes.Status410Gone);
            }
            catch (SyncSessionNotFoundException ex)
            {
                var details = SyncErrorResponse.Describe(context.HttpContext, SyncHttpErrorCodes.SessionExpired, ex, tableName: null, options.IncludeExceptionDetails);
                return SyncErrorResponse.Create(context.HttpContext, details, StatusCodes.Status410Gone);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var code = SyncErrorResponse.ClassifyUnexpected(ex, out var tableName);

                if (options.HandleUnexpectedExceptions)
                {
                    // The host's exception handler will not see this one, so make sure its loggers do.
                    context.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(SyncAgentController).Namespace!)
                        .LogError(ex, "Synchronization request {Method} {Path} failed ({ErrorCode})", context.HttpContext.Request.Method, context.HttpContext.Request.Path, code);

                    var details = SyncErrorResponse.Describe(context.HttpContext, code, ex, tableName, options.IncludeExceptionDetails);
                    return SyncErrorResponse.Create(context.HttpContext, details, StatusCodes.Status500InternalServerError);
                }

                // Not handled here: the host decides the status and the body, and keeps logging and
                // reporting the exception the way it always did. Only the headers are ours. They are
                // attached when the host's response starts because the exception handler middleware
                // clears every header set before it runs; OnStarting callbacks are not cleared.
                var traceId = context.HttpContext.TraceIdentifier;
                response.OnStarting(() =>
                {
                    if (!response.Headers.ContainsKey(SyncHttpHeaders.ErrorCode))
                    {
                        response.Headers[SyncHttpHeaders.ErrorCode] = code;
                        response.Headers[SyncHttpHeaders.TraceId] = traceId;
                    }

                    return Task.CompletedTask;
                });

                throw;
            }
        });

        return builder;
    }
}

/// <summary>
/// Builds the <see cref="SyncErrorDetails"/> responses of the sync endpoints.
/// </summary>
internal static class SyncErrorResponse
{
    /// <summary>
    /// Picks the error code for an exception the endpoint did not expect.
    /// </summary>
    /// <remarks>
    /// A <see cref="SynchronizationException"/> anywhere in the chain is a provider telling that the
    /// store refused a change (constraint violation, missing table, bad value), and it names the table
    /// when it can. <see cref="NotSupportedException"/> is what providers raise for a store they cannot
    /// synchronize as configured. Everything else is simply "the server failed", which still tells the
    /// client that the device is online.
    /// </remarks>
    public static string ClassifyUnexpected(Exception exception, out string? tableName)
    {
        tableName = null;

        for (var current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case SynchronizationException sync:
                    tableName = sync.TableName;
                    return SyncHttpErrorCodes.DataError;
                case NotSupportedException:
                    return SyncHttpErrorCodes.ConfigurationError;
            }
        }

        return SyncHttpErrorCodes.ServerError;
    }

    public static SyncErrorDetails Describe(HttpContext httpContext, string code, Exception exception, string? tableName, bool includeExceptionDetails)
    {
        var message = code switch
        {
            SyncHttpErrorCodes.SessionExpired => "The synchronization session is no longer available on the server. Start a new synchronization.",
            SyncHttpErrorCodes.DataError when tableName != null => $"The server could not apply the synchronized changes for table '{tableName}'.",
            SyncHttpErrorCodes.DataError => "The server could not apply the synchronized changes.",
            SyncHttpErrorCodes.ConfigurationError => "The server's data store is not configured in a way that can be synchronized.",
            _ => "The server could not process the synchronization request."
        };

        return new SyncErrorDetails
        {
            Code = code,
            Message = message,
            TableName = tableName,
            TraceId = httpContext.TraceIdentifier,
            Detail = includeExceptionDetails ? DescribeChain(exception) : null
        };
    }

    public static IResult Create(HttpContext httpContext, SyncErrorDetails details, int statusCode)
    {
        httpContext.Response.Headers[SyncHttpHeaders.ErrorCode] = details.Code;
        httpContext.Response.Headers[SyncHttpHeaders.TraceId] = details.TraceId;

        return Results.Json(details, statusCode: statusCode);
    }

    private static string DescribeChain(Exception exception)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var current = exception; current != null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().FullName}: {current.Message}");
        }

        return string.Join(" --> ", parts);
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
