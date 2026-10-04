// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Wormholes (#2242): placed seed-pure over the fixed procedural systems — in pairs, at most one per system, far
/// apart, beyond the outermost orbit, never in a story system — and flown through without a jump generator, both
/// ways; every gate is checked before the pilot moves.
/// </summary>
public sealed class WormholeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WormholeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wormhole_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private List<StarSystem> Systems(long seed, int count)
        => new UniverseGenerator(seed, new WorldDescription(), _content).Generate(count).Systems;

    // ---------------- Placement (pure) ----------------

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(987654321L)]
    public void AStandardUniverse_HasExactlyOnePair_TwoWay_FarApart_BeyondEveryOrbit(long seed)
    {
        var systems = Systems(seed, 12);
        var ends = WormholePlacer.Place(systems, seed, Frequency.Rare, 12, _content.Wormholes);

        Assert.Equal(2, ends.Count);
        var (a, b) = (ends[0], ends[1]);
        Assert.Equal(b.Id, a.LinkedId);
        Assert.Equal(a.Id, b.LinkedId);
        Assert.NotEqual(a.SystemId, b.SystemId);

        var sa = systems.Single(s => s.Id == a.SystemId);
        var sb = systems.Single(s => s.Id == b.SystemId);
        float mapDist = MathF.Sqrt((sa.MapX - sb.MapX) * (sa.MapX - sb.MapX) + (sa.MapY - sb.MapY) * (sa.MapY - sb.MapY));
        Assert.True(mapDist >= _content.Wormholes.MinMapDistance || systems.All(s => s == sa || MathF.Sqrt((sa.MapX - s.MapX) * (sa.MapX - s.MapX) + (sa.MapY - s.MapY) * (sa.MapY - s.MapY)) <= mapDist));

        foreach (var end in ends)
        {
            var sys = systems.Single(s => s.Id == end.SystemId);
            float r = MathF.Sqrt(end.SystemX * end.SystemX + end.SystemZ * end.SystemZ);
            Assert.All(sys.Bodies, body => Assert.True(r > MathF.Sqrt(body.SystemX * body.SystemX + body.SystemZ * body.SystemZ)));
        }
    }

    [Fact]
    public void ThePlacement_IsSeedStable_AndOneSystemNeverHoldsTwoEnds()
    {
        var systems = Systems(7, 30);
        var first = WormholePlacer.Place(systems, 7, Frequency.Frequent, 30, _content.Wormholes);
        var again = WormholePlacer.Place(systems, 7, Frequency.Frequent, 30, _content.Wormholes);

        Assert.Equal(first.Select(w => (w.Id, w.LinkedId, w.SystemX, w.SystemZ)), again.Select(w => (w.Id, w.LinkedId, w.SystemX, w.SystemZ)));
        Assert.True(first.Count >= 4);
        Assert.Equal(first.Count, first.Select(w => w.SystemId).Distinct().Count());
    }

    [Fact]
    public void GrowingTheGalaxy_NeverMovesAWormhole_AndOffMeansNone()
    {
        var fixedOnly = WormholePlacer.Place(Systems(5, 12), 5, Frequency.Normal, 12, _content.Wormholes);
        var grown = WormholePlacer.Place(Systems(5, 16), 5, Frequency.Normal, 12, _content.Wormholes); // 4 grown systems

        Assert.Equal(fixedOnly.Select(w => (w.Id, w.LinkedId)), grown.Select(w => (w.Id, w.LinkedId)));
        Assert.All(grown, w => Assert.True(int.Parse(w.SystemId.Substring(3), System.Globalization.CultureInfo.InvariantCulture) < 12));

        Assert.Empty(WormholePlacer.Place(Systems(5, 12), 5, Frequency.Off, 12, _content.Wormholes));
    }

    [Fact]
    public void NoWormhole_EverOpensInAStorySystem()
    {
        var systems = Systems(3, 12);
        systems.Add(new StarSystem { Id = "guardian_finale", Name = "Guardian Core", MapX = 1600, MapY = 0, Bodies = { new CelestialBody { Id = "guardian_finale-core", SystemId = "guardian_finale", Kind = CelestialKind.Planet, PlanetType = "rocky" } } });

        var ends = WormholePlacer.Place(systems, 3, Frequency.Frequent, 12, _content.Wormholes);

        Assert.DoesNotContain(ends, w => w.SystemId == "guardian_finale");
        Assert.False(WormholePlacer.MayHoldWormhole("guardian_finale"));
        Assert.True(SvGameServer.IsStoryLockedSystem("guardian_finale"));
        Assert.False(SvGameServer.IsStoryLockedSystem("sys4"));
    }

    // ---------------- Flying through (server) ----------------

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 21, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Puts the pilot in flight in a system that holds a wormhole end (via a borrowed jump generator, which is
    /// taken away again) and returns that end and its flight entity.</summary>
    private static (Wormhole End, CombatEntity Rift) InFlightBesideAWormhole(SvGameServer server, PlayerSession pilot)
    {
        var end = server.Galaxy.Wormholes.First();
        var here = server.Galaxy.FindBody(server.ActiveLocationId)!;
        if (here.SystemId != end.SystemId)
        {
            server.Ship.Modules.Add("jump_generator");
            server.HyperjumpToSystem(pilot.State.PlayerId, end.SystemId);
            server.Ship.Modules.Remove("jump_generator");
        }
        else
        {
            server.EnterSpace(pilot.State.PlayerId);
        }

        Assert.True(server.InSpace(pilot.State.PlayerId));
        var rift = server.SpaceEntitiesFor(pilot.State.PlayerId).Single(e => e.Kind == CombatEntityKind.Wormhole);
        Assert.Equal(end.Id, rift.Id);
        return (end, rift);
    }

    [Fact]
    public void AStandardWorld_HasOnePair_AndItsEnd_IsAnEntityInThatSystemsFlight()
    {
        var server = NewServer("placed", out var repo);
        using (repo)
        {
            Assert.Equal(2, server.Galaxy.Wormholes.Count);
            var pilot = server.AddLocalPlayer("Pilot");
            var (_, rift) = InFlightBesideAWormhole(server, pilot);
            Assert.False(rift.Hostile);
        }
    }

    [Fact]
    public void FlyingThrough_NeedsNoJumpGenerator_AndWorksBothWays()
    {
        var server = NewServer("transit", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var (end, rift) = InFlightBesideAWormhole(server, pilot);
            Assert.False(server.Ship.HasModule("jump_generator"));
            var twin = server.Galaxy.FindWormhole(end.LinkedId)!;

            server.ShipMove("Pilot", rift.Position.X, rift.Position.Y, rift.Position.Z - 10f);
            server.TraverseWormhole("Pilot", end.Id);

            Assert.True(server.InSpace("Pilot"));
            Assert.Equal(twin.SystemId, server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId);
            Assert.Contains(twin.SystemId, pilot.State.KnownSystems);
            Assert.Contains("wormhole:" + end.Id, pilot.State.Scanned);
            Assert.Contains("wormhole:" + twin.Id, pilot.State.Scanned);
            var back = server.SpaceEntitiesFor("Pilot").Single(e => e.Kind == CombatEntityKind.Wormhole);
            Assert.Equal(twin.Id, back.Id);

            // Straight back is refused for a moment (no bouncing) — then the same rift carries the pilot home.
            server.ShipMove("Pilot", back.Position.X, back.Position.Y, back.Position.Z - 10f);
            server.TraverseWormhole("Pilot", twin.Id);
            Assert.Equal(twin.SystemId, server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId);

            server.ResetWormholeLockForTest("Pilot");
            server.TraverseWormhole("Pilot", twin.Id);
            Assert.Equal(end.SystemId, server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId);
        }
    }

    [Fact]
    public void FlyingThrough_IsRefused_FromAfar_OnAnEva_AndForAnUnknownRift()
    {
        var server = NewServer("refused", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var (end, rift) = InFlightBesideAWormhole(server, pilot);
            string before = pilot.CurrentLocationId;

            server.ShipMove("Pilot", rift.Position.X + 300f, rift.Position.Y, rift.Position.Z);
            server.TraverseWormhole("Pilot", end.Id);
            Assert.Equal(before, pilot.CurrentLocationId);

            server.ShipMove("Pilot", rift.Position.X, rift.Position.Y, rift.Position.Z - 10f);
            server.TraverseWormhole("Pilot", "sys999-wh");
            Assert.Equal(before, pilot.CurrentLocationId);

            pilot.State.InEva = true;
            server.TraverseWormhole("Pilot", end.Id);
            Assert.Equal(before, pilot.CurrentLocationId);
        }
    }

    [Fact]
    public void AWormholeIntoTheStorySystem_IsRefused_EvenWhenTheGalaxySaysSo()
    {
        var server = NewServer("story", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var (end, rift) = InFlightBesideAWormhole(server, pilot);
            string before = pilot.CurrentLocationId;

            // An edited save: the twin moved into the finale system.
            var twin = server.Galaxy.FindWormhole(end.LinkedId)!;
            server.Galaxy.Systems.Add(new StarSystem
            {
                Id = "guardian_finale",
                Name = "Guardian Core",
                Bodies = { new CelestialBody { Id = "guardian_finale-core", SystemId = "guardian_finale", Kind = CelestialKind.Planet, PlanetType = "rocky" } },
            });
            twin.SystemId = "guardian_finale";

            server.ShipMove("Pilot", rift.Position.X, rift.Position.Y, rift.Position.Z - 10f);
            server.TraverseWormhole("Pilot", end.Id);

            Assert.Equal(before, pilot.CurrentLocationId);
            Assert.NotEqual("guardian_finale", server.Galaxy.FindBody(pilot.CurrentLocationId)!.SystemId);
        }
    }

    [Fact]
    public void TheScanner_ReadsWhereAWormholeLeads()
    {
        var server = NewServer("scan", out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            var (end, rift) = InFlightBesideAWormhole(server, pilot);
            var twin = server.Galaxy.FindWormhole(end.LinkedId)!;
            var twinSystem = server.Galaxy.Systems.Single(s => s.Id == twin.SystemId);

            server.ShipMove("Pilot", rift.Position.X, rift.Position.Y, rift.Position.Z - 60f);
            var result = server.ScanSpaceEntity("Pilot", end.Id);

            Assert.Equal("wormhole", result.Kind);
            Assert.Equal(twinSystem.Name, result.Subject);
            Assert.True(result.KnowledgeGained > 0);
            Assert.Contains("wormhole:" + twin.Id, pilot.State.Scanned);
            Assert.Contains(end.Id, server.ScannedIdsForTest("Pilot"));
        }
    }
}
