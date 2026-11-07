// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3336: a Ressaw block a player lingers beside cracks, and lingering on breaks it — the cell becomes the still liquid
/// Ressaw and a small, bounded puddle spreads into the free floor cells beside it. Walking past cracks nothing. Through the
/// real server: the block edits are ordinary edits (saved and broadcast), the sounds go out as <see cref="SoundFx"/>.
/// </summary>
public sealed class RessawBlockTests : IDisposable
{
    private const int Y = 200; // the floor is at Y - 1, the Ressaw block and the player's feet at Y

    private readonly string _root;
    private readonly GameContent _content;

    public RessawBlockTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_ressaw_block_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>A server with a stone floor (13 × 13, around the origin) and one Ressaw block at the origin.</summary>
    private SvGameServer NewServer(out SqliteWorldRepository repo, RecordingTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "ressaw_blocks"));
        var config = new ServerConfig { WorldName = "ressaw_blocks", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport ?? new RecordingTransport(), repo);
        server.Start();
        for (int x = -6; x <= 6; x++)
        {
            for (int z = -6; z <= 6; z++)
            {
                Set(server, x, Y - 1, z, "stone");
            }
        }

        Set(server, 0, Y, 0, RessawCrackRules.BlockKey);
        return server;
    }

    private void Set(SvGameServer server, int x, int y, int z, string key)
        => server.World.SetBlock(new Vector3i(x, y, z), key == "air" ? BlockId.Air : _content.GetBlock(key)!.NumericId);

    private string KeyAt(SvGameServer server, int x, int y, int z) => _content.BlockById(server.World.GetBlock(new Vector3i(x, y, z)))?.Key ?? "air";

    private static BlocksBeyondTheStars.GameServer.PlayerSession StandAt(SvGameServer server, float x, float z)
    {
        var p = server.AddLocalPlayer("Linger");
        p.State.Position = new Vector3f(x, Y, z);
        return p;
    }

    private static void Beats(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds - 1e-6; t += 0.1)
        {
            server.TickRessawBlocksForTest(0.1);
        }
    }

    [Fact]
    public void ABlock_CracksAfterTheDwellTime_ThroughTheRealTick()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, transport);
        using (repo)
        {
            StandAt(server, 2.5f, 0.5f); // two cells beside it
            for (int i = 0; i < 40; i++)
            {
                server.TickForTest(0.1); // 4 s: not yet
            }

            Assert.Equal(RessawCrackRules.BlockKey, KeyAt(server, 0, Y, 0));

            for (int i = 0; i < 25; i++)
            {
                server.TickForTest(0.1);
            }

            Assert.Equal(RessawCrackRules.CrackedKey, KeyAt(server, 0, Y, 0));
            Assert.Contains(transport.Messages().OfType<SoundFx>(), s => s.SoundId == RessawCrackRules.CrackSound);
            var change = transport.Messages().OfType<BlockChanged>().Last(b => b.X == 0 && b.Y == Y && b.Z == 0);
            Assert.True(change.Moved); // no mining crumble on the clients: the crack brings its own sound
            var cell = new Vector3i(0, Y, 0);
            Assert.Contains(repo.LoadChunkEdits(server.ActiveLocationId, WorldConstants.WorldToChunk(cell)), e => e.WorldPosition == cell
                && e.Block == _content.GetBlock(RessawCrackRules.CrackedKey)!.NumericId.Value); // saved like any edit
        }
    }

    [Fact]
    public void LingeringOn_BreaksTheCrackedBlock_IntoABoundedPuddle_ThatNeverReplacesASolidBlock()
    {
        var transport = new RecordingTransport();
        var server = NewServer(out var repo, transport);
        using (repo)
        {
            Set(server, -1, Y, 0, "stone"); // a wall beside the block on two sides …
            Set(server, 0, Y, 1, "stone");
            Set(server, 0, Y - 1, -1, "air"); // … and a hole in the floor on the third
            StandAt(server, 2.5f, 0.5f);

            Beats(server, RessawCrackRules.BeatSeconds + RessawCrackRules.CrackSeconds + 0.6);
            Assert.Equal(RessawCrackRules.CrackedKey, KeyAt(server, 0, Y, 0));
            Beats(server, RessawCrackRules.LeakSeconds + 0.6);

            Assert.Equal(RessawCrackRules.LiquidKey, KeyAt(server, 0, Y, 0));
            Assert.Contains(transport.Messages().OfType<SoundFx>(), s => s.SoundId == RessawCrackRules.LeakSound);
            Assert.Equal("stone", KeyAt(server, -1, Y, 0)); // never replaces a solid block
            Assert.Equal("stone", KeyAt(server, 0, Y, 1));
            Assert.Equal("air", KeyAt(server, 0, Y, -1)); // no floor under it: no puddle there
            Assert.Equal("air", KeyAt(server, 2, Y, 0)); // nor in the cell the player stands in

            int puddle = 0;
            for (int x = -6; x <= 6; x++)
            {
                for (int z = -6; z <= 6; z++)
                {
                    Assert.NotEqual(RessawCrackRules.LiquidKey, KeyAt(server, x, Y + 1, z)); // never above the source
                    Assert.NotEqual(RessawCrackRules.LiquidKey, KeyAt(server, x, Y - 1, z)); // nor below it
                    puddle += KeyAt(server, x, Y, z) == RessawCrackRules.LiquidKey ? 1 : 0;
                }
            }

            Assert.Equal(1 + RessawCrackRules.MaxPuddleCells, puddle); // the source plus a bounded puddle

            Beats(server, 10);
            Assert.Equal(0, server.RessawCrackClockCountForTest); // Ressaw itself never cracks: nothing left to time
        }
    }

    [Fact]
    public void WalkingPast_CracksNothing()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = StandAt(server, -10.5f, 1.5f);
            for (double x = -10.5; x <= 10.5; x += 0.4) // 4 blocks a second, one cell beside the block
            {
                p.State.Position = new Vector3f((float)x, Y, 1.5f);
                server.TickRessawBlocksForTest(0.1);
            }

            Assert.Equal(RessawCrackRules.BlockKey, KeyAt(server, 0, Y, 0));
            Assert.Equal(0, server.RessawCrackClockCountForTest); // gone past: the clock is forgotten
        }
    }

    [Fact]
    public void SteppingAwayInBetween_StartsTheClockOver()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = StandAt(server, 2.5f, 0.5f);
            Beats(server, 4);
            p.State.Position = new Vector3f(6.5f, Y, 6.5f); // out of reach for a moment
            Beats(server, 1);
            p.State.Position = new Vector3f(2.5f, Y, 0.5f);
            Beats(server, 4);
            Assert.Equal(RessawCrackRules.BlockKey, KeyAt(server, 0, Y, 0)); // 8 s near it, but never 5 in one go

            Beats(server, 2);
            Assert.Equal(RessawCrackRules.CrackedKey, KeyAt(server, 0, Y, 0));
        }
    }

    [Fact]
    public void ACrackedBlock_NeverBreaksUnderSomeonesFeet()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            Set(server, 0, Y, 0, "air");
            Set(server, 0, Y - 1, 0, RessawCrackRules.CrackedKey); // the floor cell under the player
            var p = StandAt(server, 0.5f, 0.5f);
            Beats(server, RessawCrackRules.BeatSeconds + RessawCrackRules.LeakSeconds + 2);
            Assert.Equal(RessawCrackRules.CrackedKey, KeyAt(server, 0, Y - 1, 0)); // it waits …

            p.State.Position = new Vector3f(1.5f, Y, 0.5f); // … until they step off (and stay near)
            Beats(server, 1);
            Assert.Equal(RessawCrackRules.LiquidKey, KeyAt(server, 0, Y - 1, 0));
        }
    }
}
