using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncAnchor : SyncAnchor
    {
        public long Version { get; }

        public SqlSyncAnchor(long version)
        {
            Version = version;
        }

    }
}
