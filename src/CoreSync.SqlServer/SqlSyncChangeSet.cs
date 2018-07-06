using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncChangeSet : SyncChangeSet
    {
        public SqlSyncChangeSet(SqlSyncAnchor anchor, IReadOnlyList<SyncItem> items) : base(anchor, items)
        {
        }
    }
}
