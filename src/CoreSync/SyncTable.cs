using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncTable
    {
        protected SyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false, string selectIncrementalQuery = null, string customSnapshotQuery = null)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            Name = name;
            SyncDirection = syncDirection;
            SkipInitialSnapshot = skipInitialSnapshot;
            SelectIncrementalQuery = selectIncrementalQuery;
            CustomSnapshotQuery = customSnapshotQuery;
        }

        /// <summary>
        /// Name of the table/collection
        /// </summary>
        public string Name { get; }

        public SyncDirection SyncDirection { get; set; }

        public bool SkipInitialSnapshot { get; set; }

        public string CustomSnapshotQuery { get; set; }

        public string SelectIncrementalQuery { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }
    }
}
