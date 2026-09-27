// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Generation 15, Toxica-Maxima (#2062) — the server half: the sky is the type's, the ladder never leaves the storm and the
/// rain is acid that burns in the open (#2063), the planet stamps six to ten factories whose halls breathe and whose terminals
/// wash ore (#2067, #2070), the decontaminator is a station near its block, and <c>/setweather</c> forces the simulation (#2065).
/// </summary>
public sealed class ToxicaMaximaServerTests : IDisposable
{
    private const string Key = "toxica_maxima";
    private readonly string _root;
    private readonly GameContent _content;

    public ToxicaMaximaServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_toxica_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string world, out LoopbackClientTransport client, string planet = Key, Action<ServerConfig>? tune = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 77, StartPlanet = planet, AutoSaveIntervalMinutes = 9999, PlaceFactories = true };
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

    /// <summary>On foot in the open, high above any terrain and any roof.</summary>
    private static void StepOutside(PlayerState p)
    {
        p.Position = new Vector3f(500.5f, 150f, 500.5f);
        p.AboardShip = false;
    }

    [Fact]
    public void TheSkyIsGreen_TheStormNeverEnds_ItsRainIsAcid_AndTheOpenBurns()
    {
        var server = NewServer("storm", out _);
        var sim = server.WeatherSimForTest;
        Assert.Equal(WeatherCatalog.MaxSeverity, sim.LadderFloor);
        Assert.Equal("storm", sim.State);
        Assert.Equal(0xA8E063, server.SkyColor); // the type's light green, not a seeded hue
        Assert.True(server.ActiveTraitsForTest.CorrosiveAir && server.ActiveTraitsForTest.ToxicWater, "always, no roll");

        // A minute later the ladder is still the storm — or one of the events the type allows; never a clear sky.
        TickSeconds(server, 60);
        Assert.Contains(sim.State, new[] { "storm", "acid_rain", "toxic_storm", "gale", "ground_fog" });

        // In the open the acid drains the suit and the corrosive air eats health; Creative mode takes nothing.
        server.SetWeatherForTest("storm");
        var p = server.Sessions[1].State;
        StepOutside(p);
        p.Health = 100f;
        p.SuitEnergy = 100f;
        TickSeconds(server, 6);
        Assert.True(p.SuitEnergy < 100f, $"acid rain should drain the suit (energy {p.SuitEnergy})");
        Assert.True(p.Health < 100f, $"the corrosive air should hurt (health {p.Health})");

        p.ModeOverride = PlayerModeOverride.Creative;
        p.Health = 100f;
        p.SuitEnergy = 100f;
        TickSeconds(server, 6);
        Assert.Equal(100f, p.Health);
        Assert.Equal(100f, p.SuitEnergy);
    }

    [Fact]
    public void SixToTenFactories_TheFirstInSightOfThePad_WashOre_AndTheirHallsBreathe()
    {
        var server = NewServer("plants", out _);
        Assert.InRange(server.FactoryCount, 6, 10);
        var factories = server.FactoriesForTest;
        var washes = _content.GetPlanet(Key)!.FactoryRecipes.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.All(factories, f => Assert.Equal(washes, f.Roster.OrderBy(k => k, StringComparer.Ordinal).ToArray()));
        Assert.All(factories, f => Assert.Equal(4, f.MachineCount));

        // The first hall stands in the near ring of the landing pad (the spawn is on pad 0).
        var spawn = server.Sessions[1].State.Position;
        var first = factories.OrderBy(f => f.Id).First();
        float cx = (first.Min.X + first.Max.X) / 2f, cz = (first.Min.Z + first.Max.Z) / 2f;
        double dist = Math.Sqrt((cx - spawn.X) * (cx - spawn.X) + (cz - spawn.Z) * (cz - spawn.Z));
        Assert.True(dist <= 140, $"the first factory stands {dist:F0} blocks from the pad");

        // Inside a hall: life support (source 4), no corrosion; in the open: none.
        var inside = new Vector3f(cx + 0.5f, first.Min.Y + 1.5f, cz + 0.5f);
        Assert.True(server.InFactoryAirForTest(inside));
        Assert.False(server.InFactoryAirForTest(new Vector3f(cx + 0.5f, 150f, cz + 0.5f)));
        var p = server.Sessions[1].State;
        p.Position = inside;
        p.AboardShip = false;
        p.Health = 100f;
        TickSeconds(server, 5);
        Assert.Equal(4, p.LifeSupportSource);
        Assert.Equal(100f, p.Health);

        StepOutside(p);
        TickSeconds(server, 5);
        Assert.Equal(0, p.LifeSupportSource);
        Assert.True(p.Health < 100f);
    }

    [Fact]
    public void TheDecontaminator_IsAStation_NearItsPlacedBlock()
    {
        var server = NewServer("wash", out _, planet: "rocky");
        var p = server.Sessions[1].State;
        p.AboardShip = false;
        var cell = new Vector3i((int)Math.Floor(p.Position.X), (int)Math.Floor(p.Position.Y), (int)Math.Floor(p.Position.Z));
        Assert.DoesNotContain("decontaminator", server.StationsInReachForTest(p.PlayerId).Available);

        server.World.SetBlock(new Vector3i(cell.X + 1, cell.Y, cell.Z), _content.GetBlock("decontaminator")!.NumericId);
        Assert.Contains("decontaminator", server.StationsInReachForTest(p.PlayerId).Available);
        Assert.All(new[] { "clean_iron_ore", "clean_diamond_ore", "clean_wood", "wash_meat" },
            key => Assert.Equal(CraftingStation.Decontaminator, _content.Recipes[key].Station));
    }

    [Fact]
    public void SetWeather_ForcesTheSimulation_AndRefusesAnUnknownKey()
    {
        // The cheat commands sit behind the world's cheat option; the role gate is the admin's.
        var server = NewServer("admin", out _, planet: "rocky", tune: c => c.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true });
        var session = server.Sessions[1];
        session.State.Role = PlayerRole.WorldAdmin;

        server.HandleForTest(session, new AdminCommandIntent { Command = "set_weather", StringArg = "acid_rain" });
        server.Tick(0.1);
        Assert.Equal("acid_rain", server.WeatherSimForTest.State); // #2065: the command used to write a field nothing read

        server.HandleForTest(session, new AdminCommandIntent { Command = "set_weather", StringArg = "hurricane" });
        server.Tick(0.1);
        Assert.Equal("acid_rain", server.WeatherSimForTest.State);
    }
}
