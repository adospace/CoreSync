using System.Collections.Generic;

namespace CoreSync.PostgreSQL
{
    internal class PostgreSQLSyncItem : SyncItem
    {
        public PostgreSQLSyncItem(PostgreSQLSyncTable table, ChangeType changeType, Dictionary<string, object?> values)
            : base(table.Name, changeType, values)
        {
        }
    }
} 