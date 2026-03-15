using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync
{
    /// <summary>
    /// Raised when <see cref="ISyncProviderBase.ApplyChangesAsync"/> is unable to apply a change set
    /// because the changes have already been applied.
    /// </summary>
    /// <remarks>
    /// This typically occurs when a remote client retries after failing to receive a response from
    /// a previous apply call. Callers should use <see cref="CandidateAnchor"/> to retrieve only
    /// the changes made since the original apply operation.
    /// </remarks>
    public class InvalidSyncOperationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidSyncOperationException"/> class.
        /// </summary>
        /// <param name="candidateAnchor">The anchor representing the state after the already-applied changes.</param>
        public InvalidSyncOperationException(SyncAnchor candidateAnchor)
        {
            CandidateAnchor = candidateAnchor;
        }

        /// <summary>
        /// Gets the anchor that callers should use to retrieve changes since the already-applied operation.
        /// </summary>
        public SyncAnchor CandidateAnchor { get; }
    }
}
