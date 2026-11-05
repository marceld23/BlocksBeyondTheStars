// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3198: a ship construction site grows under the protections of hand placement. Only the keel used to pass them — the
/// hull could grow into another player's base zone (whose owner then could not build there any more) and through walls
/// and terrain.
/// </summary>
public sealed class KeelBaseProtectionTests : IDisposable
{
    private const string Yard = "shipyard:Pilot";

    private readonly string _root;
    private readonly GameContent _content;

    public KeelBaseProtectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_keelbase_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo, string world = "keelbase")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // the keel goes down at a fixed column (see CustomShipTests)
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static int SurfaceY(SvGameServer server, int x, int z)
    {
        for (int y = 220; y > 1; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    /// <summary>The pilot lays a keel at (40, ground+1, 40) and grows a post one cell up from it — the hull row the tests
    /// extend runs one cell above the keel, through a cleared yard. Returns the keel's world cell.</summary>
    private static Vector3i KeelWithPost(SvGameServer server, PlayerSession pilot)
    {
        var core = new Vector3i(40, SurfaceY(server, 40, 40) + 1, 40);
        for (int x = core.X; x <= core.X + 8; x++)
        {
            server.World.SetBlock(new Vector3i(x, core.Y + 1, core.Z), BlockId.Air); // whatever grows there, it is not the subject
        }

        pilot.State.InstantBuild = true;
        pilot.State.Position = new Vector3f(core.X + 2.5f, core.Y + 1.5f, core.Z + 0.5f);
        server.PlaceShipCoreForTest("Pilot", core.X, core.Y, core.Z);
        Edit(server, 0, 1, 0);
        return core;
    }

    private static void Edit(SvGameServer server, int x, int y, int z)
        => server.HandleStructureEditForTest("Pilot", new StructureEditIntent
        {
            StructureId = Yard,
            X = x,
            Y = y,
            Z = z,
            Mine = false,
            ItemKey = "iron_wall",
        });

    /// <summary>Grows the hull row at structure height 1 from x = 1 to <paramref name="toX"/>; returns how far it got.</summary>
    private static int GrowRow(SvGameServer server, int toX)
    {
        for (int x = 1; x <= toX; x++)
        {
            Edit(server, x, 1, 0);
            if (server.ConstructionBoundsForTest("Pilot")!.Value.Size.X != x + 1)
            {
                return x - 1;
            }
        }

        return toX;
    }

    /// <summary>Founds a base for <paramref name="owner"/> whose zone starts three cells east of the keel.</summary>
    private static void BaseEastOfKeel(SvGameServer server, PlayerSession owner, Vector3i keel)
        => server.PlaceBaseForTest(owner, new Vector3i(keel.X + 3 + WorldConstants.BaseZoneRadius, keel.Y + 1, keel.Z));

    [Fact]
    public void AHullCell_InAForeignBaseZone_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var neighbour = server.AddLocalPlayer("Neighbour");
            var pilot = server.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(server, pilot);
            BaseEastOfKeel(server, neighbour, keel);

            int reached = GrowRow(server, 6);

            Assert.Equal(2, reached); // local x 3 is world x keel+3, the first cell of the neighbour's zone
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.protect.base");
            Assert.True(server.BaseProtectedForTest(new Vector3i(keel.X + 3, keel.Y + 1, keel.Z), "Pilot"));
        }
    }

    [Fact]
    public void AHullCell_InTheBuildersOwnBase_OrAnAllysBase_IsAllowed()
    {
        var server = Started(new RecordingTransport(), out var repo);
        using (repo)
        {
            var ally = server.AddLocalPlayer("Ally");
            var pilot = server.AddLocalPlayer("Pilot");
            server.RequestAllianceForTest("Ally", "Pilot");
            server.RespondAllianceForTest("Pilot", "Ally", accept: true);
            Assert.True(server.AreAllied("Ally", "Pilot"));
            var keel = KeelWithPost(server, pilot);
            BaseEastOfKeel(server, ally, keel);

            Assert.Equal(6, GrowRow(server, 6)); // the ally's zone is open to the pilot, as for a hand-placed block
        }

        var own = Started(new RecordingTransport(), out var ownRepo, "keelbase_own");
        using (ownRepo)
        {
            var pilot = own.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(own, pilot);
            BaseEastOfKeel(own, pilot, keel);

            Assert.Equal(6, GrowRow(own, 6)); // and so is the builder's own base
        }
    }

    [Fact]
    public void AHullCell_InsideASolidWorldBlock_IsRefused()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var keel = KeelWithPost(server, pilot);
            var wall = new Vector3i(keel.X + 2, keel.Y + 1, keel.Z);
            server.World.SetBlock(wall, _content.GetBlock("stone")!.NumericId); // a wall where the row would grow

            Assert.Equal(1, GrowRow(server, 4)); // the hull no longer grows through it
            Assert.Contains(transport.To<ActionRejected>(pilot), r => r.Action == "structure" && r.Reason == "@srv.place.not_empty");
            Assert.Equal(_content.GetBlock("stone")!.NumericId, server.World.GetBlock(wall));
        }
    }
}
