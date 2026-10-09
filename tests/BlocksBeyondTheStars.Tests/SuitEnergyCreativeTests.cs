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

/// <summary>
/// #2432: in Creative/Sandbox the suit battery never drains — like oxygen and hunger. The server keeps it at the worn
/// battery's maximum every tick, so a powered drill, the beam or the jetpack never run a Sandbox builder dry (Justus:
/// "in SANDBOX you do NOT have unlimited suit energy — please change!"). Survival keeps paying.
/// </summary>
public sealed class SuitEnergyCreativeTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public SuitEnergyCreativeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_suitenergy_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string name, GameMode mode)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        config.Rules.GameMode = mode;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        _repos.Add(repo);
        return server;
    }

    [Fact]
    public void Creative_TopsTheSuitBatteryUpEveryTick()
    {
        var server = NewServer("creative", GameMode.Creative);
        var kid = server.AddLocalPlayer("Kid");
        kid.State.AboardShip = false; // off the ship, where a Survival suit never recharges
        kid.State.SuitEnergy = 10f;

        server.TickForTest(0.1);

        Assert.True(kid.State.SuitEnergy >= 99f, $"Creative suit energy stayed at {kid.State.SuitEnergy}");
    }

    [Theory]
    [InlineData(GameMode.Creative, PlayerModeOverride.None, false)]
    [InlineData(GameMode.Survival, PlayerModeOverride.None, true)]
    [InlineData(GameMode.Survival, PlayerModeOverride.Creative, false)]
    [InlineData(GameMode.Creative, PlayerModeOverride.Survival, true)]
    public void TheRule_IsTheTwinOfOxygenAndHunger(GameMode mode, PlayerModeOverride over, bool drains)
    {
        var rules = new GameRules { GameMode = mode };
        Assert.Equal(drains, rules.SuitEnergyDrainsFor(over));
        Assert.Equal(rules.SuitEnergyDrainsFor(PlayerModeOverride.None), rules.SuitEnergyDrains);
        Assert.Equal(rules.OxygenEnabledFor(over) || rules.OxygenConsumption == OxygenConsumption.Off, drains || rules.OxygenConsumption == OxygenConsumption.Off);
    }

    [Fact]
    public void Survival_LeavesADrainedSuitDrained_OffTheShip()
    {
        var server = NewServer("survival", GameMode.Survival);
        var pilot = server.AddLocalPlayer("Pilot");
        pilot.State.AboardShip = false; // off the ship: no recharge in Survival
        pilot.State.SuitEnergy = 10f;

        server.TickForTest(0.1);

        Assert.InRange(pilot.State.SuitEnergy, 0f, 10.5f);
    }

    [Fact]
    public void ThePerPlayerOverride_MakesOneSandboxPlayerInASurvivalWorld()
    {
        var server = NewServer("mixed", GameMode.Survival);
        var kid = server.AddLocalPlayer("Kid");
        kid.State.ModeOverride = PlayerModeOverride.Creative; // #1121: the parent keeps survival, the kid free-builds
        kid.State.AboardShip = false;
        kid.State.SuitEnergy = 10f;

        server.TickForTest(0.1);

        Assert.True(kid.State.SuitEnergy >= 99f, $"overridden suit energy stayed at {kid.State.SuitEnergy}");
    }

    public void Dispose()
    {
        try
        {
            foreach (var r in _repos)
            {
                r.Dispose();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
