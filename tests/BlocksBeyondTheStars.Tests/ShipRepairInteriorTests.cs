// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The floating ship interior is a repair point too (R at the helm's cockpit cell, docs/developer/SHIP_REPAIR.md), but
/// the readout the client holds comes from the last landing or login. A hull dented in flight therefore showed no
/// repair panel inside and R at the helm did nothing — so entering the interior pushes a current readout (2026-10-09).
/// </summary>
public sealed class ShipRepairInteriorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shiprepair_int_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void EnteringTheFloatingInterior_PushesACurrentRepairReadout()
    {
        var transport = new NpcLifeWorld.RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shiprepair_int"));
        using (repo)
        {
            var config = new ServerConfig
            {
                WorldName = "shiprepair_int",
                Seed = 1,
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = true,
            };
            config.Rules.FreeSpaceFlight = true;
            var server = new SvGameServer(config, _content, transport, repo);
            server.Start();
            var pilot = server.AddLocalPlayer("Pilot");

            server.EnterSpace("Pilot");
            Assert.True(server.InSpace("Pilot"));

            // Dented after the last readout went out (login / landing) — nothing in flight re-sends it.
            var (_, max) = server.ShipHullForTest("Pilot");
            server.SetShipHullForTest("Pilot", max - 40f);
            int before = SentTo<ShipRepairStatus>(transport, pilot).Count();

            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));

            var readout = SentTo<ShipRepairStatus>(transport, pilot).Skip(before).LastOrDefault();
            Assert.NotNull(readout);
            Assert.True(readout!.NeedsRepair, "the dented hull must read as needing repair at the helm");
            Assert.True(Math.Abs(readout.Hull - (max - 40f)) < 0.01f, $"readout hull {readout.Hull} should be the dented value {max - 40f}");
            Assert.Contains("iron_plate:4", readout.Needs); // 40 hull deficit → ceil(40 / 10) plates
        }
    }

    private static IEnumerable<T> SentTo<T>(NpcLifeWorld.RecordingTransport transport, SvSession who)
        => transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<T>();

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
