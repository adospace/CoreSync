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

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet, [CanBeNull] Func<SyncItem, ConflictResolution> onConflictFunc = null);

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetChangesForStoreAsync(Guid otherStoreId);

        Task SaveVersionForStoreAsync(Guid otherStoreId, long version);
    }
}
