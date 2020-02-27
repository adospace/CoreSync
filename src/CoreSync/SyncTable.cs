using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    public abstract class SyncTable
    {
        protected SyncTable(string name, SyncDirection syncDirection = SyncDirection.UploadAndDownload, bool skipInitialSnapshot = false)
        {
            Validate.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

            Name = name;
            SyncDirection = syncDirection;
            SkipInitialSnapshot = skipInitialSnapshot;
        }

        /// <summary>
        /// Name of the table/collection
        /// </summary>
        public string Name { get; }

        public SyncDirection SyncDirection { get; }

        public bool SkipInitialSnapshot { get; }

        public override string ToString()
        {
            return $"{Name}";
        }
    }
}
