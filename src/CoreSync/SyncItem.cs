using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncItem
    {
        protected SyncItem(SyncTable table, ChangeType changeType, Dictionary<string, object> values)
        {
            Validate.NotNull(table, nameof(table));
            Validate.NotNull(values, nameof(values));

            Table = table;
            ChangeType = changeType;
            Values = values;
        }

        public SyncTable Table { get; }
        public ChangeType ChangeType { get; }
        public Dictionary<string, object> Values { get; }
    }
}
