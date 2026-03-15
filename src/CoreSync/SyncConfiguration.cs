using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Base class for sync configurations that hold the set of tables participating in synchronization.
    /// </summary>
    public abstract class SyncConfiguration(SyncTable[] tables)
    {
        /// <summary>
        /// Gets the tables registered for synchronization.
        /// </summary>
        public SyncTable[] Tables { get; } = tables;
    }
}
