// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1887 (Marcel 2026-09-14): every settlement resident has a bed — the beds are the residents, capped per size;
/// the posts are staffed by them; a bedless settlement keeps its services; the G.D.S. guardians stay extra.</summary>
public sealed class SettlementResidentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_residents_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(long seed, string planet, out SqliteWorldRepository repo, Frequency templates = Frequency.Normal)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + "_" + seed + "_" + templates));
        var config = new ServerConfig
        {
            WorldName = "residents_" + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        };
        config.World.SettlementTemplateUse = templates;
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Residents_AreTheBeds_UpToTheCap_EachWithTheirOwnBed_AndThePostsStaffed()
    {
        int checkedSettlements = 0;
        for (long seed = 1; seed <= 40 && checkedSettlements < 3; seed++)
        {
            var server = Start(seed, "jungle", out var repo);
            using (repo)
            {
                foreach (var (name, tier, beds, all) in server.SettlementResidentsForTest)
                {
                    // 2026-09: a profession post brings one extra resident on top of the bed-bound people below.
                    var residents = all.Where(r => NpcProfessions.ByJob(r.Job) == null).ToList();
                    int cap = SvGameServer.ResidentCapForTest(tier);
                    int services = residents.Count(r => r.Job is "vendor" or "quartermaster");
                    Assert.Equal(Math.Max(Math.Min(beds, cap), services), residents.Count);
                    Assert.True(residents.Count <= Math.Max(cap, services), $"{name}: {residents.Count} residents over the cap {cap}");

                    // Everyone who has a bed has their own one; as many residents sleep in a bed as there are beds (≤ cap).
                    var assigned = all.Where(r => r.Bed.HasValue).Select(r => r.Bed!.Value).ToList();
                    Assert.Equal(assigned.Count, assigned.Distinct().Count());
                    Assert.Equal(Math.Min(beds, residents.Count), residents.Count(r => r.Bed.HasValue));

                    checkedSettlements++;
                }

                // The services a settlement's markers name are staffed.
                if (server.SettlementMarkers.Any(m => m.Type == "vendor"))
                {
                    Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
                }

                if (server.SettlementMarkers.Any(m => m.Type == "mission_board"))
                {
                    Assert.Contains(server.NpcSnapshots, n => n.Role == "quartermaster");
                }
            }
        }

        Assert.True(checkedSettlements > 0, "no inhabited settlement found");
    }

    [Fact]
    public void AModularVillage_HousesOneResidentPerBed_WithAnInnkeeperAndTheirTavernSeats()
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Start(seed, "jungle", out var repo);
            using (repo)
            {
                foreach (var (name, tier, beds, all) in server.SettlementResidentsForTest)
                {
                    var residents = all.Where(r => NpcProfessions.ByJob(r.Job) == null).ToList(); // professions are extra (2026-09)
                    if (!residents.Any(r => r.Job == "innkeeper"))
                    {
                        continue;
                    }

                    Assert.Equal(Math.Min(beds, SvGameServer.ResidentCapForTest(tier)), residents.Count);
                    Assert.All(residents, r => Assert.True(r.Bed.HasValue, $"{name}: a resident without a bed"));
                    Assert.Contains(residents, r => r.Role == "vendor");
                    Assert.Contains(residents, r => r.Role == "quartermaster");
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No modular settlement with a tavern found across 60 seeds.");
    }

    [Fact]
    public void ABedlessSettlement_KeepsItsVendorAndQuartermaster()
    {
        // Settlement templates off = the procedural generator, whose two-building hamlets have no bed at all.
        // Every seed tried is a server start = a world bake (~16 s on the build machine). Seed 8 is the first
        // that has such a hamlet, and walking up to it cost eight starts — 125 s and 129 s on two pull requests
        // in a row, over the 120 s budget with every test green. So the known seed goes first; the search stays
        // behind it, and a new worldgen generation only makes this test slower again, never wrong.
        const long KnownBedlessSeed = 8;
        var seeds = new[] { KnownBedlessSeed }.Concat(Enumerable.Range(1, 80).Select(s => (long)s).Where(s => s != KnownBedlessSeed));
        foreach (long seed in seeds)
        {
            var server = Start(seed, "jungle", out var repo, Frequency.Off);
            using (repo)
            {
                foreach (var (name, _, beds, residents) in server.SettlementResidentsForTest)
                {
                    if (beds != 0)
                    {
                        continue;
                    }

                    Assert.NotEmpty(residents);
                    Assert.All(residents, r => Assert.Contains(r.Job, new[] { "vendor", "quartermaster" }));
                    Assert.All(residents, r => Assert.Null(r.Bed));
                    return;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("No bedless settlement found across 80 seeds.");
    }

    [Fact]
    public void GdsCity_HousesAtMostEightyResidents_PlusItsGuardians()
    {
        var server = Start(20260912, "gds_desert", out var repo);
        using (repo)
        {
            var (_, tier, beds, residents) = Assert.Single(server.SettlementResidentsForTest);
            Assert.Equal("metropolis", tier);
            Assert.True(beds > 80, $"the G.D.S. city has {beds} beds");
            // The cap counts the bed-bound residents; the keepers of the services districts (2026-09) come on top.
            Assert.Equal(80, residents.Count(r => NpcProfessions.ByJob(r.Job) is null));
            Assert.Equal(6, residents.Count(r => NpcProfessions.ByJob(r.Job) is not null));
            Assert.Contains(server.NpcLooksForTest, n => n.Role == "guardian");
            Assert.True(server.NpcCount < 200, $"{server.NpcCount} NPCs on the city world (was 324 before #1887)");
        }
    }

    [Fact]
    public void TheCaps_AreMarcelsNumbers()
    {
        Assert.Equal(6, SvGameServer.ResidentCapForTest("hamlet"));
        Assert.Equal(10, SvGameServer.ResidentCapForTest("village"));
        Assert.Equal(20, SvGameServer.ResidentCapForTest("town"));
        Assert.Equal(32, SvGameServer.ResidentCapForTest("city"));
        Assert.Equal(80, SvGameServer.ResidentCapForTest("metropolis"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a locked temp file must not fail the run
        }
    }
}
