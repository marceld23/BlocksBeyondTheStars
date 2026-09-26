// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
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

/// <summary>
/// Generation 13, the toxic worlds (#2024) — the server half: corrosive air slowly hurts outdoors and the liners slow it
/// (#2026), toxic water only where the world rolled it (#2027) and never in Creative mode or with hazards off (#2028, Titas
/// too), a few cave species (#2029), only ruined settlements and no bandit camps (#2031), VEGA's scan per world (#2032).
/// The trait chances are pinned to 0 or 1 in the loaded content, so each test knows what its world rolled.
/// </summary>
public sealed class ToxicWorldServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ToxicWorldServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_toxic_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private SvGameServer NewServer(string world, string planet = "toxic_world", Action<ServerConfig>? tune = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999 };
        tune?.Invoke(config);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Justus" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return server;
    }

    /// <summary>Pins every rolled trait of the class: 1 = every world has it, 0 = none.</summary>
    private void PinTraits(double air, double water, double caveFauna)
    {
        var p = _content.GetPlanet("toxic_world")!;
        p.CorrosiveAirChance = air;
        p.WaterDamageChance = water;
        p.CaveFaunaChance = caveFauna;
    }

    private static void TickSeconds(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.Tick(0.1);
        }
    }

    /// <summary>On foot in the open, high above any terrain.</summary>
    private static void StepOutside(Shared.State.PlayerState p)
    {
        p.Position = new Vector3f(500.5f, 150f, 500.5f);
        p.AboardShip = false;
    }

    private static float HealthLostOutside(SvGameServer server, double seconds)
    {
        var p = server.Sessions[1].State;
        StepOutside(p);
        p.Health = 100f;
        TickSeconds(server, seconds);
        return 100f - p.Health;
    }

    [Fact]
    public void CorrosiveAir_HurtsSlowlyOutdoors_TheShipKeepsItOut_TheLinersSlowIt_AndCreativeIsExempt()
    {
        PinTraits(air: 1, water: 1, caveFauna: 1);
        var server = NewServer("air");
        var session = server.Sessions[1];
        var p = session.State;
        var spawn = p.Position;
        Assert.True(server.ActiveTraitsForTest.CorrosiveAir);

        // 0.2 HP a second at Normal: slow — about eight minutes from full.
        float bare = HealthLostOutside(server, 10);
        Assert.InRange(bare, 1.6f, 2.4f);
        Assert.Equal("@srv.death.corrosive_air", session.HazardDeathReason);

        // Aboard the ship the air stays out.
        p.Position = spawn;
        p.Health = 100f;
        p.AboardShip = true;
        TickSeconds(server, 5);
        Assert.Equal(100f, p.Health);

        // The best liner counts: the climate rig keeps out 65 %.
        p.Inventory.Add("suit_liner_1", 1, 1);
        p.Inventory.Add("suit_liner_3", 1, 1);
        float lined = HealthLostOutside(server, 10);
        Assert.InRange(lined, bare * 0.3f, bare * 0.4f);

        // Creative mode (a per-player override) takes nothing.
        p.ModeOverride = PlayerModeOverride.Creative;
        Assert.Equal(0f, HealthLostOutside(server, 5));
        p.ModeOverride = PlayerModeOverride.None;

        // The rolled cave life: one or two cave species, and a small population for them.
        Assert.InRange(server.SpeciesRoster.Count, 1, 2);
        Assert.All(server.SpeciesRoster, sp => Assert.Equal(Shared.Definitions.CreatureHabitat.Cave, sp.Habitat));
        Assert.True(server.WorldCreatureCapForTest(1) > 0);

        // VEGA's scan names this world's hazards once.
        server.ShipAiToxicScanForTest(p.PlayerId);
        server.ShipAiToxicScanForTest(p.PlayerId);
        Assert.Single(server.MilestonesForTest(p.PlayerId), m => m == $"vega:hint:toxic:{server.ActiveLocationId}:both");
    }

    [Fact]
    public void AWorldThatRolledNothing_HasHarmlessAirAndWater_NoFauna_AndACalmScan()
    {
        PinTraits(air: 0, water: 0, caveFauna: 0);
        var server = NewServer("calm");
        var p = server.Sessions[1].State;
        var traits = server.ActiveTraitsForTest;
        Assert.False(traits.CorrosiveAir || traits.ToxicWater || traits.CaveFauna);

        Assert.Equal(0f, HealthLostOutside(server, 5));
        Assert.Equal(0f, HealthLostInWater(server, 6));
        Assert.Empty(server.SpeciesRoster);
        Assert.Equal(0, server.WorldCreatureCapForTest(1));

        server.ShipAiToxicScanForTest(p.PlayerId);
        Assert.Contains($"vega:hint:toxic:{server.ActiveLocationId}:calm", server.MilestonesForTest(p.PlayerId));
    }

    [Fact]
    public void ToxicWater_HurtsOnAWorldThatRolledIt()
    {
        PinTraits(air: 0, water: 1, caveFauna: 0);
        var server = NewServer("water");
        Assert.True(server.ActiveTraitsForTest.ToxicWater);
        Assert.True(HealthLostInWater(server, 6) > 3f, "two HP a second after the grace");
        Assert.Equal("@srv.death.toxic_water", server.Sessions[1].HazardDeathReason);
        server.ShipAiToxicScanForTest(server.Sessions[1].State.PlayerId);
        Assert.Contains($"vega:hint:toxic:{server.ActiveLocationId}:water", server.MilestonesForTest(server.Sessions[1].State.PlayerId));
    }

    [Fact]
    public void TitasToxicWater_SparesCreativeMode_AndHazardsOff()
    {
        // #2028: the bug — the water hurt in Creative mode and with environmental hazards switched off.
        var server = NewServer("titas", "titas");
        var p = server.Sessions[1].State;
        p.ModeOverride = PlayerModeOverride.Creative;
        Assert.Equal(0f, HealthLostInWater(server, 6));
        p.ModeOverride = PlayerModeOverride.None;
        Assert.True(HealthLostInWater(server, 6) > 3f, "Survival still burns");

        var off = NewServer("titas_off", "titas", c => c.Rules.EnvironmentalHazards = HazardLevel.Off);
        Assert.Equal(0f, HealthLostInWater(off, 6));
    }

    [Fact]
    public void Settlements_AreAllRuins_WithNobodyInThem_AndNoBanditCamps()
    {
        var p = _content.GetPlanet("toxic_world")!;
        p.SettlementsBias = 40; // force a few, so the rule has something to rule
        var server = NewServer("ruins");
        var settlements = server.SettlementsForTest;
        Assert.NotEmpty(settlements);
        Assert.All(settlements, s => Assert.True(s.Ruined));
        Assert.Equal(0, server.NpcCount);
        Assert.Empty(server.BanditCampsForTest());
    }

    /// <summary>Wading in the open: a water cell at the feet, the exposure clock and the health reset.</summary>
    private float HealthLostInWater(SvGameServer server, double seconds)
    {
        var p = server.Sessions[1].State;
        StepOutside(p);
        server.World.SetBlock(new Vector3i(500, 150, 500), _content.GetBlock("water")!.NumericId);
        p.Health = 100f;
        p.Exposure = 0f;
        TickSeconds(server, seconds);
        float lost = 100f - p.Health;
        server.World.SetBlock(new Vector3i(500, 150, 500), Shared.Primitives.BlockId.Air);
        return lost;
    }
}
