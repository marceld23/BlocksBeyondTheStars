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
using BlocksBeyondTheStars.Shared.Textures;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2184: tree crowns are walked through like plants — the client meshes no collider for them and they give no
/// footing, while the trunks stay solid. The client half (mesher, player controller) is pinned by the Unity
/// EditMode suite; these hold the shared crown list to the content and keep the server's rules in step with it:
/// a crown is never a door jamb, and a player standing inside one is never "entombed" and teleported out.
/// </summary>
public sealed class TreeFoliageTests : IDisposable
{
    private static readonly string[] Trunks = { "wood_log", WorldGenerator.GiantLogKey, FifiPlant.StemKey };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_treefoliage_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void EveryCrownKey_IsAShippedFloraBlock()
    {
        Assert.Equal(TreeFoliage.Keys.Count, TreeFoliage.Keys.Distinct().Count());
        foreach (var key in TreeFoliage.Keys)
        {
            var def = _content.GetBlock(key);
            Assert.True(def != null, $"crown block '{key}' is missing from data/blocks.json");
            Assert.True(TreeFoliage.IsKey(key));
            // The entombed rescue skips the flora category; a crown outside it would teleport a player who walks
            // into a tree out of it again, once a second.
            Assert.Equal("flora", def!.Category);
        }
    }

    [Fact]
    public void Crowns_AreSeeThrough_AndTheGeneratorsGiantLeavesAreOne()
    {
        // What is walked through is exactly what is drawn with holes in it — a solid-looking cube you can walk
        // into would read as a bug.
        Assert.All(TreeFoliage.Keys, key => Assert.Equal(TextureAlphaMode.Cutout, TextureTiles.AlphaModeOf(key)));
        Assert.True(TreeFoliage.IsKey(WorldGenerator.GiantLeavesKey));
    }

    [Fact]
    public void Trunks_StaySolid_CrownsAreNoDoorJamb()
    {
        foreach (var trunk in Trunks)
        {
            Assert.False(TreeFoliage.IsKey(trunk), $"'{trunk}' is a trunk, not foliage");
            Assert.True(DoorProbe.IsJamb(_content.GetBlock(trunk)), $"'{trunk}' must stay a wall a door can hang in");
        }

        foreach (var key in TreeFoliage.Keys)
        {
            Assert.False(DoorProbe.IsJamb(_content.GetBlock(key)), $"'{key}' is walked through — no door jamb");
        }

        // The Paul flower's leaf slabs are opaque platforms, not a crown.
        Assert.False(TreeFoliage.IsKey("paul_leaf"));
    }

    [Fact]
    public void APlayerInsideACrown_IsNotEntombed()
    {
        var server = Start(out var repo);
        using (repo)
        {
            var pos = new Vector3f(10.5f, 200f, 10.5f);
            foreach (var key in TreeFoliage.Keys)
            {
                Fill(server, pos, key);
                Assert.False(server.IsEntombedForTest(pos), $"standing inside '{key}' must not read as entombed");
            }

            Fill(server, pos, "wood_log");
            Assert.True(server.IsEntombedForTest(pos), "control: inside a trunk IS entombed");
        }
    }

    /// <summary>Fills the feet and head cells of <paramref name="pos"/> and the cell below with <paramref name="key"/>.</summary>
    private void Fill(SvGameServer server, Vector3f pos, string key)
    {
        var id = _content.GetBlock(key)!.NumericId;
        int x = (int)Math.Floor(pos.X), y = (int)Math.Floor(pos.Y), z = (int)Math.Floor(pos.Z);
        for (int dy = -1; dy <= 1; dy++)
        {
            server.World.SetBlock(new Vector3i(x, y + dy, z), id);
        }
    }

    private SvGameServer Start(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "treefoliage"));
        var config = new ServerConfig
        {
            WorldName = "treefoliage",
            Seed = 2184,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
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
            // A leftover temp dir is not worth failing a test run over.
        }
    }
}
