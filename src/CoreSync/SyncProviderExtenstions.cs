﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public static class SyncProviderExtenstions
    {
        public static Task<SyncAnchor> ApplyChangesAsync(this ISyncProviderBase provider, 
            SyncChangeSet changeSet, 
            ConflictResolution updateResultion, 
            ConflictResolution deleteResolution)
        {
            Validate.NotNull(provider, nameof(provider));

            return provider.ApplyChangesAsync(
                changeSet, new Func<SyncItem, ConflictResolution>((item) => 
                {
                    if (item.ChangeType == ChangeType.Update)
                        return updateResultion;
                    else if (item.ChangeType == ChangeType.Delete)
                        return deleteResolution;

                    return ConflictResolution.Skip;
                }));
        }

        public static Task<SyncChangeSet> GetChangesAsync(this ISyncProviderBase provider,
            Guid otherStoreId,
            SyncDirection syncDirection = SyncDirection.UploadAndDownload,
            CancellationToken cancellationToken = default) 
            => provider.GetChangesAsync(otherStoreId, null, syncDirection, cancellationToken);

    }
}
