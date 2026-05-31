using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>
/// Per-planet atmosphere &amp; breathability: on a breathable world the suit doesn't drain oxygen on
/// the surface, while toxic/airless worlds drain it as before. Aboard the ship always regenerates.
/// </summary>
public sealed class AtmosphereTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AtmosphereTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_atmo_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "atmo_" + planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "atmo_" + planet,
            Seed = 11,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static SvGameServer SurfacePlayer(SvGameServer server, out Spacecraft.Shared.State.PlayerState state)
    {
        var session = server.AddLocalPlayer("Diver");
        session.State.AboardShip = false;
        session.State.Position = new Vector3f(0, 64, 0);
        session.State.Oxygen = 50f;
        state = session.State;
        return server;
    }

    [Fact]
    public void BreathablePlanet_DoesNotDrainOxygenOnSurface()
    {
        var server = Started("jungle", out var repo); // breathable
        using (repo)
        {
            Assert.True(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen > 50f, "Breathable air should regenerate, not drain, oxygen.");
        }
    }

    [Fact]
    public void ToxicPlanet_DrainsOxygenOnSurface()
    {
        var server = Started("rocky", out var repo); // toxic
        using (repo)
        {
            Assert.False(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen < 50f, "A toxic atmosphere should drain oxygen outside the ship.");
        }
    }

    [Fact]
    public void AirlessPlanet_DrainsOxygenOnSurface()
    {
        var server = Started("crystal", out var repo); // none (airless)
        using (repo)
        {
            Assert.False(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen < 50f, "An airless world should drain oxygen outside the ship.");
        }
    }

    [Fact]
    public void Environment_ReportsBreathable()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            Assert.True(server.AtmosphereBreathable);
        }
    }

    private (Spacecraft.Shared.State.PlayerState withExtractor, Spacecraft.Shared.State.PlayerState without) TwoSurfaceMiners(SvGameServer server)
    {
        var a = server.AddLocalPlayer("WithExtractor").State;
        var b = server.AddLocalPlayer("Plain").State;
        foreach (var s in new[] { a, b })
        {
            s.AboardShip = false;
            s.Position = new Vector3f(0, 64, 0);
            s.Oxygen = 50f;
        }

        a.Inventory.Add("oxygen_extractor", 1, 1);
        return (a, b);
    }

    [Fact]
    public void OxygenExtractor_SlowsDrain_OnToxicPlanet()
    {
        var server = Started("rocky", out var repo); // toxic, extractability 0.6
        using (repo)
        {
            var (withExtractor, without) = TwoSurfaceMiners(server);
            server.Tick(2.0);

            Assert.True(withExtractor.Oxygen > without.Oxygen, "The extractor should slow oxygen loss in a toxic atmosphere.");
            Assert.True(without.Oxygen < 50f); // the plain suit still drains
        }
    }

    [Fact]
    public void OxygenExtractor_GivesNoBenefit_OnAirlessPlanet()
    {
        var server = Started("crystal", out var repo); // airless (none), extractability 0
        using (repo)
        {
            var (withExtractor, without) = TwoSurfaceMiners(server);
            server.Tick(2.0);

            Assert.Equal(without.Oxygen, withExtractor.Oxygen); // nothing to extract from vacuum
        }
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
