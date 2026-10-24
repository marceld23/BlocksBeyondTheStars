// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2964: a player cut a birch, saw the quick-bar stay as it was (a free slot in it) and reported the pale logs "never
/// arrived" — they had topped up the pale-log stack in the backpack. The first find that lands only in the backpack makes
/// VEGA say where it went, once; a find that shows on the quick-bar says nothing.
/// </summary>
public sealed class BackpackPickupHintTests : IDisposable
{
    private const string HintLine = "vega.hint.backpack_pickup";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_backpackhint_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

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

    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Player) Miner(string world)
    {
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        config.Rules.StoryId = StoryRegistry.NoneStoryId;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var p = server.AddLocalPlayer("Woodcutter");
        server.TickStoryPacksForTest(p);
        server.SkipTutorialForTest(p); // past the basic training: its steps hold some hints back
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, 120f, 0.5f);
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }

        return (server, transport, p);
    }

    /// <summary>Breaks a dirt block placed next to the player (no tool needed) — one dirt into the inventory.</summary>
    private void MineDirt(SvGameServer server, PlayerSession p, int x)
    {
        var cell = new Vector3i(x, 120, 2);
        server.World.SetBlock(cell, _content.GetBlock("dirt")!.NumericId);
        server.MineBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z);
        Assert.True(server.World.GetBlock(cell).IsAir);
    }

    private static int HintsTo(RecordingTransport transport, PlayerSession p)
        => transport.LineKeys(p).Count(k => k == HintLine);

    /// <summary>The reported case: a stack of the drop already sits in the backpack, a quick-bar slot is free — the find
    /// tops up the backpack stack, and VEGA says so once.</summary>
    [Fact]
    public void AFindThatTopsUpABackpackStack_TellsWhereItWent_Once()
    {
        var (server, transport, p) = Miner("backpack_topup");
        p.State.Inventory.SetSlot(12, new ItemStack("dirt", 10));

        MineDirt(server, p, 1);
        Assert.Equal(11, p.State.Inventory.Slots[12]!.Count);
        Assert.Null(p.State.Inventory.Slots[0]); // the quick-bar did not change
        Assert.Equal(1, HintsTo(transport, p));
        Assert.Contains("vega:hint:" + SvGameServer.BackpackPickupHint, p.State.Milestones);

        MineDirt(server, p, 2);
        Assert.Equal(1, HintsTo(transport, p)); // a lesson, not a nag
    }

    [Fact]
    public void AFindOnTheQuickBar_SaysNothing()
    {
        var (server, transport, p) = Miner("backpack_quickbar");

        MineDirt(server, p, 1); // an empty inventory: the first free slot is the quick-bar's first
        Assert.Equal("dirt", p.State.Inventory.Slots[0]!.Item);

        p.State.Inventory.SetSlot(12, new ItemStack("dirt", 5)); // a second stack in the backpack, one on the quick-bar
        MineDirt(server, p, 2);
        Assert.Equal(0, HintsTo(transport, p));
    }

    [Fact]
    public void OnlyInBackpack_ReadsEveryVariantOfTheItem_AndTheQuickBarWins()
    {
        var inv = new Inventory(36);
        Assert.False(SvGameServer.OnlyInBackpack(inv, "wood_log_b")); // not carried at all

        inv.SetSlot(12, new ItemStack(ItemKey.Compose("wood_log_b", 3, 0, 0, 0), 4)); // a dyed pale log
        Assert.True(SvGameServer.OnlyInBackpack(inv, "wood_log_b"));

        inv.SetSlot(8, new ItemStack("wood_log_b", 1));
        Assert.False(SvGameServer.OnlyInBackpack(inv, "wood_log_b"));
    }

    [Fact]
    public void TheHintAndTheFeedSuffix_ReadInBothLanguages()
    {
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var l = _content.CreateLocalizer(locale);
            foreach (string key in new[] { HintLine, "hud.pickup.backpack" })
            {
                Assert.NotEqual(key, l.Get(key));
            }
        }
    }
}
