using CoreSync.Http;
using CoreSync.Http.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers how a synchronization failure is classified on its way from the server (or the network) to
/// the application: the <c>X-CoreSync-Error</c> header and <see cref="SyncErrorDetails"/> body the
/// server emits, the <see cref="SyncHttpRequestException"/> the client rebuilds from them, the retry
/// decisions that depend on it, and the <see cref="SynchronizationException.Kind"/> /
/// <see cref="SynchronizationException.Stage"/> that <see cref="SyncAgent"/> finally reports.
/// </summary>
/// <remarks>
/// Every test here also guards the compatibility promise: the status codes, exception types and
/// messages an application saw before the classification existed are still what it sees. An
/// application written against CoreSync 1.0.1 must keep working against this server, and this client
/// must keep working against a 1.0.1 server, which sends none of the new signals.
/// </remarks>
[TestClass]
public class SyncErrorClassificationTests
{
    #region Server: CoreSync.Http.Server responses

    [TestMethod]
    public async Task HttpServer_UnknownDownloadSession_RespondsGoneWithSessionExpired()
    {
        using var server = SyncTestServer.Create(new ScriptedSyncProvider());

        using var response = await server.RawHttpClient.GetAsync($"/api/sync-agent/changes-bulk-item/{Guid.NewGuid()}/0/50");

        // Before it had a name this was an unhandled InvalidOperationException, a bodiless 500 the
        // client retried three times for nothing. 410: the session existed and is gone for good.
        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.SessionExpired);
        response.Headers.GetValues(SyncHttpHeaders.TraceId).Single().ShouldNotBeNullOrWhiteSpace();

        var details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>();
        details.ShouldNotBeNull();
        details!.Code.ShouldBe(SyncHttpErrorCodes.SessionExpired);
        details.Message.ShouldNotBeNullOrWhiteSpace();
        details.TraceId.ShouldBe(response.Headers.GetValues(SyncHttpHeaders.TraceId).Single());
        details.Detail.ShouldBeNull();

        // An old client calls EnsureSuccessStatusCode and nothing else: it must still see a failure.
        Should.Throw<HttpRequestException>(() => response.EnsureSuccessStatusCode());
    }

    [TestMethod]
    public async Task HttpServer_UnknownUploadSession_RespondsGoneWithSessionExpired()
    {
        using var server = SyncTestServer.Create(new ScriptedSyncProvider());

        using var response = await server.RawHttpClient.PostAsync($"/api/sync-agent/changes-bulk-complete/{Guid.NewGuid()}", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.SessionExpired);
    }

    [TestMethod]
    public async Task HttpServer_UnexpectedFailure_KeepsHostResponseAndAddsHeaders()
    {
        var failure = new SynchronizationException("Unable to Insert item to store for table Posts", new InvalidOperationException("FK"))
        {
            Kind = SyncFailureKind.DataError,
            TableName = "Posts"
        };

        using var server = SyncTestServer.Create(new ScriptedSyncProvider { ApplyChanges = _ => throw failure }, useHostExceptionHandler: true);

        using var response = await server.RawHttpClient.PostAsync($"/api/sync-agent/changes-bulk-complete/{await BeginUploadAsync(server)}", null);

        // The host still owns the response - same 500, same error page, same logging and reporting -
        // only the headers are added, surviving the Response.Clear() the exception handler performs.
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).ShouldBe(SyncTestServer.HostErrorPageBody);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.DataError);
        response.Headers.GetValues(SyncHttpHeaders.TraceId).Single().ShouldNotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public async Task HttpServer_UnexpectedFailure_ClassifiesConfigurationAndServerErrors()
    {
        using (var server = SyncTestServer.Create(new ScriptedSyncProvider { GetChanges = _ => throw new NotSupportedException("no primary key") }, useHostExceptionHandler: true))
        {
            using var response = await server.RawHttpClient.GetAsync($"/api/sync-agent/changes-bulk/{Guid.NewGuid()}");
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.ConfigurationError);
        }

        using (var server = SyncTestServer.Create(new ScriptedSyncProvider { GetChanges = _ => throw new InvalidOperationException("something else") }, useHostExceptionHandler: true))
        {
            using var response = await server.RawHttpClient.GetAsync($"/api/sync-agent/changes-bulk/{Guid.NewGuid()}");
            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.ServerError);
        }
    }

    [TestMethod]
    public async Task HttpServer_HandleUnexpectedExceptions_AnswersWithDetailsBody()
    {
        var failure = new SynchronizationException("Unable to Insert item to store for table Posts", new InvalidOperationException("FK_Posts_Users"))
        {
            Kind = SyncFailureKind.DataError,
            TableName = "Posts"
        };

        using var server = SyncTestServer.Create(
            new ScriptedSyncProvider { ApplyChanges = _ => throw failure },
            configureOptions: options => options.HandleUnexpectedExceptions = true,
            useHostExceptionHandler: true);

        using var response = await server.RawHttpClient.PostAsync($"/api/sync-agent/changes-bulk-complete/{await BeginUploadAsync(server)}", null);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).ShouldContain(SyncHttpErrorCodes.DataError);

        var details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>();
        details.ShouldNotBeNull();
        details!.Code.ShouldBe(SyncHttpErrorCodes.DataError);
        details.TableName.ShouldBe("Posts");
        details.Message!.ShouldContain("Posts");

        // The provider's message names the constraint; that stays on the server unless asked for.
        details.Detail.ShouldBeNull();
        details.Message!.ShouldNotContain("FK_Posts_Users");
    }

    [TestMethod]
    public async Task HttpServer_IncludeExceptionDetails_IsOptIn()
    {
        var failure = new SynchronizationException("Unable to Insert item", new InvalidOperationException("FK_Posts_Users"));

        using var server = SyncTestServer.Create(
            new ScriptedSyncProvider { ApplyChanges = _ => throw failure },
            configureOptions: options =>
            {
                options.HandleUnexpectedExceptions = true;
                options.IncludeExceptionDetails = true;
            });

        using var response = await server.RawHttpClient.PostAsync($"/api/sync-agent/changes-bulk-complete/{await BeginUploadAsync(server)}", null);

        var details = await response.Content.ReadFromJsonAsync<SyncErrorDetails>();
        details.ShouldNotBeNull();
        details!.Detail.ShouldNotBeNull();
        details.Detail!.ShouldContain("FK_Posts_Users");
    }

    [TestMethod]
    public async Task HttpServer_AnchorTooOld_IsUnchanged()
    {
        using var server = SyncTestServer.Create(new ScriptedSyncProvider { GetChanges = _ => throw new SyncAnchorTooOldException("[dbo].[Posts]", 10, 20) });

        using var response = await server.RawHttpClient.GetAsync($"/api/sync-agent/changes-bulk/{Guid.NewGuid()}");

        // Clients on 1.0.1 parse exactly this: 410, this header value, this body shape.
        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
        response.Headers.GetValues(SyncHttpHeaders.ErrorCode).Single().ShouldBe(SyncHttpErrorCodes.AnchorTooOld);

        var error = await response.Content.ReadFromJsonAsync<SyncAnchorTooOldError>();
        error.ShouldNotBeNull();
        error!.TableName.ShouldBe("[dbo].[Posts]");
        error.RequestedVersion.ShouldBe(10);
        error.MinValidVersion.ShouldBe(20);
    }

    #endregion

    #region Client: SyncHttpRequestException from a new server

    [TestMethod]
    public async Task HttpClient_RebuildsSessionExpiredFromServerSignal()
    {
        // A server whose cache forgets every upload session the moment it is created: what a client
        // meets after a server restart or a cache eviction between two requests of the same sync
        using var server = SyncTestServer.Create(
            new ScriptedSyncProvider(),
            configureServices: services => services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache, ForgetfulMemoryCache>());

        var changeSet = new SyncChangeSet(new SyncAnchor(Guid.NewGuid(), 1), SyncAnchor.Null, new List<SyncItem>());

        var ex = await Should.ThrowAsync<SyncHttpRequestException>(() => server.HttpSyncProvider.ApplyChangesAsync(changeSet));

        ex.StatusCode.ShouldBe(HttpStatusCode.Gone);
        ex.ErrorCode.ShouldBe(SyncHttpErrorCodes.SessionExpired);
        ex.Kind.ShouldBe(SyncFailureKind.SessionExpired);
        ex.IsTransient.ShouldBeFalse();
        ex.TraceId.ShouldNotBeNullOrWhiteSpace();
        ex.ServerMessage.ShouldNotBeNullOrWhiteSpace();

        // Still the exception type and message callers have always received for a failed request
        ex.ShouldBeAssignableTo<HttpRequestException>();
        ex.Message.ShouldStartWith("Response status code does not indicate success: 410");
    }

    [TestMethod]
    public async Task HttpClient_RebuildsDataErrorWithTableFromServerBody()
    {
        var failure = new SynchronizationException("Unable to Insert item", new InvalidOperationException("FK"))
        {
            Kind = SyncFailureKind.DataError,
            TableName = "Posts"
        };

        using var server = SyncTestServer.Create(
            new ScriptedSyncProvider { ApplyChanges = _ => throw failure },
            configureOptions: options => options.HandleUnexpectedExceptions = true);

        var changeSet = new SyncChangeSet(new SyncAnchor(Guid.NewGuid(), 1), SyncAnchor.Null, new List<SyncItem>());

        var ex = await Should.ThrowAsync<SyncHttpRequestException>(() => server.HttpSyncProvider.ApplyChangesAsync(changeSet));

        ex.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        ex.Kind.ShouldBe(SyncFailureKind.DataError);
        ex.ErrorCode.ShouldBe(SyncHttpErrorCodes.DataError);
        ex.TableName.ShouldBe("Posts");
        ex.IsTransient.ShouldBeFalse();
        ex.Message.ShouldStartWith("Response status code does not indicate success: 500");
    }

    [TestMethod]
    public async Task HttpClient_RebuildsDataErrorFromHeadersAloneWhenBodyIsTheHostErrorPage()
    {
        var failure = new SynchronizationException("Unable to Insert item", new InvalidOperationException("FK")) { TableName = "Posts" };

        using var server = SyncTestServer.Create(new ScriptedSyncProvider { ApplyChanges = _ => throw failure }, useHostExceptionHandler: true);

        var changeSet = new SyncChangeSet(new SyncAnchor(Guid.NewGuid(), 1), SyncAnchor.Null, new List<SyncItem>());

        var ex = await Should.ThrowAsync<SyncHttpRequestException>(() => server.HttpSyncProvider.ApplyChangesAsync(changeSet));

        // text/plain body: nothing to parse, the header alone has to carry the classification
        ex.Kind.ShouldBe(SyncFailureKind.DataError);
        ex.ErrorCode.ShouldBe(SyncHttpErrorCodes.DataError);
        ex.TraceId.ShouldNotBeNullOrWhiteSpace();
        ex.TableName.ShouldBeNull();
    }

    [TestMethod]
    public async Task HttpClient_StoreId_StillThrowsUnauthorizedAccessException()
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = SyncTestServer.CreateClient(handler);

        // Applications re-authenticate on exactly this type; it must not become a SyncHttpRequestException.
        var ex = await Should.ThrowAsync<UnauthorizedAccessException>(() => client.GetStoreIdAsync());
        ex.GetType().ShouldBe(typeof(UnauthorizedAccessException));
    }

    #endregion

    #region Client: classification against an old server (no X-CoreSync-Error)

    [TestMethod]
    public async Task HttpClient_OldServer_BodilessFailureIsStillHttpRequestExceptionClassifiedAsServerError()
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = SyncTestServer.CreateClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.SaveVersionForStoreAsync(Guid.NewGuid(), 1));

        var sync = ex.ShouldBeOfType<SyncHttpRequestException>();
        sync.Kind.ShouldBe(SyncFailureKind.ServerError);
        sync.ErrorCode.ShouldBeNull();
        sync.IsTransient.ShouldBeTrue();
        sync.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [TestMethod]
    public async Task HttpClient_OldServer_ServiceUnavailableIsServerUnavailable()
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("No agent is currently connected for DataStore 12.", Encoding.UTF8, "text/plain")
        });
        var client = SyncTestServer.CreateClient(handler);

        var ex = await Should.ThrowAsync<SyncHttpRequestException>(() => client.SaveVersionForStoreAsync(Guid.NewGuid(), 1));

        ex.Kind.ShouldBe(SyncFailureKind.ServerUnavailable);
        ex.IsTransient.ShouldBeTrue();
        ex.ServerMessage.ShouldBeNull();
    }

    [TestMethod]
    public async Task HttpClient_OldServer_HostProblemDetailsBodyDoesNotBreakClassification()
    {
        using var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404}""", Encoding.UTF8, "application/problem+json")
        });
        var client = SyncTestServer.CreateClient(handler);

        var ex = await Should.ThrowAsync<SyncHttpRequestException>(() => client.SaveVersionForStoreAsync(Guid.NewGuid(), 1));

        // A ProblemDetails body deserializes into SyncErrorDetails with defaults; the code must come
        // from the header (absent here), not from a body that never meant to carry one.
        ex.Kind.ShouldBe(SyncFailureKind.ServerError);
        ex.ErrorCode.ShouldBeNull();
        ex.IsTransient.ShouldBeTrue();
    }

    [TestMethod]
    public async Task HttpClient_NetworkFailure_StaysAPlainHttpRequestException()
    {
        using var handler = new ScriptedHandler(_ => throw new HttpRequestException("No such host is known.", new System.Net.Sockets.SocketException(11001)));
        var client = SyncTestServer.CreateClient(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetStoreIdAsync());

        // Never reached the server: no status, no classification layer, exactly as before
        ex.ShouldNotBeOfType<SyncHttpRequestException>();
        ex.StatusCode.ShouldBeNull();
    }

    #endregion

    #region Client: retries

    [TestMethod]
    public async Task HttpClient_Download_StopsRetryingWhenServerSaysPermanent()
    {
        foreach (var binary in new[] { false, true })
        {
            var itemRequests = 0;
            using var handler = new ScriptedHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.Contains("/changes-bulk-item"))
                {
                    itemRequests++;
                    var response = new HttpResponseMessage(HttpStatusCode.Gone);
                    response.Headers.Add(SyncHttpHeaders.ErrorCode, SyncHttpErrorCodes.SessionExpired);
                    return response;
                }

                return BulkChangeSetResponse(totalChanges: 10);
            });

            var client = SyncTestServer.CreateClient(handler, useBinaryFormat: binary);

            var ex = await Should.ThrowAsync<SyncHttpRequestException>(
                () => client.GetChangesAsync(Guid.NewGuid(), null, SyncDirection.DownloadOnly));

            ex.Kind.ShouldBe(SyncFailureKind.SessionExpired);
            itemRequests.ShouldBe(1, $"binary={binary}: a permanent failure must not be retried");
        }
    }

    [TestMethod]
    public async Task HttpClient_JsonDownload_StillRetriesFailuresWithoutAnErrorCode()
    {
        var itemRequests = 0;
        using var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/changes-bulk-item"))
            {
                itemRequests++;
                // What a 1.0.1 server (or a proxy) sends for anything unexpected
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return BulkChangeSetResponse(totalChanges: 10);
        });

        // Polly retries: 1 attempt + MaxRetryAttempts; the delay is Polly's default so keep the count small
        var client = SyncTestServer.CreateClient(handler, useBinaryFormat: false, maxRetryAttempts: 1);

        await Should.ThrowAsync<HttpRequestException>(() => client.GetChangesAsync(Guid.NewGuid(), null, SyncDirection.DownloadOnly));

        itemRequests.ShouldBe(2, "a failure the server did not classify keeps the retries it always had");
    }

    [TestMethod]
    public async Task HttpClient_BinaryDownload_StillRetriesFailuresWithoutAnErrorCode()
    {
        var itemRequests = 0;
        using var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/changes-bulk-item"))
            {
                itemRequests++;
                // What a 1.0.1 server (or a proxy) sends for anything unexpected
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return BulkChangeSetResponse(totalChanges: 10);
        });

        var client = SyncTestServer.CreateClient(handler, useBinaryFormat: true);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetChangesAsync(Guid.NewGuid(), null, SyncDirection.DownloadOnly));

        // The binary loop has always made three attempts five seconds apart; a failure the server did
        // not classify keeps every one of them
        itemRequests.ShouldBe(3);
        ex.ShouldBeOfType<SyncHttpRequestException>().Kind.ShouldBe(SyncFailureKind.ServerError);
    }

    [TestMethod]
    public async Task HttpClient_JsonDownload_DoesNotRetryAnchorTooOld()
    {
        var itemRequests = 0;
        using var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/changes-bulk-item"))
            {
                itemRequests++;
                var response = new HttpResponseMessage(HttpStatusCode.Gone)
                {
                    Content = JsonContent.Create(new SyncAnchorTooOldError { RequestedVersion = 1, MinValidVersion = 5 })
                };
                response.Headers.Add(SyncHttpHeaders.ErrorCode, SyncHttpErrorCodes.AnchorTooOld);
                return response;
            }

            return BulkChangeSetResponse(totalChanges: 10);
        });

        var client = SyncTestServer.CreateClient(handler, useBinaryFormat: false, maxRetryAttempts: 1);

        await Should.ThrowAsync<SyncAnchorTooOldException>(() => client.GetChangesAsync(Guid.NewGuid(), null, SyncDirection.DownloadOnly));

        itemRequests.ShouldBe(1);
    }

    #endregion

    #region SyncAgent: Kind and Stage

    [TestMethod]
    public async Task SyncAgent_ReportsStageAndKindFromTheHttpClient()
    {
        using var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/store-id"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Guid.NewGuid().ToString()) };

            if (request.RequestUri.AbsolutePath.EndsWith("/changes-bulk-begin"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new SyncErrorDetails
                    {
                        Code = SyncHttpErrorCodes.AgentUnavailable,
                        Message = "No agent is currently connected for DataStore 12.",
                        TraceId = "trace-123"
                    })
                };
                response.Headers.Add(SyncHttpHeaders.ErrorCode, SyncHttpErrorCodes.AgentUnavailable);
                response.Headers.Add(SyncHttpHeaders.TraceId, "trace-123");
                return response;
            }

            throw new InvalidOperationException($"unexpected request {request.RequestUri}");
        });

        var agent = new SyncAgent(new ScriptedSyncProvider(), SyncTestServer.CreateClient(handler));

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Stage.ShouldBe(SynchronizationStage.UploadChanges);
        ex.Kind.ShouldBe(SyncFailureKind.ServerUnavailable);
        ex.ErrorCode.ShouldBe(SyncHttpErrorCodes.AgentUnavailable);
        ex.TraceId.ShouldBe("trace-123");
        ex.ServerMessage.ShouldBe("No agent is currently connected for DataStore 12.");

        // Unchanged contract: the inner exception is still an HttpRequestException and the message is
        // still built the same way
        ex.InnerException.ShouldBeAssignableTo<HttpRequestException>();
        ex.Message.ShouldBe($"Unable to synchronize stores: {ex.InnerException!.Message}");

        // Mirrored into Data for error reporters that capture it
        ex.Data[SynchronizationException.DataKeyKind].ShouldBe("ServerUnavailable");
        ex.Data[SynchronizationException.DataKeyStage].ShouldBe("UploadChanges");
        ex.Data[SynchronizationException.DataKeyTraceId].ShouldBe("trace-123");
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesAnUnreachableServerAsNetwork()
    {
        using var handler = new ScriptedHandler(_ => throw new HttpRequestException("Connection refused", new System.Net.Sockets.SocketException(10061)));

        var agent = new SyncAgent(new ScriptedSyncProvider(), SyncTestServer.CreateClient(handler));

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Stage.ShouldBe(SynchronizationStage.RemoteStoreId);
        ex.Kind.ShouldBe(SyncFailureKind.Network);
        ex.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesARequestTimeoutAsNetwork()
    {
        // HttpClient reports an expired request timeout as TaskCanceledException while the caller's
        // token is untouched; that is a connectivity problem, not a cancellation.
        using var handler = new ScriptedHandler(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()));

        var agent = new SyncAgent(new ScriptedSyncProvider(), SyncTestServer.CreateClient(handler));

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.Network);
        ex.InnerException.ShouldBeOfType<TaskCanceledException>();
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesTheCallersCancellationAsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var remote = new ScriptedSyncProvider
        {
            GetStoreId = () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }
        };

        var agent = new SyncAgent(new ScriptedSyncProvider(), remote);

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync(cancellationToken: cts.Token));

        ex.Kind.ShouldBe(SyncFailureKind.Cancelled);
        ex.Stage.ShouldBe(SynchronizationStage.RemoteStoreId);
    }

    [TestMethod]
    public async Task SyncAgent_OldClient_PlainHttpRequestExceptionWithAStatusIsNotNetwork()
    {
        // A CoreSync.Http.Client that predates SyncHttpRequestException throws what
        // EnsureSuccessStatusCode throws: a plain HttpRequestException that does carry the status.
        // The server answered, so this must not be reported as "you are offline".
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError, HttpStatusCode.ServiceUnavailable })
        {
            var remote = new ScriptedSyncProvider
            {
                GetStoreId = () => throw new HttpRequestException($"Response status code does not indicate success: {(int)status} ({status}).", null, status)
            };

            var ex = await Should.ThrowAsync<SynchronizationException>(() => new SyncAgent(new ScriptedSyncProvider(), remote).SynchronizeAsync());

            ex.Kind.ShouldBe(SyncFailureKind.Unknown, $"status {status}");
            ex.Stage.ShouldBe(SynchronizationStage.RemoteStoreId);
            ex.InnerException.ShouldBeOfType<HttpRequestException>();
        }
    }

    [TestMethod]
    public async Task SyncAgent_OldClient_PlainHttpRequestExceptionWithoutAStatusIsNetwork()
    {
        // ...while the same type with no status is what HttpClient itself throws when the server was
        // never reached, on an old client and a new one alike.
        var remote = new ScriptedSyncProvider
        {
            GetStoreId = () => throw new HttpRequestException("No such host is known.", new System.Net.Sockets.SocketException(11001))
        };

        var ex = await Should.ThrowAsync<SynchronizationException>(() => new SyncAgent(new ScriptedSyncProvider(), remote).SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.Network);
    }

    [TestMethod]
    public async Task SyncAgent_LocalUnauthorizedAccessIsNotACredentialsProblem()
    {
        // SQLite raises UnauthorizedAccessException for a database file the app may not write (a
        // read-only folder, Android scoped storage). Re-authenticating with the server would not help.
        var local = new ScriptedSyncProvider { GetChanges = _ => throw new UnauthorizedAccessException("Access to the path is denied.") };

        var ex = await Should.ThrowAsync<SynchronizationException>(() => new SyncAgent(local, new ScriptedSyncProvider()).SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.Unknown);
        ex.Stage.ShouldBe(SynchronizationStage.LocalChanges);
        ex.InnerException.ShouldBeOfType<UnauthorizedAccessException>();
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesUnauthorizedAndKeepsTheInnerType()
    {
        var remote = new ScriptedSyncProvider { GetStoreId = () => throw new UnauthorizedAccessException() };

        var agent = new SyncAgent(new ScriptedSyncProvider(), remote);

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        // This is the branch existing applications take to re-authenticate
        ex.InnerException.ShouldBeOfType<UnauthorizedAccessException>();
        ex.Kind.ShouldBe(SyncFailureKind.Unauthorized);
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesAProviderFailureAsDataErrorWithTable()
    {
        // What SqlServer/MySql/PostgreSQL providers throw when a change cannot be applied
        var local = new ScriptedSyncProvider
        {
            ApplyChanges = _ => throw new SyncErrorException("An exception occurred during synchronization",
                new SynchronizationException("Unable to Insert item to store for table Comments", new InvalidOperationException("FK"))
                {
                    Kind = SyncFailureKind.DataError,
                    TableName = "Comments"
                })
        };

        var agent = new SyncAgent(local, new ScriptedSyncProvider());

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Stage.ShouldBe(SynchronizationStage.ApplyChangesLocally);
        ex.Kind.ShouldBe(SyncFailureKind.DataError);
        ex.TableName.ShouldBe("Comments");
        ex.InnerException.ShouldBeOfType<SyncErrorException>();
    }

    [TestMethod]
    public async Task SyncAgent_TreatsAnOlderProvidersSynchronizationExceptionAsDataError()
    {
        // A provider package that predates Kind leaves it Unknown; the type alone has to count
        var local = new ScriptedSyncProvider
        {
            ApplyChanges = _ => throw new SynchronizationException("Unable to Insert item to store for table Comments", new InvalidOperationException("FK"))
        };

        var agent = new SyncAgent(local, new ScriptedSyncProvider());

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.DataError);
        ex.TableName.ShouldBeNull();
    }

    [TestMethod]
    public async Task SyncAgent_ClassifiesAnchorTooOldThroughTheInterface()
    {
        var remote = new ScriptedSyncProvider { GetChanges = _ => throw new SyncAnchorTooOldException("[dbo].[Posts]", 1, 5) };

        var agent = new SyncAgent(new ScriptedSyncProvider(), remote);

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.AnchorTooOld);
        ex.Stage.ShouldBe(SynchronizationStage.DownloadChanges);
        ex.TableName.ShouldBe("[dbo].[Posts]");
        ex.InnerException.ShouldBeOfType<SyncAnchorTooOldException>();
    }

    [TestMethod]
    public async Task SyncAgent_LeavesAnUnrecognisedFailureUnknownButStaged()
    {
        var local = new ScriptedSyncProvider { GetChanges = _ => throw new InvalidOperationException("database is locked") };

        var agent = new SyncAgent(local, new ScriptedSyncProvider());

        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.Kind.ShouldBe(SyncFailureKind.Unknown);
        ex.Stage.ShouldBe(SynchronizationStage.LocalChanges);
        ex.Data.Contains(SynchronizationException.DataKeyKind).ShouldBeFalse();
        ex.Data[SynchronizationException.DataKeyStage].ShouldBe("LocalChanges");
    }

    #endregion

    #region Error code table

    [TestMethod]
    public void ErrorCodes_PermanentAndKindMappingsAreStable()
    {
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.AnchorTooOld).ShouldBeTrue();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.SessionExpired).ShouldBeTrue();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.DataError).ShouldBeTrue();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.ConfigurationError).ShouldBeTrue();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.EndpointNotFound).ShouldBeTrue();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.AgentUnavailable).ShouldBeFalse();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.AgentError).ShouldBeFalse();
        SyncHttpErrorCodes.IsPermanent(SyncHttpErrorCodes.ServerError).ShouldBeFalse();

        // A code this version does not know, or no code at all, must never stop a retry
        SyncHttpErrorCodes.IsPermanent("something-newer").ShouldBeFalse();
        SyncHttpErrorCodes.IsPermanent(null).ShouldBeFalse();

        SyncHttpErrorCodes.ToFailureKind(null).ShouldBe(SyncFailureKind.Unknown);
        SyncHttpErrorCodes.ToFailureKind("something-newer").ShouldBe(SyncFailureKind.ServerError);
        SyncHttpErrorCodes.ToFailureKind(SyncHttpErrorCodes.AgentUnavailable).ShouldBe(SyncFailureKind.ServerUnavailable);
        SyncHttpErrorCodes.ToFailureKind(SyncHttpErrorCodes.AgentError).ShouldBe(SyncFailureKind.ServerError);
        SyncHttpErrorCodes.ToFailureKind(SyncHttpErrorCodes.EndpointNotFound).ShouldBe(SyncFailureKind.ConfigurationError);
    }

    #endregion

    #region Helpers

    private static async Task<Guid> BeginUploadAsync(SyncTestServer server)
    {
        var sessionId = Guid.NewGuid();
        var begin = await server.RawHttpClient.PostAsJsonAsync("/api/sync-agent/changes-bulk-begin", new BulkSyncChangeSet
        {
            SessionId = sessionId,
            SourceAnchor = new SyncAnchor(Guid.NewGuid(), 1),
            TargetAnchor = SyncAnchor.Null
        });
        begin.EnsureSuccessStatusCode();
        return sessionId;
    }

    /// <summary>
    /// A cache that accepts every entry and remembers none, so that the very next request of a
    /// synchronization finds its session gone.
    /// </summary>
    private sealed class ForgetfulMemoryCache : Microsoft.Extensions.Caching.Memory.IMemoryCache
    {
        private readonly Microsoft.Extensions.Caching.Memory.MemoryCache _entries = new(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

        public Microsoft.Extensions.Caching.Memory.ICacheEntry CreateEntry(object key) => _entries.CreateEntry(key);

        public void Remove(object key) => _entries.Remove(key);

        public bool TryGetValue(object key, out object? value)
        {
            value = null;
            return false;
        }

        public void Dispose() => _entries.Dispose();
    }

    private static HttpResponseMessage BulkChangeSetResponse(int totalChanges)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new BulkSyncChangeSet
            {
                SessionId = Guid.NewGuid(),
                TotalChanges = totalChanges,
                SourceAnchor = new SyncAnchor(Guid.NewGuid(), 1),
                TargetAnchor = new SyncAnchor(Guid.NewGuid(), 1)
            })
        };

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that answers from a delegate: the stand-in for servers this
    /// code base does not host (older versions, proxies) and for the network itself.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    /// <summary>
    /// A provider whose every operation succeeds trivially unless a test scripts it to fail, so the
    /// HTTP layer and the agent can be driven through each stage without a database.
    /// </summary>
    private sealed class ScriptedSyncProvider : ISyncProvider
    {
        private readonly Guid _storeId = Guid.NewGuid();

        public Func<Guid>? GetStoreId { get; set; }
        public Func<Guid, SyncChangeSet>? GetChanges { get; set; }
        public Func<SyncChangeSet, SyncAnchor>? ApplyChanges { get; set; }

        public string[]? SyncTableNames => null;

        public Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(GetStoreId?.Invoke() ?? _storeId);

        public Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string[]? tables = null, CancellationToken cancellationToken = default)
            => Task.FromResult(GetChanges?.Invoke(otherStoreId)
                ?? new SyncChangeSet(new SyncAnchor(_storeId, 1), new SyncAnchor(otherStoreId, 1), new List<SyncItem>()));

        public Task<SyncAnchor> ApplyChangesAsync(SyncChangeSet changeSet, Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ApplyChanges?.Invoke(changeSet) ?? new SyncAnchor(_storeId, 1));

        public Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SyncVersion(1, 0));

        public Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    #endregion
}
