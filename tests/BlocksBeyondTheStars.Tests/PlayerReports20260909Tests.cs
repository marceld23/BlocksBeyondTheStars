// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The player reports of 2026-09-09, on 2026.9.4 — two from the school club, three from a builder on her own
/// server. Three of them are the same shape: the world decided a player or a creature belonged somewhere a
/// body cannot be, and the code that was supposed to notice could not see it.
///   · #1709 a player sealed inside their OWN hull had no rescue — the block rescue cannot see hulls at all,
///     and the hull rescue asked for two of them. The hull is indestructible, so he could not dig out either.
///   · #1708 the entombed rescue re-fired at 1 Hz, re-arming the client's settle freeze faster than its 8 s
///     grace could lift it: frozen in place, only the mouse working.
///   · #1710 the pad-ground guard protected every cell under a parked ship, player-placed blocks included,
///     and called a wooden door a ship hull.
/// </summary>
public sealed class PlayerReports20260909Tests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlayerReports20260909Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_reports0909_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo, bool withShip)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "reports0909"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "reports0909",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = withShip,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- #1709: sealed inside your own hull ----------------

    /// <summary>The trap itself: a cell whose feet AND head are hull blocks has no body space, while the
    /// cabins and corridors of the same ship — where players are meant to be — do not read as sealed.</summary>
    [Fact]
    public void SealedInHull_IsTrueInsideHullGeometry_AndFalseInTheCabin()
    {
        var server = Start(out var repo, withShip: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Paul");
            var sealedSpot = server.SealedHullSpotForTest(p.State.PlayerId);
            Assert.NotNull(sealedSpot); // the starter hull has walls; if it ever stops having any, say so loudly
            Assert.True(server.SealedInHullForTest(sealedSpot!.Value));

            // Standing aboard normally is not a trap — that distinction is the whole point of the guard.
            Assert.False(server.SealedInHullForTest(p.State.Position));
        }
    }

    /// <summary>The rescue: a player who ends up inside hull geometry — the shape of "fell, respawned at the
    /// heal tank, woke up inside the wall" — is moved somewhere a body fits, within one rescue tick.</summary>
    [Fact]
    public void RuntimeRescue_FreesAPlayerSealedInsideTheirOwnHull()
    {
        var server = Start(out var repo, withShip: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Paul");
            var sealedSpot = server.SealedHullSpotForTest(p.State.PlayerId);
            Assert.NotNull(sealedSpot);

            p.State.Position = sealedSpot!.Value;
            p.State.AboardShip = true;
            Assert.True(server.SealedInHullForTest(p.State.Position));

            server.RunVoidRescueForTest();

            Assert.False(server.SealedInHullForTest(p.State.Position));
            Assert.NotEqual(sealedSpot.Value, p.State.Position);
        }
    }

    /// <summary>Being inside your own ship must stay ordinary: the rescue does not fire on a player standing
    /// in the cabin. A guard that evicted people from their own ship would be worse than the bug.</summary>
    [Fact]
    public void RuntimeRescue_LeavesAPlayerStandingInTheirCabinAlone()
    {
        var server = Start(out var repo, withShip: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Lyxette");
            var before = p.State.Position;
            Assert.False(server.SealedInHullForTest(before));

            server.RunVoidRescueForTest();

            Assert.Equal(before, p.State.Position);
        }
    }

    // ---------------- #1708: the rescue must not re-fire while it is still landing ----------------

    /// <summary>A RespawnNotice is a hard snap on the client: it re-arms the settle freeze, which only lifts
    /// after an 8 s grace. Sending another every second meant the grace never elapsed and the player sat
    /// frozen — "kann mich nicht bewegen, nur drehen". While the last placement is unacknowledged, no second
    /// rescue goes out; once the client has adopted it, a player who really is still stuck is rescued again.
    /// </summary>
    [Fact]
    public void EntombedRescue_DoesNotFireAgain_WhileTheLastPlacementIsUnacknowledged()
    {
        var server = Start(out var repo, withShip: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            var buried = Entomb(server, p.State.Position);
            p.State.Position = buried;
            p.AwaitingSpawnAdopt = false;

            server.RunVoidRescueForTest();
            var afterFirst = p.State.Position;
            Assert.NotEqual(buried, afterFirst);
            Assert.True(p.AwaitingSpawnAdopt, "a rescue placement must wait to be adopted");

            // Still sealed in (the client has not moved yet) — the old code fired again here, every second.
            p.State.Position = buried;
            server.RunVoidRescueForTest();
            Assert.Equal(buried, p.State.Position); // suppressed: no second snap while the first is in flight

            // The client catches up and adopts the placement; now the rescue may act again.
            p.AwaitingSpawnAdopt = false;
            server.RunVoidRescueForTest();
            Assert.NotEqual(buried, p.State.Position);
        }
    }

    /// <summary>Buries a position in solid stone so the entombment guard sees it, and returns it.</summary>
    private Vector3f Entomb(SvGameServer server, Vector3f near)
    {
        int bx = (int)Math.Floor(near.X), bz = (int)Math.Floor(near.Z);
        int by = (int)Math.Floor(near.Y) - 6;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int y = by - 1; y <= by + 3; y++)
        {
            server.World.SetBlock(new Vector3i(bx, y, bz), stone);
        }

        var pos = new Vector3f(bx + 0.5f, by, bz + 0.5f);
        Assert.True(server.IsEntombedForTest(pos), "the carved block should read as entombed");
        return pos;
    }

    // ---------------- #1710: the pad guard must not swallow player blocks ----------------

    /// <summary>The ground a hull rests on stays protected; a block the player placed inside the same slab
    /// comes out again. She could not remove two wooden doors she had built, and the game told her they were
    /// ship hull.</summary>
    [Fact]
    public void PadGuard_ProtectsNaturalGround_ButNotABlockThePlayerPlaced()
    {
        var server = Start(out var repo, withShip: true);
        using (repo)
        {
            var p = server.AddLocalPlayer("Lyxette");
            var (origin, size) = server.LandedShipBoundsForTest(p.State.PlayerId);

            // A cell in the protected slab under the hull footprint.
            var cell = new Vector3i(origin.X + size.X / 2, origin.Y - 2, origin.Z + size.Z / 2);
            Assert.True(server.IsProtectedShipBlock(cell.X, cell.Y, cell.Z),
                "the ground under a parked hull is the foundation and stays put");

            // The same cell, but this time the player built there: it is hers to take out again.
            repo.SetBlock(server.World.LocationId, cell,
                _content.GetBlock("door_wood")!.NumericId.Value, owner: p.State.PlayerId);
            Assert.False(server.IsProtectedShipBlock(cell.X, cell.Y, cell.Z));
        }
    }

    // ---------------- #1711: a creature under a roof must not be handed the surface above it ----------------

    /// <summary>A player photographed a sleeping creature clipped halfway into her cave ceiling. The band an
    /// airborne creature rests at falls back to the generator's noise surface when the column offers nothing
    /// else — and for an animal under a roof that Y is on the far side of solid rock. Hollow out a cave, seal
    /// it with a thick ceiling, and the probe must keep the creature in the cave.</summary>
    [Fact]
    public void RestSurface_KeepsACreatureUnderItsCeiling_InsteadOfTheSurfaceAbove()
    {
        var server = Start(out var repo, withShip: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Lyxette");
            int x = (int)Math.Floor(p.State.Position.X) + 24;
            int z = (int)Math.Floor(p.State.Position.Z) + 24;
            int surface = (int)Math.Floor(p.State.Position.Y);

            var stone = _content.GetBlock("stone")!.NumericId;
            int caveFloor = surface - 20;
            int caveTop = caveFloor + 4; // a 4-cell hollow …

            // Fill the column solid, then hollow the cave out of it: floor below, thick roof above.
            for (int y = caveFloor - 2; y <= surface; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), stone);
            }

            for (int y = caveFloor; y < caveTop; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
            }

            int rest = server.RestSurfaceYForTest(x, z, caveFloor + 1);

            Assert.True(rest < caveTop,
                $"a creature in the cave must rest inside it, not at Y {rest} with the ceiling at {caveTop}");
            Assert.True(rest < surface, "the noise surface above the roof is not a place it can come to rest");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp dir is not worth failing a test run over.
        }
    }
}
