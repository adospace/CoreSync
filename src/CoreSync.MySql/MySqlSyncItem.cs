using System.Collections.Generic;

namespace CoreSync.MySql
{
    internal class MySqlSyncItem : SyncItem
    {
        public MySqlSyncItem(MySqlSyncTable table, ChangeType changeType, Dictionary<string, object?> values)
            : base(table.Name, changeType, values)
        {
        }
    }
}
