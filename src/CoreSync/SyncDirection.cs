using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Specifies the direction in which a table participates in synchronization.
    /// </summary>
    public enum SyncDirection
    {
        /// <summary>
        /// The table participates in both upload (local to remote) and download (remote to local) synchronization.
        /// </summary>
        UploadAndDownload,

        /// <summary>
        /// The table only uploads changes from local to remote.
        /// </summary>
        UploadOnly,

        /// <summary>
        /// The table only downloads changes from remote to local.
        /// </summary>
        DownloadOnly,
    }
}
