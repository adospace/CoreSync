using Microsoft.Extensions.DependencyInjection;

namespace CoreSync.Http.Server;

/// <summary>
/// Extension methods for registering CoreSync HTTP server services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CoreSync HTTP server controller and its dependencies (memory cache) in the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    public static void AddCoreSyncHttpServer(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<SyncAgentController>();
        services.AddMemoryCache();
    }
}
