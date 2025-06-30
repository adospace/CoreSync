using MessagePack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CoreSync.Http.Server;

class SyncAgentController
{
    public class CachedSyncChangeSet
    {
        public required SyncChangeSet ChangeSet { get; set; }
        public List<SyncItem> BufferList { get; set; } = [];
    }

    private readonly ILogger<SyncAgentController> _logger;
    private readonly ISyncProvider _syncProvider;
    private readonly IMemoryCache _memoryCache;

    public SyncAgentController(
        ILogger<SyncAgentController> logger,
        ISyncProvider syncProvider,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _syncProvider = syncProvider;
        _memoryCache = memoryCache;
    }

    public async Task<string> GetStoreIdAsync()
    {
        var result = await _syncProvider.GetStoreIdAsync();

        return result.ToString();
    }

    public async Task<BulkSyncChangeSet> GetBulkChangeSetAsync(Guid storeId)
    {
        var changeSet = await _syncProvider.GetChangesAsync(storeId, SyncDirection.DownloadOnly);

        _logger.LogInformation("GetBulkChangeSetAsync({StoreId})->(Source={SourceAnchor} Target={TargetAnchor} Items={ItemsCount})",
            storeId,
            changeSet.SourceAnchor,
            changeSet.TargetAnchor,
            changeSet.Items.Count);

        var sessionId = Guid.NewGuid();

        _memoryCache.Set(sessionId, new CachedSyncChangeSet { ChangeSet = changeSet });

        return new BulkSyncChangeSet()
        {
            SessionId = sessionId,
            TotalChanges = changeSet.Items.Count,
            SourceAnchor = changeSet.SourceAnchor,
            TargetAnchor = changeSet.TargetAnchor,
            ChangesByTable = changeSet.Items
                .GroupBy(_ => _.TableName)
                .ToDictionary(_ => _.Key, _ => _.Count())
        };
    }

    public IReadOnlyList<SyncItem> GetBulkChangeSetItem([FromQuery] BulkChangeSetDownloadItem item)
    {
        if (_memoryCache.TryGetValue(item.SessionId, out var bulkChangeSetObject) &&
            bulkChangeSetObject is CachedSyncChangeSet cachedSyncChangeSet)
        {
            var bufferList = cachedSyncChangeSet.BufferList;
            bufferList.Clear();

            // Add the items directly by index
            for (int i = item.Skip; i < item.Skip + item.Take && i < cachedSyncChangeSet.ChangeSet.Items.Count; i++)
            {
                bufferList.Add(cachedSyncChangeSet.ChangeSet.Items[i]);
            }

            if (item.Skip + item.Take >= cachedSyncChangeSet.ChangeSet.Items.Count)
                _memoryCache.Remove(item.SessionId);

            return bufferList;
        }

        throw new InvalidOperationException();
    }

    public byte[] GetBulkChangeSetItemBinary([FromBody] BulkChangeSetDownloadItem item)
    {
        if (_memoryCache.TryGetValue(item.SessionId, out var bulkChangeSetObject) &&
            bulkChangeSetObject is CachedSyncChangeSet cachedSyncChangeSet)
        {
            // Directly access items by index
            var bufferList = cachedSyncChangeSet.BufferList;
            bufferList.Clear();

            // Add the items directly by index
            for (int i = item.Skip; i < item.Skip + item.Take && i < cachedSyncChangeSet.ChangeSet.Items.Count; i++)
            {
                bufferList.Add(cachedSyncChangeSet.ChangeSet.Items[i]);
            }

            if (item.Skip + item.Take >= cachedSyncChangeSet.ChangeSet.Items.Count)
                _memoryCache.Remove(item.SessionId);

            return MessagePackSerializer.Typeless.Serialize(bufferList);
        }

        throw new InvalidOperationException();
    }

    public void BeginApplyBulkChanges(BulkSyncChangeSet bulkChangeSet)
    {
        var changeSet = new SyncChangeSet(bulkChangeSet.SourceAnchor, bulkChangeSet.TargetAnchor, new List<SyncItem>() /* do not change to []*/);

        _memoryCache.Set(bulkChangeSet.SessionId, changeSet);
    }

    public void ApplyBulkChangesItem(BulkChangeSetUploadItem bulkUploadItem)
    {
        if (_memoryCache.TryGetValue(bulkUploadItem.SessionId, out var changeSetObject) &&
            changeSetObject is SyncChangeSet changeSet)
        {
            ((List<SyncItem>)changeSet.Items).AddRange(bulkUploadItem.Items);
            return;
        }

        throw new InvalidOperationException();
    }

    public async Task ApplyBulkChangesItemBinary(HttpRequest httpRequest)
    {
        var bulkUploadItem = ((BulkChangeSetUploadItem?)
            await MessagePackSerializer.Typeless.DeserializeAsync(httpRequest.Body)) ?? throw new InvalidProgramException();

        if (_memoryCache.TryGetValue(bulkUploadItem.SessionId, out var changeSetObject) &&
            changeSetObject is SyncChangeSet changeSet)
        {
            ((List<SyncItem>)changeSet.Items).AddRange(bulkUploadItem.Items);
            return;
        }

        throw new InvalidOperationException();
    }

    public async Task<SyncAnchor> CompleteApplyBulkChangesAsync(Guid sessionId)
    {
        if (_memoryCache.TryGetValue(sessionId, out var changeSetObject) &&
           changeSetObject is SyncChangeSet changeSet)
        {
            foreach (var item in changeSet.Items)
            {
                foreach (var itemValueEntry in item.Values.Where(_ => _.Key != "__OP").ToList())
                {
                    item.Values[itemValueEntry.Key].Value = itemValueEntry.Value.Value == null ? null :
                        ConvertJsonElementToObject((JsonElement)itemValueEntry.Value.Value, itemValueEntry.Value.Type);
                }
            }

            var resAnchor = await _syncProvider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

            _memoryCache.Remove(sessionId);

            _logger.LogInformation("CompleteApplyBulkChangesAsync() => {resAnchor}", resAnchor);

            return resAnchor;
        }

        throw new InvalidOperationException();
    }

    public async Task<SyncAnchor> CompleteApplyBulkChangesBinaryAsync(Guid sessionId)
    {
        if (_memoryCache.TryGetValue(sessionId, out var changeSetObject) &&
           changeSetObject is SyncChangeSet changeSet)
        {
            var resAnchor = await _syncProvider.ApplyChangesAsync(changeSet, updateResultion: ConflictResolution.ForceWrite, deleteResolution: ConflictResolution.Skip);

            _memoryCache.Remove(sessionId);

            _logger.LogInformation("CompleteApplyBulkChangesBinaryAsync() => {resAnchor}", resAnchor);

            return resAnchor;
        }

        throw new InvalidOperationException();
    }


    private static object? ConvertJsonElementToObject(JsonElement value, SyncItemValueType targetType)
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

    public async Task SaveVersionForStoreAsync(Guid storeId, long version)
    {
        _logger.LogInformation("SaveVersionForStoreAsync(storeId={storeId}, version={version})", storeId, version);

        await _syncProvider.SaveVersionForStoreAsync(storeId, version);
    }

}

