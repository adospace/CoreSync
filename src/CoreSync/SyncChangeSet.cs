using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Represents a set of changes to be synchronized between two stores, along with version anchors
    /// that track the synchronization progress.
    /// </summary>
    public class SyncChangeSet
    {
        //public SyncChangeSet()
        //{ }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncChangeSet"/> class.
        /// </summary>
        /// <param name="sourceAnchor">The version anchor of the store that produced these changes.</param>
        /// <param name="targetAnchor">The version anchor of the store that last received changes from the source.</param>
        /// <param name="items">The collection of individual change items.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="sourceAnchor"/>, <paramref name="targetAnchor"/>, or <paramref name="items"/> is <c>null</c>.
        /// </exception>
        public SyncChangeSet(SyncAnchor sourceAnchor, SyncAnchor targetAnchor, IReadOnlyList<SyncItem> items)
        {
            Validate.NotNull(sourceAnchor, nameof(sourceAnchor));
            Validate.NotNull(targetAnchor, nameof(targetAnchor));
            Validate.NotNull(items, nameof(items));

            SourceAnchor = sourceAnchor;
            TargetAnchor = targetAnchor;
            Items = items;
        }

        /// <summary>
        /// Gets or sets the version anchor of the store that produced these changes.
        /// </summary>
        public SyncAnchor SourceAnchor { get; set; }

        /// <summary>
        /// Gets or sets the version anchor representing the last known version from the source store.
        /// </summary>
        public SyncAnchor TargetAnchor { get; set; }

        /// <summary>
        /// Gets or sets the collection of individual change items (inserts, updates, and deletes).
        /// </summary>
        public IReadOnlyList<SyncItem> Items { get; set; }
    }
}
