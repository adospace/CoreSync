using System;

namespace CoreSync
{
    public sealed class SyncAnchor
    {
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

        public Guid StoreId { get; private set; }
        public long Version { get; private set; }

        public override string ToString()
        {
            return $"Anchor {Version} (StoreId: {StoreId})";
        }
    }
}