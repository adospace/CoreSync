using System.Collections.Generic;

namespace CoreSync.SqlServerCT
{
    public class SqlServerCTSyncItem : SyncItem
    {
        public SqlServerCTSyncItem()
        {
        }

        public SqlServerCTSyncItem(SqlServerCTSyncTable table, ChangeType changeType, Dictionary<string, object?> values) :
            base(table.Name, changeType, values)
        {
        }
    }
}
