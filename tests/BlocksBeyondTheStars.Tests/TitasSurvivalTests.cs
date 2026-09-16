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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Surviving Titas (2026-09): an exposure meter instead of the suit drain — 40 minutes of cold outside, 30 in a
/// hot zone, half speed under a roof, liners and the hazard tier stretch or shorten it, the ship resets it, a full meter
/// hurts more and more; VEGA warns at 50/75/90 %. The yellow water is toxic after a short grace.</summary>
public sealed class TitasSurvivalTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TitasSurvivalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_titas_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string world, Action<ServerConfig>? tune = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = "titas", AutoSaveIntervalMinutes = 9999 };
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return server;
    }

    private static void TickSeconds(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.Tick(0.1);
        }
    }

    /// <summary>On foot in the open, high above any terrain (no roof, no underground warmth).</summary>
    private static void StepOutside(Shared.State.PlayerState p)
    {
        p.Position = new Vector3f(500.5f, 150f, 500.5f);
        p.AboardShip = false;
    }

    [Fact]
    public void ExposureMeter_FillsAtTheTypesPace_AndTheShipResetsIt()
    {
        var server = NewServer("meter");
        var session = server.Sessions[1];
        var p = session.State;
        Assert.Equal(40 * 60.0, server.ExposureSecondsForTest(p.PlayerId, hot: false));
        Assert.Equal(30 * 60.0, server.ExposureSecondsForTest(p.PlayerId, hot: true));

        var spawn = p.Position;
        StepOutside(p);
        TickSeconds(server, 60);
        Assert.True(session.ExposureActive);
        Assert.Equal(100f, p.SuitEnergy); // the suit battery is not what the cold eats here
        double expected = 60.0 / (session.ExposureHot ? 30 * 60.0 : 40 * 60.0);
        Assert.InRange(p.Exposure, expected * 0.9, expected * 1.1);
        Assert.Equal(100f, p.Health);

        // Back aboard: the meter is gone.
        p.Position = spawn;
        p.AboardShip = true;
        TickSeconds(server, 1);
        Assert.False(session.ExposureActive);
        Assert.Equal(0f, p.Exposure);
    }

    [Fact]
    public void ExposureMeter_LinersAndTheHazardTier_StretchTheTimer()
    {
        var server = NewServer("liners");
        var p = server.Sessions[1].State;
        p.Inventory.Add("suit_liner_1", 1, 1);
        Assert.Equal(40 * 60.0 * 1.25, server.ExposureSecondsForTest(p.PlayerId, hot: false), 3);
        p.Inventory.Add("suit_liner_2", 1, 1);
        Assert.Equal(40 * 60.0 * 1.5, server.ExposureSecondsForTest(p.PlayerId, hot: false), 3);
        p.Inventory.Add("suit_liner_3", 1, 1);
        Assert.Equal(40 * 60.0 * 2.0, server.ExposureSecondsForTest(p.PlayerId, hot: false), 3);

        var light = NewServer("light", c => c.Rules.EnvironmentalHazards = HazardLevel.Light);
        Assert.Equal(30 * 60.0 * 1.5, light.ExposureSecondsForTest(light.Sessions[1].State.PlayerId, hot: true), 3);
        var hard = NewServer("hard", c => c.Rules.EnvironmentalHazards = HazardLevel.Hard);
        Assert.Equal(40 * 60.0 * 0.75, hard.ExposureSecondsForTest(hard.Sessions[1].State.PlayerId, hot: false), 3);
    }

    [Fact]
    public void FullMeter_HurtsMoreAndMore_WithItsOwnDeathLine_AndHazardsOffIsExempt()
    {
        var server = NewServer("frozen");
        var session = server.Sessions[1];
        var p = session.State;
        StepOutside(p);
        p.Exposure = 1f;
        TickSeconds(server, 2);
        float firstLoss = 100f - p.Health;
        Assert.True(firstLoss > 0.5f, $"a full meter must hurt (lost {firstLoss})");
        Assert.Equal(session.ExposureHot ? "@srv.death.burned" : "@srv.death.froze", session.HazardDeathReason);
        float before = p.Health;
        TickSeconds(server, 2);
        Assert.True(before - p.Health > firstLoss, "the damage rises the longer the meter stays full");

        var off = NewServer("mild", c => c.ApplyCommandLine(new[] { "--hazards", "off" }));
        var q = off.Sessions[1].State;
        StepOutside(q);
        TickSeconds(off, 10);
        Assert.Equal(0f, q.Exposure);
        Assert.Equal(100f, q.Health);
    }

    [Fact]
    public void ToxicWater_HurtsAfterAShortGrace()
    {
        var server = NewServer("toxic");
        var session = server.Sessions[1];
        var p = session.State;
        StepOutside(p);
        var water = _content.GetBlock("water")!.NumericId;
        for (int y = 150; y <= 150; y++) // wading: feet in the water, head in the air — the breathable-air regen must not cancel it
        {
            server.World.SetBlock(new Vector3i(500, y, 500), water);
        }

        p.Exposure = 0f;
        TickSeconds(server, 2);
        Assert.True(server.ToxicWaterSecondsForTest(p.PlayerId) > 1.5);
        float afterGrace = p.Health;
        TickSeconds(server, 4);
        Assert.True(p.Health < afterGrace - 3f, $"two HP a second after the grace (health {p.Health}, was {afterGrace})");
        Assert.Equal("@srv.death.toxic_water", session.HazardDeathReason);
    }

    [Fact]
    public void Titas_KeepsOnlyItsSpsLabs_AirlessColdModules_AndManyMachines()
    {
        var server = NewServer("labs");
        var labs = server.SpsLabsForTest();
        Assert.InRange(labs.Count, 1, 6);
        Assert.Empty(server.SettlementsForTest);
        Assert.Empty(server.BanditCampsForTest());
        Assert.Empty(server.MonumentsForTest());

        // The lab module: its data terminal at the back wall, no air and −90 °C inside, a roof overhead.
        var lab = labs[0];
        Assert.Equal(_content.GetBlock("factory_terminal")!.NumericId, server.World.GetBlock(new Vector3i(lab.Min.X + 8, lab.Min.Y + 1, lab.Min.Z + 3)));
        var inside = new Vector3f(lab.Min.X + 8.5f, lab.Min.Y + 1f, lab.Min.Z + 6.5f);
        Assert.True(server.InSpsLabForTest(inside));
        Assert.False(server.InSpsLabForTest(new Vector3f(lab.Min.X + 8.5f, lab.Min.Y + 1f, lab.Min.Z + 24.5f))); // the open ship pad

        var session = server.Sessions[1];
        var p = session.State;
        p.Position = inside;
        p.AboardShip = false;
        TickSeconds(server, 3);
        Assert.True(p.Oxygen < 100f, "a lab module holds no air");
        Assert.Equal(-90f, session.EffectiveTemperatureC);
        Assert.True(session.ExposureRoofed);

        // "Very many guardians": the machine cap ×2.5 (Normal = 2 per player).
        Assert.Equal(5, server.PlanetEnemyCapForTest(1));
        Assert.Equal(10, server.PlanetEnemyCapForTest(2));
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
