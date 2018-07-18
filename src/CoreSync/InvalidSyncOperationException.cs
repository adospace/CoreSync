using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// This exception is raised when <see cref="ISyncProvider.ApplyChangesAsync(SyncChangeSet)"/> is unable to apply a change set
    /// </summary>
    /// <remarks>This exception usually signal caller that provided changes has been already applied. 
    /// Consider for example when set of items and anchor are provided by a remote client that has 
    /// previously failed to get response from a first <see cref="ISyncProvider.ApplyChangesAsync(SyncChangeSet)"/> call.
    /// If this is the case calling code should take the candidate anchor <see cref="InvalidSyncOperationException.CandidateAnchor"/> and 
    /// call <see cref="ISyncProvider.GetIncreamentalChangesAsync(SyncAnchor)"/> to get latest changes since first <see cref="ISyncProvider.ApplyChangesAsync(SyncChangeSet)"/> call.</remarks>
    public class InvalidSyncOperationException : Exception
    {
        public InvalidSyncOperationException(SyncAnchor candidateAnchor)
        {
            CandidateAnchor = candidateAnchor;
        }

        public SyncAnchor CandidateAnchor { get; }
    }
}
