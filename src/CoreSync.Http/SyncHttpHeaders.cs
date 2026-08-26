namespace CoreSync.Http;

/// <summary>
/// Custom HTTP header names used by the CoreSync HTTP protocol.
/// </summary>
public static class SyncHttpHeaders
{
    /// <summary>
    /// Comma-separated list of table names the client wants to sync.
    /// When absent, the server returns changes for all configured tables.
    /// </summary>
    public const string Tables = "X-CoreSync-Tables";

    /// <summary>
    /// The expected number of table names in <see cref="Tables"/>.
    /// The server uses this to detect truncated headers.
    /// </summary>
    public const string TablesCount = "X-CoreSync-Tables-Count";

    /// <summary>
    /// A machine-readable identifier for a failure the client is expected to handle specifically,
    /// set on the response alongside the corresponding status code.
    /// See <see cref="SyncHttpErrorCodes"/> for the defined values.
    /// </summary>
    public const string ErrorCode = "X-CoreSync-Error";
}
