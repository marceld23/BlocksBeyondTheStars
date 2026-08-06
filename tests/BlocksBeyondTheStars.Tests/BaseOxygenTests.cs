// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The base life-support field (#782): a founded base's zone (the shared radius-8 cube around its base_core)
/// always breathes — oxygen regenerates for ANYONE standing inside it, even on a world whose own air is not
/// breathable, and the field vanishes together with the base when the core is mined. Rocky (toxic atmosphere)
/// makes oxygen matter; every test position stays below rocky's atmosphere line (190), so only the toxic air —
/// not the altitude — is what the base has to overcome.
/// </summary>
public sealed class BaseOxygenTests : IDisposable
{
    // Mid-air on rocky (terrain tops out well below): the core cell is empty, in reach, below the atmosphere line.
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;

    private readonly string _root;
    private readonly GameContent _content;

    public BaseOxygenTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_baseoxy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string name)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // toxic atmosphere → oxygen drains outside a life-support field
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Adds a player on foot. With no physical ship placed, UpdateAboard keeps the spawn default
    /// (aboard = life support everywhere), so the flag must be dropped explicitly for oxygen to matter.</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Adds "Builder" hovering next to the core cell and founds their base at (CoreX, CoreY, CoreZ).</summary>
    private static PlayerSession Founder(SvGameServer server)
    {
        var p = OnFoot(server, "Builder");
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock("Builder", CoreX, CoreY, CoreZ, "base_core");
        Assert.Single(server.BaseSnapshots);
        return p;
    }

    /// <summary>Ticks the environment with the player pinned at <paramref name="at"/> (re-set every tick so
    /// nothing that nudges positions can drift the player out of / into the zone mid-test).</summary>
    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds = 6)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void InsideBaseZone_OxygenRefills_OutsideItDrains()
    {
        var server = Start(out var repo, "baseoxy");
        using (repo)
        {
            var p = Founder(server);
            Assert.False(server.AtmosphereBreathable, "rocky should be toxic for this test");

            // Inside the zone (well off-centre, still within the radius-8 cube): the base breathes.
            float inside = p.State.Oxygen = 50f;
            TickAt(server, p, new Vector3f(CoreX + 5.5f, CoreY + 3f, CoreZ + 2.5f));
            Assert.True(p.State.Oxygen > inside, $"Oxygen should refill inside the base zone (was {p.State.Oxygen}).");

            // One step past the cube: the toxic air wins again.
            float outside = p.State.Oxygen = 80f;
            TickAt(server, p, new Vector3f(CoreX + 30.5f, CoreY, CoreZ + 0.5f));
            Assert.True(p.State.Oxygen < outside, $"Oxygen should drain outside the base zone (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void StrangerInSomeoneElsesBaseZone_AlsoBreathes()
    {
        var server = Start(out var repo, "baseoxyguest");
        using (repo)
        {
            Founder(server);

            // A second, unallied player: the field is not ownership-gated — visitors breathe too
            // (the build protection still keeps them from touching anything).
            var guest = OnFoot(server, "Visitor");
            float before = guest.State.Oxygen = 50f;
            TickAt(server, guest, new Vector3f(CoreX - 4.5f, CoreY + 1f, CoreZ - 3.5f));
            Assert.True(guest.State.Oxygen > before, $"A visitor should breathe in a stranger's base zone (was {guest.State.Oxygen}).");
        }
    }

    [Fact]
    public void MiningTheBaseCore_RemovesTheAirField()
    {
        var server = Start(out var repo, "baseoxygone");
        using (repo)
        {
            var p = Founder(server);
            var spot = new Vector3f(CoreX + 3.5f, CoreY + 1f, CoreZ + 0.5f);

            float before = p.State.Oxygen = 50f;
            TickAt(server, p, spot);
            Assert.True(p.State.Oxygen > before, $"Sanity: the founded base should breathe (was {p.State.Oxygen}).");

            // Pull the Grundstein: the base — and with it the air field — is gone the moment the core is.
            p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
            server.MineBlock("Builder", CoreX, CoreY, CoreZ);
            Assert.Empty(server.BaseSnapshots);

            float after = p.State.Oxygen = 80f;
            TickAt(server, p, spot);
            Assert.True(p.State.Oxygen < after, $"Oxygen should drain again once the base is dissolved (was {p.State.Oxygen}).");
        }
    }

    [Fact]
    public void SubmergedInsideTheZone_StillBreathes_DomeBehavior()
    {
        var server = Start(out var repo, "baseoxydome");
        using (repo)
        {
            var p = Founder(server);

            // A pool inside the zone: stone floor + a deep body of water (mirrors OxygenTests' pool build).
            var water = _content.GetBlock("water")!.NumericId;
            var stone = _content.GetBlock("stone")!.NumericId;
            int floorY = CoreY - 4;
            for (int x = CoreX + 1; x <= CoreX + 7; x++)
                for (int z = CoreZ - 3; z <= CoreZ + 3; z++)
                {
                    server.World.SetBlock(new Vector3i(x, floorY, z), stone);
                    for (int y = floorY + 1; y <= floorY + 8; y++) server.World.SetBlock(new Vector3i(x, y, z), water);
                }

            // Head fully underwater, still inside the cube: life support overrides the diving drain —
            // an underwater base is a dome (same rule as diving inside the ship's cabin).
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, new Vector3f(CoreX + 4.5f, floorY + 2f, CoreZ + 0.5f));
            Assert.True(p.State.Oxygen > before, $"Submerged inside the base zone the player should still breathe (was {p.State.Oxygen}).");
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
