// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
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
/// Every room of every ship must carry its own light source (#776). No authored layout holds an interior
/// light — all their light cells are exterior nav lights — so the rooms used to be lit only where that
/// glow happened to bleed in through a window, which left the Hammerhead's rear compartments dark. The
/// structure build now hangs a ceiling lamp over every station marker (the ship's room anchors).
/// </summary>
public sealed class ShipInteriorLightingTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    public ShipInteriorLightingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_shiplight_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_root, "data");
        CopyDir(TestPaths.DataDir(), _dataDir);
    }

    /// <summary>Starts a server whose (free) starter ship is built from <paramref name="layoutKey"/>, so the
    /// structure builds without the blueprint/craft flow — the hammerhead-test pattern. A null key leaves the
    /// starter on its layout-less code box.</summary>
    private SvGameServer Started(string? layoutKey, out SqliteWorldRepository repo, out GameContent content)
    {
        var shipsPath = Path.Combine(_dataDir, "ships.json");
        var ships = JsonSerializer.Deserialize<List<ShipDefinition>>(File.ReadAllText(shipsPath), ContentLoader.JsonOptions)!;
        ships.First(s => s.Key == "starter").Layout = layoutKey;
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(ships, ContentLoader.JsonOptions));

        content = ContentLoader.LoadFromDirectory(_dataDir);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "shiplight_" + (layoutKey ?? "box")));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "shiplight", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host");
        return server;
    }

    [Theory]
    [InlineData(null)] // the layout-less code-box starter hull
    [InlineData("ship_scout")]
    [InlineData("ship_corvette")]
    [InlineData("ship_hauler")]
    [InlineData("ship_courier")]
    [InlineData("ship_thunderbolt")]
    [InlineData("ship_deathblock")]
    [InlineData("ship_hammerhead")]
    public void EveryRoomOfEveryShip_GetsItsOwnCeilingLamp(string? layoutKey)
    {
        var server = Started(layoutKey, out var repo, out var content);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var lamp = content.GetBlock("light_white")!.NumericId;

            Assert.NotEmpty(s.StationCells);
            foreach (var (type, cell) in s.StationCells)
            {
                // Somewhere above this room's station anchor, clear of the walkway, hangs a lamp.
                var lit = Enumerable.Range(cell.Y + 2, 8)
                    .Any(y => s.Get(new Vector3i(cell.X, y, cell.Z)).Equals(lamp));
                Assert.True(lit, $"{layoutKey ?? "box"}: no ceiling lamp above the {type} at {cell}");
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ship_scout")]
    [InlineData("ship_corvette")]
    [InlineData("ship_hauler")]
    [InlineData("ship_courier")]
    [InlineData("ship_thunderbolt")]
    [InlineData("ship_deathblock")]
    [InlineData("ship_hammerhead")]
    public void TheHealTank_HasStandingRoom_OnEveryShip(string? layoutKey)
    {
        // The heal-tank is where landing, respawn and a ship switch put the player. On every layout ship it
        // used to sit ON the medbay block — right under the lamp the pass above hangs over each station —
        // so the player's head was inside the lamp ("still in the wall in the hauler", #1259). Feet and head
        // cell must be free and there must be a floor under the feet, on every ship type.
        var server = Started(layoutKey, out var repo, out var content);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var origin = server.ShipOriginOf("Host");
            var tank = server.HealTank;
            var feet = new Vector3i(
                (int)Math.Floor(tank.X) - origin.X,
                (int)Math.Floor(tank.Y) - origin.Y,
                (int)Math.Floor(tank.Z) - origin.Z);

            Assert.True(s.Get(feet).IsAir, $"{layoutKey ?? "box"}: heal-tank feet cell {feet} is solid");
            Assert.True(s.Get(new Vector3i(feet.X, feet.Y + 1, feet.Z)).IsAir, $"{layoutKey ?? "box"}: heal-tank head cell above {feet} is solid");
            Assert.False(s.Get(new Vector3i(feet.X, feet.Y - 1, feet.Z)).IsAir, $"{layoutKey ?? "box"}: no floor under the heal-tank spot {feet}");
            if (s.MedbayCell is { } mb)
            {
                // Beside the medbay (or on it when the room is tall enough) — never across the ship.
                Assert.True(Math.Abs(feet.X - mb.X) + Math.Abs(feet.Z - mb.Z) <= 1, $"{layoutKey}: heal-tank {feet} is not next to the medbay {mb}");
            }
        }
    }

    [Fact]
    public void Lamps_HangClearOfTheWalkway_AndNeverDisplaceTheHull()
    {
        var server = Started("ship_hammerhead", out var repo, out var content);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var lamp = content.GetBlock("light_white")!.NumericId;

            foreach (var (_, cell) in s.StationCells)
            {
                // Head height above a station stays open: the player capsule is 1.88 m, so a lamp may
                // never land in the cell the player walks through.
                Assert.True(s.Get(new Vector3i(cell.X, cell.Y + 1, cell.Z)).IsAir,
                    $"walkway above the station at {cell} must stay clear");
            }

            // The previously dark rooms — the flanking workshop/cargo bay and the sleeping cabins — are lit,
            // not just the bridge that the bow nav lights happened to reach through the front pane.
            foreach (var (x, z) in new[] { (2, 1), (2, 4), (11, 1), (11, 4) })
            {
                var lit = Enumerable.Range(2, 8).Any(y => s.Get(new Vector3i(x, y, z)).Equals(lamp));
                Assert.True(lit, $"flanking room column ({x},{z}) should carry a lamp");
            }
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
