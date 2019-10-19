using System;

namespace CoreSync
{
    public abstract class SyncAnchor
    {
        public Guid StoreId { get; }
        protected SyncAnchor(Guid storeId)
        {
            if (storeId == Guid.Empty)
            {
                throw new ArgumentException("Invalid store id", nameof(storeId));
            }

            StoreId = storeId;
        }
    }
}