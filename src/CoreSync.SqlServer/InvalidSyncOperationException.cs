using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.SqlServer
{
    /// <summary>
    /// This exception is raised when <see cref="SqlSyncProvider.ApplyChangesAsync(SyncChangeSet)"/> is unable to apply a change set
    /// </summary>
    /// <remarks>This exception usually signal caller that provided changes has been already applied. 
    /// Consider for example when set of items and anchor are provided by a remote client that has 
    /// previously failed to get response from a first <see cref="SqlSyncProvider.ApplyChangesAsync(SyncChangeSet)"/> call.
    /// If this is the case calling code should take the candidate anchor <see cref="InvalidSyncOperationException.CandidateAnchor"/> and 
    /// call <see cref="SqlSyncProvider.GetIncreamentalChangesAsync(SyncAnchor)"/> to get latest changes since first <see cref="SqlSyncProvider.ApplyChangesAsync(SyncChangeSet)"/> call.</remarks>
    public class InvalidSyncOperationException : Exception
    {
        internal InvalidSyncOperationException(SqlSyncAnchor candidateAnchor)
        {
            CandidateAnchor = candidateAnchor;
        }

        public SqlSyncAnchor CandidateAnchor { get; }
    }
}
