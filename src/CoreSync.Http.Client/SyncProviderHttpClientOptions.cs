using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Http.Client;

/// <summary>
/// Configuration options for <see cref="ISyncProviderHttpClient"/>.
/// </summary>
public class SyncProviderHttpClientOptions
{
    /// <summary>
    /// Gets or sets the route prefix for the sync controller endpoints on the server.
    /// Defaults to <c>"api/sync-agent"</c>.
    /// </summary>
    public string SyncControllerRoute { get; set; } = "api/sync-agent";

    /// <summary>
    /// Gets or sets the named <see cref="System.Net.Http.HttpClient"/> to resolve from
    /// <see cref="System.Net.Http.IHttpClientFactory"/>. When <c>null</c>, the default client is used.
    /// </summary>
    public string? HttpClientName { get; set; }

    /// <summary>
    /// Gets or sets the number of sync items sent or received per bulk request.
    /// Defaults to 50.
    /// </summary>
    public int BulkItemSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient HTTP failures (using Polly).
    /// Defaults to 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets a value indicating whether to use MessagePack binary format for HTTP payloads.
    /// When <c>false</c>, JSON is used. Defaults to <c>false</c>.
    /// </summary>
    public bool UseBinaryFormat { get; set; }
}
