using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncChangeSet : SyncChangeSet
    {
        internal SqlSyncChangeSet(SyncAnchor anchor, IReadOnlyList<SyncItem> items) : base(anchor, items)
        {
        }
    }
}
