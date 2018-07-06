using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync
{
    public static class SyncProviderExtenstions
    {
        public static Task<SyncAnchor> ApplyChangesAsync(this ISyncProvider provider, SyncChangeSet changeSet, ConflictResolution updateResultion, ConflictResolution deleteResolution)
        {
            Validate.NotNull(provider, nameof(provider));

            return provider.ApplyChangesAsync(
                changeSet, new Func<SyncItem, ConflictResolution>((item) => 
                {
                    if (item.ChangeType == ChangeType.Update)
                        return updateResultion;
                    else if (item.ChangeType == ChangeType.Delete)
                        return deleteResolution;

                    throw new NotSupportedException();
                }));
        }
    }
}
