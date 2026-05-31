using System.Linq;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

/// <summary>Day/night clock, weather state and sun colour (World systems).</summary>
public sealed class WorldEnvironmentTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorldEnvironmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_env_" + Guid.NewGuid().ToString("N"));
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
    public void SunColour_IsFromPalette_AndWeatherValid()
    {
        var server = Started(out var repo);
        using (repo)
        {
            int[] palette = { 0xFFF6E8, 0xFFE08A, 0x9FC0FF, 0xFF9E80, 0xFFC070 };
            Assert.Contains(server.SunColor, palette);
            Assert.Contains(server.Weather, new[] { "clear", "clouds", "rain", "storm" });
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
