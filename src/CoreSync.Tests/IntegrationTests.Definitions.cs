using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Tests
{
    [TestClass]
    public partial class IntegrationTests
    {
        [TestMethod]
        public async Task Test1_SqlServer_SqlServer()
        {
            using (var localDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test1_Local"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test1_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();


                await Test1(localDb,
                    localSyncProvider,
                    remoteDb,
                    remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test2_SqlServer_SqlServer()
        {
            using (var localDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test2_Local"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test2_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();

                await Test2(localDb,
                    localSyncProvider,
                    remoteDb,
                    remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test1_Sqlite_Sqlite()
        {
            var localDbFile = $"{Path.GetTempPath()}Test1_Sqlite_Sqlite_local.sqlite";
            var remoteDbFile = $"{Path.GetTempPath()}Test1_Sqlite_Sqlite_remote.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();


                await Test1(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test1_SqlServer_Sqlite()
        {
            var remoteDbFile = $"{Path.GetTempPath()}Test1_SqlServer_Sqlite_remote.sqlite";

            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

            using (var localDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test1_Local"))
            using (var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");


                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();


                await Test1(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test2_Sqlite_Sqlite()
        {
            var localDbFile = $"{Path.GetTempPath()}Test2_Sqlite_Sqlite_local.sqlite";
            var remoteDbFile = $"{Path.GetTempPath()}Test2_Sqlite_Sqlite_remote.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();

                await Test2(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test1_Sqlite_SqlServer()
        {
            var localDbFile = $"{Path.GetTempPath()}Test1_Sqlite_SqlServer_local.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test1_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();


                await Test1(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task Test2_Sqlite_SqlServer()
        {
            var localDbFile = $"{Path.GetTempPath()}Test2_Sqlite_SqlServer_local.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test2_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());
                await remoteSyncProvider.ApplyProvisionAsync();

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());
                await localSyncProvider.ApplyProvisionAsync();


                //await Test2(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task TestSyncAgent_Sqlite_SqlServer()
        {
            var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_local.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());


                await TestSyncAgent(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

                [TestMethod]
        public async Task TestSyncAgent_Sqlite_Sqlite()
        {
            var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_local.sqlite";
            var remoteDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_Sqlite_remote.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build());

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());

                await TestSyncAgent(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable()
        {
            var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_MultipleRecordsSameTable"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build());

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build());


                await TestSyncAgentMultipleRecordsOnSameTable(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task TestSyncAgent_Sqlite_SqlServer_WithInitialData()
        {
            var localDbFile = $"{Path.GetTempPath()}TestSyncAgent_Sqlite_SqlServer_WithInitialData.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_Sqlite_SqlServer_WithInitialData"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build(), ProviderMode.Remote);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build(), ProviderMode.Local);


                await TestSyncAgentWithInitialData(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        [TestMethod]
        public async Task TestSyncAgent_SqlServer_SqlServer_UpdatedRemoteDeletedLocal()
        {
            using (var localDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_SqlServer_SqlServer_UpdatedRemoteDeletedLocal_Local"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=TestSyncAgent_SqlServer_SqlServer_UpdatedRemoteDeletedLocal_Remote"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Build(), ProviderMode.Remote);

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Build());

                await TestSyncAgentWithUpdatedRemoteDeletedLocal(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }


        [TestMethod]
        public async Task Test_Sqlite_Sqlite_DataRetention()
        {
            var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DataRetention_local.sqlite";
            var remoteDbFile = $"{Path.GetTempPath()}Test_Sqlite_Sqlite_DataRetention_remote.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);
            if (File.Exists(remoteDbFile)) File.Delete(remoteDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqliteBlogDbContext($"Data Source={remoteDbFile}"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build(), ProviderMode.Remote);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build(), ProviderMode.Local);


                await TestSyncAgentWithDataRetention(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }


        [TestMethod]
        public async Task Test_Sqlite_SqlServer_DataRetention()
        {
            var localDbFile = $"{Path.GetTempPath()}Test_Sqlite_SqlServer_DataRetention.sqlite";

            if (File.Exists(localDbFile)) File.Delete(localDbFile);

            using (var localDb = new SqliteBlogDbContext($"Data Source={localDbFile}"))
            using (var remoteDb = new SqlServerBlogDbContext(ConnectionString + ";Initial Catalog=Test_Sqlite_SqlServer_DataRetention"))
            {
                await localDb.Database.EnsureDeletedAsync();
                await remoteDb.Database.EnsureDeletedAsync();

                await localDb.Database.MigrateAsync();
                await remoteDb.Database.MigrateAsync();

                var remoteConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(remoteDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Build(), ProviderMode.Remote);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Build(), ProviderMode.Local);


                await TestSyncAgentWithDataRetention(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

    }


}