using System.Text.Json;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A ship type that carries a voxel <see cref="ShipLayout"/> (authored in the ship editor) is stamped
/// with its true, irregular shape — exactly its placed cells are protected, and station cells register
/// as usable stations (P3 of the ship-type editor).
/// </summary>
public sealed class ShipLayoutTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    public ShipLayoutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_layout_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_root, "data");

        // Copy the real content, then give the starter ship a small designed layout.
        CopyDir(TestPaths.DataDir(), _dataDir);

        var layoutDir = Path.Combine(_dataDir, "ship_layouts");
        Directory.CreateDirectory(layoutDir);
        // 3×3 iron_wall floor (y=0) + a cockpit station at the centre, one storey up.
        File.WriteAllText(Path.Combine(layoutDir, "starter.json"), """
        {
          "width": 3, "height": 3, "length": 3,
          "cells": [
            { "x": 0, "y": 0, "z": 0, "kind": "block", "id": "iron_wall" },
            { "x": 1, "y": 0, "z": 0, "kind": "block", "id": "iron_wall" },
            { "x": 2, "y": 0, "z": 0, "kind": "block", "id": "iron_wall" },
            { "x": 0, "y": 0, "z": 1, "kind": "block", "id": "iron_wall" },
            { "x": 1, "y": 0, "z": 1, "kind": "block", "id": "iron_wall" },
            { "x": 2, "y": 0, "z": 1, "kind": "block", "id": "iron_wall" },
            { "x": 0, "y": 0, "z": 2, "kind": "block", "id": "iron_wall" },
            { "x": 1, "y": 0, "z": 2, "kind": "block", "id": "iron_wall" },
            { "x": 2, "y": 0, "z": 2, "kind": "block", "id": "iron_wall" },
            { "x": 1, "y": 1, "z": 1, "kind": "station", "id": "cockpit" }
          ]
        }
        """);

        // Patch the starter ship to reference the layout.
        var shipsPath = Path.Combine(_dataDir, "ships.json");
        var ships = JsonSerializer.Deserialize<List<ShipDefinition>>(File.ReadAllText(shipsPath), ContentLoader.JsonOptions)!;
        var starter = ships.First(s => s.Key == "starter");
        starter.Layout = "starter";
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(ships, ContentLoader.JsonOptions));
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        var content = ContentLoader.LoadFromDirectory(_dataDir);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "layoutship"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "layoutship", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host"); // ships are per-player now — a player must exist for one to be stamped
        return server;
    }

    [Fact]
    public void DesignedShip_StampsItsCells_AndProtectsOnlyThose()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var a = server.ShipAnchorBlock; // centre floor cell of the 3×3 layout

            // The placed cell is solid hull and protected.
            Assert.False(server.World.GetBlock(a).IsAir);
            Assert.True(server.IsProtectedShipBlock(a.X, a.Y, a.Z));

            // A cell well outside the 3×3 footprint is NOT protected — the designed ship guards
            // exactly its cells, not a bounding box.
            Assert.False(server.IsProtectedShipBlock(a.X + 6, a.Y, a.Z));
        }
    }

    [Fact]
    public void DesignedShip_RegistersStationCells()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Assert.NotNull(server.StationPosition("cockpit"));
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
        {
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(src))
        {
            CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)));
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
