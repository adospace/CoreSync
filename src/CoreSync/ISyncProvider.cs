using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProvider
    {
        [NotNull, ItemCanBeNull]
        Task<SyncAnchor> GetLastAnchorForRemoteStoreAsync(Guid storeId);

        [NotNull]
        Task<SyncAnchor> GetLocalAnchorAsync();

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetInitialSetAsync();

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetIncreamentalChangesAsync([NotNull] SyncAnchor anchor);

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null);
    }
}
