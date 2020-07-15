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
    public partial class IntegrationTests
    {
        public static string ConnectionString => Environment.GetEnvironmentVariable("CORE-SYNC_CONNECTION_STRING") ??
                                                 throw new ArgumentException(
                                                     "Set CORE-SYNC_CONNECTION_STRING environmental variable containing connection string to Sql Server");

        private async Task Test1(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var localStoreId = await localSyncProvider.GetStoreIdAsync();
            var remoteStoreId = await remoteSyncProvider.GetStoreIdAsync();

            var initialLocalSet = await localSyncProvider.GetChangesAsync(remoteStoreId);
            var initialRemoteSet = await remoteSyncProvider.GetChangesAsync(localStoreId);

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
            //await remoteSyncProvider.ApplyChangesAsync(initialLocalSet);
            //await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, initialLocalSet.SourceAnchor.Version);

            var changeSetAfterUserAdd = await remoteSyncProvider.GetChangesAsync(localStoreId);
            Assert.IsNotNull(changeSetAfterUserAdd);
            Assert.IsNotNull(changeSetAfterUserAdd.Items);
            Assert.AreEqual(1, changeSetAfterUserAdd.Items.Count);
            Assert.AreEqual(ChangeType.Insert, changeSetAfterUserAdd.Items[0].ChangeType);
            Assert.AreEqual(newUser.Email, changeSetAfterUserAdd.Items[0].Values["Email"].Value);
            Assert.AreEqual(newUser.Name, changeSetAfterUserAdd.Items[0].Values["Name"].Value);

            var finalLocalAnchor = await localSyncProvider.ApplyChangesAsync(changeSetAfterUserAdd);
            Assert.IsNotNull(finalLocalAnchor);
            await remoteSyncProvider.SaveVersionForStoreAsync(localStoreId, changeSetAfterUserAdd.SourceAnchor.Version);

            newUser.Created = new DateTime(2018, 1, 1);
            await remoteDb.SaveChangesAsync();

            {
                //after saved changes version should be updated at 2 as well
                var changeSetAfterUserEdit = await remoteSyncProvider.GetChangesAsync(localStoreId);
                Assert.IsNotNull(changeSetAfterUserEdit);
                Assert.IsNotNull(changeSetAfterUserEdit.Items);
                Assert.AreEqual(1, changeSetAfterUserEdit.Items.Count);
                Assert.AreEqual(newUser.Email, changeSetAfterUserEdit.Items[0].Values["Email"].Value);
                Assert.AreEqual(newUser.Name, changeSetAfterUserEdit.Items[0].Values["Name"].Value);
                Assert.AreEqual(newUser.Created, changeSetAfterUserEdit.Items[0].Values["Created"].Value);
            }

            {
                //now let's change same record in local database and try to apply changes to remote db
                //this should result in a conflict
                var newUserInLocalDb = await localDb.Users.FirstAsync(_ => _.Name == newUser.Name);
                newUserInLocalDb.Name = "modified-name";
                await localDb.SaveChangesAsync();

                //get changes from local db
                var localChangeSet = await localSyncProvider.GetChangesAsync(remoteStoreId);
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
                        Assert.AreEqual(newUserInLocalDb.Email, item.Values["Email"].Value);
                        Assert.AreEqual(newUserInLocalDb.Name, item.Values["Name"].Value);
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

                var localChangeSet = await localSyncProvider.GetChangesAsync(remoteStoreId);
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
                        Assert.AreEqual(userInLocalDbDeletedOnRemoteDb.Email, item.Values["Email"].Value);
                        Assert.AreEqual(userInLocalDbDeletedOnRemoteDb.Name, item.Values["Name"].Value);
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
            var localChangeSet = await localSyncProvider.GetChangesAsync(remoteStoreId);
            Assert.IsNotNull(localChangeSet);
            Assert.AreEqual(2, localChangeSet.Items.Count);

            var remoteChangeSet = await remoteSyncProvider.GetChangesAsync(localStoreId);
            Assert.IsNotNull(remoteChangeSet);
            Assert.AreEqual(0, remoteChangeSet.Items.Count);

            var anchorAfterApplyChanges = await remoteSyncProvider.ApplyChangesAsync(localChangeSet);
            Assert.IsNotNull(anchorAfterApplyChanges);
            await localSyncProvider.SaveVersionForStoreAsync(remoteStoreId, localChangeSet.SourceAnchor.Version);


            var changeSetAfterApplyChangesToRemoteDb = await remoteSyncProvider.GetChangesAsync(localStoreId);
            Assert.IsNotNull(changeSetAfterApplyChangesToRemoteDb);
            Assert.AreEqual(0, changeSetAfterApplyChangesToRemoteDb.Items.Count);

            await localSyncProvider.ApplyChangesAsync(changeSetAfterApplyChangesToRemoteDb);
            await remoteSyncProvider.SaveVersionForStoreAsync(localStoreId, changeSetAfterApplyChangesToRemoteDb.SourceAnchor.Version);

            newUserLocal.Posts[0].Comments.Add(new Comment() { Content = "my first comment on post", Created = new DateTime(2018, 3, 2) });
            newUserLocal.Posts[0].Stars = 4.0f;
            newUserLocal.Posts[0].Updated = new DateTime(2018, 3, 2);
            await localDb.SaveChangesAsync();

            localChangeSet = await localSyncProvider.GetChangesAsync(remoteStoreId);
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
            await syncAgent.InitializeAsync();
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

            localDb = localDb.Refresh();
            localUser = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user@email.com");
            localPost = localUser.Posts[0];
            Assert.AreEqual("user", localPost.Author.Name);
            Assert.AreEqual("this is my first post", localPost.Content);
            Assert.AreEqual(1, localPost.Claps);

            //so to handle this scenario (when a record is often edited on multiple devices)
            //we should take care of restoring any pending records (posts) locally
            //for example using a PendingPosts table (not synched)


        }

        private async Task TestSyncAgentMultipleRecordsOnSameTable(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
            await syncAgent.InitializeAsync();
            await syncAgent.SynchronizeAsync();

            //create a user on server
            var remoteUser = new User() { Email = "user@email.com", Name = "user", Created = new DateTime(2018, 1, 1) };
            remoteDb.Users.Add(remoteUser);
            await remoteDb.SaveChangesAsync();

            //create a second user on server
            var remoteUser2 = new User() { Email = "user2@email.com", Name = "user2", Created = new DateTime(2019, 1, 1) };
            remoteDb.Users.Add(remoteUser2);
            await remoteDb.SaveChangesAsync();

            //sync with remote server
            await syncAgent.SynchronizeAsync();

            //verify that new user is stored now locally too
            var localUser = await localDb.Users.FirstAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual("user", localUser.Name);
            Assert.AreEqual(new DateTime(2018, 1, 1), localUser.Created);

            //verify that new second user is stored now locally too
            var localUser2 = await localDb.Users.FirstAsync(_ => _.Email == "user2@email.com");
            Assert.AreEqual("user2", localUser2.Name);
            Assert.AreEqual(new DateTime(2019, 1, 1), localUser2.Created);

            localUser.Posts.Add(new Post()
            {
                Content = "This is first post from user 1",
                Updated = new DateTime(2019, 1, 1)
            });

            localUser.Posts.Add(new Post()
            {
                Content = "This is second post from user 1",
                Updated = new DateTime(2019, 1, 2)
            });

            localUser2.Posts.Add(new Post()
            {
                Content = "This is first post from user 2",
                Updated = DateTime.Now
            });
            await localDb.SaveChangesAsync();


            //create a third user on server
            var remoteUser3 = new User() { Email = "user3@email.com", Name = "user3", Created = new DateTime(2019, 1, 1) };
            remoteDb.Users.Add(remoteUser3);
            await remoteDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync();

            localDb = localDb.Refresh();

            //verify that first user is still stored locally
            localUser = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual("user", localUser.Name);
            Assert.AreEqual(new DateTime(2018, 1, 1), localUser.Created);

            //verify that second user is still stored locally
            localUser2 = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user2@email.com");
            Assert.AreEqual("user2", localUser2.Name);
            Assert.AreEqual(new DateTime(2019, 1, 1), localUser2.Created);

            //verify that new third user is stored locally now
            var localUser3 = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user3@email.com");
            Assert.AreEqual("user3", localUser3.Name);
            Assert.AreEqual(new DateTime(2019, 1, 1), localUser3.Created);

            //verify that user posts are still stored locally
            Assert.AreEqual("This is first post from user 1", localUser.Posts.OrderBy(_ => _.Updated).ToArray()[0].Content);
            Assert.AreEqual("This is second post from user 1", localUser.Posts.OrderBy(_ => _.Updated).ToArray()[1].Content);
            Assert.AreEqual("This is first post from user 2", localUser2.Posts.OrderBy(_ => _.Updated).ToArray()[0].Content);

            remoteDb = remoteDb.Refresh();

            //verify that first user is still stored remotely
            remoteUser = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual("user", remoteUser.Name);
            Assert.AreEqual(new DateTime(2018, 1, 1), remoteUser.Created);

            //verify that second user is still stored remotely
            remoteUser2 = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user2@email.com");
            Assert.AreEqual("user2", remoteUser2.Name);
            Assert.AreEqual(new DateTime(2019, 1, 1), remoteUser2.Created);

            //verify that new third user still stored remotely
            remoteUser3 = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user3@email.com");
            Assert.AreEqual("user3", remoteUser3.Name);
            Assert.AreEqual(new DateTime(2019, 1, 1), remoteUser3.Created);

            //verify that user posts are still stored remotely
            Assert.AreEqual("This is first post from user 1", remoteUser.Posts.OrderBy(_ => _.Updated).ToArray()[0].Content);
            Assert.AreEqual("This is second post from user 1", remoteUser.Posts.OrderBy(_ => _.Updated).ToArray()[1].Content);
            Assert.AreEqual(1, remoteUser2.Posts.Count);
            Assert.AreEqual("This is first post from user 2", remoteUser2.Posts.OrderBy(_ => _.Updated).ToArray()[0].Content);

            //now delete a post locally
            localDb.Posts.Remove(localUser2.Posts.First());
            await localDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync();

            remoteDb = remoteDb.Refresh();

            remoteUser2 = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user2@email.com");
            Assert.AreEqual(0, remoteUser2.Posts.Count);

            //now delete a post on server
            remoteDb.Posts.Remove(remoteUser.Posts[0]);
            await remoteDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync();

            localDb = localDb.Refresh();

            localUser = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user@email.com");
            Assert.AreEqual(1, localUser.Posts.Count);


            remoteUser2.Posts.Add(new Post()
            {
                Content = "Post add to remote user while user is delete on local db",
                Updated = DateTime.Now
            });
            remoteUser2.Name = "edited name";
            await remoteDb.SaveChangesAsync();

            localDb.Users.Remove(localUser2);
            await localDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync(conflictResolutionOnLocalStore: ConflictResolution.ForceWrite);

            remoteDb = remoteDb.Refresh();
            localDb = localDb.Refresh();

            //ensure that local db updated (local user 2 is present)
            localUser2 = await localDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user2@email.com");
            localUser2.Posts.Count.ShouldBe(1);
            localUser2.Posts[0].Content.ShouldBe("Post add to remote user while user is delete on local db");

            //ensure that remote db is updated
            remoteUser2 = await remoteDb.Users.Include(_ => _.Posts).FirstAsync(_ => _.Email == "user2@email.com");
            remoteUser2.Posts.Count.ShouldBe(1);
            remoteUser2.Posts[0].Content.ShouldBe("Post add to remote user while user is delete on local db");
        }

        private async Task TestSyncAgentWithInitialData(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            User remoteUser;
            remoteDb.Users.Add(remoteUser = new User() { Email = "user@test.com", Name = "User created before sync", Created = DateTime.Now });

            remoteUser.Posts.Add(new Post() { Content = "This is a post created before sync of the client", Title = "Initial post of user 1", Claps = 1, Stars = 10 });
            remoteUser.Posts.Add(new Post() { Content = "This is a second post created before sync of the client", Title = "Initial post 2 of user 1", Claps = 2, Stars = 1 });

            await remoteDb.SaveChangesAsync();

            await remoteSyncProvider.ApplyProvisionAsync();

            remoteUser.Posts.Add(new Post() { Content = "This is a third post created before sync of the client but after applying provision to remote db", Title = "Initial post 3 of user 1", Claps = 3, Stars = 1 });

            await remoteDb.SaveChangesAsync();

            var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
            await syncAgent.InitializeAsync();

            var localUser = await localDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@test.com");
            localUser.ShouldNotBeNull();
            localUser.Email.ShouldBe("user@test.com");
            localUser.Name.ShouldBe("User created before sync");

            var localUserPosts = localUser.Posts.OrderBy(_ => _.Claps).ToList();
            localUserPosts.Count().ShouldBe(3);
            localUserPosts[0].Content.ShouldBe("This is a post created before sync of the client");
            localUserPosts[0].Title.ShouldBe("Initial post of user 1");
            localUserPosts[0].Claps.ShouldBe(1);
            localUserPosts[0].Stars.ShouldBe(10);

            localUserPosts[1].Content.ShouldBe("This is a second post created before sync of the client");
            localUserPosts[1].Title.ShouldBe("Initial post 2 of user 1");
            localUserPosts[1].Claps.ShouldBe(2);
            localUserPosts[1].Stars.ShouldBe(1);

            localUserPosts[2].Content.ShouldBe("This is a third post created before sync of the client but after applying provision to remote db");
            localUserPosts[2].Title.ShouldBe("Initial post 3 of user 1");
            localUserPosts[2].Claps.ShouldBe(3);
            localUserPosts[2].Stars.ShouldBe(1);

            await syncAgent.SynchronizeAsync();

            localUser.Posts.Add(new Post() { Content = "Post created on local db after first sync", Title = "Post created on local db", Claps = 4 });
            localUserPosts[0].Title = "Post edited on local db";

            await localDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync();

            remoteDb = remoteDb.Refresh();

            var remoteUserPosts = remoteDb.Posts.OrderBy(_ => _.Claps).ToList();
            remoteUserPosts.Count().ShouldBe(4);
            remoteUserPosts[0].Content.ShouldBe("This is a post created before sync of the client");
            //even if edited on localdb post that was synched as initial snapshot can't be modified on server
            remoteUserPosts[0].Title.ShouldBe("Initial post of user 1");
            remoteUserPosts[0].Claps.ShouldBe(1);
            remoteUserPosts[0].Stars.ShouldBe(10);

            remoteUserPosts[1].Content.ShouldBe("This is a second post created before sync of the client");
            remoteUserPosts[1].Title.ShouldBe("Initial post 2 of user 1");
            remoteUserPosts[1].Claps.ShouldBe(2);
            remoteUserPosts[1].Stars.ShouldBe(1);

            remoteUserPosts[2].Content.ShouldBe("This is a third post created before sync of the client but after applying provision to remote db");
            remoteUserPosts[2].Title.ShouldBe("Initial post 3 of user 1");
            remoteUserPosts[2].Claps.ShouldBe(3);
            remoteUserPosts[2].Stars.ShouldBe(1);

            remoteUserPosts[3].Content.ShouldBe("Post created on local db after first sync");
            remoteUserPosts[3].Title.ShouldBe("Post created on local db");
            remoteUserPosts[3].Claps.ShouldBe(4);
            remoteUserPosts[3].Stars.ShouldBe(0);
        }


        private async Task TestSyncAgentWithUpdatedRemoteDeletedLocal(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            #region Initialize remote data, sync local and verify

            User remoteUser;
            remoteDb.Users.Add(remoteUser = new User() { Email = "user@test.com", Name = "User created before sync", Created = DateTime.Now });

            remoteUser.Posts.Add(new Post()
            {
                Content = "This is a post created before sync of the client",
                Title = "Initial post of user 1",
                Claps = 1,
                Stars = 10
            });

            remoteUser.Posts.Add(new Post()
            {
                Content = "This is a second post created before sync of the client",
                Title = "Initial post 2 of user 1",
                Claps = 2,
                Stars = 1
            });

            remoteUser.Posts.Add(new Post()
            {
                Content = "This is a third post created before sync of the client",
                Title = "Initial post 3 of user 1",
                Claps = 3,
                Stars = 1
            });

            await remoteDb.SaveChangesAsync();

            var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
            await syncAgent.InitializeAsync();

            var localUser = await localDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@test.com");
            localUser.ShouldNotBeNull();
            localUser.Email.ShouldBe("user@test.com");
            localUser.Name.ShouldBe("User created before sync");

            var localUserPosts = localUser.Posts.OrderBy(_ => _.Claps).ToList();
            localUserPosts.Count().ShouldBe(3);
            localUserPosts[0].Content.ShouldBe("This is a post created before sync of the client");
            localUserPosts[0].Title.ShouldBe("Initial post of user 1");
            localUserPosts[0].Claps.ShouldBe(1);
            localUserPosts[0].Stars.ShouldBe(10);

            localUserPosts[1].Content.ShouldBe("This is a second post created before sync of the client");
            localUserPosts[1].Title.ShouldBe("Initial post 2 of user 1");
            localUserPosts[1].Claps.ShouldBe(2);
            localUserPosts[1].Stars.ShouldBe(1);

            localUserPosts[2].Content.ShouldBe("This is a third post created before sync of the client");
            localUserPosts[2].Title.ShouldBe("Initial post 3 of user 1");
            localUserPosts[2].Claps.ShouldBe(3);
            localUserPosts[2].Stars.ShouldBe(1);

            await syncAgent.SynchronizeAsync();

            remoteDb = remoteDb.Refresh();

            var remoteUserPosts = remoteDb.Posts.OrderBy(_ => _.Claps).ToList();
            remoteUserPosts.Count().ShouldBe(3);
            remoteUserPosts[0].Content.ShouldBe("This is a post created before sync of the client");
            //even if edited on localdb post that was synched as initial snapshot can't be modified on server
            remoteUserPosts[0].Title.ShouldBe("Initial post of user 1");
            remoteUserPosts[0].Claps.ShouldBe(1);
            remoteUserPosts[0].Stars.ShouldBe(10);

            remoteUserPosts[1].Content.ShouldBe("This is a second post created before sync of the client");
            remoteUserPosts[1].Title.ShouldBe("Initial post 2 of user 1");
            remoteUserPosts[1].Claps.ShouldBe(2);
            remoteUserPosts[1].Stars.ShouldBe(1);

            remoteUserPosts[2].Content.ShouldBe("This is a third post created before sync of the client");
            remoteUserPosts[2].Title.ShouldBe("Initial post 3 of user 1");
            remoteUserPosts[2].Claps.ShouldBe(3);
            remoteUserPosts[2].Stars.ShouldBe(1);

            #endregion

            #region Remove a local record and verify

            var postToRemove = localUser.Posts[2];

            localDb.Posts.Remove(localUser.Posts[2]);
            await localDb.SaveChangesAsync();

            localUser = await localDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@test.com");
            Assert.AreEqual(2, localUser.Posts.Count);

            #endregion

            #region Update a remote record

            var remotePost = remoteUser.Posts.First(p => p.Id == postToRemove.Id);
            remotePost.Content = "Updated remote for test.";

            remoteDb = remoteDb.Refresh();
            remoteDb.Posts.Update(remotePost);

            await remoteDb.SaveChangesAsync();

            remoteUser = await remoteDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@test.com");
            Assert.AreEqual("Updated remote for test.", remoteUser.Posts.First(p => p.Id == postToRemove.Id).Content);

            #endregion

            #region Syncronize and verify the local database has the deleted record
            //I've delete a post on local database AND updated the same posts on rempte database
            //SynchronizeAsync() accepts 2 args remoteConflictResolutionFunc and localConflictResolutionFunc
            //that describe how agent should deal with conflits:
            //1) remoteConflictResolutionFunc: (itemInConflict) => ConflictResolution.Skip or simply remoteConflictResolution: ConflictResolution.Skip
            //   means that remote database should not apply the delete operation on post coming from local database
            //2) localConflictResolutionFunc: (itemInConflict) => ConflictResolution.ForceWrite or simply localConflictResolutionFunc: ConflictResolution.ForceWrite
            //   means that local datase should update the local post that was updated from remote db and since the record doesn't exist anymore it actually means insert
            //   the post record again with data coming from server

            await syncAgent.SynchronizeAsync();
            //this above call is the same as this:
            // await syncAgent.SynchronizeAsync(conflictResolutionOnRemoteStore: ConflictResolution.Skip, conflictResolutionOnLocalStore: ConflictResolution.ForceWrite);
            //Thanks to Mike Perrenoud I've found that by default await syncAgent.SynchronizeAsync() was instead defaulted to 
            // await syncAgent.SynchronizeAsync(conflictResolutionOnRemoteStore: ConflictResolution.Skip, conflictResolutionOnLocalStore: ConflictResolution.Skip);
            // causing this test to fail

            // following code allows to enumerate any item in conflict and take appropriate action for each of them:
            //await syncAgent.SynchronizeAsync(
            //    remoteConflictResolutionFunc: (itemInConflict) =>
            //    {
            //        itemInConflict.ChangeType.ShouldBe(ChangeType.Delete);
            //        itemInConflict.TableName.ShouldBe("Posts");
            //        itemInConflict.Values["Id"].Value = postToRemove.Id;
            //        return ConflictResolution.Skip;
            //    },
            //    localConflictResolutionFunc: (itemInConflict) =>
            //    {
            //        return ConflictResolution.ForceWrite;
            //    });

            localDb = localDb.Refresh();

            localUser = await localDb.Users.Include(_ => _.Posts).FirstOrDefaultAsync(_ => _.Email == "user@test.com");
            Assert.AreEqual(3, localUser.Posts.Count);
            Assert.AreEqual("Updated remote for test.", localUser.Posts.First(p => p.Id == postToRemove.Id).Content);

            #endregion
        }


        private async Task TestSyncAgentWithDataRetention(
            BlogDbContext localDb,
            ISyncProvider localSyncProvider,
            BlogDbContext remoteDb,
            ISyncProvider remoteSyncProvider)
        {
            //setup change tracking
            await remoteSyncProvider.ApplyProvisionAsync();

            //create remote data
            User remoteUser;
            remoteDb.Users.Add(remoteUser = new User() { Email = "user@test.com", Name = "User created before sync", Created = DateTime.Now });

            for (int i = 1; i <= 10; i++)
            {
                remoteUser.Posts.Add(new Post()
                {
                    Content = $"Content of Post {i}",
                    Title = $"Post {i}",
                    Claps = 1,
                    Stars = 10
                });
            }

            await remoteDb.SaveChangesAsync();


            //set data retention -> remove first records in the change tracking table
            var remoteSyncVersion = await remoteSyncProvider.GetSyncVersionAsync();
            remoteSyncVersion.Minimum.ShouldBe(1);
            remoteSyncVersion.Current.ShouldBe(11);//1 users + 10 posts

            remoteSyncVersion = await remoteSyncProvider.ApplyRetentionPolicyAsync(4);
            remoteSyncVersion.Minimum.ShouldBe(4);
            remoteSyncVersion.Current.ShouldBe(11);// 11 - 4 = 7 posts

            //local provider is still not synched
            var localSyncVersion = await localSyncProvider.GetSyncVersionAsync();
            localSyncVersion.Minimum.ShouldBe(0);
            localSyncVersion.Current.ShouldBe(0);

            //now let's sync
            var syncAgent = new SyncAgent(localSyncProvider, remoteSyncProvider);
            await syncAgent.InitializeAsync();

            localSyncVersion = await localSyncProvider.GetSyncVersionAsync();
            localSyncVersion.Minimum.ShouldBe(0);
            localSyncVersion.Current.ShouldBe(0);

            (await localDb.Users.ToListAsync()).Count.ShouldBe(1);
            (await localDb.Posts.ToListAsync()).Count.ShouldBe(10);

            //now add a new post on server, one on local db than sync
            remoteDb = remoteDb.Refresh();
            remoteUser = await remoteDb.Users.FirstAsync(_ => _.Email == "user@test.com");

            remoteUser.Posts.Add(new Post()
            {
                Content = $"Another post on server",
                Title = $"New post on server",
                Claps = 1,
                Stars = 10
            });

            await remoteDb.SaveChangesAsync();

            var localUser = await localDb.Users.FirstAsync(_ => _.Email == "user@test.com");

            localUser.Posts.Add(new Post()
            {
                Content = $"A post from client",
                Title = $"New post on client",
                Claps = 1,
                Stars = 10
            });

            await localDb.SaveChangesAsync();

            await syncAgent.SynchronizeAsync();

            remoteDb = remoteDb.Refresh();
            localDb = localDb.Refresh();

            (await remoteDb.Users.SingleAsync()).Email.ShouldBe("user@test.com");
            (await localDb.Users.SingleAsync()).Email.ShouldBe("user@test.com");

            (await remoteDb.Posts.ToListAsync()).Count.ShouldBe(12);
            (await localDb.Posts.ToListAsync()).Count.ShouldBe(12);
           

            remoteSyncVersion = await remoteSyncProvider.ApplyRetentionPolicyAsync(4);
            remoteSyncVersion.Minimum.ShouldBe(4);
            remoteSyncVersion.Current.ShouldBe(13);// +2 posts

            localSyncVersion = await localSyncProvider.GetSyncVersionAsync();
            localSyncVersion.Minimum.ShouldBe(1);
            localSyncVersion.Current.ShouldBe(2); // +2 posts
        }
    }
}
