using CoreSync.Sqlite;
using CoreSync.Tests.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using SQLite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreSync.Tests;


[TestClass]
public class SqlitenetTests
{
    [TestMethod]
    public async Task TestPrimaryKeyGuid()
    {
        var localDbFile = $"{Path.GetTempPath()}TestPrimaryKeyGuid_local.sqlite";
        var remoteDbFile = $"{Path.GetTempPath()}TestPrimaryKeyGuid_remote.sqlite";

        if (File.Exists(localDbFile)) File.Delete(localDbFile);
        if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

        var localDb = new SQLiteConnection(localDbFile);
        localDb.CreateTable<Stock>();
        localDb.CreateTable<Valuation>();

        var remoteDb = new SQLiteConnection(remoteDbFile);
        remoteDb.CreateTable<Stock>();
        remoteDb.CreateTable<Valuation>();

        var remoteConfigurationBuilder =
            new SqliteSyncConfigurationBuilder($"Data Source={remoteDbFile}")
                .Table<Stock>()
                .Table<Valuation>();

        ISyncProvider remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build(), logger: new ConsoleLogger("REM"));
        await remoteSyncProvider.ApplyProvisionAsync();

        var localConfigurationBuilder =
            new SqliteSyncConfigurationBuilder($"Data Source={localDbFile}")
                .Table<Stock>()
                .Table<Valuation>();

        ISyncProvider localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build(), logger: new ConsoleLogger("LOC"));
        await localSyncProvider.ApplyProvisionAsync();

        var stock = new Stock()
        {
            Id = Guid.NewGuid(),
            Symbol = "MY_SYMBOL"
        };
        localDb.Insert(stock);

        var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
        await syncAgent.SynchronizeAsync();

        var id = stock.Id;
        remoteDb.Table<Stock>().Single(v => v.Id == id).ShouldNotBeNull();
        remoteDb.Table<Stock>().Single(v => v.Symbol == "MY_SYMBOL").ShouldNotBeNull();

    }
}
