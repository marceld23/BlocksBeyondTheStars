// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3173: the real <see cref="NetworkClient"/> against the real server over the ENCODED wire hands the game a bridge deck's
/// changes with <c>BlockChanged.Moved</c> set — the flag the Unity client's <c>GameBootstrap.BlockChangeApplied</c> passes
/// on so the mining crumble and sound stay off — while a block mined by hand arrives as an ordinary edit.
/// </summary>
public sealed class MovedBlockClientTests
{
    [Fact]
    public void ABridgeDeck_ArrivesAsMoved_AndAHandMinedDeck_AsAnOrdinaryEdit()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content, passObjects: false);
        h.Join("Builder");
        Assert.NotNull(h.JoinAccepted);
        string me = h.JoinAccepted!.PlayerId;
        var session = h.Server.Sessions.Values.Single(s => s.State.PlayerId == me);
        session.State.AboardShip = false;
        session.State.SuitEnergy = 100f;
        foreach (var key in new[] { "bridge_motor", "crystal_switch", "stone" })
        {
            Assert.Equal(0, session.State.Inventory.Add(key, 64, 1024));
        }

        var at = new Vector3f(0, 203, 0); // up in the air: the cells around y 200 are empty and in reach
        session.State.Position = at;
        h.Server.PlaceBlock(me, 0, 200, 0, "crystal_switch");
        h.Server.PlaceBlock(me, 1, 200, 0, "bridge_motor", deviceDir: 1); // points +X
        h.Server.PlaceBlock(me, 4, 200, 0, "stone");                    // two deck blocks fit before it
        h.Server.SetCrystalDeviceForTest(session, new Vector3i(0, 200, 0), action: 0);
        for (int i = 0; i < 25; i++)
        {
            session.State.Position = at;
            h.Tick(0.1);
        }

        ushort deck = content.GetBlock("bridge_deck")!.NumericId.Value;
        var decks = h.BlockChanges.Where(m => m.Y == 200 && m.Z == 0 && m.Block == deck).ToList();
        Assert.Equal(new[] { 2, 3 }, decks.Select(m => m.X).OrderBy(x => x));
        Assert.All(decks, m => Assert.True(m.Moved));

        h.Server.MineBlock(me, 3, 200, 0);
        h.Tick(0.1);
        var mined = h.BlockChanges.Last(m => m.X == 3 && m.Y == 200 && m.Z == 0);
        Assert.Equal(0, mined.Block);
        Assert.False(mined.Moved);
    }
}
