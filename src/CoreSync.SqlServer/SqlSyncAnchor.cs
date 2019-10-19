using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncAnchor : SyncAnchor
    {
        public long Version { get; }

        public SqlSyncAnchor(Guid storeId, long version)
            :base(storeId)
        {
            Version = version;
        }

    }
}
