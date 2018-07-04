using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncItem
    {
        protected SyncItem(Dictionary<string, object> values)
        {
            Values = values;
        }

        public Dictionary<string, object> Values { get; }
    }
}
