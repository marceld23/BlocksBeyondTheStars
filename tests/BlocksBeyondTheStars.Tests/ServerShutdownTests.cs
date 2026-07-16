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
/// Shutdown-request semantics (issue #243): the SIGINT handler is registered before Start(), so a stop
/// can be requested while startup worldgen is still running — Run() must honor that pre-latched request
/// and go straight to the drain + save instead of re-arming the loop and running forever.
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)] // real-time Run() loops starve in the parallel suite
public sealed class ServerShutdownTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ServerShutdownTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shutdown_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shutdown"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "shutdown", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public async Task RequestStop_BeforeRun_DrainsAndReturnsImmediatelyAsync()
    {
        var server = Started(out var repo);
        using (repo)
        {
            // The SIGINT-during-startup case: the stop request lands before Run() begins.
            server.RequestStop();

            var run = Task.Run(server.Run);
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));

            Assert.Same(run, finished); // Run() must not re-arm the loop over the latched request
            await run; // propagate any Run/Shutdown exception into the test
        }
    }

    [Fact]
    public async Task RequestStop_DuringRun_StopsTheLoopAsync()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var run = Task.Run(server.Run);
            await Task.Delay(300); // let the loop tick (if it hasn't started yet, the latched path covers it)
            server.RequestStop();

            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));

            Assert.Same(run, finished);
            await run;
        }
    }
}
