using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Specifies how a conflict should be resolved when applying changes to a store.
    /// </summary>
    public enum ConflictResolution
    {
        /// <summary>
        /// Skip the conflicting change, keeping the existing value in the target store.
        /// </summary>
        Skip = 0,

        /// <summary>
        /// Overwrite the existing value in the target store with the incoming change.
        /// </summary>
        ForceWrite = 1
    }
}
