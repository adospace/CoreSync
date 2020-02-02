using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Sqlite
{
    public class SqliteSyncItem : SyncItem
    {
        public SqliteSyncItem()
        { }

        public SqliteSyncItem(SqliteSyncTable table, ChangeType changeType, Dictionary<string, object> values) :
            base(table.Name, changeType, values)
        {
        }

    }
}
