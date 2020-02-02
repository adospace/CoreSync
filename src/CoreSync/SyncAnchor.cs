using System;

namespace CoreSync
{
    public class SyncAnchor
    {
        public SyncAnchor()
        { }

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

        public Guid StoreId { get; set; }
        public long Version { get; set; }

        public override string ToString()
        {
            return $"Anchor {Version} (StoreId: {StoreId})";
        }
    }
}