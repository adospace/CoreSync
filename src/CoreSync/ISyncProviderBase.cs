using JetBrains.Annotations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProviderBase
    {
        [NotNull]
        Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default);

        Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default);

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null, CancellationToken cancellationToken = default);

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[] syncFilterParameters, SyncDirection syncDirection = SyncDirection.UploadAndDownload, CancellationToken cancellationToken = default);
    }
}
