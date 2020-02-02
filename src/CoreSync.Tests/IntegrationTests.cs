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

                var remoteSyncProvider = new SqliteSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqlSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table("Users")
                        .Table("Posts")
                        .Table("Comments");


                var localSyncProvider = new SqlSyncProvider(localConfigurationBuilder.Configuration);


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

                var remoteSyncProvider = new SqlSyncProvider(remoteConfigurationBuilder.Configuration);

                var localConfigurationBuilder =
                    new SqliteSyncConfigurationBuilder(localDb.ConnectionString)
                        .Table<User>("Users")
                        .Table<Post>("Posts")
                        .Table<Comment>("Comments");

                var localSyncProvider = new SqliteSyncProvider(localConfigurationBuilder.Configuration);


                await TestSyncAgent(localDb, localSyncProvider, remoteDb, remoteSyncProvider);
            }
        }

        private async Task Test1(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var localStoreId = await localSyncProvider.GetStoreIdAsync();
            var remoteStoreId = await remoteSyncProvider.GetStoreIdAsync();

            var initialLocalSet = await localSyncProvider.GetChangesForStoreAsync(remoteStoreId);
            var initialRemoteSet = await remoteSyncProvider.GetChangesForStoreAsync(localStoreId);

            Assert.IsNotNull(initialRemoteSet);
            Assert.IsNotNull(initialRemoteSet.Items);
            Assert.AreEqual(0, initialRemoteSet.Items.Count);

            Assert.IsNotNull(initialLocalSet);
            Assert.IsNotNull(initialLocalSet.Items);
            Assert.AreEqual(0, initialLocalSet.Items.Count);

            var newUser = new User() { Email = "myemail@test.com", Name = "User1", Created = new DateTime(2019, 1, 1) };
            remoteDb.Users.Add(newUser);
            await remoteDb.SaveChangesAsync();

            //applying local (empty) change set should not have consequences
            await remoteSyncProvider.ApplyChangesAsync(initialLocalSet);
            await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, initialLocalSet.SourceAnchor.Version);

            var changeSetAfterUserAdd = await remoteSyncProvider.GetChangesForStoreAsync(localStoreId);
            Assert.IsNotNull(changeSetAfterUserAdd);
            Assert.IsNotNull(changeSetAfterUserAdd.Items);
            Assert.AreEqual(1, changeSetAfterUserAdd.Items.Count);
            Assert.AreEqual(ChangeType.Insert, changeSetAfterUserAdd.Items[0].ChangeType);
            Assert.AreEqual(newUser.Email, changeSetAfterUserAdd.Items[0].Values["Email"]);
            Assert.AreEqual(newUser.Name, changeSetAfterUserAdd.Items[0].Values["Name"]);

            var finalLocalAnchor = await localSyncProvider.ApplyChangesAsync(changeSetAfterUserAdd);
            Assert.IsNotNull(finalLocalAnchor);
            await remoteSyncProvider.SaveVersionForStoreAsync(localStoreId, changeSetAfterUserAdd.SourceAnchor.Version);


            //try to apply same changeset result in an exception
            var exception = await Assert.ThrowsExceptionAsync<InvalidSyncOperationException>(() => localSyncProvider.ApplyChangesAsync(changeSetAfterUserAdd));
            Assert.IsNotNull(exception);

            newUser.Created = new DateTime(2018, 1, 1);
            await remoteDb.SaveChangesAsync();

            {
                //after saved changes version should be updated at 2 as well
                var changeSetAfterUserEdit = await remoteSyncProvider.GetChangesForStoreAsync(localStoreId);
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
                var localChangeSet = await localSyncProvider.GetChangesForStoreAsync(remoteStoreId);
                Assert.IsNotNull(localChangeSet);

                //try to apply changes to remote provider
                var anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(localChangeSet);
                await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);

                //given we didn't provide a resolution function for the conflict provider just skip 
                //to apply the changes from local db
                //so nothing should be changed in remote db
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                var userNotChangedInRemoteDb = await remoteDb.Users.FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userNotChangedInRemoteDb);
                Assert.AreEqual(newUser.Name, userNotChangedInRemoteDb.Name);
                Assert.AreEqual(newUser.Created, userNotChangedInRemoteDb.Created);

                //ok now try apply changes but forcing any write on remote store on conflict
                anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(localChangeSet,
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

                await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);


                //and local db changes should be applied to remote db
                var userChangedInRemoteDb = await remoteDb.Users.AsNoTracking().FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userChangedInRemoteDb);
                Assert.AreEqual(newUserInLocalDb.Name, userChangedInRemoteDb.Name);
                Assert.AreEqual(newUserInLocalDb.Created, userChangedInRemoteDb.Created);
            }

            {
                //now let's try to update a deleted record
                remoteDb.Users.Remove(newUser);
                await remoteDb.SaveChangesAsync();

                var userInLocalDbDeletedOnRemoteDb = await localDb.Users.FirstAsync(_ => _.Email == newUser.Email);
                userInLocalDbDeletedOnRemoteDb.Name = "modified name of a remote delete record";
                await localDb.SaveChangesAsync();

                var localChangeSet = await localSyncProvider.GetChangesForStoreAsync(remoteStoreId);
                Assert.IsNotNull(localChangeSet);

                //try to apply changes to remote provider
                var anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(localChangeSet);
                //given we didn't provide a resolution function for the conflict provider just skip 
                //to apply the changes from local db
                //so nothing should be changed in remote db
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);

                await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);

                //user should not be present
                var userNotChangedInRemoteDb = await remoteDb.Users.FirstOrDefaultAsync(_ => _.Email == newUser.Email);
                Assert.IsNull(userNotChangedInRemoteDb);

                //ok now try apply changes but forcing any write on remote store on conflict
                anchorAfterChangesAppliedFromLocalProvider =
                    await remoteSyncProvider.ApplyChangesAsync(localChangeSet,
                    (item) =>
                    {
                        //assert that conflict occurred on item we just got from local db
                        Assert.IsNotNull(item);
                        Assert.AreEqual(userInLocalDbDeletedOnRemoteDb.Email, item.Values["Email"]);
                        Assert.AreEqual(userInLocalDbDeletedOnRemoteDb.Name, item.Values["Name"]);
                        Assert.AreEqual(ChangeType.Update, item.ChangeType);

                        //force write in remote store
                        return ConflictResolution.ForceWrite;
                    });

                //now we should have a new version  (+1)
                Assert.IsNotNull(anchorAfterChangesAppliedFromLocalProvider);
                await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);

                //and local db changes should be applied to remote db
                var userChangedInRemoteDb = await remoteDb.Users.AsNoTracking().FirstAsync(_ => _.Email == newUser.Email);
                Assert.IsNotNull(userChangedInRemoteDb);
                Assert.AreEqual(userInLocalDbDeletedOnRemoteDb.Name, userChangedInRemoteDb.Name);
                Assert.AreEqual(new DateTime(2019, 1, 1), userChangedInRemoteDb.Created);

            }
        }

        private async Task Test2(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var localStoreId = await localSyncProvider.GetStoreIdAsync();
            var remoteStoreId = await remoteSyncProvider.GetStoreIdAsync();

            var newUserLocal = new User() { Email = "user1@email.com", Name = "user1", Created = new DateTime(2018, 1, 1) };
            newUserLocal.Posts.Add(new Post() { Title = "title of post", Content = "content of post", Claps = 2, Stars = 4.5f, Updated = new DateTime(2018, 3, 1) });
            localDb.Users.Add(newUserLocal);
            await localDb.SaveChangesAsync();

            //let's apply changes from local db to remote db
            var localChangeSet = await localSyncProvider.GetChangesForStoreAsync(remoteStoreId);
            Assert.IsNotNull(localChangeSet);
            Assert.AreEqual(2, localChangeSet.Items.Count);

            var remoteChangeSet = await remoteSyncProvider.GetChangesForStoreAsync(localStoreId);
            Assert.IsNotNull(remoteChangeSet);
            Assert.AreEqual(0, remoteChangeSet.Items.Count);

            var anchorAfterApplyChanges = await remoteSyncProvider.ApplyChangesAsync(localChangeSet);
            Assert.IsNotNull(anchorAfterApplyChanges);
            await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);


            var changeSetAfterApplyChangesToRemoteDb = await remoteSyncProvider.GetChangesForStoreAsync(localStoreId);
            Assert.IsNotNull(changeSetAfterApplyChangesToRemoteDb);
            Assert.AreEqual(0, changeSetAfterApplyChangesToRemoteDb.Items.Count);

            await localSyncProvider.ApplyChangesAsync(changeSetAfterApplyChangesToRemoteDb);
            await remoteSyncProvider.SaveVersionForStoreAsync(localStoreId, changeSetAfterApplyChangesToRemoteDb.SourceAnchor.Version);

            newUserLocal.Posts[0].Comments.Add(new Comment() { Content = "my first comment on post", Created = new DateTime(2018, 3, 2) });
            newUserLocal.Posts[0].Stars = 4.0f;
            newUserLocal.Posts[0].Updated = new DateTime(2018, 3, 2);
            await localDb.SaveChangesAsync();

            localChangeSet = await localSyncProvider.GetChangesForStoreAsync(remoteStoreId);
            Assert.IsNotNull(localChangeSet);

            anchorAfterApplyChanges = await remoteSyncProvider.ApplyChangesAsync(localChangeSet);
            Assert.IsNotNull(anchorAfterApplyChanges);
            await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);

            var commentAdded = await remoteDb.Comments.FirstOrDefaultAsync(_ => _.Content == "my first comment on post");
            Assert.IsNotNull(commentAdded);
        }

        private async Task TestSyncAgent(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
            await syncAgent.SynchronizeAsync();

            //create a user on server
            var remoteUser = new User() { Email = "user@email.com", Name = "user", Created = new DateTime(2018, 1, 1) };
            remoteDb.Users.Add(remoteUser);
            await remoteDb.SaveChangesAsync();

            //sync with remote server
            await syncAgent.SynchronizeAsync();
            remoteDb = remoteDb.Refresh(); //discard any cache data in ef

            //verify that new user is stored now locally too
            var localUser = await localDb.Users.FirstAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual("user", localUser.Name);
            Assert.AreEqual(new DateTime(2018, 1, 1), localUser.Created);

            //create an article for user locally
            var localPost = new Post() { Content = "this is my first post", Title = "First Post", Updated = DateTime.Now.Date };
            localUser.Posts.Add(localPost);
            await localDb.SaveChangesAsync();

            //sync with remote server
            await syncAgent.SynchronizeAsync();
            remoteDb = remoteDb.Refresh(); //discard any cache data in ef

            //verify that user on server and locally have the same post    
            remoteUser = await remoteDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual("user", remoteUser.Name);
            Assert.AreEqual(new DateTime(2018, 1, 1), remoteUser.Created);
            Assert.AreEqual(1, remoteUser.Posts.Count);
            var remotePost = remoteUser.Posts[0];
            Assert.AreEqual(localPost.Author.Name, remotePost.Author.Name);
            Assert.AreEqual(localPost.Content, remotePost.Content);
            Assert.AreEqual(localPost.Title, remotePost.Title);

            //now make a change to post content while claps it on server
            localPost.Content = "this is my my first post edited";
            await localDb.SaveChangesAsync();

            remotePost.Claps += 1;
            await remoteDb.SaveChangesAsync();

            //then sync
            await syncAgent.SynchronizeAsync(conflictResolutionOnLocalStore: ConflictResolution.ForceWrite);
            remoteDb = remoteDb.Refresh(); //discard any cache data in ef

            //verify that claps is 1 on both server and local stores
            //content is not changed because we set conflictResolutionOnLocalStore to ConflictResolution.ForceWrite
            //so server skipped our try to update content while local store forcely write data coming from server
            remoteUser = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user@email.com");
            remotePost = remoteUser.Posts[0];
            Assert.AreEqual("user", remotePost.Author.Name);
            Assert.AreEqual("this is my first post", remotePost.Content);
            Assert.AreEqual(1, remotePost.Claps);

            localUser = await localDb.Users.FirstAsync(_ => _.Email == "user@email.com");
            localPost = localUser.Posts[0];
            Assert.AreEqual("user", remotePost.Author.Name);
            Assert.AreEqual("this is my first post", remotePost.Content);
            Assert.AreEqual(1, remotePost.Claps);

            //so to handle this scenario (when a record is often edited on multiple devices)
            //we should take care of restoring any pending records (posts) locally
            //for example using a PendingPosts table (not synched)


        }
    }
}
