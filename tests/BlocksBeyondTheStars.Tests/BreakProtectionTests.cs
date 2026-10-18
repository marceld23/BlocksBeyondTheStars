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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#2784: the breaks that cannot answer with a reason — the cells around an area burst and the crystal
/// drill/laser — share one protection list with the same kinds a single mine refuses. Before, an area burst swept the
/// blocks of a factory next to the aimed one.</summary>
public sealed class BreakProtectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_breakprotect_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string save, long seed, out SqliteWorldRepository repo, string? planet = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, save));
        var config = new ServerConfig
        {
            WorldName = save,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        if (planet is not null)
        {
            config.StartPlanet = planet;
            config.IntercityRailChance = 1.0; // a line wherever one fits
        }

        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AnAreaBurst_LeavesAFactorysBlocksStanding()
    {
        var server = Start("factory", 1, out var repo);
        using (repo)
        {
            // An unclaimed factory is read-only for everyone but admins; its footprint is x 8..12 around the terminal.
            server.AddFactoryForTest(new Vector3f(10.5f, 64f, 0.5f));
            var p = server.AddLocalPlayer("Miner");
            p.State.Role = PlayerRole.Player; // the world's first joiner is its WorldAdmin, and admins edit factories
            p.State.Position = new Vector3f(7.5f, 66f, 0.5f);
            p.State.Inventory.SetSlot(6, new ItemStack("mining_beam", 1)); // radius 1
            p.State.SelectedHotbarSlot = 6;

            var stone = _content.GetBlock("stone")!.NumericId;
            var center = new Vector3i(7, 64, 0);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        server.World.SetBlock(new Vector3i(center.X + dx, center.Y + dy, center.Z + dz), stone);
                    }

            Assert.True(server.BreakProtectedForTest(new Vector3i(8, 64, 0), "Miner"));
            server.MineBlock("Miner", center.X, center.Y, center.Z);

            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    Assert.Equal(stone, server.World.GetBlock(new Vector3i(8, center.Y + dy, center.Z + dz)));
                    Assert.True(server.World.GetBlock(new Vector3i(6, center.Y + dy, center.Z + dz)).IsAir,
                        "the burst still sweeps the unprotected side");
                }
        }
    }

    [Fact]
    public void TheIntercityLine_StaysProtected_InTheSharedList()
    {
        // The intercity tests' meadow world: seed 5 carries a line; a short search covers a content change that moved it.
        SvGameServer? server = null;
        SqliteWorldRepository? repo = null;
        foreach (long seed in new long[] { 5 }.Concat(Enumerable.Range(1, 10).Select(i => (long)i).Where(i => i != 5)))
        {
            server = Start("rail" + seed, seed, out repo, planet: "meadowlands");
            if (server.IntercityRailForTest() is not null)
            {
                break;
            }

            server.Stop();
            repo.Dispose();
            server = null;
        }

        Assert.NotNull(server);
        using (repo)
        {
            var ic = server!.IntercityRailForTest()!.Value;
            var mid = ic.Pylons[ic.Pylons.Count / 2];
            Assert.True(server.BreakProtectedForTest(mid, "Stranger"), "a line pylon is protected");
            Assert.True(server.BreakProtectedForTest(mid, "Admin", isAdmin: true), "the line is protected for admins too, as for a single mine");

            var loose = new Vector3i(mid.X + 1, mid.Y + 7, mid.Z);
            server.World.SetBlock(loose, _content.GetBlock(RailRules.PylonBlockKey)!.NumericId);
            Assert.False(server.BreakProtectedForTest(loose, "Stranger"), "an ordinary pylon beside the line is not");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cleanup must never hide the actual test result.
        }
    }
}
