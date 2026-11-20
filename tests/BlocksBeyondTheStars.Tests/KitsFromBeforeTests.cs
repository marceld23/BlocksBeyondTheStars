// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// The lively places — the settlement kits from before generation 46 are not dead content: a new world still builds a
/// small share of its settlements from them (their own old houses, planned and furnished like every settlement of the
/// world), while an older world draws exactly what it always did — not one random number more.
/// </summary>
public sealed class KitsFromBeforeTests : IDisposable
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static GameContent Content => Shipped.Value;

    private static readonly string[] Tiers = { "hamlet", "village", "town", "city" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_kitsbefore_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string world, int generation, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 7,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { TerrainGeneration = generation },
        };
        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TheCatalog_KnowsWhichKitsAWorldHasOutgrown()
    {
        int lively = WorldDescription.LivelyPlacesGeneration;
        foreach (string tier in Tiers)
        {
            var before = Content.OutgrownKitsFor(StructureKit.KindSettlement, tier, null, null, lively).Select(k => k.Key).ToList();
            Assert.Equal(new[] { $"{tier}_modular_1", $"{tier}_modular_2", $"{tier}_grown_1", $"{tier}_grid_1" }, before);

            // The regular draw of a new world: the successors only. The world before: the four kits, and nothing outgrown.
            Assert.Equal(new[] { $"{tier}_grown_2", $"{tier}_grid_2" },
                Content.KitsFor(StructureKit.KindSettlement, tier, null, null, lively).Select(k => k.Key));
            Assert.Equal(before, Content.KitsFor(StructureKit.KindSettlement, tier, null, null, lively - 1).Select(k => k.Key));
            Assert.Empty(Content.OutgrownKitsFor(StructureKit.KindSettlement, tier, null, null, lively - 1));
            Assert.Empty(Content.OutgrownKitsFor(StructureKit.KindSettlement, tier, null, null, StructureCatalog.AnyGeneration));
        }

        // No station kit and no city kit is outgrown: the share is the settlements' alone.
        Assert.Empty(Content.OutgrownKitsFor(StructureKit.KindStation, null, null, null, lively));
        Assert.Empty(Content.OutgrownKitsFor(StructureKit.KindCity, null, null, null, lively));
    }

    [Fact]
    public void ANewWorld_BuildsAboutOneSettlementInTwenty_FromAKitFromBefore()
    {
        var server = Started("share", WorldDescription.LivelyPlacesGeneration, out var repo);
        using (repo)
        {
            foreach (string tier in Tiers)
            {
                const int Picks = 4000;
                int fromBefore = 0;
                var lane = new Random(4711);
                for (int i = 0; i < Picks; i++)
                {
                    var kits = server.KitsOfThisPickForTest(tier, lane);
                    bool before = kits.All(k => k.OutgrownAt(WorldDescription.LivelyPlacesGeneration));
                    Assert.True(before || kits.All(k => k.DrawnAt(WorldDescription.LivelyPlacesGeneration)), "one pick mixes the two pools");
                    Assert.Equal(before ? 4 : 2, kits.Count);
                    fromBefore += before ? 1 : 0;
                }

                double share = fromBefore / (double)Picks;
                Assert.InRange(share, SvGameServer.KitsFromBeforeShare - 0.02, SvGameServer.KitsFromBeforeShare + 0.02);
                Assert.InRange(SvGameServer.KitsFromBeforeShare, 0.03, 0.08); // rare: the maintainer's "about one in twenty"
            }

            server.Stop();
        }
    }

    [Fact]
    public void AnOlderWorld_DrawsItsKitsAsEver_AndNotOneRandomNumberMore()
    {
        var server = Started("older", WorldDescription.LivelyPlacesGeneration - 1, out var repo);
        using (repo)
        {
            foreach (string tier in Tiers)
            {
                var lane = new Random(99);
                var untouched = new Random(99);
                for (int i = 0; i < 50; i++)
                {
                    var kits = server.KitsOfThisPickForTest(tier, lane).Select(k => k.Key).ToList();
                    Assert.Equal(new[] { $"{tier}_modular_1", $"{tier}_modular_2", $"{tier}_grown_1", $"{tier}_grid_1" }, kits);
                }

                Assert.Equal(untouched.Next(), lane.Next()); // the lane was never drawn from
            }

            server.Stop();
        }
    }

    /// <summary>A planned kit from before, drawn in a new world, is planned and baked like every settlement there: its own
    /// old houses, with the lanterns, the benches and the stalls of a lively place — and every door still walkable.</summary>
    [Theory]
    [InlineData("hamlet")]
    [InlineData("village")]
    [InlineData("town")]
    [InlineData("city")]
    public void APlannedKitFromBefore_BakesAsALivelyPlace_WithItsOwnHouses(string tier)
    {
        var pool = Content.SettlementTemplates.Where(t => t.IsModule).ToList();
        var failures = new List<string>();
        foreach (string key in new[] { $"{tier}_grown_1", $"{tier}_grid_1" })
        {
            var kit = Content.KitByKey(key)!;
            Assert.True(kit.IsPlanned);
            int lamps = 0, furniture = 0;
            for (long seed = 1; seed <= 4; seed++)
            {
                var plan = SettlementPlanner.Plan(new SettlementPlanRequest
                {
                    Tier = tier,
                    Seed = seed,
                    Kit = kit,
                    Pool = pool,
                    Ground = seed % 2 == 0 ? SettlementPlannerTests.Hills : null,
                    Revision = SettlementPlan.RevisionFor(WorldDescription.LivelyPlacesGeneration),
                });
                Assert.True(plan.IsLively);
                var s = SettlementPlanBaker.Bake(plan, tier, false, seed, "grass", Content, pool);

                // Its own houses: every module of the plan is one the kit names (none of the successors' modules).
                var named = kit.Entries.Select(e => e.Module).ToHashSet(StringComparer.Ordinal);
                Assert.All(plan.Lots.Where(l => l.Module.Length > 0), l => Assert.Contains(l.Module, named));

                lamps += s.Markers.Count(m => m.Type == SettlementPlanBaker.LampMarker);
                furniture += s.Markers.Count(m => m.Type is SettlementPlanBaker.BenchMarker or SettlementPlanBaker.StallMarker
                    or SettlementPlanBaker.WellMarker or SettlementPlanBaker.FireMarker);

                var doors = s.Markers.Where(m => RoomFurnisher.IsDoorMarker(m.Type)).Select(m => m.LocalPos).ToList();
                var blocked = RoomFurnisher.BlockedDoorLanes(s.Get, s.GetShape, s.Width, s.Height, s.Length, doors);
                if (doors.Count == 0 || blocked.Count > 0)
                {
                    failures.Add($"{key} seed {seed}: {doors.Count} doors, {blocked.Count} blocked lane cell(s)");
                }
            }

            Assert.True(lamps > 0, $"{key}: no lantern in four seeds");
            Assert.True(furniture > 0, $"{key}: no bench, stall, well or fire in four seeds");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
