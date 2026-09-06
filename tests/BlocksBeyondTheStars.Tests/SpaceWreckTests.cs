// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Space wrecks (#1664): the star map's derelict bodies exist in flight as voxel hulls — visited by
/// flying up to them, carved for salvage with a mining laser, read for lore.</summary>
public sealed class SpaceWreckTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SpaceWreckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_swreck_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>A world whose galaxy is littered with wrecks (Frequent), free flight + mining on.</summary>
    private SvGameServer NewServer(string name, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = name, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.AsteroidDestruction = AsteroidDestructionMode.MiningOnly;
        config.World.Wrecks = Frequency.Frequent;
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Puts the pilot at a landable body that shares its system with a wreck body (the home system when
    /// it has one, else the first system that does — via a temporary jump generator) and returns both.</summary>
    private static (CelestialBody Wreck, CelestialBody Anchor) ParkNextToAWreck(SvGameServer server, PlayerSession pilot)
    {
        var systems = server.Galaxy.Systems
            .OrderBy(s => s.Bodies.Any(b => b.Id == server.ActiveLocationId) ? 0 : 1) // prefer home
            .ToList();
        foreach (var sys in systems)
        {
            var wreck = sys.Bodies.FirstOrDefault(b => b.Kind == CelestialKind.Wreck);
            var anchor = sys.Bodies.FirstOrDefault(b =>
                b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
                && !string.IsNullOrEmpty(b.PlanetType));
            if (wreck is null || anchor is null)
            {
                continue;
            }

            if (pilot.CurrentLocationId != anchor.Id)
            {
                if (!server.Ship.Modules.Contains("jump_generator"))
                {
                    server.Ship.Modules.Add("jump_generator");
                }

                server.Travel(pilot.State.PlayerId, anchor.Id);
                Assert.Equal(anchor.Id, pilot.CurrentLocationId);
            }

            return (wreck, anchor);
        }

        throw new Xunit.Sdk.XunitException("the Frequent wreck setting should litter at least one system with a wreck");
    }

    [Fact]
    public void EnterSpace_ParksTheSystemsWreck_AsAVoxelHullAtItsChartPosition()
    {
        var server = NewServer("wreck_exists", 11, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (wreck, anchor) = ParkNextToAWreck(server, pilot);

            server.EnterSpace("Salvager");
            var entity = server.SpaceEntitiesFor("Salvager").FirstOrDefault(e => e.Id == wreck.Id);
            Assert.NotNull(entity);
            Assert.Equal(CombatEntityKind.Wreck, entity!.Kind);
            Assert.Equal(wreck.Name, entity.Name);
            Assert.False(entity.Hostile);

            // Exactly where the chart puts the body: the star-map delta to the launch body × the flight scale.
            float cx = (wreck.SystemX - anchor.SystemX) * SystemBodyLayout.FlightViewScale;
            float cz = (wreck.SystemZ - anchor.SystemZ) * SystemBodyLayout.FlightViewScale;
            Assert.Equal(cx, entity.Position.X, 3);
            Assert.Equal(cz, entity.Position.Z, 3);

            int cells = server.StructureBlockCountForTest(wreck.Id);
            Assert.True(cells > 0, "a space wreck should be a voxel hull with plating to salvage");
            Assert.Equal(System.Math.Max(8, cells), entity.HullMax); // hull == blocks, like a voxel asteroid
            Assert.Contains(entity.Loot, l => l.Item == "iron_plate");
        }
    }

    [Fact]
    public void SpaceWreck_IsTheSameHull_OnEveryEntry()
    {
        var server = NewServer("wreck_stable", 11, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (wreck, _) = ParkNextToAWreck(server, pilot);

            server.EnterSpace("Salvager");
            var first = server.SpaceEntitiesFor("Salvager").First(e => e.Id == wreck.Id);
            int cellsFirst = server.StructureBlockCountForTest(wreck.Id);
            var posFirst = first.Position;
            var lootFirst = first.Loot.Select(l => (l.Item, l.Count)).ToList();
            server.LeaveSpace("Salvager");

            server.EnterSpace("Salvager");
            var again = server.SpaceEntitiesFor("Salvager").First(e => e.Id == wreck.Id);
            Assert.Equal(cellsFirst, server.StructureBlockCountForTest(wreck.Id));
            Assert.Equal(posFirst, again.Position);
            Assert.Equal(lootFirst, again.Loot.Select(l => (l.Item, l.Count)).ToList());
        }
    }

    [Fact]
    public void FlyingUpToTheWreck_ReadsItsManifest_AndMarksItVisited()
    {
        var server = NewServer("wreck_visit", 11, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            var (wreck, _) = ParkNextToAWreck(server, pilot);
            server.EnterSpace("Salvager");
            var entity = server.SpaceEntitiesFor("Salvager").First(e => e.Id == wreck.Id);

            // Far away: nothing read yet, and the wreck is not a place the pilot has been.
            Assert.DoesNotContain("wreck:" + wreck.Id, pilot.State.Scanned);
            Assert.DoesNotContain(wreck.Id, pilot.State.LandedBodies);

            // Fly up to it — the approach IS the visit.
            server.ShipMove("Salvager", entity.Position.X, entity.Position.Y, entity.Position.Z - 30f);
            Assert.Contains("wreck:" + wreck.Id, pilot.State.Scanned);
            Assert.Contains(pilot.State.Milestones, m => m.StartsWith("lore:derelict", StringComparison.Ordinal));
            Assert.Contains(wreck.Id, pilot.State.LandedBodies);
            Assert.Contains("place:" + wreck.Id, pilot.State.Scanned); // the Places codex entry (#1113)

            // An explicit scan still answers with the manifest readout — as a wreck, naming its salvage.
            var result = server.ScanSpaceEntity("Salvager", wreck.Id);
            Assert.Equal("wreck", result.Kind);
            Assert.Equal(wreck.Name, result.Subject);
            Assert.Contains(result.Drops, d => d.Item == "iron_plate");
            Assert.Equal(0, result.KnowledgeGained); // the approach already banked the discovery
        }
    }

    [Fact]
    public void MiningLaser_CarvesTheWreck_AndSalvageBanksWhenItIsGone()
    {
        var server = NewServer("wreck_salvage", 11, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            server.Ship.Modules.Add("asteroid_breaker");
            server.Ship.Modules.Remove("tractor_beam"); // direct-to-inventory loot path
            var (wreck, _) = ParkNextToAWreck(server, pilot);
            server.EnterSpace("Salvager");
            var entity = server.SpaceEntitiesFor("Salvager").First(e => e.Id == wreck.Id);

            server.ShipMove("Salvager", entity.Position.X + 6f, entity.Position.Y, entity.Position.Z);
            int blocksBefore = server.StructureBlockCountForTest(wreck.Id);
            float hullBefore = entity.Hull;

            // The first hit peels plating off (fewer blocks) without destroying the hull.
            server.FireWeapon("Salvager", "asteroid_breaker", wreck.Id);
            Assert.True(entity.Hull < hullBefore, "a mining laser should damage a derelict hull");
            Assert.True(server.StructureBlockCountForTest(wreck.Id) < blocksBefore, "shooting should carve plating off the wreck");
            Assert.Contains(server.SpaceEntitiesFor("Salvager"), e => e.Id == wreck.Id);

            // Keep firing (the tick cycles the weapon's server-side cooldown, #694) until nothing is left.
            for (int i = 0; i < 80 && server.SpaceEntitiesFor("Salvager").Any(e => e.Id == wreck.Id); i++)
            {
                server.TickForTest(2.0);
                server.FireWeapon("Salvager", "asteroid_breaker", wreck.Id);
            }

            Assert.DoesNotContain(server.SpaceEntitiesFor("Salvager"), e => e.Id == wreck.Id); // entity gone
            Assert.Equal(0, server.StructureBlockCountForTest(wreck.Id));                      // hull gone
            Assert.True(pilot.State.Inventory.CountOf("iron_plate") >= 3, "a salvaged wreck pays out plating");
            Assert.Contains(wreck.Id, pilot.State.LandedBodies); // stripped it → been there
        }
    }

    [Fact]
    public void CombatCannon_MayNotBreakTheWreck_UnderMiningOnlyRules()
    {
        var server = NewServer("wreck_rules", 11, out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Salvager");
            server.Ship.Modules.Add("ship_cannon_1");
            var (wreck, _) = ParkNextToAWreck(server, pilot);
            server.EnterSpace("Salvager");
            var entity = server.SpaceEntitiesFor("Salvager").First(e => e.Id == wreck.Id);

            server.ShipMove("Salvager", entity.Position.X + 6f, entity.Position.Y, entity.Position.Z);
            float hullBefore = entity.Hull;
            server.FireWeapon("Salvager", "ship_cannon_1", wreck.Id);
            Assert.Equal(hullBefore, entity.Hull); // same gate as an asteroid: mining tools only
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort: a lingering SQLite handle on Windows can hold the directory for a moment.
        }
    }
}
