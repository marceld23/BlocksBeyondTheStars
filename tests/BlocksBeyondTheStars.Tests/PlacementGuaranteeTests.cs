// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Placement guarantee + terrain-adaptive seating (#586): whatever the deterministic rolls decide a world
/// gets (settlements, ruins, factories, camps, monuments, vaults, chests, cubes) must actually be placed —
/// the escalating search adapts the foundation to the terrain (slope skirt, rugged shelf, stilts over
/// water, basalt in lava) instead of silently dropping instances on dramatic worlds. Placements are pinned
/// in WorldMetadata at first stamp, so a reload replays the exact positions instead of re-running the
/// search (whose algorithm may evolve).
/// </summary>
public sealed class PlacementGuaranteeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public PlacementGuaranteeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_placement_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Start(string world, string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // Rugged/extreme landform types + water-heavy types are exactly where the old first-fit search used
    // to drop instances; seeds are arbitrary but fixed (deterministic worldgen ⇒ no flakiness).
    [Theory]
    [InlineData("rocky", 11)]
    [InlineData("highland", 23)]
    [InlineData("tablelands", 37)]
    [InlineData("karst", 41)]
    [InlineData("badlands", 53)]
    [InlineData("swamp", 67)]
    [InlineData("ocean", 71)]
    public void FreshWorld_PlacesEverythingTheRollsRequested(string planet, long seed)
    {
        var server = Start("guarantee_" + planet, planet, seed, out var repo);
        using (repo)
        {
            foreach (var (kind, requested, placed) in server.StampReportForTest)
            {
                Assert.True(placed == requested,
                    $"{planet}/{seed}: {kind} placed {placed}/{requested} — the guarantee must not drop instances on a fresh world.");
            }
        }
    }

    [Fact]
    public void FreshWorld_PinsPlacementsInMetadata()
    {
        var server = Start("pinning", "highland", 23, out var repo);
        using (repo)
        {
            int settlements = server.SettlementCount;
            var records = server.PlacementRecordsForTest;
            Assert.Equal(settlements, records.Count(r => r.Kind == "settlement" && r.Placed));

            // Every pinned settlement carries a non-legacy seat style and its display name.
            foreach (var r in records.Where(r => r.Kind == "settlement" && r.Placed))
            {
                Assert.NotEqual("legacy", r.Seat);
                Assert.False(string.IsNullOrEmpty(r.Name));
            }
        }
    }

    [Fact]
    public void Reload_ReplaysPinnedPositionsAndNames()
    {
        var first = Start("replay", "tablelands", 37, out var repoA);
        var bounds = first.SettlementsForTest.ToList();
        var names = first.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed)
            .OrderBy(r => r.Index).Select(r => r.Name).ToList();
        var vaults = first.VaultEntrances.ToList();
        repoA.Dispose();

        // Second boot on the SAME save: the replay path must reproduce positions + names from the records
        // (not by re-running the search — that is the whole point of pinning).
        var second = Start("replay", "tablelands", 37, out var repoB);
        using (repoB)
        {
            Assert.Equal(bounds.Count, second.SettlementsForTest.Count);
            for (int i = 0; i < bounds.Count; i++)
            {
                Assert.Equal(bounds[i], second.SettlementsForTest[i]);
            }

            var namesB = second.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed)
                .OrderBy(r => r.Index).Select(r => r.Name).ToList();
            Assert.Equal(names, namesB);
            Assert.Equal(vaults, second.VaultEntrances.ToList());
        }
    }

    [Fact]
    public void OceanWorld_SettlementsStandOnStiltsOrDryGround_NeverInLava()
    {
        var server = Start("stilts", "ocean", 71, out var repo);
        using (repo)
        {
            foreach (var r in server.PlacementRecordsForTest.Where(x => x.Kind == "settlement" && x.Placed))
            {
                Assert.NotEqual("lava", r.Seat); // inhabited/ruined settlements never seat IN lava...
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A repo file may still be locked on Windows — the temp dir is disposable either way.
        }
    }
}
