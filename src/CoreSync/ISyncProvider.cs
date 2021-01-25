using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProvider
    {
        [NotNull]
        Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default);

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null, CancellationToken cancellationToken = default);

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncDirection syncDirection = SyncDirection.UploadAndDownload, CancellationToken cancellationToken = default);

        Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default);

        Task ApplyProvisionAsync(CancellationToken cancellationToken = default);

        Task RemoveProvisionAsync(CancellationToken cancellationToken = default);

        Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default);

        Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default);
    }
}
