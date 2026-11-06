// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3291 (Justus: "a mask with a breathing filter like the robbers wear and a black jacket — then you are no longer
/// robbed"): a robber who walks up to a player in the full disguise takes them for one of his own — no demand, he says so
/// and walks off, and the player gets the quiet spell a payment would buy. The mask alone (or the jacket alone) fools
/// nobody.
/// </summary>
public sealed class RobberDisguiseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_disguise_" + Guid.NewGuid().ToString("N"));
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
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string world, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Normal;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>An on-foot player with goods worth taking, wearing <paramref name="gear"/>; a robber spawns three steps
    /// away and one tick brings him into talking range.</summary>
    private static PlayerSession WalkUpTo(SvGameServer server, string name, params (EquipSlot Slot, string Item)[] gear)
    {
        var pilot = server.AddLocalPlayer(name);
        pilot.State.AboardShip = false;
        var inv = pilot.State.Inventory;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            inv.SetSlot(i, null);
        }

        inv.SetSlot(0, new ItemStack("iron_ore", 100));
        foreach (var (slot, item) in gear)
        {
            pilot.State.Equipment.SetSlot((int)slot, new ItemStack(item, 1));
        }

        var at = pilot.State.Position;
        server.SpawnBanditAtForTest(new Vector3f(at.X + 3f, at.Y, at.Z + 3f), name);
        server.Tick(0.1);
        return pilot;
    }

    [Fact]
    public void TheFullDisguise_MakesTheRobberWalkOn_WithoutADemand()
    {
        var transport = new RecordingTransport();
        var server = Started("disguised", transport);
        var pilot = WalkUpTo(server, "Justus", (EquipSlot.Head, "robber_mask"), (EquipSlot.Chest, "robber_jacket"));

        Assert.Equal(0, server.PendingBanditDemandIdForTest("Justus"));
        var robber = Assert.Single(server.Bandits);
        Assert.Equal(BanditPhase.Leaving, robber.BanditPhase);
        Assert.False(robber.Hostile);
        Assert.Equal(100, pilot.State.Inventory.CountOf("iron_ore"));
        Assert.Contains("@srv.bandit.disguised:" + robber.Name, transport.TextsTo(pilot));
        Assert.True(pilot.Bandit.NextBanditAmbushAt >= 1200.0); // the quiet spell of a payment
    }

    [Theory]
    [InlineData(EquipSlot.Head, "robber_mask")]
    [InlineData(EquipSlot.Chest, "robber_jacket")]
    public void HalfADisguise_FoolsNobody(EquipSlot slot, string item)
    {
        var transport = new RecordingTransport();
        var server = Started("half_" + item, transport);
        var pilot = WalkUpTo(server, "Justus", (slot, item));

        Assert.NotEqual(0, server.PendingBanditDemandIdForTest("Justus"));
        Assert.Equal(BanditPhase.Demanding, Assert.Single(server.Bandits).BanditPhase);
        Assert.DoesNotContain(transport.TextsTo(pilot), t => t.StartsWith("@srv.bandit.disguised", StringComparison.Ordinal));
    }
}
