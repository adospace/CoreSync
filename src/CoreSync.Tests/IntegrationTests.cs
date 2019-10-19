using CoreSync.Sqlite;
using CoreSync.SqlServer;
using CoreSync.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace CoreSync.Tests
{
    [TestClass]
    public class IntegrationTests
    {
        public static string ConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
                                                 throw new ArgumentException(
                                                     "Set CORE-SYNC_CONNECTION_STRING environmental variable containing connection string to Sql Server");

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

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Configuration);


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

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                    .Table("Users")
                    .Table("Posts")
                    .Table("Comments");

                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Configuration);

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

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Configuration);


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

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Configuration);

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

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Configuration);


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

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Configuration);


                await Test2(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }


        private async Task Test1(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var initialRemoteSet = await remoteSyncProvider.GetInitialSetAsync();
            var initialLocalSet = await localSyncProvider.GetInitialSetAsync();

            Assert.IsNotNull(initialRemoteSet);
            Assert.IsNotNull(initialRemoteSet.Items);
            Assert.AreEqual(0, initialRemoteSet.Items.Count);

            Assert.IsNotNull(initialLocalSet);
            Assert.IsNotNull(initialLocalSet.Items);
            Assert.AreEqual(0, initialLocalSet.Items.Count);

            var changeSet = await remoteSyncProvider.GetIncreamentalChangesAsync(initialRemoteSet.Anchor);
            Assert.IsNotNull(changeSet);
            Assert.IsNotNull(changeSet.Items);
            Assert.AreEqual(0, changeSet.Items.Count);

            var newUser = new User() { Email = "myemail@test.com", Name = "User1", Created = DateTime.Now };
            remoteDb.Users.Add(newUser);
            await remoteDb.SaveChangesAsync();

            var changeSetAfterUserAdd = await remoteSyncProvider.GetIncreamentalChangesAsync(initialRemoteSet.Anchor);
            Assert.IsNotNull(changeSetAfterUserAdd);
            Assert.IsNotNull(changeSetAfterUserAdd.Items);
            Assert.AreEqual(1, changeSetAfterUserAdd.Items.Count);
            Assert.AreEqual(ChangeType.Insert, changeSetAfterUserAdd.Items[0].ChangeType);
            Assert.AreEqual(newUser.Email, changeSetAfterUserAdd.Items[0].Values["Email"]);
            Assert.AreEqual(newUser.Name, changeSetAfterUserAdd.Items[0].Values["Name"]);

            var finalLocalAnchor = await localSyncProvider.ApplyChangesAsync(new SyncChangeSet(initialLocalSet.Anchor, changeSetAfterUserAdd.Items));
            Assert.IsNotNull(finalLocalAnchor);

            //try to apply same changeset result in an exception
            var exception = await Assert.ThrowsExceptionAsync<InvalidSyncOperationException>(() => localSyncProvider.ApplyChangesAsync(new SyncChangeSet(initialLocalSet.Anchor, changeSetAfterUserAdd.Items)));
            Assert.IsNotNull(exception);

            newUser.Created = new DateTime(2018, 1, 1);
            await remoteDb.SaveChangesAsync();

            {
                //after saved changes version should be updated as well at 2
                var changeSetAfterUserEdit = await remoteSyncProvider.GetIncreamentalChangesAsync(initialRemoteSet.Anchor);
                Assert.IsNotNull(changeSetAfterUserEdit);
                Assert.IsNotNull(changeSetAfterUserEdit.Items);
                Assert.AreEqual(1, changeSetAfterUserEdit.Items.Count);
                Assert.AreEqual(newUser.Email, changeSetAfterUserEdit.Items[0].Values["Email"]);
                Assert.AreEqual(newUser.Name, changeSetAfterUserEdit.Items[0].Values["Name"]);
                Assert.AreEqual(newUser.Created, changeSetAfterUserEdit.Items[0].Values["Created"]);
            }

            {
                //now let's change same record in local database and try to apply changes to remote db
                //this should result in a conflict
                var newUserInLocalDb = await localDb.Users.FirstAsync(_ => _.Name == newUser.Name);
                newUserInLocalDb.Name = "modified-name";
                await localDb.SaveChangesAsync();

                //get changes from local db
                var localChangeSet = await localSyncProvider.GetIncreamentalChangesAsync(finalLocalAnchor);
                Assert.IsNotNull(localChangeSet);

                //try to apply changes to remote provider
                var anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(new SyncChangeSet(changeSetAfterUserAdd.Anchor, localChangeSet.Items));
                //given we didn't provide a resolution function for the conflict provider just skip 
                //to apply the changes from local db
                //so nothing should be changed in remote db
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                var userNotChangedInRemoteDb = await remoteDb.Users.FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userNotChangedInRemoteDb);
                Assert.AreEqual(newUser.Name, userNotChangedInRemoteDb.Name);

                //ok now try apply changes but forcing any write on remote store on conflict
                anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(new SyncChangeSet(changeSetAfterUserAdd.Anchor, localChangeSet.Items),
                    (item) =>
                    {
                        //assert that conflict occurred on item we just got from local db
                        Assert.IsNotNull(item);
                        Assert.AreEqual(newUserInLocalDb.Email, item.Values["Email"]);
                        Assert.AreEqual(newUserInLocalDb.Name, item.Values["Name"]);
                        Assert.AreEqual(ChangeType.Update, item.ChangeType);

                        //force write in remote store
                        return ConflictResolution.ForceWrite;
                    });

                //now we should have a new version  (+1)
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                //and local db changes should be applied to remote db
                var userChangedInRemoteDb = await remoteDb.Users.AsNoTracking().FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userChangedInRemoteDb);
                Assert.AreEqual(newUserInLocalDb.Name, userChangedInRemoteDb.Name);
            }

            {
                //now let's try to update a deleted record
                remoteDb.Users.Remove(newUser);
                await remoteDb.SaveChangesAsync();

                var newUserInLocalDb = await localDb.Users.FirstAsync(_ => _.Email == newUser.Email);
                var localChangeSet = await localSyncProvider.GetIncreamentalChangesAsync(finalLocalAnchor);
                Assert.IsNotNull(localChangeSet);

                //try to apply changes to remote provider
                var anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(new SyncChangeSet(changeSetAfterUserAdd.Anchor, localChangeSet.Items));
                //given we didn't provide a resolution function for the conflict provider just skip 
                //to apply the changes from local db
                //so nothing should be changed in remote db
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                //user should not be present
                var userNotChangedInRemoteDb = await remoteDb.Users.FirstOrDefaultAsync(_ => _.Email == newUser.Email);
                Assert.IsNull(userNotChangedInRemoteDb);

                //ok now try apply changes but forcing any write on remote store on conflict
                anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(new SyncChangeSet(changeSetAfterUserAdd.Anchor, localChangeSet.Items),
                    (item) =>
                    {
                        //assert that conflict occurred on item we just got from local db
                        Assert.IsNotNull(item);
                        Assert.AreEqual(newUserInLocalDb.Email, item.Values["Email"]);
                        Assert.AreEqual(newUserInLocalDb.Name, item.Values["Name"]);
                        Assert.AreEqual(ChangeType.Update, item.ChangeType);

                        //force write in remote store
                        return ConflictResolution.ForceWrite;
                    });

                //now we should have a new version  (+1)
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                //and local db changes should be applied to remote db
                var userChangedInRemoteDb = await remoteDb.Users.AsNoTracking().FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userChangedInRemoteDb);
                Assert.AreEqual(newUserInLocalDb.Name, userChangedInRemoteDb.Name);

            }
        }

        private async Task Test2(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {

            var newUserLocal = new User() { Email = "user1@email.com", Name = "user1", Created = new DateTime(2018, 1, 1) };
            newUserLocal.Posts.Add(new Post() { Title = "title of post", Content = "content of post", Claps = 2, Stars = 4.5f, Updated = new DateTime(2018, 3, 1) });
            localDb.Users.Add(newUserLocal);
            await localDb.SaveChangesAsync();

            //let's apply changes from local db to remote db
            var localChangeSet = await localSyncProvider.GetInitialSetAsync();
            Assert.IsNotNull(localChangeSet);

            var remoteChangeSet = await remoteSyncProvider.GetInitialSetAsync();

            var changeSetForRemoteDb = new SyncChangeSet(remoteChangeSet.Anchor, localChangeSet.Items);
            var anchorAfterApplyChanges = await remoteSyncProvider.ApplyChangesAsync(changeSetForRemoteDb);
            Assert.IsNotNull(anchorAfterApplyChanges);

            var changeSetAfterApplyChangesToRemoteDb = await remoteSyncProvider.GetIncreamentalChangesAsync(anchorAfterApplyChanges);
            Assert.IsNotNull(changeSetAfterApplyChangesToRemoteDb);
            Assert.AreEqual(0, changeSetAfterApplyChangesToRemoteDb.Items.Count);

            newUserLocal.Posts[0].Comments.Add(new Comment() { Content = "my first comment on post", Created = new DateTime(2018, 3, 2) });
            newUserLocal.Posts[0].Stars = 4.0f;
            newUserLocal.Posts[0].Updated = new DateTime(2018, 3, 2);
            await localDb.SaveChangesAsync();

            localChangeSet = await localSyncProvider.GetIncreamentalChangesAsync(localChangeSet.Anchor);
            Assert.IsNotNull(localChangeSet);

            changeSetForRemoteDb = new SyncChangeSet(anchorAfterApplyChanges, localChangeSet.Items);
            anchorAfterApplyChanges = await remoteSyncProvider.ApplyChangesAsync(changeSetForRemoteDb);
            Assert.IsNotNull(anchorAfterApplyChanges);

            var commentAdded = await remoteDb.Comments.FirstOrDefaultAsync(_ => _.Content == "my first comment on post");
            Assert.IsNotNull(commentAdded);
        }
    }
}
