using CoreSync.Http;
using CoreSync.Sqlite;
using CoreSync.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Covers the aged-out anchor path: a peer asking for changes relative to a version the store no
/// longer retains must get a typed <see cref="SyncAnchorTooOldException"/>, and that condition must
/// survive the trip over HTTP instead of collapsing into a generic transport failure.
/// </summary>
/// <remarks>
/// This is the failure behind "synchronization rarely works": any client idle longer than the change
/// retention window asks for a version that has been cleaned up. It is permanent for that client -
/// the only cure is a fresh snapshot - so what matters is that callers can *recognise* it rather than
/// see an anonymous error they are tempted to retry forever.
/// </remarks>
[TestClass]
public class AnchorTooOldTests
{
    private static string CreateDatabase(string testName)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"{testName}_{Guid.NewGuid():N}.sqlite");

        using (var db = new SQLiteConnection(dbFile))
        {
            db.CreateTable<Stock>();
        }

        SqliteConnection.ClearAllPools();

        return dbFile;
    }

    private static SqliteSyncProvider CreateProvider(string dbFile)
        => new(new SqliteSyncConfigurationBuilder($"Data Source={dbFile}")
            .Table<Stock>()
            .Build(), logger: new ConsoleLogger("LOC"));

    [TestMethod]
    public async Task GetChangesAsync_ThrowsTypedExceptionWhenAnchorFellOutOfRetention()
    {
        var dbFile = CreateDatabase(nameof(GetChangesAsync_ThrowsTypedExceptionWhenAnchorFellOutOfRetention));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.ApplyProvisionAsync();

        using (var db = new SQLiteConnection(dbFile))
        {
            for (var i = 0; i < 10; i++)
            {
                db.Insert(new Stock { Id = Guid.NewGuid(), Symbol = $"SYM{i}" });
            }
        }

        SqliteConnection.ClearAllPools();

        var otherStoreId = Guid.NewGuid();

        //the peer last synchronized at version 1 and then went quiet
        await syncProvider.SaveVersionForStoreAsync(otherStoreId, 1);

        //retention cleanup discards everything below version 5, exactly like SQL Server change
        //tracking auto-cleanup does once CHANGE_RETENTION elapses
        await syncProvider.ApplyRetentionPolicyAsync(5);

        var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(
            () => syncProvider.GetChangesAsync(otherStoreId, syncDirection: SyncDirection.DownloadOnly));

        ex.RequestedVersion.ShouldBe(1);

        //the oldest retained change is version 5, so the oldest anchor still resolvable is 4
        ex.MinValidVersion.ShouldBe(4);

        //this store keeps one shared journal, so no individual table is implicated
        ex.TableName.ShouldBeNull();

        //the message has to name both versions: it is the only diagnostic that reaches the operator
        ex.Message.ShouldContain("1");
        ex.Message.ShouldContain("4");
    }

    [TestMethod]
    public async Task GetChangesAsync_SucceedsWhenAnchorIsStillWithinRetention()
    {
        var dbFile = CreateDatabase(nameof(GetChangesAsync_SucceedsWhenAnchorIsStillWithinRetention));
        var syncProvider = CreateProvider(dbFile);

        await syncProvider.ApplyProvisionAsync();

        using (var db = new SQLiteConnection(dbFile))
        {
            for (var i = 0; i < 10; i++)
            {
                db.Insert(new Stock { Id = Guid.NewGuid(), Symbol = $"SYM{i}" });
            }
        }

        SqliteConnection.ClearAllPools();

        var otherStoreId = Guid.NewGuid();

        //anchor sits exactly on the retention boundary: the oldest retained change is 5, so an
        //anchor of 4 is still resolvable and must not be rejected
        await syncProvider.SaveVersionForStoreAsync(otherStoreId, 4);
        await syncProvider.ApplyRetentionPolicyAsync(5);

        var changeSet = await syncProvider.GetChangesAsync(otherStoreId, syncDirection: SyncDirection.DownloadOnly);

        changeSet.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task SyncAgent_WrapsTheTypedExceptionSoExistingCatchesStillFire()
    {
        var localFile = CreateDatabase(nameof(SyncAgent_WrapsTheTypedExceptionSoExistingCatchesStillFire) + "_local");
        var remoteFile = CreateDatabase(nameof(SyncAgent_WrapsTheTypedExceptionSoExistingCatchesStillFire) + "_remote");

        var local = CreateProvider(localFile);
        var remote = CreateProvider(remoteFile);

        await local.ApplyProvisionAsync();
        await remote.ApplyProvisionAsync();

        using (var db = new SQLiteConnection(remoteFile))
        {
            for (var i = 0; i < 10; i++)
            {
                db.Insert(new Stock { Id = Guid.NewGuid(), Symbol = $"SYM{i}" });
            }
        }

        SqliteConnection.ClearAllPools();

        //the remote store believes this client is at version 1, then loses everything below 5
        await remote.SaveVersionForStoreAsync(await local.GetStoreIdAsync(), 1);
        await remote.ApplyRetentionPolicyAsync(5);

        var agent = new SyncAgent(local, remote);

        // Callers have caught SynchronizationException around SynchronizeAsync since before the typed
        // exception existed. Letting this one case escape unwrapped would break them at runtime with
        // nothing to catch it at compile time, so every failure still leaves here wrapped.
        var ex = await Should.ThrowAsync<SynchronizationException>(() => agent.SynchronizeAsync());

        ex.InnerException.ShouldBeOfType<SyncAnchorTooOldException>();

        var tooOld = (SyncAnchorTooOldException)ex.InnerException!;
        tooOld.RequestedVersion.ShouldBe(1);
        tooOld.MinValidVersion.ShouldBe(4);
    }

    [TestMethod]
    public async Task HttpClient_RebuildsTypedExceptionFromServerSignal()
    {
        var thrown = new SyncAnchorTooOldException("[admin].[UserRole]", 1378, 1925);
        using var server = SyncTestServer.Create(new ThrowingSyncProvider(thrown));

        var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(
            () => server.HttpSyncProvider.GetChangesAsync(Guid.NewGuid(), syncDirection: SyncDirection.DownloadOnly));

        //every field the caller needs to diagnose the failure has to survive the wire
        ex.TableName.ShouldBe("[admin].[UserRole]");
        ex.RequestedVersion.ShouldBe(1378);
        ex.MinValidVersion.ShouldBe(1925);
    }

    [TestMethod]
    public async Task HttpClient_RebuildsTypedExceptionForAStoreWideFailure()
    {
        var thrown = new SyncAnchorTooOldException(1378, 1925);
        using var server = SyncTestServer.Create(new ThrowingSyncProvider(thrown));

        var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(
            () => server.HttpSyncProvider.GetChangesAsync(Guid.NewGuid(), syncDirection: SyncDirection.DownloadOnly));

        ex.TableName.ShouldBeNull();
        ex.RequestedVersion.ShouldBe(1378);
        ex.MinValidVersion.ShouldBe(1925);
    }

    [TestMethod]
    public async Task HttpClient_RebuildsTypedExceptionRaisedWhileApplyingChanges()
    {
        var thrown = new SyncAnchorTooOldException("[core].[Survey]", 1378, 1925);
        using var server = SyncTestServer.Create(new ThrowingSyncProvider(thrown));

        //the upload path guards the anchor too, and has to report it just as distinguishably
        var changeSet = new SyncChangeSet(
            new SyncAnchor(Guid.NewGuid(), 1378),
            SyncAnchor.Null,
            new List<SyncItem>());

        var ex = await Should.ThrowAsync<SyncAnchorTooOldException>(
            () => server.HttpSyncProvider.ApplyChangesAsync(changeSet));

        ex.TableName.ShouldBe("[core].[Survey]");
        ex.RequestedVersion.ShouldBe(1378);
        ex.MinValidVersion.ShouldBe(1925);
    }

    [TestMethod]
    public async Task HttpServer_RespondsWithGoneAndAMachineReadableCode()
    {
        var thrown = new SyncAnchorTooOldException("[ref].[AgeClass]", 2107, 2120);
        using var server = SyncTestServer.Create(new ThrowingSyncProvider(thrown));

        using var response = await server.RawHttpClient.GetAsync(
            $"/api/sync-agent/changes-bulk/{Guid.NewGuid()}");

        //410 rather than 500: the requested history existed once and is permanently gone, so a
        //client that retries it can never succeed
        response.StatusCode.ShouldBe(HttpStatusCode.Gone);

        response.Headers.GetValues(SyncHttpHeaders.ErrorCode)
            .ShouldContain(SyncHttpErrorCodes.AnchorTooOld);

        var error = await response.Content.ReadFromJsonAsync<SyncAnchorTooOldError>();

        error.ShouldNotBeNull();
        error!.TableName.ShouldBe("[ref].[AgeClass]");
        error.RequestedVersion.ShouldBe(2107);
        error.MinValidVersion.ShouldBe(2120);
    }

    [TestMethod]
    public async Task HttpClient_LeavesUnrelatedFailuresAlone()
    {
        using var server = SyncTestServer.Create(
            new ThrowingSyncProvider(new InvalidOperationException("something else went wrong")));

        //an ordinary server-side error must not be mistaken for an aged-out anchor, which would tell
        //the operator to reinitialize a client that has nothing wrong with it
        var ex = await Should.ThrowAsync<Exception>(
            () => server.HttpSyncProvider.GetChangesAsync(Guid.NewGuid(), syncDirection: SyncDirection.DownloadOnly));

        ex.ShouldNotBeAssignableTo<SyncAnchorTooOldException>();
    }

    [TestMethod]
    public async Task HttpServer_LeavesUnrelatedFailuresWithoutTheErrorHeader()
    {
        using var server = SyncTestServer.Create(
            new ThrowingSyncProvider(new InvalidOperationException("something else went wrong")));

        //the filter must only claim the failures it actually recognises
        var ex = await Should.ThrowAsync<Exception>(
            () => server.RawHttpClient.GetAsync($"/api/sync-agent/changes-bulk/{Guid.NewGuid()}"));

        ex.ShouldNotBeAssignableTo<SyncAnchorTooOldException>();
    }

    /// <summary>
    /// A provider whose only job is to fail in a specific way, so the HTTP layer can be tested
    /// independently of any particular database.
    /// </summary>
    private sealed class ThrowingSyncProvider : ISyncProvider
    {
        private readonly Exception _exception;

        public ThrowingSyncProvider(Exception exception) => _exception = exception;

        public string[]? SyncTableNames => null;

        public Task<SyncChangeSet> GetChangesAsync(Guid otherStoreId, SyncFilterParameter[]? syncFilterParameters = null, SyncDirection syncDirection = SyncDirection.UploadAndDownload, string[]? tables = null, CancellationToken cancellationToken = default)
            => throw _exception;

        public Task<SyncAnchor> ApplyChangesAsync(SyncChangeSet changeSet, Func<SyncItem, ConflictResolution>? onConflictFunc = null, CancellationToken cancellationToken = default)
            => throw _exception;

        public Task<Guid> GetStoreIdAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task SaveVersionForStoreAsync(Guid otherStoreId, long version, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ApplyProvisionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveProvisionAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<SyncVersion> GetSyncVersionAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SyncVersion> ApplyRetentionPolicyAsync(int minVersion, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task EnableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisableChangeTrackingForTable(string tableName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
