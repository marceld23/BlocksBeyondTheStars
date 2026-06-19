using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The /bump debug command writes a persisted diagnostic snapshot (no comm radio needed), and the
/// screenshot variant additionally drops a JPG alongside it.</summary>
public sealed class BumpTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    // The server may route bumps to a shared <repo>/bugreports/server folder (when tests run inside the
    // working tree), so each test uses a unique world name and only ever touches its own files.
    private readonly string _world = "bumpworld_" + Guid.NewGuid().ToString("N");
    private string _bumpDir;
    private SqliteWorldRepository _repo;
    private LoopbackServerTransport _st;
    private LoopbackClientTransport _client;

    public BumpTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_bump_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void BumpCommand_WritesPersistedSnapshot()
    {
        var (server, client, paths) = StartWorld();

        Assert.Equal(0, server.BumpsWritten);

        // /bump works without a comm radio (it's intercepted before the chat gate).
        client.Send(NetCodec.Encode(new ChatIntent { Text = "/bump the ship interior glitches with flora" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);

        var files = MyBumpFiles(paths, "*.json");
        Assert.Single(files);

        string json = File.ReadAllText(files[0]);
        Assert.Contains("the ship interior glitches with flora", json); // description captured
        Assert.Contains("environment", json);                            // env snapshot present
    }

    [Fact]
    public void BumpReport_WithScreenshot_WritesJpgAlongsideSnapshot()
    {
        var (server, client, paths) = StartWorld();

        var image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5 }; // stand-in JPG bytes
        client.Send(NetCodec.Encode(new BumpReport { Description = "ufo wreck cannot be opened", Image = image }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(1, server.BumpsWritten);

        var jsonFiles = MyBumpFiles(paths, "*.json");
        var jpgFiles = MyBumpFiles(paths, "*.jpg");
        Assert.Single(jsonFiles);
        Assert.Single(jpgFiles);

        Assert.Equal(image, File.ReadAllBytes(jpgFiles[0]));

        string json = File.ReadAllText(jsonFiles[0]);
        Assert.Contains("ufo wreck cannot be opened", json);
        // The json references its screenshot file (same stem) so a dev can pair them.
        Assert.Contains(Path.GetFileName(jpgFiles[0]), json);
    }

    private (SvGameServer server, LoopbackClientTransport client, SaveGamePaths paths) StartWorld()
    {
        var paths = new SaveGamePaths(_root, _world);
        _repo = new SqliteWorldRepository(paths);
        var link = new LoopbackLink();
        _st = new LoopbackServerTransport(link);
        _client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = _world, Seed = 1, AutoSaveIntervalMinutes = 9999 };

        var server = new SvGameServer(config, _content, _st, _repo);
        server.Start();
        _client.Connect("loopback", 0);
        _client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Tester" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // Resolve the directory exactly as the server does, so the test works whether it lands in the repo's
        // bugreports folder or the per-world fallback.
        _bumpDir = BugReportPaths.Resolve(Path.Combine(paths.WorldDirectory, "bumps"));
        return (server, _client, paths);
    }

    private string[] MyBumpFiles(SaveGamePaths paths, string suffix)
        => Directory.Exists(_bumpDir)
            ? Directory.GetFiles(_bumpDir, $"bump_{_world}_{suffix}")
            : Array.Empty<string>();

    public void Dispose()
    {
        try
        {
            _client?.Dispose();
            _st?.Dispose();
            _repo?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Remove only this test's own bump files (the dir may be the shared repo bugreports folder).
            if (_bumpDir != null && Directory.Exists(_bumpDir))
            {
                foreach (var f in Directory.GetFiles(_bumpDir, $"bump_{_world}_*"))
                {
                    File.Delete(f);
                }
            }

            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
