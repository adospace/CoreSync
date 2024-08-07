using System;
using System.Collections.Generic;
using System.Text;

namespace CoreSync.Http.Client;

public class SyncProviderHttpClientOptions
{
    public string SyncControllerRoute { get; set; } = "api/sync-agent";

    public string? HttpClientName { get; set; }

    public int BulkItemSize { get; set; } = 50;

    public int MaxRetryAttempts { get; set; } = 3;
}
