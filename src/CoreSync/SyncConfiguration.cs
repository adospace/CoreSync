using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncConfiguration(SyncTable[] tables)
    {
        public SyncTable[] Tables { get; } = tables;
    }
}
