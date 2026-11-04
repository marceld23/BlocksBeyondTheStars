// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3172 (Lyxette 2026-11-04: "I can't give the bridge motors names — that doesn't work on the lift either"): the name a
/// player gives a Crystal Net device. The menu's name field sends the name alone (action 7) on Enter, on leaving the field
/// and on close, so an emptied field clears it; the configure click (action 2), which an older client sends with the field's
/// text every time, keeps a name it gets no new one for. The name is saved with the device's row and survives a reload.
/// </summary>
public sealed class CrystalDeviceNameTests : IDisposable
{
    private static readonly Vector3i Bridge = new(1, 200, 0);

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_crystalname_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private (SvGameServer Server, BlocksBeyondTheStars.GameServer.PlayerSession Player) Server(SqliteWorldRepository repo)
    {
        var config = new ServerConfig { WorldName = "crystalname", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, 200, 0); // up in the air → the cells are empty + within reach
        Assert.Equal(0, p.State.Inventory.Add("bridge_motor", 4, 1024));
        return (server, p);
    }

    private SqliteWorldRepository Repo() => new(new SaveGamePaths(_root, "crystalname"));

    [Fact]
    public void TheNameAlone_SetsAndClearsIt_TheConfigureClickKeepsIt()
    {
        using var repo = Repo();
        var (server, p) = Server(repo);
        server.PlaceBlock("Builder", Bridge.X, Bridge.Y, Bridge.Z, "bridge_motor");
        Assert.Equal(string.Empty, server.CrystalDeviceLabelForTest(Bridge));

        server.SetCrystalDeviceForTest(p, Bridge, action: 7, label: "North drawbridge");
        Assert.Equal("North drawbridge", server.CrystalDeviceLabelForTest(Bridge));

        server.SetCrystalDeviceForTest(p, Bridge, action: 2, config: "len=4", label: ""); // a click in the menu: the name stays
        Assert.Equal("North drawbridge", server.CrystalDeviceLabelForTest(Bridge));

        server.SetCrystalDeviceForTest(p, Bridge, action: 7, label: ""); // the field emptied: the name goes
        Assert.Equal(string.Empty, server.CrystalDeviceLabelForTest(Bridge));
    }

    [Fact]
    public void AName_SurvivesAReload()
    {
        string loc;
        using (var repo = Repo())
        {
            var (server, p) = Server(repo);
            server.PlaceBlock("Builder", Bridge.X, Bridge.Y, Bridge.Z, "bridge_motor");
            server.SetCrystalDeviceForTest(p, Bridge, action: 7, label: "Moat");
            loc = server.ActiveLocationId;
        }

        using (var repo = Repo())
        {
            var (server, _) = Server(repo);
            Assert.Equal("Moat", server.CrystalDeviceLabelForTest(Bridge));
            Assert.Equal("Moat", repo.ListCrystalCells(loc).Single(r => r.Kind == "BridgeMotor").Label);
        }
    }
}
