using CoreSync.Http.Client;
using CoreSync.Http.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;

namespace CoreSync.Tests;

/// <summary>
/// Spins up an in-memory ASP.NET Core test server that hosts the CoreSync HTTP endpoints
/// backed by a real <see cref="ISyncProvider"/>, and provides an <see cref="ISyncProviderHttpClient"/>
/// that communicates with it over HTTP.
/// </summary>
internal sealed class SyncTestServer : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _httpClient;
    private readonly ServiceProvider _clientServiceProvider;

    public ISyncProviderHttpClient HttpSyncProvider { get; }

    private SyncTestServer(WebApplication app, HttpClient httpClient, ServiceProvider clientServiceProvider, ISyncProviderHttpClient httpSyncProvider)
    {
        _app = app;
        _httpClient = httpClient;
        _clientServiceProvider = clientServiceProvider;
        HttpSyncProvider = httpSyncProvider;
    }

    public static SyncTestServer Create(ISyncProvider remoteSyncProvider, bool useBinaryFormat = false)
    {
        // Build and start the server
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddCoreSyncHttpServer();
        builder.Services.AddSingleton<ISyncProvider>(remoteSyncProvider);

        var app = builder.Build();
        app.UseCoreSyncHttpServer();
        app.StartAsync().GetAwaiter().GetResult();

        var httpClient = app.GetTestServer().CreateClient();

        // Build the client side via DI so we get the real SyncProviderHttpClient
        var clientServices = new ServiceCollection();
        clientServices.AddSingleton<IHttpClientFactory>(new SingleClientFactory(httpClient));
        clientServices.AddCoreSyncHttpClient(options =>
        {
            options.UseBinaryFormat = useBinaryFormat;
        });

        var clientServiceProvider = clientServices.BuildServiceProvider();
        var httpSyncProvider = clientServiceProvider.GetRequiredService<ISyncProviderHttpClient>();

        return new SyncTestServer(app, httpClient, clientServiceProvider, httpSyncProvider);
    }

    public void Dispose()
    {
        _clientServiceProvider.Dispose();
        _httpClient.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        (_app as IDisposable)?.Dispose();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
