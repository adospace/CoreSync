namespace CoreSync
{
    public class SyncVersion
    {
        public SyncVersion(long current, long minimum)
        {
            Current = current;
            Minimum = minimum;
        }

        public long Current { get; }
        public long Minimum { get; }

        public override string ToString()
        {
            return $"SyncVersion(Current={Current} Minimum={Minimum})";
        }
    }
}