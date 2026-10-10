// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Settlement-only test servers for the settlement suites (#2456). They start with settlements and nothing that is
/// stamped after them: the intercity rail, ruins, bandit camps, monuments, factories, vaults, chests and wrecks only
/// read the settlement list, so the settlements, their footprints and the landing pads come out exactly as on a full
/// world, at a fraction of the boot (1–3.5 s less per server, and these suites start hundreds).
/// </summary>
internal sealed class SettlementTestWorlds : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_settle_" + Guid.NewGuid().ToString("N"));

    public SvGameServer Start(GameContent content, string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + seed));
        var config = new ServerConfig
        {
            WorldName = planet + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceIntercityRail = false,
            PlaceRuins = false,
            PlaceRailRuins = false,
            PlaceBanditCamps = false,
            PlaceMonuments = false,
            PlaceFactories = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceChests = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort: a temp folder left behind is harmless
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
