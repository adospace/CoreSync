using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Http.Client;

/// <summary>
/// Extension methods for registering CoreSync HTTP client services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ISyncProviderHttpClient"/> and its dependencies in the service collection.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="optionsConfigure">
    /// An optional action to configure <see cref="SyncProviderHttpClientOptions"/>.
    /// </param>
    public static void AddCoreSyncHttpClient(this IServiceCollection services, Action<SyncProviderHttpClientOptions>? optionsConfigure = null)
    {
        var options = new SyncProviderHttpClientOptions();
        optionsConfigure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ISyncProviderHttpClient, Implementation.SyncProviderHttpClient>();
    }
}
