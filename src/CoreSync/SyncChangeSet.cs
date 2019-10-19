using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SyncChangeSet
    {
        public SyncChangeSet(SyncAnchor anchor, IReadOnlyList<SyncItem> items)
        {
            Validate.NotNull(anchor, nameof(anchor));
            Validate.NotNull(items, nameof(items));

            Anchor = anchor;
            Items = items;
        }

        public SyncAnchor Anchor { get; private set; }
        public IReadOnlyList<SyncItem> Items { get; private set; }
    }
}
