using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProvider
    {
        [NotNull]
        Task<Guid> GetStoreIdAsync();

        //Task<SyncChangeSet> GetInitialSnapshotAsync(Guid otherStoreId, SyncDirection syncDirection = SyncDirection.UploadAndDownload);

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null);

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncDirection syncDirection = SyncDirection.UploadAndDownload);

        Task SaveVersionForStoreAsync(Guid otherStoreId, long version);

        Task ApplyProvisionAsync();

        Task RemoveProvisionAsync();

        Task<SyncVersion> GetSyncVersionAsync();

        Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion);
    }
}
