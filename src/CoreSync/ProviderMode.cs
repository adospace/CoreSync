using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Specifies the role of a sync provider in the synchronization topology.
    /// </summary>
    public enum ProviderMode
    {
        /// <summary>
        /// The provider can both send and receive changes.
        /// </summary>
        Bidirectional,

        /// <summary>
        /// The provider acts as the local (client) side of the sync.
        /// </summary>
        Local,

        /// <summary>
        /// The provider acts as the remote (server) side of the sync.
        /// </summary>
        Remote
    }
}
