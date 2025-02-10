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
using MessagePack;
using System.Net.Http.Headers;

namespace CoreSync.Http.Client.Implementation;

internal class SyncProviderHttpClient : ISyncProviderHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SyncProviderHttpClientOptions _options;

    public event EventHandler<SyncProgressEventArgs>? SyncProgress;

    public SyncProviderHttpClient(IHttpClientFactory httpClientProvider, SyncProviderHttpClientOptions options)
    {
        _httpClientFactory = httpClientProvider;
        _options = options;
    }

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

        (await httpClient.PostAsJsonAsync($"/{_options.SyncControllerRoute}/changes-bulk-begin", bulkChangeSet, cancellationToken))
            .EnsureSuccessStatusCode();

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
                    MessagePackSerializer.Typeless.Serialize(bulkUploadItem, cancellationToken: cancellationToken));

                beginUploadItemContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");

                (await httpClient.PostAsync($"{_options.SyncControllerRoute}/changes-bulk-item-binary", beginUploadItemContent, cancellationToken))
                    .EnsureSuccessStatusCode();
            }
            else
            {
                var beginUploadItemContent = new StringContent(JsonSerializer.Serialize(new BulkChangeSetUploadItem()
                {
                    SessionId = sessionId,
                    Items = listOfItemsToUpload
                }), Encoding.UTF8, "application/json");

                (await httpClient.PostAsync($"{_options.SyncControllerRoute}/changes-bulk-item", beginUploadItemContent, cancellationToken))
                    .EnsureSuccessStatusCode();
            }

            SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ApplyChanges, skip / (double)changeSet.Items.Count));
        }

        var remoteChangeSetResponse = await httpClient.PostAsync(
            $"{_options.SyncControllerRoute}/changes-bulk-complete{(_options.UseBinaryFormat ? "-binary" : string.Empty)}/{sessionId}", null, cancellationToken);
        remoteChangeSetResponse.EnsureSuccessStatusCode();

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ApplyChanges, 1.0));

        return await remoteChangeSetResponse.Content.ReadFromJsonAsync<SyncAnchor>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException();
    }

    public async Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters, SyncDirection syncDirection, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);

        SyncProgress?.Invoke(this, new SyncProgressEventArgs(SyncStage.ComputingRemotingChanges));

        var bulkSyncChangeSet = await httpClient.GetFromJsonAsync<BulkSyncChangeSet>($"/{_options.SyncControllerRoute}/changes-bulk/{otherStoreId}", cancellationToken)
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
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = _options.MaxRetryAttempts }) // Add retry using the default options
            .Build(); // Builds the resilience pipeline

        List<SyncItem> items = [];
        for (var skip = 0; skip < bulkSyncChangeSet.TotalChanges; skip += _options.BulkItemSize)
        {
            await pipeline.ExecuteAsync(async (token) =>
            {
                var bulkItems = await httpClient.GetFromJsonAsync<SyncItem[]>($"/{_options.SyncControllerRoute}/changes-bulk-item/{bulkSyncChangeSet.SessionId}/{skip}/{_options.BulkItemSize}", cancellationToken: token)
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
                    var response = await httpClient.GetAsync($"{_options.SyncControllerRoute}/changes-bulk-item-binary/{bulkSyncChangeSet.SessionId}/{skip}/{_options.BulkItemSize}", cancellationToken);

                    response.EnsureSuccessStatusCode();

                    using var s = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var downloadedItems = await MessagePackSerializer.Typeless.DeserializeAsync(s, cancellationToken: cancellationToken);
                    var bulkItems = (List<SyncItem>?)(downloadedItems) ?? throw new InvalidOperationException();
                    items.AddRange(bulkItems);
                    break;
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
                item.Values[itemValueEntry.Key].Value = itemValueEntry.Value.Value == null ? null :
                    ConvertJsonValueToNetObject((JsonElement)itemValueEntry.Value.Value, itemValueEntry.Value.Type);

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
        var res = await httpClient.GetAsync($"/{_options.SyncControllerRoute}/store-id", cancellationToken);

        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException();

        res.EnsureSuccessStatusCode();

        return Guid.Parse(await res.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);
        (await httpClient.PostAsync($"/{_options.SyncControllerRoute}/save-version/{otherStoreId}/{version}", null, cancellationToken))
            .EnsureSuccessStatusCode();
    }

    public async Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
    {
        var httpClient = _httpClientFactory.CreateClient(_options.HttpClientName ?? Options.DefaultName);
        return await httpClient.GetFromJsonAsync<SyncVersion>($"/{_options.SyncControllerRoute}/sync-version", cancellationToken) ?? throw new InvalidOperationException();
    }
}
