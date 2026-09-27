// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Mini-Michi-Paul on a live generation-16 jungle world (#2080–#2084, Paul and Ben's school club creature): the herd comes for
/// the bananas in a hand, two bananas thrown by one player tame one of them (the meals count per herd), another food does not
/// count, a toxic banana cannot even be thrown, hurting one sends the whole herd running, the scan names what it loves, the wire
/// carries its arms and its big head, and <c>/biped</c> rolls a begging biped into a world that has none.
/// </summary>
public sealed class BipedServerTests : IDisposable
{
    private const string MmpId = "au_mini_michi_paul";
    private readonly string _root;
    private readonly GameContent _content;

    public BipedServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_biped_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string planet = "jungle")
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "biped"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "biped",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static void Tick(SvGameServer server, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(0.1);
        }
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>A flat stone pad well above the terrain, so the body checks see open ground.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r - 4; dx <= r + 4; dx++)
        {
            for (int dz = -r - 4; dz <= r + 4; dz++)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        int padY = max + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    /// <summary>Keeps every other wild animal out of the test: none of them begs or rushes a piece while the test runs.</summary>
    private static void Quiet(SvGameServer server, ICollection<string> mine)
    {
        foreach (var c in server.Creatures)
        {
            if (!mine.Contains(c.Id) && !c.IsCompanion)
            {
                c.BegCooldownUntil = 1e9;
                c.FrozenTimer = 1e9;
            }
        }
    }

    /// <summary>Spawns <paramref name="count"/> Mini-Michi-Pauls in a row east of the pad's centre.</summary>
    private static List<string> SpawnHerd(SvGameServer server, int cx, int cz, int padY, int count)
    {
        var ids = new List<string>();
        for (int i = 0; i < count; i++)
        {
            ids.Add(server.SpawnCreatureAtForTest(new Vector3f(cx + 5.5f + i, padY + 1, cz + 0.5f + (i % 2)), MmpId));
        }

        return ids;
    }

    /// <summary>Throws one piece from the selected slot and ticks until it is eaten (or the time runs out).</summary>
    private static void ThrowAndWaitUntilEaten(SvGameServer server, string playerId, ICollection<string> mine)
    {
        for (int i = 0; i < 120 && server.ThrownFoodCellForTest() is null; i++)
        {
            Quiet(server, mine);
            server.ThrowFoodForTest(playerId, 0);
            if (server.ThrownFoodCellForTest() is null)
            {
                Tick(server, 1); // no one begging yet — wait for the herd to come back
            }
        }

        Assert.NotNull(server.ThrownFoodCellForTest());
        for (int i = 0; i < 150 && server.ThrownFoodCellForTest() is not null; i++)
        {
            Quiet(server, mine);
            Tick(server, 1);
        }

        Assert.Null(server.ThrownFoodCellForTest()); // eaten
    }

    [Fact]
    public void TwoBananas_TameOneOfTheHerd_AndTheCountStartsAgain()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = server.SpeciesRoster.Single(s => s.Id == MmpId);
            Assert.Equal("fruit_banana", sp.FavouriteFood);
            var p = server.AddLocalPlayer("Paul");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            p.State.Yaw = 0f;
            p.State.Inventory.SetSlot(0, new ItemStack("fruit_banana", 6));
            p.State.SelectedHotbarSlot = 0;
            var herd = SpawnHerd(server, cx, cz, padY, 3);
            Quiet(server, herd);
            Tick(server, 30);
            Assert.Contains(herd, id => server.BegPhaseForTest(id) == "Beg");

            ThrowAndWaitUntilEaten(server, p.State.PlayerId, herd);
            Assert.Equal(1, server.FavouriteMealsForTest(p.State.PlayerId, MmpId));
            Assert.Empty(server.CompanionEntitiesForTest(p.State.PlayerId));
            Assert.Contains("vega:hint:favourite_food", p.State.Milestones);

            // The herd wants more: back within seconds, not a minute — the second banana wins one over.
            ThrowAndWaitUntilEaten(server, p.State.PlayerId, herd);
            Tick(server, 2); // the tame runs a tick after the meal
            var companions = server.CompanionEntitiesForTest(p.State.PlayerId);
            Assert.Single(companions);
            Assert.Equal(MmpId, companions[0].SpeciesId);
            Assert.Contains(server.TamedCreaturesForTest(p.State.PlayerId), t => t.Species.Arms == 2 && t.Species.FavouriteFood == "fruit_banana");
            Assert.Equal(0, server.FavouriteMealsForTest(p.State.PlayerId, MmpId)); // the count starts again
            Assert.Equal(2, server.Creatures.Count(c => herd.Contains(c.Id) && !c.IsCompanion)); // the rest stay wild
        }
    }

    [Fact]
    public void AnotherFood_DoesNotCount_AndAToxicBananaCannotBeThrown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ben");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            p.State.Yaw = 0f;
            p.State.Inventory.SetSlot(0, new ItemStack("berries", 6));
            p.State.Inventory.SetSlot(1, new ItemStack("toxic_fruit_banana", 3));
            p.State.SelectedHotbarSlot = 0;
            var herd = SpawnHerd(server, cx, cz, padY, 2);
            Quiet(server, herd);
            Tick(server, 30);

            ThrowAndWaitUntilEaten(server, p.State.PlayerId, herd);
            Assert.Equal(0, server.FavouriteMealsForTest(p.State.PlayerId, MmpId));
            Assert.Empty(server.CompanionEntitiesForTest(p.State.PlayerId));

            int toxic = p.State.Inventory.CountOf("toxic_fruit_banana");
            server.ThrowFoodForTest(p.State.PlayerId, 1);
            Assert.Equal(toxic, p.State.Inventory.CountOf("toxic_fruit_banana"));
            Assert.Null(server.ThrownFoodCellForTest());
        }
    }

    [Fact]
    public void HurtingOne_SendsTheWholeHerdRunning_AndOffTheFood()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 22);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            string victimId = server.SpawnCreatureAtForTest(new Vector3f(cx + 2.5f, padY + 1, cz + 0.5f), MmpId);
            string farId = server.SpawnCreatureAtForTest(new Vector3f(cx + 2.5f, padY + 1, cz + 18.5f), MmpId); // past the classic 12
            Quiet(server, new[] { victimId, farId });
            server.Creatures.First(c => c.Id == victimId).Hull = 9999f; // panic must not need a kill

            server.AttackEntity("Hunter", victimId);

            var far = server.Creatures.First(c => c.Id == farId);
            Assert.True(far.PanicTimer > 4.0, $"the whole herd runs, longer than the classic 4 s startle (got {far.PanicTimer:0.0})");
            Assert.True(far.BegCooldownUntil > 1.0, "and wants no food for a while");
        }
    }

    [Fact]
    public void TheScan_AndTheWire_ShowTheBiped()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Paul");
            var scan = server.ScanSubject("Paul", "creature", MmpId);
            Assert.Contains("ui.scan.body.biped", scan.TraitKeys);
            Assert.Contains("ui.scan.behaviour.begs", scan.TraitKeys);
            Assert.Contains("ui.scan.favourite|fruit_banana", scan.TraitKeys);
            Assert.Contains("ui.scan.behaviour.feed_tame", scan.TraitKeys);
            Assert.Contains("ui.scan.voice.gibber", scan.TraitKeys);

            string id = server.SpawnCreatureAtForTest(new Vector3f(310.5f, 80f, 310.5f), MmpId);
            var net = server.NetCreatureForTest(id);
            Assert.Equal("Biped", net.BodyPlan);
            Assert.Equal(2, net.Arms);
            Assert.Equal(1.8f, net.HeadRatio, 3);
            Assert.Equal("skin", net.Hide);
            Assert.Equal("Mini-Michi-Paul", net.Name);
        }
    }

    [Fact]
    public void SummonBiped_RollsABeggingBipedIntoAWorldThatHasNone()
    {
        var server = Started(out var repo, planet: "savanna");
        using (repo)
        {
            Assert.DoesNotContain(server.SpeciesRoster, s => s.Id == MmpId); // not a tropical type
            var p = server.AddLocalPlayer("Admin");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 18);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);

            server.SummonBipedForTest(p.State.PlayerId);

            var biped = server.SpeciesRoster.FirstOrDefault(s => s.BodyPlan == CreatureBodyPlan.Biped && HerdRules.BegsForFood(s));
            Assert.NotNull(biped);
            Assert.False(string.IsNullOrEmpty(biped!.FavouriteFood));
            Assert.Equal(2, biped.Arms);
            Assert.Contains(server.Creatures, c => c.SpeciesId == biped.Id);
        }
    }
}
