// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Coloured glass and tinted lamps (#1126, B4): glass and the light fixtures opt into the dye system via
/// <c>tintable</c> in blocks.json — WITHOUT joining the shapeable set (shaped glass would need a transparent
/// shaped mesh). Doors stay out: they are animated entities, not chunk voxels — their placement path returns
/// before the tint is ever stamped, so a door dye would silently vanish.
/// </summary>
public sealed class TintedBlocksTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TintedBlocksTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tinted_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) Start(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Painter" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.AboardShip = false; // craft from the personal inventory, not a ship hold
        return (server, client, repo);
    }

    // ---------------- Content flags ----------------

    [Fact]
    public void GlassAndLightFixtures_AreTintable_DoorsAndFluidsAreNot()
    {
        foreach (var key in new[]
        {
            "glass", "torch", "lantern", "light_white", "light_red", "light_green",
            "strip_light_cyan", "strip_light_warm",
        })
        {
            Assert.True(_content.GetBlock(key)!.Tintable, key + " should accept dye (#1126)");
        }

        // Doors are entities (their cell stays air — a dye would vanish), fields/fluids keep their optics.
        foreach (var key in new[] { "door_slide", "door_hinge", "door_wood", "door_energy", "water", "force_field" })
        {
            Assert.False(_content.GetBlock(key)!.Tintable, key + " must stay undyeable");
        }
    }

    [Fact]
    public void JsonTintableOptIn_DoesNotLeakIntoTheShapeableSet()
    {
        // Shapeable stays the curated solid-material list: dyed glass yes, glass spheres no.
        Assert.False(_content.GetBlock("glass")!.Shapeable);
        Assert.False(_content.GetBlock("lantern")!.Shapeable);
        Assert.True(_content.GetBlock("stone")!.Shapeable); // the curated set is untouched
    }

    // ---------------- The dye craft ----------------

    [Fact]
    public void TintCraft_MintsDyedGlass()
    {
        var (server, client, repo) = Start("tinted_glass");
        using (repo)
        {
            var inv = server.Sessions[1].State.Inventory;
            inv.SetSlot(3, new ItemStack("glass", 4));

            client.Send(NetCodec.Encode(new TintCraftIntent
            {
                SourceItemKey = "glass",
                Tint = 0x3F6FB0,
                Count = 4,
                Slot = 3,
            }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal("glass#t3f6fb0", inv.Slots[3]!.Item);
            Assert.Equal(4, inv.Slots[3]!.Count);
        }
    }

    [Fact]
    public void GlowCraft_MintsAGlowingLamp_ForACrystalEach()
    {
        var (server, client, repo) = Start("tinted_lamp");
        using (repo)
        {
            var inv = server.Sessions[1].State.Inventory;
            inv.SetSlot(2, new ItemStack("light_white", 2));
            inv.SetSlot(9, new ItemStack("crystal", 2));

            client.Send(NetCodec.Encode(new TintCraftIntent
            {
                SourceItemKey = "light_white",
                Glow = 0xFF00FF,
                Count = 2,
                Slot = 2,
            }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal("light_white#gff00ff", inv.Slots[2]!.Item);
            Assert.Equal(0, inv.CountOf("crystal")); // one luminescent crystal per unit
        }
    }

    [Fact]
    public void TintCraft_StillRefusesAnUndyeableBlock()
    {
        var (server, client, repo) = Start("tinted_refuse");
        using (repo)
        {
            var inv = server.Sessions[1].State.Inventory;
            inv.SetSlot(1, new ItemStack("door_slide", 1));

            client.Send(NetCodec.Encode(new TintCraftIntent
            {
                SourceItemKey = "door_slide",
                Tint = 0x3F6FB0,
                Count = 1,
                Slot = 1,
            }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            Assert.Equal("door_slide", inv.Slots[1]!.Item); // untouched — the gate held
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
