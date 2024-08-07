using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Http.Client;

public static class ServiceCollectionExtensions
{
    public static void AddCoreSyncHttpClient(this IServiceCollection services, Action<SyncProviderHttpClientOptions>? optionsConfigure = null)
    {
        var options = new SyncProviderHttpClientOptions();
        optionsConfigure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ISyncProviderHttpClient, Implementation.SyncProviderHttpClient>();
    }
}
