using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    public class SqlSyncItem : SyncItem
    {
        public SqlSyncItem()
        { 
        }

        public SqlSyncItem(SqlSyncTable table, ChangeType changeType, Dictionary<string, object?> values) :
            base(table.Name, changeType, values)
        {
        }

    }
}
