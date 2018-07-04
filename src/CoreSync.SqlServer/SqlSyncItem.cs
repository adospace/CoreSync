using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    internal class SqlSyncItem : SyncItem
    {
        internal SqlSyncItem(SqlSyncTable table, ChangeType changeType, Dictionary<string, object> values) :
            base(table, changeType, values)
        {
        }

    }
}
