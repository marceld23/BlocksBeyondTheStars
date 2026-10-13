// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2530: raw food is a WEAK taming bait. A creature craving meat bait takes raw creature meat, one craving forage bait or
/// a nectar lure takes berries / raw fruit — but a raw bite counts half (two fill one feeding step) while a crafted bait
/// fills it at once. Before, raw food never counted and a player without crafted bait stalled silently.
/// </summary>
public sealed class TamingRawBaitTests : IDisposable
{
    private static readonly string[] CraftedBaits = { "forage_bait", "meat_bait", "nectar_lure" };

    private readonly string _root;
    private readonly GameContent _content;

    public TamingRawBaitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_rawbait_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rawbait"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "rawbait",
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // the classic relief, as in CreatureTamingTests
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A ranger with the translator and NO food or bait at all — each test hands out exactly what it checks.</summary>
    private static BlocksBeyondTheStars.GameServer.PlayerSession Ranger(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Ranger");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Inventory.Add("creature_translator", 1, 1);
        foreach (var item in CraftedBaits.Concat(SvGameServer.RawBaitsFor("meat_bait")).Concat(SvGameServer.RawBaitsFor("forage_bait")))
        {
            p.State.Inventory.Remove(item, p.State.Inventory.CountOf(item));
        }

        p.State.SuitEnergy = 100f;
        return p;
    }

    /// <summary>Makes every species placid and opens an attempt on a wild creature whose first need is "feed". Returns the
    /// crafted bait it craves.</summary>
    private static string StartOnAHungryCreature(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        server.Tick(6.0);
        foreach (var sp in server.SpeciesRoster)
        {
            sp.Temperament = CreatureTemperament.Passive; // forgiving, and two in three steps want feeding
        }

        foreach (var c in server.Creatures.Where(c => c.OwnerId.Length == 0).ToList())
        {
            p.State.Position = c.Position;
            server.TameDecodeForTest("Ranger");
            if (server.TameCurrentNeedForTest("Ranger") == "feed")
            {
                return server.TamePreferredBaitForTest("Ranger");
            }

            server.TameRespondForTest("Ranger", "cancel");
        }

        Assert.Fail("the lush world should have a wild creature that wants feeding first");
        return string.Empty;
    }

    [Fact]
    public void RawFood_IsAWeakBait_TwoBitesFillOneFeedingStep()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            string bait = StartOnAHungryCreature(server, p);
            string raw = SvGameServer.RawBaitsFor(bait)[0];
            p.State.Inventory.Add(raw, 5, 20);

            // The first bite counts, but only half: the raw food is eaten, no trust yet, and it is still hungry.
            server.TameRespondForTest("Ranger", "feed");
            Assert.Equal(4, p.State.Inventory.CountOf(raw));
            Assert.Equal(0, server.TameTrustForTest("Ranger"));
            Assert.Equal("feed", server.TameCurrentNeedForTest("Ranger"));

            // The second bite completes the step like one crafted bait would.
            server.TameRespondForTest("Ranger", "feed");
            Assert.Equal(3, p.State.Inventory.CountOf(raw));
            Assert.True(server.TameTrustForTest("Ranger") == 1 || server.TamedCreaturesForTest("Ranger").Count == 1,
                "two raw bites should earn one trust point");
        }
    }

    [Fact]
    public void CraftedBait_FillsAFeedingStepAtOnce_AndIsUsedBeforeRawFood()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            string bait = StartOnAHungryCreature(server, p);
            string raw = SvGameServer.RawBaitsFor(bait)[0];
            p.State.Inventory.Add(bait, 2, 20);
            p.State.Inventory.Add(raw, 5, 20);

            server.TameRespondForTest("Ranger", "feed");

            Assert.Equal(1, p.State.Inventory.CountOf(bait)); // the crafted bait went first …
            Assert.Equal(5, p.State.Inventory.CountOf(raw));  // … and the raw food stayed in the pack
            Assert.True(server.TameTrustForTest("Ranger") == 1 || server.TamedCreaturesForTest("Ranger").Count == 1,
                "one crafted bait should earn a full trust point");
        }
    }

    [Fact]
    public void TheWrongKindOfRawFood_DoesNotCount()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            string bait = StartOnAHungryCreature(server, p);

            // A meat-eater does not take berries, a forager does not take raw meat.
            string wrong = bait == "meat_bait" ? "berries" : "creature_meat";
            p.State.Inventory.Add(wrong, 5, 20);

            server.TameRespondForTest("Ranger", "feed");

            Assert.Equal(5, p.State.Inventory.CountOf(wrong));
            Assert.Equal(0, server.TameTrustForTest("Ranger"));
            Assert.Equal("feed", server.TameCurrentNeedForTest("Ranger")); // still waiting for the right food
        }
    }

    [Theory]
    [InlineData("forage_bait")]
    [InlineData("meat_bait")]
    [InlineData("nectar_lure")]
    public void RawBaits_AreRealEdibleItems_NeverToxic(string bait)
    {
        var raws = SvGameServer.RawBaitsFor(bait);

        Assert.NotEmpty(raws);
        foreach (var raw in raws)
        {
            Assert.True(_content.Items.TryGetValue(raw, out var def), $"raw bait '{raw}' must be a real item");
            Assert.Equal(ItemCategory.Consumable, def!.Category);
            Assert.DoesNotContain("toxic", raw);
        }

        Assert.Equal(bait == "meat_bait", raws.Contains("creature_meat"));
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
            // best effort — a still-open SQLite handle on Windows
        }
    }
}
