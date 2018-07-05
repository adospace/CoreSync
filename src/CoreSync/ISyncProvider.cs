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
        Task ApplyProvisionAsync();

        [NotNull]
        Task RemoveProvisionAsync();

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetInitialSetAsync();

        [NotNull, ItemNotNull]
        Task<SyncChangeSet> GetIncreamentalChangesAsync([NotNull] SyncAnchor anchor);

        [NotNull, ItemNotNull]
        Task<SyncAnchor> ApplyChangesAsync([NotNull] SyncChangeSet changeSet);
    }
}
