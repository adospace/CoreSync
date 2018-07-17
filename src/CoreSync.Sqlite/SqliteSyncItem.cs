using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Sqlite
{
    internal class SqliteSyncItem : SyncItem
    {
        internal SqliteSyncItem(SqliteSyncTable table, ChangeType changeType, Dictionary<string, object> values) :
            base(table, changeType, values)
        {
        }

    }
}
