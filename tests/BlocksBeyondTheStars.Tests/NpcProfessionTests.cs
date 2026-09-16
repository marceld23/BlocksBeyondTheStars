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
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// NPC professions (Justus' ideas, 2026-09-15): doctor, grocer, arms dealer, sage, animal tamer, blockfarmer, streamer and
/// reporter — the table, their buildings in fresh settlements, and their posts at a base or on a station.
/// </summary>
public sealed class NpcProfessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_professions_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void TheTable_IsUnique_AndEveryProfessionHasItsRoomItsBuildingAndItsKeys()
    {
        Assert.Equal(8, NpcProfessions.All.Count);
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.Job).Distinct().Count());
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.PostBlock).Distinct().Count());
        Assert.Equal(NpcProfessions.All.Count, NpcProfessions.All.Select(p => p.Function).Distinct().Count());

        foreach (var p in NpcProfessions.All)
        {
            Assert.Contains(p.Role, new[] { "vendor", "settler" });
            Assert.Equal(p.Trades, p.Theme.Length > 0);
            Assert.True(Enum.TryParse<RoomFurnisher.RoomRole>(p.Room, out _), $"{p.Job}: unknown room '{p.Room}'");
            Assert.True(StructureRoles.IsPlotRole(p.Function), $"{p.Job}: '{p.Function}' is not a plot role");
            Assert.Same(p, NpcProfessions.ByMarker(p.Marker));
            Assert.Same(p, NpcProfessions.ByPostBlock(p.PostBlock));
            Assert.Same(p, NpcProfessions.ByFunction(p.Function));
            Assert.Equal(p.Trades, NpcProfessions.IsTradeMarker(p.Marker));
            Assert.True(NpcProfessions.IsStaffedPostMarker(p.Marker));
        }

        // The classic posts keep their meaning; a settler's spot is no post.
        Assert.True(NpcProfessions.IsTradeMarker("vendor"));
        Assert.False(NpcProfessions.IsTradeMarker("mission_board"));
        Assert.True(NpcProfessions.IsStaffedPostMarker("mission_board"));
        Assert.False(NpcProfessions.IsStaffedPostMarker("npc"));
    }

    [Fact]
    public void EveryProfessionBuilding_IsInEveryModularKit_OptionalAndAtMostOnce()
    {
        foreach (var kit in Content.StructureKits.Where(k => k.Key.Contains("_modular_", StringComparison.Ordinal) && k.Kind == StructureKit.KindSettlement))
        {
            string style = kit.Tier is "hamlet" or "village" ? "village" : "town";
            foreach (var p in NpcProfessions.All)
            {
                foreach (string key in new[] { $"{style}_{p.Function}_1", $"{style}_{p.Function}_1_alien" })
                {
                    var entry = Assert.Single(kit.Entries, e => e.Module == key);
                    Assert.False(entry.Required, $"{kit.Key}: {key} must stay optional");
                    Assert.Equal(1, entry.Max);
                }
            }
        }
    }

    [Fact]
    public void AFreshSettlement_WithAProfessionBuilding_StaffsItsPost_WithTheProfessionsNameThemeAndTool()
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "prof_" + seed));
            using (repo)
            {
                var config = new ServerConfig
                {
                    WorldName = "prof_" + seed,
                    Seed = seed,
                    StartPlanet = "jungle",
                    AutoSaveIntervalMinutes = 9999,
                    PlaceStarterShip = false,
                    PlaceSettlements = true,
                    PlaceWrecks = false,
                };
                var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();

                var posts = server.SettlementMarkers.Where(m => NpcProfessions.ByMarker(m.Type) != null).ToList();
                if (posts.Count == 0)
                {
                    continue;
                }

                // One keeper per post (a job may stand in several settlements of the world).
                foreach (var group in posts.GroupBy(m => m.Type))
                {
                    var p = NpcProfessions.ByMarker(group.Key)!;
                    var keepers = server.NpcJobsForTest.Where(n => n.Job == p.Job).ToList();
                    Assert.Equal(group.Count(), keepers.Count);
                    foreach (var npc in keepers)
                    {
                        Assert.Equal(p.Role, npc.Role);
                        Assert.Equal(p.NameKey, npc.NameKey);
                        Assert.Equal(p.Held, npc.Held);
                        if (p.Trades)
                        {
                            Assert.Equal(p.Theme, npc.Theme);
                        }
                    }
                }

                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No fresh settlement with a profession building across 80 seeds.");
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
