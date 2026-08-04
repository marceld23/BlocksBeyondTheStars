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
/// The courier, thunderbolt and deathblock are the three ships built from hand-drawn floor plans
/// (#727, #728, #729). Each is a multi-room layout: every drawn room must keep its shape, all
/// doors and stations must register, and (deathblock) the floor-fill guarantee must not grow
/// tiles into the notches of the stepped silhouette or under the flank attachments.
/// </summary>
public abstract class DrawnShipTestsBase : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    protected DrawnShipTestsBase(string layoutKey)
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_drawn_" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_root, "data");
        CopyDir(TestPaths.DataDir(), _dataDir);

        // Give the (free) starter ship the layout under test so the served player's structure
        // builds from it without needing the blueprint/craft flow (the hammerhead-test pattern).
        var shipsPath = Path.Combine(_dataDir, "ships.json");
        var ships = JsonSerializer.Deserialize<List<ShipDefinition>>(File.ReadAllText(shipsPath), ContentLoader.JsonOptions)!;
        ships.First(s => s.Key == "starter").Layout = layoutKey;
        File.WriteAllText(shipsPath, JsonSerializer.Serialize(ships, ContentLoader.JsonOptions));
    }

    protected SvGameServer Started(out SqliteWorldRepository repo)
    {
        var content = ContentLoader.LoadFromDirectory(_dataDir);
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "drawn"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = "drawn", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host");
        return server;
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

public sealed class CourierShipTests : DrawnShipTestsBase
{
    public CourierShipTests() : base("ship_courier") { }

    [Fact]
    public void CourierStructure_RegistersBothDoors_AndAllRoomStations()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            Assert.Equal(5, s.Width);
            Assert.Equal(9, s.Length);

            // Stern airlock + the control-cabin door at the z=5 partition.
            Assert.Equal(2, s.DoorCells.Count);
            Assert.Contains(new Vector3i(2, 1, 0), s.DoorCells);
            Assert.Contains(new Vector3i(2, 1, 5), s.DoorCells);

            // Helm in the cabin; quarters/cargo/medbay in the living room. No cannons — the courier
            // flies with just the basic laser (a mining tool), per the drawn design.
            var stationTypes = s.StationCells.Select(c => c.Type).ToHashSet();
            foreach (var type in new[] { "cockpit", "quarters", "cargo", "medbay" })
            {
                Assert.Contains(type, stationTypes);
            }

            Assert.NotNull(s.MedbayCell);
        }
    }
}

public sealed class ThunderboltShipTests : DrawnShipTestsBase
{
    public ThunderboltShipTests() : base("ship_thunderbolt") { }

    [Fact]
    public void ThunderboltStructure_RegistersBothDoors_AndAllRoomStations()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            Assert.Equal(9, s.Width);
            Assert.Equal(11, s.Length);

            // Stern airlock into the workshop hall + the interior door up to the bridge.
            Assert.Equal(2, s.DoorCells.Count);
            Assert.Contains(new Vector3i(4, 1, 0), s.DoorCells);
            Assert.Contains(new Vector3i(4, 1, 7), s.DoorCells);

            var stationTypes = s.StationCells.Select(c => c.Type).ToHashSet();
            foreach (var type in new[] { "cockpit", "console", "workshop", "cargo", "quarters", "medbay" })
            {
                Assert.Contains(type, stationTypes);
            }

            Assert.NotNull(s.MedbayCell);
        }
    }

    [Fact]
    public void ThunderboltStructure_KeepsTheBridgeInset_NoFloorBesideTheBridge()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");

            // The bridge (z=8..10) is inset one column from each flank: the bounding-rect strips
            // beside it (x=0 and x=8 at bridge depth) must stay empty — no floor-fill growth.
            foreach (var (x, z) in new[] { (0, 8), (0, 9), (8, 8), (8, 9) })
            {
                for (int y = 0; y <= s.Height; y++)
                {
                    Assert.True(s.Get(new Vector3i(x, y, z)).IsAir, $"bridge notch cell ({x},{y},{z}) should be air");
                }
            }
        }
    }
}

public sealed class DeathblockShipTests : DrawnShipTestsBase
{
    public DeathblockShipTests() : base("ship_deathblock") { }

    [Fact]
    public void DeathblockStructure_RegistersThreeDoors_AndAllRoomStations()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            Assert.Equal(11, s.Width);
            Assert.Equal(12, s.Length);

            // Stern airlock + one interior door into each forward room.
            Assert.Equal(3, s.DoorCells.Count);
            Assert.Contains(new Vector3i(5, 1, 0), s.DoorCells);   // stern (rear wall — the forced-X hatch)
            Assert.Contains(new Vector3i(2, 1, 7), s.DoorCells);   // hall -> sleeping quarters
            Assert.Contains(new Vector3i(7, 1, 7), s.DoorCells);   // hall -> control room

            var stationTypes = s.StationCells.Select(c => c.Type).ToHashSet();
            foreach (var type in new[] { "cockpit", "console", "workshop", "cargo", "quarters", "medbay" })
            {
                Assert.Contains(type, stationTypes);
            }

            Assert.NotNull(s.MedbayCell);
        }
    }

    [Fact]
    public void DeathblockStructure_KeepsTheSteppedSilhouette_NoFloorInTheNotchesOrUnderAttachments()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");

            // The aft hall is inset one column from each flank (x=1..9); the forward rooms overhang
            // to x=0/x=10. The aft flank strips carry only attachments (stub wing z=1, cannons z=4/5)
            // — the strips between them must stay fully empty.
            foreach (var (x, z) in new[] { (0, 2), (0, 3), (0, 6), (10, 2), (10, 3), (10, 6) })
            {
                for (int y = 0; y <= s.Height; y++)
                {
                    Assert.True(s.Get(new Vector3i(x, y, z)).IsAir, $"notch cell ({x},{y},{z}) should be air");
                }
            }

            // The flank cannons and stub wings are pure attachments: the cell itself is solid,
            // but no floor tile may grow underneath it.
            foreach (var (x, z) in new[] { (0, 1), (0, 4), (0, 5), (10, 1), (10, 4), (10, 5) })
            {
                Assert.False(s.Get(new Vector3i(x, 2, z)).IsAir, $"attachment cell ({x},2,{z}) should be solid");
                Assert.True(s.Get(new Vector3i(x, 0, z)).IsAir, $"no floor may grow under attachment ({x},0,{z})");
            }
        }
    }
}
