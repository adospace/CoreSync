using System;

namespace CoreSync
{
    /// <summary>
    /// Represents a version marker that tracks synchronization progress for a specific store.
    /// Each anchor combines a store identifier with a monotonically increasing version number.
    /// </summary>
    public class SyncAnchor
    {
        /// <summary>
        /// Gets a sentinel anchor representing an uninitialized or null state (version = -1).
        /// </summary>
        public static SyncAnchor Null { get; } = new SyncAnchor() { Version = -1 };

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAnchor"/> class for deserialization.
        /// </summary>
        public SyncAnchor()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncAnchor"/> class.
        /// </summary>
        /// <param name="storeId">The unique identifier of the store. Must not be <see cref="Guid.Empty"/>.</param>
        /// <param name="version">The version number. Must be non-negative.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="storeId"/> is <see cref="Guid.Empty"/> or <paramref name="version"/> is negative.
        /// </exception>
        public SyncAnchor(Guid storeId, long version)
        {
            if (storeId == Guid.Empty)
            {
                throw new ArgumentException("Invalid store id", nameof(storeId));
            }
            if (version < 0)
            {
                throw new ArgumentException("Invalid version, must be not negative", nameof(version));
            }

            StoreId = storeId;
            Version = version;
        }

        /// <summary>
        /// Gets or sets the unique identifier of the store this anchor belongs to.
        /// </summary>
        public Guid StoreId { get; set; }

        /// <summary>
        /// Gets or sets the version number representing the synchronization progress.
        /// </summary>
        public long Version { get; set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Version == -1 ? "Null Anchor" : $"Anchor {Version} (StoreId: {StoreId})";
        }

        /// <summary>
        /// Determines whether this anchor represents the null (uninitialized) state.
        /// </summary>
        /// <returns><c>true</c> if the version is -1; otherwise, <c>false</c>.</returns>
        public bool IsNull() => Version == -1;
    }
}
