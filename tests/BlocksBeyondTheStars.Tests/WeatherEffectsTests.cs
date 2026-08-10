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
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// What the weather DOES to the world (#900): corrosive weather costs you out in the open but not
/// under a roof, an ion storm charges an exposed suit, rain waters the ground, grit shortens a scan,
/// and the per-world RNG salt actually reaches the live server.
/// </summary>
public sealed class WeatherEffectsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WeatherEffectsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wx_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string world, string planet = "varied")
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Stormy" }), DeliveryMode.ReliableOrdered);
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

    /// <summary>On foot in the open, clear of the landed starter ship (AboardShip is recomputed from
    /// geometry every tick, so clearing the flag alone would not stick).</summary>
    private static void StepOutside(Shared.State.PlayerState p)
    {
        p.Position = new Vector3f(500f, 150f, 500f);
        p.AboardShip = false;
    }

    [Fact]
    public void AcidRain_DrainsTheSuitInTheOpen()
    {
        var server = NewServer("acid");
        var p = server.Sessions[1].State;
        StepOutside(p);
        server.SetWeatherForTest("acid_rain");
        p.SuitEnergy = 100f;

        TickSeconds(server, 10);

        Assert.True(p.SuitEnergy < 100f, "acid rain should eat into an exposed suit");
    }

    [Fact]
    public void AnIonStorm_ChargesAnExposedSuit()
    {
        // The inversion that makes bad weather worth walking into (#900).
        var server = NewServer("ions");
        var p = server.Sessions[1].State;
        StepOutside(p);
        server.SetWeatherForTest("ion_storm");
        p.SuitEnergy = 40f;

        TickSeconds(server, 10);

        Assert.True(p.SuitEnergy > 40f, $"an ion storm should charge the suit, got {p.SuitEnergy}");
    }

    [Fact]
    public void ARoofKeepsCorrosiveWeatherOff()
    {
        // Compared against the SAME exposure without a roof, because at this altitude the temperature
        // hazard is also nibbling at the suit — an absolute "still 100" assertion would be measuring
        // the cold, not the acid.
        float open = AcidDrain("acid_open", roofed: false);
        float sheltered = AcidDrain("acid_roofed", roofed: true);

        Assert.True(sheltered < open * 0.6f,
            $"a roof must keep most of the acid off (open {open}, sheltered {sheltered})");
    }

    private float AcidDrain(string world, bool roofed)
    {
        var server = NewServer(world);
        var p = server.Sessions[1].State;
        StepOutside(p);
        if (roofed)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    server.World.SetBlock(new Vector3i(500 + dx, 154, 500 + dz), stone);
                }
            }
        }

        server.SetWeatherForTest("acid_rain");
        p.SuitEnergy = 100f;
        TickSeconds(server, 10);
        return 100f - p.SuitEnergy;
    }

    [Fact]
    public void BlownGritShortensAScan()
    {
        var server = NewServer("grit");
        StepOutside(server.Sessions[1].State);

        server.SetWeatherForTest("clear");
        Assert.Equal(1.0, server.WeatherScanFactorForTest());

        foreach (string harsh in new[] { "ion_storm", "gale", "fog", "blizzard", "storm" })
        {
            server.SetWeatherForTest(harsh);
            Assert.True(server.WeatherScanFactorForTest() < 1.0, $"{harsh} should shorten a scan pulse");
        }
    }

    [Fact]
    public void TheForecast_ReadsTheSkyWithoutDisturbingIt()
    {
        var server = NewServer("forecast");
        var p = server.Sessions[1].State;
        StepOutside(p);

        var forecast = server.WeatherForecastForTest(p.PlayerId);
        Assert.NotNull(forecast);
        Assert.Contains(forecast!.Current, WeatherCatalog.AllKeys);
        Assert.NotEmpty(forecast.Upcoming);
        Assert.All(forecast.Upcoming, e =>
        {
            Assert.Contains(e.State, WeatherCatalog.AllKeys);
            Assert.True(e.StartsInSeconds > 0);
        });

        // Asking twice must give the same answer: the peek forks the RNG, it does not consume it.
        var again = server.WeatherForecastForTest(p.PlayerId);
        Assert.Equal(
            forecast.Upcoming.Select(e => e.State),
            again!.Upcoming.Select(e => e.State));
    }

    [Fact]
    public void RainSpeedsFloraRegrow()
    {
        // ONE world under two skies — two servers would also differ by their season phase, which would
        // make the comparison meaningless. A jungle at sea level so the precipitation is reliably water:
        // on a cooler world the same storm comes down as snow, which waters nothing.
        var server = NewServer("regrow", "jungle");
        StepOutside(server.Sessions[1].State);
        var cell = new Vector3i(500, 64, 500);

        server.SetWeatherForTest("clear");
        double dry = server.WeatherRegrowFactorForTest(cell);

        // "storm", not "rain": the per-biome offset can shift a position a step DRIER, and from rain
        // that lands on clouds — no precipitation at all, so nothing would be watered there.
        server.SetWeatherForTest("storm");
        double wet = server.WeatherRegrowFactorForTest(cell);

        Assert.True(wet > dry, $"planted flora should come back faster while it rains (dry {dry}, wet {wet})");
    }

    [Fact]
    public void EachWorldGetsItsOwnWeatherStream()
    {
        // The bug this package started from: the weather RNG was seeded from the SAVE SEED alone, so
        // every world in a save drew the same stream. Same seed, two different bodies — their weather
        // personalities must not match.
        // varied and highland both carry stormChance 0.4 and NO per-type volatility override — the exact
        // pair that used to produce literally the same weather sequence.
        var a = NewServer("streamA", "varied");
        var b = NewServer("streamB", "highland");
        Assert.NotEqual(a.WeatherSimForTest.Volatility, b.WeatherSimForTest.Volatility);
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
        catch (IOException)
        {
            // A still-open SQLite handle on Windows — the temp dir is disposable either way.
        }
    }
}
