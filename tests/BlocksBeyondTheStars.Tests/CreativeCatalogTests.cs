// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1930 ("please unlock everything in Sandbox — you shouldn't have to craft anything any more"): the "All items"
/// catalog hands any item out, but only to a player who plays the Creative game mode.</summary>
public sealed class CreativeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_catalog_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private SvGameServer Started(string world, GameMode mode, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig { WorldName = world, Seed = 5, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.GameMode = mode;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void InSandbox_AnyItemIsHandedOut_ClampedToItsStack()
    {
        var server = Started("sandbox", GameMode.Creative, out var repo);
        using var _ = repo;
        var player = server.AddLocalPlayer("Screelit");

        server.CreativeTakeItemForTest("Screelit", "diamond_drill", 5);
        server.CreativeTakeItemForTest("Screelit", "iron_plate", 99);

        Assert.Equal(1, player.State.Inventory.CountOf("diamond_drill")); // a tool stacks to one
        Assert.Equal(System.Math.Min(99, _content.MaxStackOf("iron_plate")), player.State.Inventory.CountOf("iron_plate"));
    }

    [Fact]
    public void InSurvival_NothingIsHandedOut_UnlessThePlayerPlaysCreative()
    {
        var server = Started("survival", GameMode.Survival, out var repo);
        using var _ = repo;
        var player = server.AddLocalPlayer("Screelit");

        server.CreativeTakeItemForTest("Screelit", "diamond_drill", 1);
        Assert.Equal(0, player.State.Inventory.CountOf("diamond_drill"));

        player.State.ModeOverride = PlayerModeOverride.Creative; // the per-player Creative override (#1121)
        server.CreativeTakeItemForTest("Screelit", "diamond_drill", 1);
        Assert.Equal(1, player.State.Inventory.CountOf("diamond_drill"));
    }

    [Fact]
    public void AnUnknownItem_IsRefused()
    {
        var server = Started("unknown", GameMode.Creative, out var repo);
        using var _ = repo;
        var player = server.AddLocalPlayer("Screelit");
        int before = player.State.Inventory.Slots.Count(s => s is { IsEmpty: false });

        server.CreativeTakeItemForTest("Screelit", "not_an_item", 1);

        Assert.Equal(before, player.State.Inventory.Slots.Count(s => s is { IsEmpty: false }));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
