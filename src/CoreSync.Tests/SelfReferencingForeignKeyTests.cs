using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CoreSync.Tests;

/// <summary>
/// Tests that ApplyChangesAsync handles self-referencing foreign keys correctly
/// by retrying failed items when progress is being made.
///
/// Provider-specific test definitions are in SelfReferencingForeignKeyTests.Definitions.cs.
/// </summary>
[TestClass]
public partial class SelfReferencingForeignKeyTests
{
    /// <summary>
    /// Core test: changeset with 2 records where the child appears before the parent.
    /// The retry loop should insert the parent first, then successfully retry the child.
    /// </summary>
    private static async Task TestChildBeforeParent(
        ISyncProvider remoteSyncProvider,
        ISyncProvider localSyncProvider,
        Func<Task> verifyLocalData)
    {
        var localStoreId = await localSyncProvider.GetStoreIdAsync();
        var changeSet = await remoteSyncProvider.GetChangesAsync(localStoreId);

        changeSet.Items.Count.ShouldBe(2);

        // Reorder items so the child comes BEFORE the parent
        var reorderedItems = changeSet.Items.Reverse().ToList();
        reorderedItems[0].Values["Id"].Value.ShouldNotBe(reorderedItems[1].Values["Id"].Value);

        var reorderedChangeSet = new SyncChangeSet(
            changeSet.SourceAnchor,
            changeSet.TargetAnchor,
            reorderedItems);

        var anchor = await localSyncProvider.ApplyChangesAsync(reorderedChangeSet);
        anchor.ShouldNotBeNull();

        await verifyLocalData();
    }

    /// <summary>
    /// Core test: a record references a parent that doesn't exist in the changeset at all.
    /// The retry loop should terminate (no progress) without looping forever.
    /// </summary>
    private static async Task TestUnresolvableReference(
        ISyncProvider remoteSyncProvider,
        ISyncProvider localSyncProvider,
        Func<Task> verifyLocalData)
    {
        var localStoreId = await localSyncProvider.GetStoreIdAsync();
        var changeSet = await remoteSyncProvider.GetChangesAsync(localStoreId);

        changeSet.Items.Count.ShouldBe(1);

        var anchor = await localSyncProvider.ApplyChangesAsync(changeSet);
        anchor.ShouldNotBeNull();

        await verifyLocalData();
    }

    /// <summary>
    /// Core test: 3-level chain (grandchild -> child -> root) arriving in reverse order.
    /// The retry loop needs multiple passes to fully resolve the chain.
    /// </summary>
    private static async Task TestDeepChain(
        ISyncProvider remoteSyncProvider,
        ISyncProvider localSyncProvider,
        Func<Task> verifyLocalData)
    {
        var localStoreId = await localSyncProvider.GetStoreIdAsync();
        var changeSet = await remoteSyncProvider.GetChangesAsync(localStoreId);

        changeSet.Items.Count.ShouldBe(3);

        // Reverse order: grandchild first, then child, then parent
        var reversed = changeSet.Items.Reverse().ToList();

        var reorderedChangeSet = new SyncChangeSet(
            changeSet.SourceAnchor,
            changeSet.TargetAnchor,
            reversed);

        var anchor = await localSyncProvider.ApplyChangesAsync(reorderedChangeSet);
        anchor.ShouldNotBeNull();

        await verifyLocalData();
    }
}
