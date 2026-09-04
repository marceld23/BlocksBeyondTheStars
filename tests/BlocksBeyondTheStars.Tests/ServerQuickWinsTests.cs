// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>PR bundle 2 of the performance package (#1501): server-side quick wins that change no behaviour —
/// one transaction per simulation step (#1505), prepared SQLite statements (#1506), the settled-view skip in
/// chunk streaming (#1507), the 1 Hz flora step (#1508), the custom-ship stats memo (#1509) and the codec
/// formatter warm-up (#1510).</summary>
public sealed class ServerQuickWinsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_quickwins_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Start(string tag, out SqliteWorldRepository repo, int viewDistance = 1)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            ViewDistanceChunks = viewDistance,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void FluidStep_WritesAllItsCells_InOneTransaction()
    {
        var server = Start("fluid_tx", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Plumber");
            int before = repo.TransactionsBegun;

            // A water source high in the air column: it falls and spreads, so every fluid step touches many
            // cells (each a SetBlock + fluid-cell row). Per cell that used to be one autocommit each.
            server.PlaceFluidSource("water", 0, 130, 0);
            for (int i = 0; i < 8; i++)
            {
                server.TickForTest(0.3); // 2.4 s ≈ 9 fluid steps at the 0.25 s cadence
            }

            int steps = repo.TransactionsBegun - before;
            Assert.True(steps >= 2, $"the fluid steps should have run inside transactions (got {steps})");
            Assert.True(steps <= 12, $"one transaction per STEP, not per cell (got {steps} for ~9 steps)");
        }
    }

    [Fact]
    public void PreparedStatements_RebindValuesPerCall()
    {
        // The cached commands are reused across calls with different values, planets and chunks — a stale
        // binding would show up as the wrong block, the wrong planet or a missing fluid/fire row.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "prepared"));
        repo.Initialize();

        repo.SetBlock("planet:a", new Vector3i(3, 40, 5), 7);
        repo.SetBlock("planet:a", new Vector3i(3, 40, 5), 9);          // same cell, new block → upsert
        repo.SetBlock("planet:b", new Vector3i(3, 40, 5), 11, tint: 0x123456);
        repo.SetBlock("planet:a", new Vector3i(100, 40, 5), 13);       // another chunk

        var chunkA = WorldConstants.WorldToChunk(new Vector3i(3, 40, 5));
        var editsA = repo.LoadChunkEdits("planet:a", chunkA);
        Assert.Single(editsA);
        Assert.Equal(9, editsA[0].Block);
        var editsB = repo.LoadChunkEdits("planet:b", chunkA);
        Assert.Single(editsB);
        Assert.Equal(11, editsB[0].Block);
        Assert.Equal(0x123456, editsB[0].Tint);
        Assert.Single(repo.LoadChunkEdits("planet:a", WorldConstants.WorldToChunk(new Vector3i(100, 40, 5))));

        repo.SaveFluidCell("planet:a", new Vector3i(1, 2, 3), 5, falling: true);
        repo.SaveFluidCell("planet:a", new Vector3i(1, 2, 3), 6, falling: false); // upsert
        repo.SaveFluidCell("planet:a", new Vector3i(4, 5, 6), 2, falling: false);
        var fluids = repo.ListFluidCells("planet:a");
        Assert.Equal(2, fluids.Count);
        Assert.Contains(fluids, f => f.WorldPosition == new Vector3i(1, 2, 3) && f.Level == 6 && !f.Falling);
        repo.DeleteFluidCell("planet:a", new Vector3i(1, 2, 3));
        Assert.Single(repo.ListFluidCells("planet:a"));

        repo.SaveFireCell("planet:a", new Vector3i(7, 8, 9), 12.5, generation: 2);
        repo.SaveFireCell("planet:a", new Vector3i(7, 8, 9), 3.25, generation: 3);
        var fires = repo.ListFireCells("planet:a");
        Assert.Single(fires);
        Assert.Equal(3.25, fires[0].Remaining, 6);
        Assert.Equal(3, fires[0].Generation);
        repo.DeleteFireCell("planet:a", new Vector3i(7, 8, 9));
        Assert.Empty(repo.ListFireCells("planet:a"));
    }

    [Fact]
    public void StreamChunks_SkipsTheEnumeration_WhileAPlayersViewIsSettled()
    {
        var server = Start("settled", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Camper");
            // Stream the (small) view until nothing arrives for a while.
            int lastSent = -1;
            for (int i = 0; i < 60 && p.SentChunks.Count != lastSent; i++)
            {
                lastSent = p.SentChunks.Count;
                server.TickForTest(0.1);
            }

            server.TickForTest(0.1); // the pass that records "settled"
            int enumerations = server.StreamEnumerationsForTest;
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.True(server.StreamEnumerationsForTest - enumerations <= 1,
                $"a settled, stationary player must not re-enumerate the view every tick (got {server.StreamEnumerationsForTest - enumerations} in 10 ticks)");

            // Moving to another chunk column invalidates the settled marker at once.
            var pos = p.State.Position;
            p.State.Position = new Vector3f(pos.X + 32f, pos.Y, pos.Z);
            int beforeMove = server.StreamEnumerationsForTest;
            server.TickForTest(0.1);
            Assert.True(server.StreamEnumerationsForTest > beforeMove, "a moved player re-enumerates on the next pass");
            Assert.True(p.SentChunks.Count > lastSent, "and the new column streams");
        }
    }

    [Fact]
    public void CustomShipStats_AreParsedOnce_PerBuiltCellsBlob()
    {
        var server = Start("custom_memo", out var repo);
        using (repo)
        {
            var a = server.AddLocalPlayer("Builder");
            server.AddLocalPlayer("Neighbour");
            var ship = a.Ships[a.ActiveShipId];
            ship.ShipType = ShipState.CustomShipType;
            ship.BuiltCells = "0:0:0:5;1:0:0:5;2:0:0:5;0:1:0:6";

            // Two players ⇒ the ship cursor flips between them every tick, recomputing combat stats each time;
            // the blob is parsed once per blob instance, not once per recompute.
            for (int i = 0; i < 10; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.Equal(1, server.CustomShipStatsParsesForTest);

            ship.BuiltCells = "0:0:0:5;1:0:0:5"; // a rebuilt hull is a new string instance → one new parse
            server.TickForTest(0.1);
            server.TickForTest(0.1);
            Assert.Equal(2, server.CustomShipStatsParsesForTest);
        }
    }

    [Fact]
    public void NetCodecWarmUp_CompilesTheRegisteredFormatters()
    {
        int warmed = NetCodec.WarmUp();
        int registered = NetCodec.RegisteredMessages.Count;
        Assert.True(warmed >= registered * 9 / 10,
            $"warm-up should reach (nearly) every registered message type (warmed {warmed} of {registered})");
        Assert.True(NetCodec.WarmUp() == warmed, "warm-up is idempotent");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
