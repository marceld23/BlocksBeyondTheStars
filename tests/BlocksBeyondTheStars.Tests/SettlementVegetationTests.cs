// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1659: a natural tree is never the settlement's. The stamp clears trees out of the footprint (to tree
/// height, plus a crown-wide ring), and whatever tree block still stands inside the protected box is
/// mineable — while the settlement's own logs (greenhouse frames, stilt piles) stay protected.
/// </summary>
public sealed class SettlementVegetationTests : IDisposable
{
    private static readonly string[] TreeBlocks = { "wood_log", "tree_leaves", "pine_needles", "palm_frond" };

    private readonly string _root;
    private readonly GameContent _content;

    public SettlementVegetationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_setveg_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "setveg_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = "setveg_" + seed,
            Seed = seed,
            StartPlanet = "jungle", // the densest tree cover — the carve has the most to do here
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        }, _content, st, repo);
        server.Start();
        return server;
    }

    private HashSet<ushort> TreeIds()
        => TreeBlocks.Select(k => _content.GetBlock(k)).Where(b => b != null).Select(b => b!.NumericId.Value).ToHashSet();

    [Fact]
    public void TheStamp_ClearsTreesOutOfTheFootprint_AndTheCrownRingAroundIt()
    {
        var trees = TreeIds();
        int boxes = 0;
        for (long seed = 1; seed <= 4; seed++)
        {
            var server = Start(seed, out var repo);
            using (repo)
            {
                foreach (var box in server.SettlementBoxesForTest)
                {
                    boxes++;
                    for (int x = box.Min.X - 4; x <= box.Max.X + 4; x++)
                        for (int z = box.Min.Z - 4; z <= box.Max.Z + 4; z++)
                            for (int y = box.GroundY + 1; y <= box.GroundY + 21; y++)
                            {
                                var cell = new Vector3i(x, y, z);
                                if (trees.Contains(server.World.GetBlock(cell).Value) && !server.IsSettlementLayoutCellForTest(cell))
                                {
                                    Assert.Fail($"seed {seed}: a tree block survived the stamp at {cell} (box {box.Min}..{box.Max}, ground {box.GroundY})");
                                }
                            }
                }
            }
        }

        Assert.True(boxes > 0, "no settlement was stamped across the seeds");
    }

    [Fact]
    public void ATreeInsideTheProtectedBox_IsMineable_ButTheLayoutsOwnLogsStayProtected()
    {
        for (long seed = 1; seed <= 40; seed++)
        {
            var server = Start(seed, out var repo);
            using (repo)
            {
                var box = server.SettlementBoxesForTest.FirstOrDefault(b => !b.Ruined);
                if (box.Max.X == 0 && box.Min.X == 0)
                {
                    continue; // only ruins on this seed
                }

                var log = _content.GetBlock("wood_log")!.NumericId;
                var leaves = _content.GetBlock("tree_leaves")!.NumericId;

                // A lane cell: inside the box, no layout block there. A tree "grows" into it after the stamp.
                Vector3i? lane = null;
                for (int x = box.Min.X; x <= box.Max.X && lane is null; x++)
                    for (int z = box.Min.Z; z <= box.Max.Z && lane is null; z++)
                    {
                        var c = new Vector3i(x, box.GroundY + 2, z);
                        if (!server.IsSettlementLayoutCellForTest(c) && server.World.GetBlock(c).IsAir)
                        {
                            lane = c;
                        }
                    }

                Assert.NotNull(lane);
                var trunk = lane!.Value;
                var crown = new Vector3i(trunk.X, trunk.Y + 3, trunk.Z);
                Assert.True(server.IsSettlementBlock(trunk));
                server.World.SetBlock(trunk, log);
                server.World.SetBlock(crown, leaves);
                Assert.False(server.IsSettlementProtectedForTest(trunk));
                Assert.False(server.IsSettlementProtectedForTest(crown));

                // A stone in the same lane is still the village's business — only trees are exempt.
                server.World.SetBlock(trunk, _content.GetBlock("stone")!.NumericId);
                Assert.True(server.IsSettlementProtectedForTest(trunk));

                // The layout's own cell keeps its protection even when the block there is a log.
                Vector3i? own = null;
                for (int x = box.Min.X; x <= box.Max.X && own is null; x++)
                    for (int z = box.Min.Z; z <= box.Max.Z && own is null; z++)
                        for (int y = box.GroundY + 1; y <= box.Max.Y && own is null; y++)
                        {
                            var c = new Vector3i(x, y, z);
                            if (server.IsSettlementLayoutCellForTest(c))
                            {
                                own = c;
                            }
                        }

                Assert.NotNull(own);
                server.World.SetBlock(own!.Value, log);
                Assert.True(server.IsSettlementProtectedForTest(own.Value));
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No inhabited settlement found across 40 seeds.");
    }
}
