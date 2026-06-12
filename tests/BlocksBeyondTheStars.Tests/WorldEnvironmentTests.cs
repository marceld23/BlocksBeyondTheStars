using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Day/night clock, weather state and sun colour (World systems).</summary>
public sealed class WorldEnvironmentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorldEnvironmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_env_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "env"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "env", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TimeOfDay_Advances()
    {
        var server = Started(out var repo);
        using (repo)
        {
            float t0 = server.TimeOfDay;
            server.Tick(60.0); // 60s of a (default) 600s day → +0.1
            Assert.True(server.TimeOfDay > t0, $"time should advance ({t0} -> {server.TimeOfDay})");
        }
    }

    [Fact]
    public void SunColour_IsAPlausibleStarColour_AndWeatherValid()
    {
        var server = Started(out var repo);
        using (repo)
        {
            int c = server.SunColor;
            int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            // Sampled from the hot→cool stellar ramp (blue-white → white → yellow → orange → red): always a
            // bright, warm-to-cool star tint — never a dim, green-dominant or near-black "sun".
            Assert.InRange(r, 0xC0, 0xFF);
            Assert.InRange(g, 0xA0, 0xFF);
            Assert.InRange(b, 0x60, 0xFF);
            Assert.Contains(server.Weather, new[] { "clear", "clouds", "rain", "storm" });
        }
    }

    [Fact]
    public void FloraTint_IsAColourfulPlanetHue()
    {
        var server = Started(out var repo);
        using (repo)
        {
            int c = server.FloraTint;
            Assert.NotEqual(0xFFFFFF, c); // a real per-planet flora hue was chosen, not the "no tint" default
            int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
            Assert.True(r + g + b > 60, "the flora hue shouldn't be near-black");
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
