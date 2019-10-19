using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProvider
    {
        //[NotNull, ItemCanBeNull]
        //Task<SyncAnchor> GetLastAnchorForStoreAsync(Guid otherStoreId);

        //[NotNull]
        //Task<SyncAnchor> GetLocalAnchorAsync();

        //[NotNull, ItemNotNull]
        //Task<SyncChangeSet> GetInitialSetAsync(Guid otherStoreId);

        //[NotNull, ItemNotNull]
        //Task<SyncChangeSet> GetIncrementalChangesAsync([NotNull] SyncAnchor anchor);

        [NotNull]
        Task<Guid> GetStoreIdAsync();

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null);

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId);
    }
}
