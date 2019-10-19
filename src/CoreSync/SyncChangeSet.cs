using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public class SyncChangeSet
    {
        public SyncChangeSet(SyncAnchor sourceAnchor, SyncAnchor targetAnchor, IReadOnlyList<SyncItem> items)
        {
            Validate.NotNull(sourceAnchor, nameof(sourceAnchor));
            Validate.NotNull(targetAnchor, nameof(targetAnchor));
            Validate.NotNull(items, nameof(items));

            SourceAnchor = sourceAnchor;
            TargetAnchor = targetAnchor;
            Items = items;
        }

        public SyncAnchor SourceAnchor { get; private set; }
        public SyncAnchor TargetAnchor { get; private set; }
        public IReadOnlyList<SyncItem> Items { get; private set; }
    }
}
