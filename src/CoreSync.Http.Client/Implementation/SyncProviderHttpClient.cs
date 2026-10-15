using CoreSync.Http;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Net.Http.Json;
using Polly.Retry;
using Polly;
using System.Net.Http.Headers;

namespace CoreSync.Http.Client.Implementation;

internal class SyncProviderHttpClient : ISyncProviderHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SyncProviderHttpClientOptions _options;

    public event EventHandler<SyncProgressEventArgs>? SyncProgress;

    public string[]? SyncTableNames => null;

    public SyncProviderHttpClient(IHttpClientFactory httpClientProvider, SyncProviderHttpClientOptions options)
    {
        _httpClientFactory = httpClientProvider;
        _options = options;
    }

    /// <summary>
    /// Throws for an unsuccessful response, rebuilding <see cref="SyncAnchorTooOldException"/> from the
    /// server's 410 Gone signal and <see cref="SyncHttpRequestException"/> from everything else, so that
    /// callers see what the server said instead of a bare status code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used in place of <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> on every sync endpoint:
    /// without it a failure arrives as a bare <see cref="HttpRequestException"/> and the only thing an
    /// application can tell the user is that synchronization failed.
    /// </para>
    /// <para>
    /// <see cref="SyncHttpRequestException"/> derives from <see cref="HttpRequestException"/> and carries
    /// the very message <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> would have produced, so
    /// the failure callers used to see is still there; the <see cref="SyncHttpHeaders.ErrorCode"/> header
    /// and <see cref="SyncErrorDetails"/> body are read when present and inferred from the status code
    /// when the server (an older one) sends neither.
    /// </para>
    /// </remarks>
    private static async Task EnsureSyncSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorCode = ReadHeader(response, SyncHttpHeaders.ErrorCode);
        var traceId = ReadHeader(response, SyncHttpHeaders.TraceId);

        if (response.StatusCode == HttpStatusCode.Gone && errorCode == SyncHttpErrorCodes.AnchorTooOld)
        {
            SyncAnchorTooOldError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<SyncAnchorTooOldError>(cancellationToken);
            }
            catch (JsonException)
            {
                // The header alone identifies the condition; a body we cannot read must not downgrade
                // this into an ordinary transport failure.
            }

            var requestedVersion = error?.RequestedVersion ?? -1;
            var minValidVersion = error?.MinValidVersion ?? -1;

            throw error?.TableName is { } tableName
                ? new SyncAnchorTooOldException(tableName, requestedVersion, minValidVersion)
                : new SyncAnchorTooOldException(requestedVersion, minValidVersion);
        }

        // The body is only read on the server's word: a JSON body without the header is the host's
        // (a ProblemDetails for a 404, an error page), and reading it as SyncErrorDetails would
        // invent an error code the server never sent.
        SyncErrorDetails? details = null;
        if (errorCode != null && IsJson(response.Content.Headers.ContentType))
        {
            try
            {
                details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>(cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                // A body in a shape we do not know is not a reason to lose the header or the status code.
            }
        }

        string message;
        try
        {
            response.EnsureSuccessStatusCode();
            return;
        }
        catch (HttpRequestException ex)
        {
            // Reused verbatim so the message callers have been reading (and logging) does not change.
            message = ex.Message;
        }

        throw new SyncHttpRequestException(message, response.StatusCode, errorCode, traceId, details);
    }

    private static string? ReadHeader(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static bool IsJson(MediaTypeHeaderValue? contentType)
        => contentType?.MediaType is { } mediaType
           && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Decides whether a failed bulk download request is worth repeating.
    /// </summary>
    /// <remarks>
    /// Only a failure the server marked as permanent is not retried: an aged-out anchor, an expired
    /// session, a data or configuration error. Everything else - a dropped connection, a timeout, a
    /// 5xx without an error code from an older server or a proxy - keeps the retries it always had,
    /// because those are what let a synchronization survive a flaky connection in the field.
    /// </remarks>
    private static bool IsWorthRetrying(Exception exception)
        => exception switch
        {
            SyncAnchorTooOldException => false,
            SyncHttpRequestException http => http.IsTransient,
            _ => true
        };

    public async Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);

        var sessionId = Guid.NewGuid();
        var bulkChangeSet = new BulkSyncChangeSet()
        {
            SessionId = sessionId,
            TotalChanges = changeSet.Items.Count,
            SourceAnchor = changeSet.SourceAnchor,
            TargetAnchor = changeSet.TargetAnchor,
            //ChangesByTable = changeSet.Items.GroupBy(_ => _.TableName).ToDictionary(_ => _.Key, _ => _.Sum(g => g.Values.Count))
        };

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ComputingLocalChanges));

        await EnsureSyncSuccessAsync(
            await httpClient.PostAsJsonAsync($"/{_options.SyncControllerRoute}/changes-bulk-begin", bulkChangeSet, cancellationToken),
            cancellationToken);

        var listOfItemsToUpload = new List<SyncItem>();

        for (int skip = 0; skip < changeSet.Items.Count; skip += _options.BulkItemSize)
        {
            listOfItemsToUpload.Clear();
            for (int i = skip; i < skip + _options.BulkItemSize && i < changeSet.Items.Count; i++)
            {
                listOfItemsToUpload.Add(changeSet.Items[i]);
            }

            if (_options.UseBinaryFormat)
            {
                var bulkUploadItem = new BulkChangeSetUploadItem()
                {
                    SessionId = sessionId,
                    Items = listOfItemsToUpload
                };

                var beginUploadItemContent = new ByteArrayContent(
                    CoreSyncMessagePackSerializer.Serialize<object>(bulkUploadItem, cancellationToken));

                beginUploadItemContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");

                await EnsureSyncSuccessAsync(
                    await httpClient.PostAsync($"{_options.SyncControllerRoute}/changes-bulk-item-binary", beginUploadItemContent, cancellationToken),
                    cancellationToken);
            }
            else
            {
                var beginUploadItemContent = new StringContent(JsonSerializer.Serialize(new BulkChangeSetUploadItem()
                {
                    SessionId = sessionId,
                    Items = listOfItemsToUpload
                }), Encoding.UTF8, "application/json");

                await EnsureSyncSuccessAsync(
                    await httpClient.PostAsync($"{_options.SyncControllerRoute}/changes-bulk-item", beginUploadItemContent, cancellationToken),
                    cancellationToken);
            }

            SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ApplyChanges, skip / (double)changeSet.Items.Count));
        }

        using var remoteChangeSetResponse = await httpClient.PostAsync(
            $"{_options.SyncControllerRoute}/changes-bulk-complete{(_options.UseBinaryFormat ? "-binary" : string.Empty)}/{sessionId}", null, cancellationToken);
        await EnsureSyncSuccessAsync(remoteChangeSetResponse, cancellationToken);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ApplyChanges, 1.0));

        return await remoteChangeSetResponse.Content.ReadFromJsonAsync<SyncAnchor>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException();
    }

    public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters, SyncDirection syncDirection, string[]? tables = null, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ComputingRemotingChanges));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{_options.SyncControllerRoute}/changes-bulk/{otherStoreId}");
        if (tables is { Length: > 0 })
        {
            request.Headers.Add(SyncHttpHeaders.Tables, string.Join(",", tables));
            request.Headers.Add(SyncHttpHeaders.TablesCount, tables.Length.ToString());
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSyncSuccessAsync(response, cancellationToken);

        var bulkSyncChangeSet = await response.Content.ReadFromJsonAsync<BulkSyncChangeSet>(cancellationToken)
            ?? throw new InvalidOperationException();

        if (_options.UseBinaryFormat)
        {
            return await DownloadBulkChangeSetBinary(bulkSyncChangeSet, SyncStage.GetChanges, cancellationToken);
        }
        else
        {
            return await DownloadBulkChangeSetJson(bulkSyncChangeSet, SyncStage.GetChanges, cancellationToken);
        }
    }

    private async Task<SyncChangeSet> DownloadBulkChangeSetJson(BulkSyncChangeSet bulkSyncChangeSet, SyncStage stage, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, 0.0));

        ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                // Polly's default handles every exception except a cancellation; keep that and
                // additionally stop on failures the server marked as permanent.
                ShouldHandle = args => new ValueTask<bool>(
                    args.Outcome.Exception is { } exception
                    && exception is not OperationCanceledException
                    && IsWorthRetrying(exception))
            })
            .Build(); // Builds the resilience pipeline

        List<SyncItem> items = [];
        for (var skip = 0; skip < bulkSyncChangeSet.TotalChanges; skip += _options.BulkItemSize)
        {
            await pipeline.ExecuteAsync(async (token) =>
            {
                // Headers first and disposed when done, as GetFromJsonAsync did before the response had
                // to be inspected for the error signal.
                using var response = await httpClient.GetAsync($"/{_options.SyncControllerRoute}/changes-bulk-item/{bulkSyncChangeSet.SessionId}/{skip}/{_options.BulkItemSize}", HttpCompletionOption.ResponseHeadersRead, token);

                await EnsureSyncSuccessAsync(response, token);

                var bulkItems = await response.Content.ReadFromJsonAsync<SyncItem[]>(cancellationToken: token)
                    ?? throw new InvalidOperationException();

                items.AddRange(bulkItems);
            }, cancellationToken);

            SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, items.Count / (double)bulkSyncChangeSet.TotalChanges));
        }

        var changeSet = new SyncChangeSet(bulkSyncChangeSet.SourceAnchor, bulkSyncChangeSet.TargetAnchor, items);

        ConvertJsonValueToNetObject(changeSet);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, 1.0));

        return changeSet;
    }

    private async Task<SyncChangeSet> DownloadBulkChangeSetBinary(BulkSyncChangeSet bulkSyncChangeSet, SyncStage stage, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, 0.0));

        List<SyncItem> items = [];
        for (var skip = 0; skip < bulkSyncChangeSet.TotalChanges; skip += _options.BulkItemSize)
        {
            int retry = 3;
            while (true)
            {
                retry--;

                try
                {
                    using var response = await httpClient.GetAsync($"{_options.SyncControllerRoute}/changes-bulk-item-binary/{bulkSyncChangeSet.SessionId}/{skip}/{_options.BulkItemSize}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    await EnsureSyncSuccessAsync(response, cancellationToken);

                    using var s = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var downloadedItems = await CoreSyncMessagePackSerializer.DeserializeAsync<object>(s, cancellationToken);
                    var bulkItems = (List<SyncItem>?)(downloadedItems) ?? throw new InvalidOperationException();
                    items.AddRange(bulkItems);
                    break;
                }
                catch (Exception ex) when (!IsWorthRetrying(ex))
                {
                    // Permanent for this client (the change history it asked for is gone, the session
                    // expired, the server refused the data). Retrying burns three round trips and
                    // fifteen seconds to reach the same answer.
                    throw;
                }
                catch (Exception)
                {
                    if (retry == 0)
                        throw;

                    await Task.Delay(5000, cancellationToken);
                }
            }

            SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, items.Count / (double)bulkSyncChangeSet.TotalChanges));
        }

        var changeSet = new SyncChangeSet(bulkSyncChangeSet.SourceAnchor, bulkSyncChangeSet.TargetAnchor, items);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(stage, 1.0));

        return changeSet;
    }


    private static void ConvertJsonValueToNetObject(SyncChangeSet changeSet)
    {
        foreach (var item in changeSet.Items)
        {
            foreach (var itemValueEntry in item.Values.Where(_ => _.Key != "__OP").ToList())
            {
                item.Values[itemValueEntry.Key].Value = itemValueEntry.Value.Value switch
                {
                    null => null,
                    JsonElement jsonElement => ConvertJsonValueToNetObject(jsonElement, itemValueEntry.Value.Type),
                    _ => itemValueEntry.Value.Value // already deserialized to the target type
                };

                //WARN: can't convert every Id to lowercase because sqlite is case sensitive!
                //if (itemValueEntry.Value.Value != null &&
                //    itemValueEntry.Value.Type == SyncItemValueType.String &&
                //    (itemValueEntry.Key == "Id" || itemValueEntry.Key.EndsWith("Id")))
                //{
                //    item.Values[itemValueEntry.Key].Value = itemValueEntry.Value.Value.ToString().ToLowerInvariant();
                //}
            }
        }
    }

    private static object? ConvertJsonValueToNetObject(JsonElement value, SyncItemValueType targetType)
    {
        return targetType switch
        {
            SyncItemValueType.Null => null,
            SyncItemValueType.String => value.GetString(),
            SyncItemValueType.Int32 => value.GetInt32(),
            SyncItemValueType.Float => value.GetSingle(),
            SyncItemValueType.Double => value.GetDouble(),
            SyncItemValueType.DateTime => value.GetDateTime(),
            SyncItemValueType.Boolean => value.GetBoolean(),
            SyncItemValueType.ByteArray => value.GetBytesFromBase64(),
            SyncItemValueType.Guid => value.GetGuid(),
            SyncItemValueType.Int64 => value.GetInt64(),
            SyncItemValueType.Decimal => value.GetDecimal(),
            _ => throw new NotSupportedException(),
        };
    }

    public async Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);
        using var res = await httpClient.GetAsync($"/{_options.SyncControllerRoute}/store-id", cancellationToken);

        // Kept ahead of the generic handling on purpose: applications have been catching this exact
        // type around SynchronizeAsync to trigger a re-authentication.
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException();

        await EnsureSyncSuccessAsync(res, cancellationToken);

        return Guid.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);
        using var response = await httpClient.PostAsync($"/{_options.SyncControllerRoute}/save-version/{otherStoreId}/{version}", null, cancellationToken);
        await EnsureSyncSuccessAsync(response, cancellationToken);
    }

    public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);
        using var response = await httpClient.GetAsync($"/{_options.SyncControllerRoute}/sync-version", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSyncSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<SyncVersion>(cancellationToken) ?? throw new InvalidOperationException();
    }
}
