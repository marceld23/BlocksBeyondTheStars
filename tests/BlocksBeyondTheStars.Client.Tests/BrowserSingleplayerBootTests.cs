// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Boot parity for the browser singleplayer: the EXACT ServerConfig the client's
/// <c>BrowserLocalServer</c> builds must start on the memory repository — on several seeds, since
/// worldgen paths (settlements, wrecks, starter ship) vary per seed. Guards the WebGL path with a
/// full-CLR stack trace instead of an IL2CPP one-liner when something in Start() regresses.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class BrowserSingleplayerBootTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_spboot_" + Guid.NewGuid().ToString("N"));

    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Theory]
    [InlineData(1234567L)]
    [InlineData(987654321L)]
    [InlineData(42L)]
    public void BrowserConfig_StartsOnTheMemoryRepository(long seed)
    {
        var content = LoadContent();
        var config = new ServerConfig
        {
            WorldName = "browser",
            Seed = seed,
            MaxPlayers = 1,
            EnableWebSocket = false,
            IdleShutdownMinutes = 0,
            AiLevel = AiLevel.Off,
        };

        using var repo = new MemoryWorldRepository(new SaveGamePaths(_root, config.WorldName + "_" + seed));
        var link = new LoopbackLink();
        var server = new SvGameServer(config, content, new LoopbackServerTransport(link), repo);
        try
        {
            server.Start();
            server.Tick(0.1);
        }
        finally
        {
            server.Stop();
        }
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
        catch
        {
            // best effort — temp cleanup only
        }
    }
}
