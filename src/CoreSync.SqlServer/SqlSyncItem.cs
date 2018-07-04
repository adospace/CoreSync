using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    internal class SqlSyncItem : SyncItem
    {
        internal SqlSyncItem(SqlSyncTable table, Dictionary<string, object> values) : base(values)
        {
        }
    }
}
