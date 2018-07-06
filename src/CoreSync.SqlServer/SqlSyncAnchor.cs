using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncAnchor : SyncAnchor
    {
        public long Version { get; }

        internal SqlSyncAnchor(long version)
        {
            Version = version;
        }

    }
}
