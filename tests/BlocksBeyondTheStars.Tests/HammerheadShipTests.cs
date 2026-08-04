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
/// The hammerhead is the first multi-room ship layout: a T-shaped hull (wide bridge, central corridor,
/// two flanking rooms) with interior doors. Its structure must keep the T-shape — in particular the
/// floor-fill guarantee must not grow floor tiles in the notches of the bounding rect — and all four
/// doors and six stations must register.
/// </summary>
public sealed class HammerheadShipTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    public HammerheadShipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hammer_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_root, "data");
        CopyDir(TestPaths.DataDir(), _dataDir);

        // Give the (free) starter ship the real hammerhead layout so the served player's structure
        // builds from it without needing the blueprint/craft flow.
        var shipsPath = Path.Combine(_dataDir, "ships.json");
        var ships = JsonSerializer.Deserialize<List<ShipDefinition>>(File.ReadAllText(shipsPath), ContentLoader.JsonOptions)!;
        ships.First(s => s.Key == "starter").Layout = "ship_hammerhead";
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(ships, ContentLoader.JsonOptions));
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        var content = ContentLoader.LoadFromDirectory(_dataDir);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "hammer"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "hammer", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host");
        return server;
    }

    [Fact]
    public void HammerheadStructure_KeepsTheTShape_NoFloorInTheBoundingRectNotches()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            Assert.Equal(14, s.Width);
            Assert.Equal(15, s.Length);

            // Floor exists across the real footprint: workshop, corridor and bridge.
            Assert.False(s.Get(new Vector3i(2, 0, 2)).IsAir);   // workshop
            Assert.False(s.Get(new Vector3i(6, 0, 7)).IsAir);   // corridor
            Assert.False(s.Get(new Vector3i(6, 0, 12)).IsAir);  // bridge
            Assert.False(s.Get(new Vector3i(11, 0, 3)).IsAir);  // sleeping cabins

            // The notches of the bounding rect (beside the corridor, beside the bridge) stay empty —
            // the floor-fill guarantee must not stamp floating tiles there.
            foreach (var (x, z) in new[] { (0, 8), (2, 7), (4, 9), (9, 9), (11, 7), (13, 8), (0, 14), (13, 14) })
            {
                for (int y = 0; y <= s.Height; y++)
                {
                    Assert.True(s.Get(new Vector3i(x, y, z)).IsAir, $"notch cell ({x},{y},{z}) should be air");
                }
            }

            // The nav lights hang on the bridge flanks as pure attachments: the light cell itself is
            // solid, but no floor tile may grow underneath it.
            Assert.False(s.Get(new Vector3i(0, 2, 12)).IsAir);
            Assert.True(s.Get(new Vector3i(0, 0, 12)).IsAir);
            Assert.False(s.Get(new Vector3i(13, 2, 12)).IsAir);
            Assert.True(s.Get(new Vector3i(13, 0, 12)).IsAir);
        }
    }

    [Fact]
    public void HammerheadStructure_RegistersFourDoors_AndAllRoomStations()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");

            // Stern airlock + bridge door + workshop door + sleeping-cabin door.
            Assert.Equal(4, s.DoorCells.Count);
            Assert.Contains(new Vector3i(6, 1, 0), s.DoorCells);   // stern (rear wall — the forced-X hatch)
            Assert.Contains(new Vector3i(6, 1, 10), s.DoorCells);  // corridor -> bridge (faces Z)
            Assert.Contains(new Vector3i(5, 1, 2), s.DoorCells);   // corridor -> workshop (faces X)
            Assert.Contains(new Vector3i(8, 1, 3), s.DoorCells);   // corridor -> sleeping cabins (faces X)

            // Every drawn room got its jobs; the medbay in the sleeping cabins is the spawn/heal anchor.
            var stationTypes = s.StationCells.Select(c => c.Type).ToHashSet();
            foreach (var type in new[] { "cockpit", "console", "workshop", "cargo", "quarters", "medbay" })
            {
                Assert.Contains(type, stationTypes);
            }

            Assert.NotNull(s.MedbayCell);
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
