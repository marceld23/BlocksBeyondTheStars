// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ship scanner (#2237–#2240): every ship scans with its cockpit, the Deep and Quantum scanners raise the tier;
/// the server checks the range and answers every space object kind; anomalies pay for every new one; the planet
/// overview grows with the tier and never loads a world; the Quantum scanner replaces the Deep scanner.
/// </summary>
public sealed class ShipScannerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_scanner_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer NewServer(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 11, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A pilot in flight, parked at the instance origin.</summary>
    private static PlayerSession Pilot(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Pilot");
        server.EnterSpace("Pilot");
        Assert.True(server.InSpace("Pilot"));
        server.ShipMove("Pilot", 0f, 0f, 0f);
        return p;
    }

    private static CombatEntity Entity(string id, CombatEntityKind kind, float z, string name = "", bool hostile = false)
        => new() { Id = id, Kind = kind, Name = name, Hostile = hostile, Hull = 10f, HullMax = 10f, Position = new Vector3f(0f, 0f, z) };

    [Fact]
    public void EveryShip_ScansWithItsCockpit_AndTheScannerModulesRaiseTheTier()
    {
        var server = NewServer("tiers", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            var scanner = server.ShipScannerForTest("Pilot");
            Assert.Equal(1, scanner.Tier);
            Assert.Equal(150f, scanner.Range);
            Assert.Equal("cockpit", scanner.ModuleKey);

            server.Ship.Modules.Add("planet_scanner");
            scanner = server.ShipScannerForTest("Pilot");
            Assert.Equal(2, scanner.Tier);
            Assert.Equal(300f, scanner.Range);

            server.Ship.Modules.Add("quantum_scanner");
            scanner = server.ShipScannerForTest("Pilot");
            Assert.Equal(3, scanner.Tier);
            Assert.Equal(500f, scanner.Range);
            Assert.Equal(0.5f, scanner.ScanTime);
        }
    }

    [Fact]
    public void TheScanner_ReachesAsFarAsItsTier()
    {
        var server = NewServer("range", out var repo);
        using (repo)
        {
            Pilot(server);
            server.AddSpaceEntityForTest("Pilot", Entity("anomaly:far", CombatEntityKind.Anomaly, 400f));

            var tooFar = server.ScanSpaceEntity("Pilot", "anomaly:far");
            Assert.Equal("ui.scan.out_of_range", tooFar.InfoKey);
            Assert.False(tooFar.FirstTime);

            server.Ship.Modules.Add("quantum_scanner"); // 500 units
            var read = server.ScanSpaceEntity("Pilot", "anomaly:far");
            Assert.Equal("anomaly", read.Kind);
            Assert.True(read.FirstTime);
        }
    }

    [Fact]
    public void EveryNewAnomaly_PaysKnowledge_TheSameOneOnlyOnce_AndIsRememberedAsScanned()
    {
        var server = NewServer("anomalies", out var repo);
        using (repo)
        {
            Pilot(server);
            server.AddSpaceEntityForTest("Pilot", Entity("anomaly:a", CombatEntityKind.Anomaly, 50f, "???"));
            server.AddSpaceEntityForTest("Pilot", Entity("anomaly:b", CombatEntityKind.Anomaly, -50f, "???"));

            var first = server.ScanSpaceEntity("Pilot", "anomaly:a");
            Assert.True(first.FirstTime);
            Assert.True(first.KnowledgeGained > 0);

            server.ResetShipScanCooldownForTest();
            var again = server.ScanSpaceEntity("Pilot", "anomaly:a");
            Assert.False(again.FirstTime);
            Assert.Equal(0, again.KnowledgeGained);

            // #2238: it used to be once per player overall — a second anomaly paid nothing.
            var second = server.ScanSpaceEntity("Pilot", "anomaly:b");
            Assert.True(second.FirstTime);
            Assert.True(second.KnowledgeGained > 0);

            var scanned = server.ScannedIdsForTest("Pilot");
            Assert.Contains("anomaly:a", scanned);
            Assert.Contains("anomaly:b", scanned);
        }
    }

    [Fact]
    public void Pods_Stations_Machines_AndRaiders_AnswerTheScanner()
    {
        var server = NewServer("objects", out var repo);
        using (repo)
        {
            Pilot(server);
            server.AddSpaceEntityForTest("Pilot", Entity("pod:t", CombatEntityKind.EscapePod, 30f, "Mira Kessel"));
            server.AddSpaceEntityForTest("Pilot", Entity("station:t", CombatEntityKind.SpaceStation, -30f, "Rim Exchange"));
            server.AddSpaceEntityForTest("Pilot", Entity("drone:t", CombatEntityKind.Drone, 40f, hostile: true));
            server.AddSpaceEntityForTest("Pilot", Entity("bandit:t", CombatEntityKind.BanditShip, -40f, "Rust Fang"));

            var pod = server.ScanSpaceEntity("Pilot", "pod:t");
            Assert.Equal("pod", pod.Kind);
            Assert.Equal("Mira Kessel", pod.Subject);
            Assert.Equal("ui.scan.pod", pod.InfoKey);
            Assert.True(pod.KnowledgeGained > 0);

            var station = server.ScanSpaceEntity("Pilot", "station:t");
            Assert.Equal("station", station.Kind);
            Assert.Contains("ui.scan.trait.station_trade", station.TraitKeys);

            var drone = server.ScanSpaceEntity("Pilot", "drone:t");
            Assert.Equal("machine", drone.Kind);
            Assert.Equal("drone", drone.SubjectKey);
            Assert.Equal("ui.scan.threat.hostile", drone.ThreatKey);

            var bandit = server.ScanSpaceEntity("Pilot", "bandit:t");
            Assert.Equal("bandit", bandit.Kind);
            Assert.Equal("ui.scan.threat.provokable", bandit.ThreatKey); // still hailing — not yet firing
        }
    }

    [Fact]
    public void AResourceDrop_IsNoScanTarget_AndOneTargetCannotBeHammered()
    {
        var server = NewServer("guards", out var repo);
        using (repo)
        {
            Pilot(server);
            server.AddSpaceEntityForTest("Pilot", Entity("drop:t", CombatEntityKind.ResourceDrop, 10f));
            server.AddSpaceEntityForTest("Pilot", Entity("anomaly:t", CombatEntityKind.Anomaly, 20f));

            Assert.Equal("ui.scan.not_scannable", server.ScanSpaceEntity("Pilot", "drop:t").InfoKey);

            Assert.Equal("anomaly", server.ScanSpaceEntity("Pilot", "anomaly:t").Kind);
            Assert.Equal("ui.scan.recharging", server.ScanSpaceEntity("Pilot", "anomaly:t").InfoKey);
        }
    }

    [Fact]
    public void ThePlanetOverview_GrowsWithTheTier_AndPaysKnowledgeOnce()
    {
        var server = NewServer("overview", out var repo);
        using (repo)
        {
            Pilot(server);

            var basic = server.PlanetScanForTest("Pilot", string.Empty, out string reason);
            Assert.NotNull(basic);
            Assert.Equal(string.Empty, reason);
            Assert.Equal(1, basic!.Tier);
            Assert.True(basic.ResourcesLocked);
            Assert.Empty(basic.Ores);
            Assert.True(basic.KnowledgeGained > 0);
            foreach (string topic in new[] { "air", "temperature", "gravity", "plants", "animals", "machines", "terrain", "structures", "frontier" })
            {
                Assert.Contains(basic.Rows, r => r.Topic == topic && r.ValueKey.Length > 0);
            }

            Assert.InRange(basic.Danger, (byte)0, (byte)2);

            server.Ship.Modules.Add("planet_scanner");
            var deep = server.PlanetScanForTest("Pilot", string.Empty, out _)!;
            Assert.Equal(2, deep.Tier);
            Assert.False(deep.ResourcesLocked);
            Assert.NotEmpty(deep.Ores);
            Assert.DoesNotContain(deep.Ores, o => o.RareTier); // the rare veins need the Quantum scanner
            Assert.Equal(0, deep.KnowledgeGained);             // the same body pays once
            Assert.Contains(deep.Rows, r => r.Topic == "gravity" && r.Extra.EndsWith(" g", StringComparison.Ordinal));

            server.Ship.Modules.Add("quantum_scanner");
            var quantum = server.PlanetScanForTest("Pilot", string.Empty, out _)!;
            Assert.Equal(3, quantum.Tier);
            Assert.True(quantum.Ores.Length >= deep.Ores.Length);
        }
    }

    [Fact]
    public void ThePlanetOverview_OfAnotherBody_LoadsNoWorld()
    {
        var server = NewServer("overview_remote", out var repo);
        using (repo)
        {
            Pilot(server);
            var here = server.Galaxy.FindBody(server.ActiveLocationId)!;
            var other = server.Galaxy.AllBodies().First(b => b.SystemId == here.SystemId && b.Id != here.Id
                && !string.IsNullOrEmpty(b.PlanetType) && b.Kind is CelestialKind.Planet or CelestialKind.Moon);
            int resident = server.ResidentWorldCount;

            var report = server.PlanetScanForTest("Pilot", other.Id, out string reason);

            Assert.NotNull(report);
            Assert.Equal(other.Id, report!.BodyId);
            Assert.NotEmpty(report.Rows);
            Assert.Equal(resident, server.ResidentWorldCount);
        }
    }

    [Fact]
    public void TheQuantumScanner_ReplacesTheDeepScanner_AndTheDeepScannerCannotBeBuiltBesideIt()
    {
        var server = NewServer("replace", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            p.State.InstantBuild = true;
            p.State.AboardShip = true;
            if (!server.Ship.HasModule("workshop"))
            {
                server.Ship.Modules.Add("workshop");
            }

            p.State.UnlockedBlueprints.Add("planet_scanner");
            p.State.UnlockedBlueprints.Add("quantum_scanner");

            Assert.True(server.BuildModuleForTest("Pilot", "planet_scanner"));
            Assert.True(server.BuildModuleForTest("Pilot", "quantum_scanner"));
            Assert.False(server.Ship.HasModule("planet_scanner")); // the Deep scanner came out of the rack

            Assert.False(server.BuildModuleForTest("Pilot", "planet_scanner")); // superseded
            Assert.Equal(3, server.ShipScannerForTest("Pilot").Tier);
        }
    }

    [Fact]
    public void TheScannerContent_IsWired()
    {
        _content.Validate();
        Assert.Contains("scanner_strength", _content.GetShipModule("cockpit")!.Stats.Keys);
        Assert.Equal(new[] { "planet_scanner" }, _content.GetShipModule("quantum_scanner")!.Replaces);
        Assert.Equal(new[] { "ai_core_mk2" }, _content.GetShipModule("ai_core_mk3")!.Replaces);
        Assert.Contains(_content.GetShipModule("planet_scanner")!.BuildCost, c => c.Item == "sensor_lens");
        Assert.Contains(_content.GetShipModule("quantum_scanner")!.BuildCost, c => c.Item == "quantum_sensor");
        Assert.NotNull(_content.GetItem("sensor_lens"));
        Assert.NotNull(_content.GetItem("quantum_sensor"));
        Assert.Contains(_content.Recipes.Values, r => r.Key == "sensor_lens" && r.RequiredBlueprint == "planet_scanner");
        Assert.Contains(_content.Recipes.Values, r => r.Key == "quantum_sensor" && r.RequiredBlueprint == "quantum_scanner");
        var bp = _content.GetBlueprint("quantum_scanner")!;
        Assert.Contains("planet_scanner", bp.Prerequisites);
        Assert.Contains("radar_array", bp.Prerequisites);
    }
}
