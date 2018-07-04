using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncChangeSet
    {
        protected SyncChangeSet(SyncAnchor anchor, IReadOnlyList<SyncItem> items)
        {
            Anchor = anchor;
            Items = items;
        }

        public SyncAnchor Anchor { get; }
        public IReadOnlyList<SyncItem> Items { get; }
    }
}
