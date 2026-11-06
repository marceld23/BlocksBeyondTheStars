// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3294 — an owner-locked crate (the finale's reward crate, a knocked-out robot's parts) on the client: the HUD and the
/// Actions list ask <see cref="InteractReach.LockedFor"/> whose it is. Before, every owned container (a village's cache,
/// <c>OwnedBy</c>) was "not yours" for everyone; a locked crate is the owner's to loot and nobody else's.
/// </summary>
public sealed class OwnerLockedCrateHintTests
{
    private static NetContainer Crate(string ownerId, string ownerName, int items)
        => new() { Id = "c", Kind = "crate", OwnerId = ownerId, OwnerName = ownerName, ItemCount = items };

    [Fact]
    public void SomeoneElsesLockedCrate_NamesItsOwner()
    {
        Assert.Equal("Ada", InteractReach.LockedFor(Crate("ada-id", "Ada", 1), "ben-id"));
        Assert.True(InteractReach.IsOwnerLocked(Crate("ada-id", "Ada", 1)));
    }

    [Fact]
    public void TheOwnersOwnCrate_IsTheirsToLoot()
        => Assert.Equal(string.Empty, InteractReach.LockedFor(Crate("ada-id", "Ada", 1), "ada-id"));

    [Fact]
    public void AnEmptiedOrUnownedCrate_IsNobodysLock()
    {
        Assert.Equal(string.Empty, InteractReach.LockedFor(Crate("ada-id", "Ada", 0), "ben-id"));
        Assert.Equal(string.Empty, InteractReach.LockedFor(Crate(string.Empty, string.Empty, 3), "ben-id"));
        Assert.Equal(string.Empty, InteractReach.LockedFor(null, "ben-id"));
        Assert.False(InteractReach.IsOwnerLocked(Crate("ada-id", "Ada", 0)));
    }

    [Fact]
    public void AnOwnerWithoutAName_FallsBackToTheId()
        => Assert.Equal("ada-id", InteractReach.LockedFor(Crate("ada-id", string.Empty, 2), null));

    [Fact]
    public void TheHudAndTheActionsList_AskTheHelper()
    {
        // The Unity scripts are not compiled here, so the wiring is checked in the source (as SeamWiringTests does).
        string hud = ClientTestPaths.ScriptPartsText("HudUi");
        string interact = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("InteractReach.LockedFor(nearest, Game.LocalPlayerId)", hud);
        Assert.Contains("InteractReach.LockedFor(nearest, Game.LocalPlayerId)", interact);
    }
}
