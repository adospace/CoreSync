using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncConfiguration
    {
        public SyncTable[] Tables { get; protected set; }
    }
}
