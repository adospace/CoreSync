using System.Threading;
using System.Threading.Tasks;

namespace CoreSync
{
    public interface ISyncProvider : ISyncProviderBase
    {
        Task ApplyProvisionAsync(CancellationToken cancellationToken = default);

        Task RemoveProvisionAsync(CancellationToken cancellationToken = default);

        Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default);

        Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default);
    }
}
