// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Temperature survival hazard (#666–#670): extreme heat/cold/vacuum drain suit energy first
/// (insulation slows it), then slowly damage health; underground is milder; Creative and hazard tier
/// Off are exempt; the tier is live-editable by the world admin.</summary>
public sealed class TemperatureHazardTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TemperatureHazardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_temp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    // ---- Pure band math -------------------------------------------------------------------------

    [Fact]
    public void Severity_IsZeroInsideTheComfortBand_AndGrowsBeyondIt()
    {
        Assert.Equal(0f, SvGameServer.TemperatureSeverityFor(15f));   // temperate
        Assert.Equal(0f, SvGameServer.TemperatureSeverityFor(-9f));   // grace zone below the band
        Assert.Equal(0f, SvGameServer.TemperatureSeverityFor(44f));   // grace zone above the band
        Assert.Equal(28f, SvGameServer.TemperatureSeverityFor(-38f)); // ice-world base
        Assert.Equal(25f, SvGameServer.TemperatureSeverityFor(70f));  // ashen-world base
        Assert.Equal(60f, SvGameServer.TemperatureSeverityFor(115f)); // lava world hits the cap (70 → 60)
        Assert.Equal(60f, SvGameServer.TemperatureSeverityFor(-150f)); // deep vacuum shadow, capped
    }

    [Fact]
    public void VacuumTemperature_FollowsTheSun()
    {
        Assert.Equal(120f, SvGameServer.VacuumTemperature(0.5), 1f);  // noon: full sunlight
        Assert.Equal(-150f, SvGameServer.VacuumTemperature(0.0), 1f); // midnight: shadow
        Assert.True(SvGameServer.VacuumTemperature(0.25) is > -20f and < -10f); // terminator ≈ the mean
    }

    [Fact]
    public void Rules_GateAndScaleTheHazard()
    {
        Assert.True(new GameRules().TemperatureHazardsEnabled); // Survival + Normal = on by default
        Assert.False(new GameRules { GameMode = GameMode.Creative }.TemperatureHazardsEnabled);
        Assert.False(new GameRules { EnvironmentalHazards = HazardLevel.Off }.TemperatureHazardsEnabled);
        Assert.Equal(0.5f, new GameRules { EnvironmentalHazards = HazardLevel.Light }.HazardSeverityFactor);
        Assert.Equal(1f, new GameRules().HazardSeverityFactor);
        Assert.Equal(1.75f, new GameRules { EnvironmentalHazards = HazardLevel.Hard }.HazardSeverityFactor);
    }

    [Fact]
    public void UndergroundFactor_RampsFromSurfaceToFullDepth()
    {
        var gen = new WorldGenerator(2026, _content);
        var ice = _content.GetPlanet("ice")!;
        int surface = gen.SurfaceHeight(ice, 100, 100);
        Assert.Equal(0.0, gen.UndergroundFactor(ice, 100, surface, 100));
        Assert.Equal(0.0, gen.UndergroundFactor(ice, 100, surface + 20, 100)); // above ground never blends
        Assert.Equal(0.5, gen.UndergroundFactor(ice, 100, surface - 12, 100), 2);
        Assert.Equal(1.0, gen.UndergroundFactor(ice, 100, surface - 24, 100));
        Assert.Equal(1.0, gen.UndergroundFactor(ice, 100, surface - 200, 100));
    }

    // ---- Live server ----------------------------------------------------------------------------

    private SvGameServer NewServer(string world, System.Action<ServerConfig>? tune, out LoopbackClientTransport client)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = "ice", AutoSaveIntervalMinutes = 9999 };
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Frosty" }), DeliveryMode.ReliableOrdered);
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

    /// <summary>Puts the player on foot in the open, away from the landed starter ship — AboardShip is
    /// recomputed from geometry every tick, so clearing the flag alone would not stick. Y sits well
    /// ABOVE any terrain column (but below the atmosphere line) so the underground blend never softens
    /// the reading and the altitude lapse pins the cold at the severity cap — deterministic per seed.</summary>
    private static void StepOutside(Shared.State.PlayerState p)
    {
        p.Position = new Shared.Geometry.Vector3f(500f, 150f, 500f);
        p.AboardShip = false;
    }

    [Fact]
    public void IceWorld_DrainsSuitEnergy_AndInsulationSlowsIt()
    {
        var server = NewServer("cold", null, out _);
        var p = server.Sessions[1].State;
        StepOutside(p); // on foot in the cold — no ship life support

        TickSeconds(server, 20);
        float nakedDrain = 100f - p.SuitEnergy;
        Assert.True(nakedDrain > 0.5f, $"expected a real drain on an ice world, got {nakedDrain}");
        Assert.True(p.SuitClimateActive);

        // Best-of insulation (85% rig): refill, re-run the same exposure — the drain must shrink hard.
        p.SuitEnergy = 100f;
        p.Inventory.Add("suit_liner_3", 1, 1);
        Assert.Equal(1, p.Inventory.CountOf("suit_liner_3"));
        TickSeconds(server, 20);
        float insulatedDrain = 100f - p.SuitEnergy;
        Assert.True(insulatedDrain < nakedDrain * 0.35f,
            $"insulation should cut the drain hard (naked {nakedDrain}, insulated {insulatedDrain})");
    }

    [Fact]
    public void EmptySuit_TakesSlowExposureDamage()
    {
        var server = NewServer("frostbite", null, out _);
        var p = server.Sessions[1].State;
        StepOutside(p);
        p.SuitEnergy = 0f;

        TickSeconds(server, 10);
        Assert.True(p.Health < 100f, "an empty suit in the cold must cost health");
        Assert.True(p.Health > 60f, "exposure damage must stay slow — never a burst kill");
    }

    [Fact]
    public void HazardsOff_AndCreative_AreExempt()
    {
        var offServer = NewServer("mild", c => c.ApplyCommandLine(new[] { "--hazards", "off" }), out _);
        var off = offServer.Sessions[1].State;
        StepOutside(off);
        TickSeconds(offServer, 10);
        Assert.Equal(100f, off.SuitEnergy);
        Assert.False(off.SuitClimateActive);

        var creativeServer = NewServer("builder", c => c.Rules.GameMode = GameMode.Creative, out _);
        var creative = creativeServer.Sessions[1].State;
        StepOutside(creative);
        TickSeconds(creativeServer, 10);
        Assert.Equal(100f, creative.SuitEnergy);
    }

    [Fact]
    public void Eva_DrainsFromVacuumExposure_EvenOverAMildWorld()
    {
        var server = NewServer("spacewalk", c => c.StartPlanet = "jungle", out _);
        var p = server.Sessions[1].State;
        p.InEva = true; // spacewalk: sun-side vacuum reads ≈ +64 °C at the start-of-day fraction

        TickSeconds(server, 20);
        Assert.True(p.SuitEnergy < 100f, "vacuum exposure must drain the suit even over a mild world");
        Assert.True(p.SuitClimateActive);
    }

    [Fact]
    public void Admin_CanLiveEditTheHazardTier_AndItPersists()
    {
        var server = NewServer("switch", null, out var client);
        client.Send(NetCodec.Encode(new SetWorldRulesIntent { EnvironmentalHazards = "Off" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.False(server.Metadata.RulesOverride!.TemperatureHazardsEnabled);
        Assert.Equal(HazardLevel.Off, server.Metadata.RulesOverride.EnvironmentalHazards);

        var p = server.Sessions[1].State;
        StepOutside(p);
        p.SuitEnergy = 100f;
        TickSeconds(server, 10);
        Assert.Equal(100f, p.SuitEnergy); // the live switch really turns the mechanic off
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
