// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2658 (a player report): at a base the life support gives air but never food — the player expected it to feed them
/// like the ship and their station do. Hunger rules stay as they are; VEGA tells a hungry player breathing base air, once,
/// that a heal tank feeds them at home — and keeps quiet next to a tank or while not hungry.
/// </summary>
public sealed class BaseHungerHintTests : IDisposable
{
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;
    private const string HintMilestone = "vega:hint:base_hunger";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_basehunger_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private SvGameServer Start(string name, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // no breathable air: the base core's zone is the life support
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot who founded a base and stands in its core zone.</summary>
    private static PlayerSession Found(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Justus");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
        return p;
    }

    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, float hunger, int halfSeconds)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            p.State.Hunger = hunger;
            server.TickForTest(0.5);
        }
    }

    private static readonly Vector3f InTheCoreZone = new(CoreX - 2.5f, CoreY, CoreZ + 0.5f);

    [Fact]
    public void HungryInBaseAir_VegaPointsToTheHealTank_Once()
    {
        var server = Start("hungry", out var repo);
        using (repo)
        {
            var p = Found(server);
            TickAt(server, p, InTheCoreZone, hunger: 40f, halfSeconds: 6);

            Assert.Equal(3, p.State.LifeSupportSource); // sanity: the base keeps them breathing
            Assert.Contains(HintMilestone, p.State.Milestones);
            Assert.True(p.State.Hunger < 50f, "Hunger rules are unchanged: base air does not feed.");
        }
    }

    [Fact]
    public void NotHungry_NoHint()
    {
        var server = Start("fed", out var repo);
        using (repo)
        {
            var p = Found(server);
            TickAt(server, p, InTheCoreZone, hunger: 90f, halfSeconds: 6);
            Assert.DoesNotContain(HintMilestone, p.State.Milestones);
        }
    }

    [Fact]
    public void NextToAHealTank_NoHint()
    {
        var server = Start("tank", out var repo);
        using (repo)
        {
            var p = Found(server);
            p.State.Inventory.Add("heal_tank", 1, 16);
            server.PlaceBlock(p.State.PlayerId, CoreX - 3, CoreY, CoreZ, "heal_tank");
            TickAt(server, p, InTheCoreZone, hunger: 40f, halfSeconds: 6);
            Assert.DoesNotContain(HintMilestone, p.State.Milestones);
        }
    }
}
