// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The cloud-save version rule (#1355): a newer cloud version replaces the local world, an already-synced
/// one does not — and a fetch that only peeks (the browser name lookup) must leave the synced version
/// untouched, or the boot right behind it silently starts from the older local blob.
/// </summary>
public sealed class CloudSaveVersionsTests
{
    [Theory]
    [InlineData(5, 4, true, true)]    // newer than what we synced → cloud
    [InlineData(4, 4, true, false)]   // this browser uploaded that very version → local
    [InlineData(3, 4, true, false)]   // older than what we synced → local
    [InlineData(4, 4, false, true)]   // no local blob at all → even a seen version beats an empty world
    [InlineData(0, 0, false, true)]
    public void CloudWins_OnlyForNewerVersions_OrWithoutALocalBlob(int cloud, int synced, bool localExists, bool expected)
    {
        Assert.Equal(expected, CloudSaveVersions.CloudWins(cloud, synced, localExists));
    }

    /// <summary>Models the two fetches of a deep-linked browser start: the name peek, then the boot.</summary>
    private static (bool PeekGotCloud, bool BootGotCloud) PeekThenBoot(bool peekRecordsVersion)
    {
        const int cloudVersion = 7;
        int synced = 3; // the last version this browser uploaded; a newer one waits in the cloud
        const bool localExists = true;

        bool peek = CloudSaveVersions.CloudWins(cloudVersion, synced, localExists);
        if (peek && peekRecordsVersion)
        {
            synced = cloudVersion; // what FetchLatest(markSeen: true) does
        }

        bool boot = CloudSaveVersions.CloudWins(cloudVersion, synced, localExists);
        return (peek, boot);
    }

    [Fact]
    public void PeekThatRecordsTheVersion_StarvesTheBoot()
    {
        var (peek, boot) = PeekThenBoot(peekRecordsVersion: true);
        Assert.True(peek);
        Assert.False(boot); // the bug: the name came from the cloud world, the boot got the old local one
    }

    [Fact]
    public void SideEffectFreePeek_LeavesTheCloudVersionForTheBoot()
    {
        var (peek, boot) = PeekThenBoot(peekRecordsVersion: false);
        Assert.True(peek);
        Assert.True(boot);
    }

    /// <summary>Models two starts in a build that is older than the world waiting in the cloud (#2223): the
    /// boot's fetch delivers the cloud world and records its version, the start refuses the world as saved by
    /// a newer version of the game, and the player presses Play again.</summary>
    private static bool SecondBootGetsTheCloudWorld(bool refusalTakesTheMarkBack)
    {
        const int cloudVersion = 4;
        int synced = 3; // another device saved version 4 with a newer build
        const bool localExists = true;

        Assert.True(CloudSaveVersions.CloudWins(cloudVersion, synced, localExists));
        int before = synced;
        synced = cloudVersion; // what FetchLatest(markSeen: true) records before the world is started

        // The start refuses the world (SaveVersionTooNewException) and leaves the local blob as it was.
        synced = CloudSaveVersions.SyncedAfterBoot(before, synced, refusedAsTooNew: refusalTakesTheMarkBack);

        return CloudSaveVersions.CloudWins(cloudVersion, synced, localExists);
    }

    [Fact]
    public void ARefusedCloudWorld_ThatStaysMarkedAsSynced_LetsTheOlderLocalWorldBoot()
    {
        // The bug: the older local world starts without a notice, and its first upload carries the cloud's own
        // version as its base — no conflict, the newer world in the cloud is replaced.
        Assert.False(SecondBootGetsTheCloudWorld(refusalTakesTheMarkBack: false));
    }

    [Fact]
    public void ARefusedCloudWorld_IsNotCountedAsSynced_SoTheNextStartMeetsItAgain()
    {
        Assert.True(SecondBootGetsTheCloudWorld(refusalTakesTheMarkBack: true));
    }

    [Theory]
    [InlineData(3, 4, true, 3)]   // refused as too new → what was synced before the fetch
    [InlineData(3, 4, false, 4)]  // started (or refused for another reason) → the fetched version stays recorded
    [InlineData(0, 1, true, 0)]   // a browser that had never synced stays at "nothing synced"
    public void SyncedAfterBoot_TakesTheMarkBack_OnlyForAWorldRefusedAsTooNew(int before, int fetched, bool refused, int expected)
    {
        Assert.Equal(expected, CloudSaveVersions.SyncedAfterBoot(before, fetched, refused));
    }
}
