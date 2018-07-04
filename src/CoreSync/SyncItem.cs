using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncItem
    {
        protected SyncItem(ChangeType changeType, Dictionary<string, object> values)
        {
            Validate.NotNull(values, nameof(values));

            ChangeType = changeType;
            Values = values;
        }

        public ChangeType ChangeType { get; }
        public Dictionary<string, object> Values { get; }
    }
}
