// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The reach is checked before the world is touched (#3199). The height band already guarded Y, but X and Z of a place or
/// mine intent are free — and both handlers read the target cell (which loads or generates its chunk on the tick thread)
/// before asking whether the player can reach it at all. A modified client could make the server generate a far chunk per
/// intent; a far air cell even logged a ghost-block heal and re-streamed a chunk.
/// </summary>
public sealed class FarBlockEditReachTests : IDisposable
{
    private const int Far = 9_871; // odd: a chunk-aligned world size never wraps it back beside the player

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_farreach_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private (SvGameServer Server, PlayerSession Player) Started(string name, RecordingTransport transport, CapturingLogger? log = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, transport, repo, log);
        server.Start();
        _repos.Add(repo);
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 200f, 0.5f); // up in free air: every cell around is empty
        transport.Sent.Clear();
        return (server, p);
    }

    private static List<string> Rejections(RecordingTransport t, PlayerSession who)
        => t.To<ActionRejected>(who).Select(r => r.Reason).ToList();

    [Theory]
    [InlineData(Far, 64, Far)]
    [InlineData(-Far, 30, 7)]
    public void MiningFarAway_IsOutOfReach_AndLoadsNoChunk(int x, int y, int z)
    {
        var transport = new RecordingTransport();
        var log = new CapturingLogger();
        var (server, p) = Started("far_mine_" + (x > 0 ? "east" : "west"), transport, log);
        int before = server.World.LoadedChunkCount;

        server.MineBlockOnce(p.State.PlayerId, x, y, z);

        Assert.Equal(before, server.World.LoadedChunkCount);
        Assert.Equal(new[] { "@out_of_reach" }, Rejections(transport, p));
        Assert.DoesNotContain(log.Warnings, w => w.Contains("Ghost block healed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("iron_wall")]
    [InlineData("torch")] // the torch rule reads the air at the target first
    public void PlacingFarAway_IsOutOfReach_AndLoadsNoChunk(string item)
    {
        var transport = new RecordingTransport();
        var (server, p) = Started("far_place_" + item, transport);
        p.State.Inventory.Add(item, 4, 99);
        int before = server.World.LoadedChunkCount;

        server.PlaceBlock(p.State.PlayerId, Far, 64, Far, item);

        Assert.Equal(before, server.World.LoadedChunkCount);
        Assert.Equal(new[] { "@out_of_reach" }, Rejections(transport, p));
        Assert.Equal(4, p.State.Inventory.CountOf(item)); // a refused placement costs nothing
    }

    [Fact]
    public void ANearBlock_IsStillMinedAndPlaced()
    {
        var transport = new RecordingTransport();
        var (server, p) = Started("far_near", transport);
        var cell = new Vector3i(1, 200, 0);
        p.State.Inventory.Add("iron_wall", 4, 99);

        server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "iron_wall");
        Assert.False(server.World.GetBlock(cell).IsAir);

        server.World.SetBlock(cell, _content.GetBlock("mud")!.NumericId); // soft: one basic-drill hit
        server.MineBlockOnce(p.State.PlayerId, cell.X, cell.Y, cell.Z);

        Assert.True(server.World.GetBlock(cell).IsAir);
        Assert.DoesNotContain("@out_of_reach", Rejections(transport, p));
    }
}
