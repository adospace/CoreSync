using System;

namespace CoreSync
{
    public sealed class SyncAnchor
    {
        public Guid StoreId { get; }
        public long Version { get; }

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
    }
}