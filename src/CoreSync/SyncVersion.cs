namespace CoreSync
{
    /// <summary>
    /// Represents the current and minimum version numbers of a sync store's change tracking history.
    /// </summary>
    public class SyncVersion
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SyncVersion"/> class.
        /// </summary>
        /// <param name="current">The current (latest) version number in the change tracking history.</param>
        /// <param name="minimum">The oldest version number still available in the change tracking history.</param>
        public SyncVersion(long current, long minimum)
        {
            Current = current;
            Minimum = minimum;
        }

        /// <summary>
        /// Gets the current (latest) version number in the change tracking history.
        /// </summary>
        public long Current { get; }

        /// <summary>
        /// Gets the oldest version number still available in the change tracking history.
        /// </summary>
        public long Minimum { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"SyncVersion(Current={Current} Minimum={Minimum})";
        }
    }
}
