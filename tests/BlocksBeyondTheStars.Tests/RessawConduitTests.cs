// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3340: the Ressaw conduit is a crystal conduit in every respect, and a crystal network that holds at least one may grow to
/// <see cref="CrystalNetRules.MaxCellsPerRessawNet"/> (512) cells instead of <see cref="CrystalNetRules.MaxCellsPerNet"/>
/// (256). The cap follows the network's whole shape — not the order its cells were placed or reloaded in. All through the
/// real server (placement, mining, reload).
/// </summary>
public sealed class RessawConduitTests : IDisposable
{
    private const int Y = 200;

    private readonly string _root;
    private readonly GameContent _content;

    public RessawConduitTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_ressaw_conduit_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer NewServer(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "ressaw_net"));
        var config = new ServerConfig { WorldName = "ressaw_net", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Builder(SvGameServer server, params string[] items)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.Position = new Vector3f(0, Y + 3, 0);
        foreach (var key in items)
        {
            Assert.Equal(0, p.State.Inventory.Add(key, 1000, 1024));
        }

        return p;
    }

    /// <summary>Places a block with the builder hovering right above the cell (always within reach).</summary>
    private static void Place(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, int x, int z, string key)
    {
        p.State.Position = new Vector3f(x + 0.5f, Y + 2, z + 0.5f);
        server.PlaceBlock("Builder", x, Y, z, key);
    }

    /// <summary>A flat sheet of conduits, <paramref name="width"/> × <paramref name="depth"/>, placed row by row (with
    /// <paramref name="backwards"/> from the far corner back to the origin) — the Ressaw conduit (if any) at
    /// <paramref name="ressawAt"/>, placed when its turn comes unless <paramref name="skipRessaw"/>.</summary>
    private static void Sheet(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, int width, int depth, (int X, int Z)? ressawAt = null,
        bool skipRessaw = false, bool backwards = false)
    {
        for (int i = 0; i < width * depth; i++)
        {
            int at = backwards ? (width * depth) - 1 - i : i;
            int x = at % width;
            int z = at / width;
            bool ressaw = ressawAt is { } r && r.X == x && r.Z == z;
            if (ressaw && skipRessaw)
            {
                continue;
            }

            Place(server, p, x, z, ressaw ? CrystalNetRules.RessawConduitBlockKey : CrystalNetRules.ConduitBlockKey);
        }
    }

    private static int LargestNet(SvGameServer server) => server.CrystalNetSnapshots.Max(n => n.Cells);

    [Fact]
    public void TheRessawConduit_IsAConduitByKind()
    {
        var def = _content.GetBlock(CrystalNetRules.RessawConduitBlockKey);
        Assert.NotNull(def);
        Assert.Equal(CrystalDeviceKind.Conduit, CrystalNetRules.KindOf(def));
        Assert.Equal(CrystalDeviceKind.Conduit, CrystalNetRules.KindOfKey(CrystalNetRules.RessawConduitBlockKey));
        Assert.True(CrystalNetRules.IsConduitKey(CrystalNetRules.RessawConduitBlockKey));
        Assert.True(CrystalNetRules.IsConduitKey(CrystalNetRules.ConduitBlockKey));
        Assert.False(CrystalNetRules.IsRessawConduitKey(CrystalNetRules.ConduitBlockKey));
        Assert.Equal(512, CrystalNetRules.MaxCellsFor(holdsRessawConduit: true));
        Assert.Equal(256, CrystalNetRules.MaxCellsFor(holdsRessawConduit: false));
    }

    [Fact]
    public void AThreeHundredCellNet_IsCappedAt256_WithoutARessawConduit()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey);
            Sheet(server, p, 20, 15);
            Assert.Equal(300, server.CrystalCellCount); // every conduit registered …
            Assert.Equal(CrystalNetRules.MaxCellsPerNet, LargestNet(server)); // … but the network stops at 256
        }
    }

    [Fact]
    public void AThreeHundredCellNet_Works_WithOneRessawConduit()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey, CrystalNetRules.RessawConduitBlockKey);
            Sheet(server, p, 20, 15, ressawAt: (0, 0));
            Assert.Single(server.CrystalNetSnapshots);
            Assert.Equal(300, LargestNet(server));
        }
    }

    [Fact]
    public void ASixHundredCellNet_IsCappedAt512_EvenWithARessawConduit()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey, CrystalNetRules.RessawConduitBlockKey);
            Sheet(server, p, 30, 20, ressawAt: (0, 0));
            Assert.Equal(600, server.CrystalCellCount);
            Assert.Equal(CrystalNetRules.MaxCellsPerRessawNet, LargestNet(server));
        }
    }

    [Fact]
    public void ARessawConduitLaidIntoAFullNet_LetsTheCellsPlacedOverTheCapJoin()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey, CrystalNetRules.RessawConduitBlockKey);
            Sheet(server, p, 20, 15, ressawAt: (19, 14), skipRessaw: true); // 299 conduits: 256 in, 43 inert
            Assert.Equal(CrystalNetRules.MaxCellsPerNet, LargestNet(server));

            Place(server, p, 19, 14, CrystalNetRules.RessawConduitBlockKey); // the far corner: only inert cells beside it
            Assert.Single(server.CrystalNetSnapshots);
            Assert.Equal(300, LargestNet(server));
        }
    }

    [Fact]
    public void MiningTheLastRessawConduit_TrimsTheNetBackTo256_AndLayingItAgainRestoresIt()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey, CrystalNetRules.RessawConduitBlockKey);
            Sheet(server, p, 20, 15, ressawAt: (19, 14), backwards: true); // grown out of the Ressaw conduit
            Assert.Equal(300, LargestNet(server));

            p.State.Position = new Vector3f(19.5f, Y + 2, 14.5f);
            server.MineBlock("Builder", 19, Y, 14);
            Assert.Equal(299, server.CrystalCellCount);
            Assert.Equal(CrystalNetRules.MaxCellsPerNet, LargestNet(server)); // the rest went inert, as if placed over the cap

            Place(server, p, 19, 14, CrystalNetRules.RessawConduitBlockKey);
            Assert.Equal(300, LargestNet(server));
        }
    }

    [Fact]
    public void ARessawNet_KeepsAllItsCells_AfterAReload_WhereverItsRessawConduitSits()
    {
        var server = NewServer(out var repo1);
        using (repo1)
        {
            var p = Builder(server, CrystalNetRules.ConduitBlockKey, CrystalNetRules.RessawConduitBlockKey);
            Sheet(server, p, 20, 15, ressawAt: (19, 14), backwards: true); // placed first — but its row comes back last
            Assert.Equal(300, LargestNet(server));
            var row = repo1.ListCrystalCells(server.ActiveLocationId).Single(r => r.X == 19 && r.Z == 14);
            Assert.Equal("key=" + CrystalNetRules.RessawConduitBlockKey, row.Config); // the row remembers WHICH conduit it is
        }

        var reloaded = NewServer(out var repo2);
        using (repo2)
        {
            Builder(reloaded);
            Assert.Equal(300, reloaded.CrystalCellCount);
            Assert.Single(reloaded.CrystalNetSnapshots);
            Assert.Equal(300, LargestNet(reloaded));
        }
    }

    [Fact]
    public void ARessawConduit_CarriesOnAndOff_LikeACrystalConduit()
    {
        var server = NewServer(out var repo);
        using (repo)
        {
            var p = Builder(server, "crystal_switch", CrystalNetRules.RessawConduitBlockKey, "light_white");
            Place(server, p, 1, 0, "crystal_switch");
            Place(server, p, 2, 0, CrystalNetRules.RessawConduitBlockKey);
            Place(server, p, 3, 0, "light_white"); // placed beside a net cell → it joins
            Assert.Single(server.CrystalNetSnapshots);
            Assert.Null(server.CrystalDeviceOutput(new Vector3i(2, Y, 0))); // a wire, not a device

            Ticks(server, 0.7);
            Assert.False(server.CrystalLevelAt(new Vector3i(2, Y, 0)));
            Assert.Equal("light_white_off", KeyAt(server, 3, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, Y, 0), action: 0); // the lever ON
            Ticks(server, 0.7);
            Assert.True(server.CrystalLevelAt(new Vector3i(2, Y, 0))); // the network is ON: the client glows its cells
            Assert.Equal("light_white", KeyAt(server, 3, 0));

            server.SetCrystalDeviceForTest(p, new Vector3i(1, Y, 0), action: 0); // and OFF again
            Ticks(server, 0.7);
            Assert.False(server.CrystalLevelAt(new Vector3i(2, Y, 0)));
            Assert.Equal("light_white_off", KeyAt(server, 3, 0));
        }
    }

    private static void Ticks(SvGameServer server, double seconds)
    {
        for (double t = 0; t < seconds; t += 0.1)
        {
            server.TickForTest(0.1);
        }
    }

    private string KeyAt(SvGameServer server, int x, int z) => _content.BlockById(server.World.GetBlock(new Vector3i(x, Y, z)))?.Key ?? "air";
}
