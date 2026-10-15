using CoreSync.Http.Client;
using CoreSync.Http.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    /// <summary>
    /// Body the simulated host error page writes for an unhandled exception, so that tests can tell
    /// a response produced by the host from one produced by the sync endpoints.
    /// </summary>
    public const string HostErrorPageBody = "host error page";

    private readonly WebApplication _app;
    private readonly HttpClient _httpClient;
    private readonly ServiceProvider _clientServiceProvider;

    public ISyncProviderHttpClient HttpSyncProvider { get; }

    /// <summary>
    /// The underlying <see cref="HttpClient"/>, for tests that need to assert on the raw response
    /// (status code, headers, body) rather than on what the sync provider makes of it.
    /// </summary>
    public HttpClient RawHttpClient => _httpClient;

    private SyncTestServer(WebApplication app, HttpClient httpClient, ServiceProvider clientServiceProvider, ISyncProviderHttpClient httpSyncProvider)
    {
        _app = app;
        _httpClient = httpClient;
        _clientServiceProvider = clientServiceProvider;
        HttpSyncProvider = httpSyncProvider;
    }

    /// <param name="remoteSyncProvider">The provider the endpoints are backed by.</param>
    /// <param name="useBinaryFormat">Whether the client uses the message pack endpoints.</param>
    /// <param name="configureOptions">Optional <see cref="SyncControllerOptions"/> configuration.</param>
    /// <param name="useHostExceptionHandler">
    /// When <c>true</c> the app gets an exception handler that answers 500 with <see cref="HostErrorPageBody"/>,
    /// the way a production host does; without it the test server surfaces unhandled exceptions
    /// straight to the calling test, which is convenient but is not what a client ever sees.
    /// </param>
    /// <param name="configureServices">
    /// Optional hook run after the CoreSync services are registered, for tests that need to replace
    /// one of them (for example the memory cache that holds upload/download sessions).
    /// </param>
    public static SyncTestServer Create(
        ISyncProvider remoteSyncProvider,
        bool useBinaryFormat = false,
        Action<SyncControllerOptions>? configureOptions = null,
        bool useHostExceptionHandler = false,
        Action<IServiceCollection>? configureServices = null)
    {
        // Build and start the server
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddCoreSyncHttpServer();
        builder.Services.AddSingleton<ISyncProvider>(remoteSyncProvider);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        if (useHostExceptionHandler)
        {
            app.UseExceptionHandler(errorApp => errorApp.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "text/plain";
                return context.Response.WriteAsync(HostErrorPageBody);
            }));
        }

        app.UseCoreSyncHttpServer(optionsConfigure: configureOptions);
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

    /// <summary>
    /// Builds an <see cref="ISyncProviderHttpClient"/> over a scripted <see cref="HttpMessageHandler"/>
    /// instead of a real server, to simulate servers this code base does not host: older versions that
    /// send no error signal, proxies, an unreachable network.
    /// </summary>
    public static ISyncProviderHttpClient CreateClient(HttpMessageHandler handler, bool useBinaryFormat = false, int maxRetryAttempts = 3)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://sync.test/") };

        var clientServices = new ServiceCollection();
        clientServices.AddSingleton<IHttpClientFactory>(new SingleClientFactory(httpClient));
        clientServices.AddCoreSyncHttpClient(options =>
        {
            options.UseBinaryFormat = useBinaryFormat;
            options.MaxRetryAttempts = maxRetryAttempts;
        });

        return clientServices.BuildServiceProvider().GetRequiredService<ISyncProviderHttpClient>();
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
