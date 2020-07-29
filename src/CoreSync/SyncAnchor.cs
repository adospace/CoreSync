using System;

namespace CoreSync
{
    public class SyncAnchor
    {
        public static SyncAnchor Null { get; } = new SyncAnchor() { Version = -1 };

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
            return Version == -1 ? "Null Anchor" : $"Anchor {Version} (StoreId: {StoreId})";
        }

        public bool IsNull() => Version == -1;
    }
}