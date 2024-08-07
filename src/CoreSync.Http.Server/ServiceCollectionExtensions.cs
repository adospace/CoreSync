using Microsoft.Extensions.DependencyInjection;

namespace CoreSync.Http.Server;

public static class ServiceCollectionExtensions
{
    public static void AddCoreSyncHttpServer(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<SyncAgentController>();
    }
}
