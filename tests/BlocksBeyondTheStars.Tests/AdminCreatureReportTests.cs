// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary><c>/creatures</c> (#1489): the footing readout names every animal nearby with its feet, the real
/// ground of its column and the delta — and says plainly when nothing is in range.</summary>
public sealed class AdminCreatureReportTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AdminCreatureReportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_creaturereport_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string name)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 11,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        config.Rules.CreatureAbundance = AlienActivity.Frequent; // fauna arrives quickly
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void CreaturesReport_ListsFeetGroundAndDelta_OrSaysNothingIsNear()
    {
        var server = Start(out var repo, "creatures");
        using (repo)
        {
            var admin = server.AddLocalPlayer("Admin");
            admin.State.AboardShip = false;

            for (int i = 0; i < 400 && server.Creatures.Count == 0; i++)
            {
                server.Tick(0.5);
            }

            Assert.NotEmpty(server.Creatures);

            // Stand next to the first animal: one head line + one row per animal in range, each with the numbers.
            var first = server.Creatures[0];
            admin.State.Position = new Vector3f(first.Position.X + 2f, first.Position.Y, first.Position.Z);
            var lines = server.CreaturesReportForTest(admin);
            Assert.True(lines.Count >= 2, string.Join("\n", lines));
            Assert.Contains("within 48 blocks", lines[0]);
            Assert.Contains("feet ", lines[1]);
            Assert.Contains("ground ", lines[1]);
            Assert.Contains("delta ", lines[1]);

            // Far away from every animal: the report says so in one line instead of listing nothing.
            admin.State.Position = new Vector3f(first.Position.X + 900f, first.Position.Y, first.Position.Z + 900f);
            var none = server.CreaturesReportForTest(admin);
            Assert.Single(none);
            Assert.Contains("No creatures", none[0]);
        }
    }
}
