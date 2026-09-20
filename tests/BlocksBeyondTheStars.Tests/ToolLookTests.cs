// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player tool looks (#1963): the payload of a small coloured voxel model, its merge into held-model boxes, and
/// the server rules — a look belongs to the player and a base tool key, is validated, capped, persisted, and
/// never touches what the item does.
/// </summary>
public sealed class ToolLookTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ToolLookTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_toollook_" + Guid.NewGuid().ToString("N"));
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
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo, string world = "toollook")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A look: a grey grip (colour 2) and a glowing cyan bar (colour 12) along the tool.</summary>
    private static string Look(int barLength = 10, int extraVoxels = 0)
    {
        var v = new byte[ToolLook.VoxelCount];
        for (int y = 0; y < 3; y++)
        {
            v[ToolLook.IndexOf(3, y, 2)] = 2;
        }

        for (int z = 2; z < 2 + barLength; z++)
        {
            v[ToolLook.IndexOf(3, 3, z)] = 12;
        }

        for (int i = 0; i < extraVoxels; i++)
        {
            v[ToolLook.IndexOf(7, 7, i)] = 8;
        }

        return ToolLook.Compose(v, 1 << 12);
    }

    // ---------------------------------------------------------------- format

    [Fact]
    public void ALook_MergesPerColour_IntoTheBoxesOfAHeldModel()
    {
        string look = Look();

        Assert.StartsWith("t1:1000:", look);
        Assert.True(ToolLook.IsValid(look));
        var parts = ToolLook.ToParts(look);

        Assert.Equal(2, parts.Count); // the grip and the bar — two colours, two boxes
        Assert.All(parts, p => Assert.True(p.IsValid()));
        var grip = parts.Single(p => p.C == ToolLook.Palette[2]);
        var bar = parts.Single(p => p.C == ToolLook.Palette[12]);
        Assert.False(grip.G);
        Assert.True(bar.G);
        Assert.Equal(3 * ToolLook.Voxel, grip.S[1], 4);
        Assert.Equal(10 * ToolLook.Voxel, bar.S[2], 4);
        Assert.True(bar.P[2] > grip.P[2]); // the bar reaches away from the holder
    }

    [Fact]
    public void WhatIsNotALook_IsRefused()
    {
        string look = Look();
        var checker = new byte[ToolLook.VoxelCount];
        for (int i = 0; i < checker.Length; i++)
        {
            int x = i % ToolLook.SizeX, z = (i / ToolLook.SizeX) % ToolLook.SizeZ, y = i / (ToolLook.SizeX * ToolLook.SizeZ);
            checker[i] = (byte)(((x + y + z) & 1) == 0 ? 3 : 0);
        }

        Assert.False(ToolLook.IsValid(null));
        Assert.False(ToolLook.IsValid(string.Empty));                                  // "no look" is the caller's business
        Assert.False(ToolLook.IsValid(look.Substring(0, look.Length - 1)));
        Assert.False(ToolLook.IsValid(look.ToUpperInvariant()));
        Assert.False(ToolLook.IsValid("t1: 1a :" + look.Substring(8)));                // a mask with blanks
        Assert.False(ToolLook.IsValid("t2:" + look.Substring(3)));
        Assert.Equal(string.Empty, ToolLook.Compose(new byte[ToolLook.VoxelCount], 0)); // nothing drawn
        Assert.Equal(string.Empty, ToolLook.Compose(checker, 0));                       // hundreds of boxes
        Assert.Equal(string.Empty, ToolLook.Compose(new byte[12], 0));
        Assert.Empty(ToolLook.ToParts("garbage"));
    }

    [Fact]
    public void ALook_RoundTrips()
    {
        string look = Look();

        Assert.True(ToolLook.TryRead(look, out byte[] voxels, out int glow));
        Assert.Equal(1 << 12, glow);
        Assert.Equal(look, ToolLook.Compose(voxels, glow));
        Assert.Equal(16, ToolLook.Palette.Count);
    }

    [Fact]
    public void AShareCode_CarriesALook_AndRefusesWhatTheServerWouldRefuse()
    {
        string look = Look();

        string code = BlocksBeyondTheStars.Shared.World.ShareCode.EncodeToolLook(look, "titanium_drill");

        Assert.StartsWith("BBTS1-L-", code);
        Assert.True(BlocksBeyondTheStars.Shared.World.ShareCode.TryDecodeToolLook(" " + code + " ", out string back, out string tool));
        Assert.Equal(look, back);
        Assert.Equal("titanium_drill", tool);
        Assert.Equal(string.Empty, BlocksBeyondTheStars.Shared.World.ShareCode.EncodeToolLook("t1:garbage", "titanium_drill"));
        string forged = BlocksBeyondTheStars.Shared.World.ShareCode.Encode("L", look.ToUpperInvariant(), "titanium_drill");
        Assert.False(BlocksBeyondTheStars.Shared.World.ShareCode.TryDecodeToolLook(forged, out back, out _));
        Assert.Equal(string.Empty, back);
    }

    // ---------------------------------------------------------------- server

    [Fact]
    public void ALook_IsStoredWithThePlayer_AndSurvivesARestart()
    {
        string look = Look();
        var server = NewServer(out var repo, "toollook_restart");
        var p = server.AddLocalPlayer("Smith");
        server.SetToolLookForTest(p, "titanium_drill", look);
        Assert.Equal(look, p.State.ToolLooks["titanium_drill"]);
        string playerId = p.State.PlayerId;
        server.Stop();
        repo.Dispose();

        var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "toollook_restart"));
        repo2.Initialize();
        using (repo2)
        {
            var loaded = repo2.LoadPlayer(playerId);
            Assert.NotNull(loaded);
            Assert.Equal(look, loaded!.ToolLooks["titanium_drill"]);
        }
    }

    [Fact]
    public void OnlyARealTool_UnderItsBaseKey_TakesALook()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Smith");
            string look = Look();

            server.SetToolLookForTest(p, "stone", look);                  // not a tool
            server.SetToolLookForTest(p, "no_such_item", look);
            server.SetToolLookForTest(p, "titanium_drill#s03", look);     // a composed key
            server.SetToolLookForTest(p, "titanium_drill", "t1:garbage");

            Assert.Empty(p.State.ToolLooks);
        }
    }

    [Fact]
    public void ALook_CanBeTakenBack_AndTheThrottleIsSharedWithTheOtherAppearanceEdits()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Smith");
            server.SetToolLookForTest(p, "titanium_drill", Look());

            server.SetToolLookForTest(p, "titanium_drill", string.Empty); // inside the 2 s window → ignored
            Assert.Single(p.State.ToolLooks);

            server.Tick(2.5);
            server.SetToolLookForTest(p, "titanium_drill", string.Empty);
            Assert.Empty(p.State.ToolLooks);
        }
    }

    [Fact]
    public void APlayer_HasAtMostSixteenLooks()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Smith");
            var tools = _content.Items.Values.Where(i => i.Tool != null).Select(i => i.Key).Take(ToolLook.MaxLooksPerPlayer + 1).ToList();
            Assert.Equal(ToolLook.MaxLooksPerPlayer + 1, tools.Count);

            foreach (string tool in tools)
            {
                server.SetToolLookForTest(p, tool, Look());
                server.Tick(2.5);
            }

            Assert.Equal(ToolLook.MaxLooksPerPlayer, p.State.ToolLooks.Count);
            Assert.DoesNotContain(tools[^1], p.State.ToolLooks.Keys);

            // replacing one of the sixteen is always possible
            server.SetToolLookForTest(p, tools[0], Look(barLength: 4));
            Assert.Equal(Look(barLength: 4), p.State.ToolLooks[tools[0]]);
        }
    }
}
