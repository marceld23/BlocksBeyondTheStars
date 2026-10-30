// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The teleporter's places (#2994) and the Quantum Teleporter (#2995): a jump to an own or allied base, beam pad, radio
/// beacon or map marker on the same body, never to a stranger's; a free point (typed coordinates or the map waypoint) only
/// with the Quantum Teleporter, wrapped to the canonical range, on the surface, never over lava; the costs and cooldowns
/// come from <see cref="TeleportRules"/>. Plus the marker positions kept canonical (#2993).
/// </summary>
public sealed class TeleportToPointTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TeleportToPointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tppoint_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string tag = "tppoint")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A flat 9×9 stone floor at <paramref name="floorY"/> with clear air above, high above the terrain.</summary>
    private void Floor(SvGameServer server, int cx, int floorY, int cz)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = cx - 4; x <= cx + 4; x++)
        {
            for (int z = cz - 4; z <= cz + 4; z++)
            {
                server.World.SetBlock(new Vector3i(x, floorY, z), stone);
                for (int y = floorY + 1; y <= floorY + 4; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }
        }
    }

    private static PlayerSession Player(SvGameServer server, string name, string device = TeleportRules.SuitTeleporterItem)
    {
        var p = server.AddLocalPlayer(name);
        p.State.SuitEnergy = 100f;
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(-300.5f, 120f, -300.5f);
        p.State.Inventory.Add(device, 1, 1);
        return p;
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAlliance(a, b);
        server.RespondAlliance(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    private static float HorizontalDistance(Vector3f a, float x, float z)
        => MathF.Sqrt(((a.X - x) * (a.X - x)) + ((a.Z - z) * (a.Z - z)));

    [Fact]
    public void OwnBase_LandsBesideTheCore_AndCostsTheSuitTeleportersPrice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            Floor(server, 40, 300, 40);
            server.PlaceBaseForTest(alice, new Vector3i(40, 301, 40));

            server.TeleportToPointForTest(alice, TeleportRules.KindBase, 40, 301, 40);

            var at = alice.State.Position;
            Assert.Equal(301f, at.Y);                                   // on the floor, beside the core
            Assert.InRange(HorizontalDistance(at, 40.5f, 40.5f), 0.9f, 1.6f);
            Assert.Equal(100f - TeleportRules.SuitEnergyCost, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void AStrangersBase_IsRefused_AnAlliesBaseIsNot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            alice.State.Role = BlocksBeyondTheStars.Shared.State.PlayerRole.Player; // a local player is the host (admin), and an admin may use any place
            var bob = server.AddLocalPlayer("Bob");
            Floor(server, 40, 300, 40);
            server.PlaceBaseForTest(bob, new Vector3i(40, 301, 40));
            var before = alice.State.Position;

            server.TeleportToPointForTest(alice, TeleportRules.KindBase, 40, 301, 40);
            Assert.Equal(before, alice.State.Position);                // a stranger's base: refused, nothing spent
            Assert.Equal(100f, alice.State.SuitEnergy);

            Ally(server, "Alice", "Bob");
            server.TeleportToPointForTest(alice, TeleportRules.KindBase, 40, 301, 40);
            Assert.Equal(301f, alice.State.Position.Y);
        }
    }

    [Fact]
    public void BeamPad_LandsOnTop_WithoutStandingOnAPad()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            Floor(server, 60, 300, 60);
            server.World.SetBlock(new Vector3i(60, 301, 60), _content.GetBlock("beam_block")!.NumericId);
            server.PlaceBeamForTest(alice, new Vector3i(60, 301, 60), "Mine");

            server.TeleportToPointForTest(alice, TeleportRules.KindBeam, 60, 301, 60);

            Assert.Equal(new Vector3f(60.5f, 302f, 60.5f), alice.State.Position);
        }
    }

    [Fact]
    public void OwnMarker_LandsOnTheSurfaceColumn_AndAPingIsNoTarget()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            server.SetMarkerForTest("Alice", 120.2f, 80f, 75.7f, "Camp");
            string id = server.VisibleMarkersForTest("Alice").Single(m => !m.Ping).Id;
            server.MarkExploredForTest(alice, 120, 75); // the suit teleporter reaches markers on explored ground

            server.TeleportToPointForTest(alice, TeleportRules.KindMarker, 0, 0, 0, id);

            var at = alice.State.Position;
            Assert.Equal(120.5f, at.X);
            Assert.Equal(75.5f, at.Z);
            Assert.False(server.World.GetBlock(new Vector3i(120, (int)at.Y - 1, 75)).IsAir); // standing on something

            server.ResetTeleportCooldownForTest("Alice");
            server.PingForTest("Alice", 10f, 80f, 10f);
            server.TeleportToPointForTest(alice, TeleportRules.KindMarker, 0, 0, 0, "ping");
            Assert.Equal(at, alice.State.Position); // a ping is never a destination
        }
    }

    [Fact]
    public void AMarkerOnUnknownGround_NeedsTheQuantumTeleporter()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            server.SetMarkerForTest("Alice", 900.5f, 80f, 400.5f, "Far");
            string id = server.VisibleMarkersForTest("Alice").Single(m => !m.Ping).Id;
            var before = alice.State.Position;

            server.TeleportToPointForTest(alice, TeleportRules.KindMarker, 0, 0, 0, id);
            Assert.Equal(before, alice.State.Position); // a marker set on ground never seen is a free point

            alice.State.Inventory.Add(TeleportRules.QuantumTeleporterItem, 1, 1);
            server.TeleportToPointForTest(alice, TeleportRules.KindMarker, 0, 0, 0, id);
            Assert.Equal(900.5f, alice.State.Position.X);
            Assert.Equal(400.5f, alice.State.Position.Z);
            Assert.Equal(100f - TeleportRules.QuantumEnergyCost, alice.State.SuitEnergy); // unknown ground: a free jump's price
        }
    }

    [Fact]
    public void Coordinates_NeedTheQuantumTeleporter()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            var before = alice.State.Position;

            server.TeleportToPointForTest(alice, TeleportRules.KindCoords, 100, 0, 50);

            Assert.Equal(before, alice.State.Position);
            Assert.Equal(100f, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void QuantumCoordinates_WrapAndLandOnTheSurface_ForTheQuantumPrice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice", TeleportRules.QuantumTeleporterItem);
            int circ = server.World.Circumference;

            server.TeleportToPointForTest(alice, TeleportRules.KindCoords, circ + 100.3f, 0, 50.9f);

            var at = alice.State.Position;
            Assert.Equal(100.5f, at.X);                                // wrapped into the canonical range
            Assert.Equal(50.5f, at.Z);
            Assert.False(server.World.GetBlock(new Vector3i(100, (int)at.Y - 1, 50)).IsAir);
            Assert.True(server.World.GetBlock(new Vector3i(100, (int)at.Y, 50)).IsAir);
            Assert.Equal(100f - TeleportRules.QuantumEnergyCost, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void QuantumTeleporter_StillJumpsToFixedPlaces_AtTheSuitPrice()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice", TeleportRules.QuantumTeleporterItem);
            Floor(server, 40, 300, 40);
            server.PlaceBaseForTest(alice, new Vector3i(40, 301, 40));

            server.TeleportToPointForTest(alice, TeleportRules.KindBase, 40, 301, 40);

            Assert.Equal(301f, alice.State.Position.Y);
            Assert.Equal(100f - TeleportRules.SuitEnergyCost, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void AFreePointOverLava_IsRefused()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice", TeleportRules.QuantumTeleporterItem);
            server.TeleportToPointForTest(alice, TeleportRules.KindWaypoint, 140, 0, 30);
            int ay = (int)alice.State.Position.Y;
            server.ResetTeleportCooldownForTest("Alice");
            alice.State.SuitEnergy = 100f;
            alice.State.Position = new Vector3f(-300.5f, 120f, -300.5f);
            server.World.SetBlock(new Vector3i(140, ay, 30), _content.GetBlock("lava")!.NumericId);

            server.TeleportToPointForTest(alice, TeleportRules.KindWaypoint, 140, 0, 30);

            Assert.Equal(new Vector3f(-300.5f, 120f, -300.5f), alice.State.Position);
            Assert.Equal(100f, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void TheCooldownIsShared_WithTheShipRecall()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = Player(server, "Alice");
            Floor(server, 40, 300, 40);
            server.PlaceBaseForTest(alice, new Vector3i(40, 301, 40));
            server.TeleportToPointForTest(alice, TeleportRules.KindBase, 40, 301, 40);
            var after = alice.State.Position;

            server.TeleportToShip("Alice");

            Assert.Equal(after, alice.State.Position); // still recharging
        }
    }

    [Fact]
    public void MarkersAreSavedCanonical()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            int circ = server.World.Circumference;

            server.SetMarkerForTest("Alice", circ + 12.5f, 70f, 3.5f, "Far");

            var m = alice.State.Markers.Single();
            Assert.Equal(12.5f, m.X);
            Assert.Equal(3.5f, m.Z);
        }
    }

    [Fact]
    public void TheQuantumTeleporter_IsContentThatResolves()
    {
        var item = _content.GetItem(TeleportRules.QuantumTeleporterItem);
        Assert.NotNull(item);
        Assert.Equal(1, item!.MaxStack);
        var recipe = _content.GetRecipe(TeleportRules.QuantumTeleporterItem);
        Assert.NotNull(recipe);
        Assert.Contains(recipe!.Inputs, i => i.Item == TeleportRules.SuitTeleporterItem); // the upgrade consumes the old device
        Assert.Equal(TeleportRules.QuantumTeleporterItem, recipe.RequiredBlueprint);
        var blueprint = _content.Blueprints.Values.Single(b => b.Key == TeleportRules.QuantumTeleporterItem);
        Assert.Contains("suit_teleporter", blueprint.Prerequisites);
        Assert.Contains("quantum_scanner", blueprint.Prerequisites);
    }
}
