// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4012: flower pictures. A tester wanted to choose the colours of the flowers to plant whole pictures: the flower seed takes
/// the Dye action (the colour only), a dyed seed plants one fixed flower with the colour in its cell, and picking it gives
/// the dyed seed back — nothing regrows in its place.
/// </summary>
public sealed class DyedFlowerSeedTests : IDisposable
{
    private const int Red = 0xE03A3A;
    private const string RedSeed = "flower_seed#te03a3a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts-dyedflowers-" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void TheFlowerSeed_IsDyeOnly_AndPlantsTheSunblossomWhenDyed()
    {
        var seed = _content.GetItem("flower_seed")!;
        Assert.Equal("flora_sunblossom", seed.DyedPlaces);
        Assert.Contains(seed.DyedPlaces!, seed.PlacesAnyOf!); // one of the meadow flowers
        Assert.True(_content.GetBlock(seed.DyedPlaces!)!.Accent, "the dye goes on the blossom: the flower needs accent texels");
        Assert.True(_content.IsDyeable(seed));
        Assert.True(GameContent.IsDyeOnly(seed));
        Assert.Same(seed, _content.DyedSeedFor("flora_sunblossom"));
        Assert.Null(_content.DyedSeedFor("flora_tulip"));

        Assert.True(_content.IsDyeable(_content.GetItem("stone")));     // a building material, as before
        Assert.False(GameContent.IsDyeOnly(_content.GetItem("stone"))); // which also glows
        Assert.False(_content.IsDyeable(_content.GetItem("marigold_seed")));
    }

    [Fact]
    public void TheDyeAction_ColoursFlowerSeeds_ButNeverMakesThemGlow()
    {
        var (server, client, repo) = Connected("dye");
        using (repo)
        {
            var inv = server.Sessions[1].State.Inventory;
            inv.SetSlot(3, new ItemStack("flower_seed", 5));
            inv.SetSlot(9, new ItemStack("crystal", 5));

            client.Send(NetCodec.Encode(new TintCraftIntent { SourceItemKey = "flower_seed", Glow = Red, Count = 5, Slot = 3 }),
                DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            Assert.Equal("flower_seed", inv.Slots[3]!.Item); // refused: a seed takes a colour only
            Assert.Equal(5, inv.CountOf("crystal"));

            client.Send(NetCodec.Encode(new TintCraftIntent { SourceItemKey = "flower_seed", Tint = Red, Count = 5, Slot = 3 }),
                DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            Assert.Equal(RedSeed, inv.Slots[3]!.Item);
            Assert.Equal(5, inv.Slots[3]!.Count);
        }
    }

    [Fact]
    public void ADyedSeed_PlantsTheSunblossomInItsColour_AndPickingGivesTheSeedBack_WithoutRegrowth()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var session = Bed(server, RedSeed);
            var sunblossom = _content.GetBlock("flora_sunblossom")!.NumericId;
            for (int dx = -2; dx <= 2; dx++)
            {
                var c = new Vector3i(50 + dx, 50, 50);
                server.PlaceBlock("Gardener", c.X, c.Y, c.Z, RedSeed);
                Assert.Equal(sunblossom, server.World.GetBlock(c)); // one flower, every cell — a picture reads evenly
                Assert.Equal(Red, server.World.GetModifier(c).Tint);
                Assert.Equal(0, server.World.GetModifier(c).Glow);
            }

            int seedsBefore = session.State.Inventory.CountOf(RedSeed);
            int fibreBefore = session.State.Inventory.CountOf("plant_fiber");
            var picked = new Vector3i(50, 50, 50);
            server.MineBlock("Gardener", picked.X, picked.Y, picked.Z);
            Assert.True(server.World.GetBlock(picked).IsAir);
            Assert.Equal(seedsBefore + 1, session.State.Inventory.CountOf(RedSeed)); // the dyed seed, back in the pocket
            Assert.Equal(fibreBefore, session.State.Inventory.CountOf("plant_fiber")); // instead of the flower's own drops

            for (int i = 0; i < 40; i++)
            {
                server.Tick(1.0); // past the 30 s a picked wild flower takes to regrow
            }

            Assert.True(server.World.GetBlock(picked).IsAir, "a picked dyed flower must not grow back (the seed came back)");
            server.PlaceBlock("Gardener", picked.X, picked.Y, picked.Z, RedSeed); // the picture can be rearranged
            Assert.Equal(Red, server.World.GetModifier(picked).Tint);
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "garden"));
        var config = new ServerConfig { WorldName = "garden", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A grass bed around (50, 49, 50) with free air above it and a gardener beside it carrying <paramref name="item"/>.</summary>
    private BlocksBeyondTheStars.GameServer.PlayerSession Bed(SvGameServer server, string item)
    {
        var grass = _content.GetBlock("grass")!.NumericId;
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dz = -4; dz <= 4; dz++)
            {
                server.World.SetBlock(new Vector3i(50 + dx, 49, 50 + dz), grass);
                for (int y = 50; y <= 60; y++)
                {
                    server.World.SetBlock(new Vector3i(50 + dx, y, 50 + dz), BlockId.Air);
                }
            }
        }

        var session = server.AddLocalPlayer("Gardener");
        session.State.Inventory.Add(item, 20, 1024);
        session.State.Position = new Vector3f(50.5f, 50f, 54.5f);
        return session;
    }

    private (SvGameServer Server, LoopbackClientTransport Client, SqliteWorldRepository Repo) Connected(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(link), repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Painter" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.Sessions[1].State.AboardShip = false; // craft from the personal inventory, not a ship hold
        return (server, client, repo);
    }
}
