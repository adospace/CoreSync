using System;

namespace CoreSync.Http.Server;

/// <summary>
/// Raised by <see cref="SyncAgentController"/> when a request refers to an upload or download session
/// the memory cache no longer holds: it was evicted, the process restarted, or the client took too long
/// between two requests of the same synchronization.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>, which is what the controller threw before the
/// condition had a name, so any host code catching that type keeps working. The endpoint filter turns
/// it into a 410 Gone carrying <see cref="SyncHttpErrorCodes.SessionExpired"/>.
/// </remarks>
internal sealed class SyncSessionNotFoundException : InvalidOperationException
{
    public SyncSessionNotFoundException(Guid sessionId)
        : base($"Synchronization session '{sessionId}' is not available on the server: it expired or the server restarted. Start a new synchronization.")
    {
        SessionId = sessionId;
    }

    public Guid SessionId { get; }
}
