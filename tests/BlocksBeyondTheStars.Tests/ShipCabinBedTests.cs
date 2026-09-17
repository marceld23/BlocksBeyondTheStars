// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The ship cabin's sleeping place (#1941/#1942/#1943). A player report called the quarters marker "the old bed
/// block": station cells stamped as plain cubes, so the bed tile was painted on all six faces. A cabin with room
/// now gets the real two-cell bed, the cramped starter box gets the one-cell crew bunk, and furniture a player
/// builds into a ship keeps the form its placement ghost showed.
/// </summary>
public sealed class ShipCabinBedTests : IDisposable
{
    private readonly string _root;

    public ShipCabinBedTests()
        => _root = Path.Combine(Path.GetTempPath(), "bbts_cabinbed_" + Guid.NewGuid().ToString("N"));

    /// <summary>A server whose (free) starter ship carries the given authored layout, so the served player's
    /// structure builds from it without the blueprint/craft flow — the HammerheadShipTests recipe.</summary>
    private SvGameServer Started(string? layout, out SqliteWorldRepository repo, out GameContent content)
    {
        string dataDir = TestPaths.DataDir();
        if (layout != null)
        {
            dataDir = Path.Combine(_root, "data_" + layout);
            CopyDir(TestPaths.DataDir(), dataDir);
            string shipsPath = Path.Combine(dataDir, "ships.json");
            var ships = JsonSerializer.Deserialize<List<ShipDefinition>>(File.ReadAllText(shipsPath), ContentLoader.JsonOptions)!;
            ships.First(s => s.Key == "starter").Layout = layout;
            File.WriteAllText(shipsPath, JsonSerializer.Serialize(ships, ContentLoader.JsonOptions));
        }

        content = ContentLoader.LoadFromDirectory(dataDir);
        string world = "cabin_" + (layout ?? "box");
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        server.AddLocalPlayer("Host");
        return server;
    }

    [Theory]
    [InlineData("ship_hammerhead")]
    [InlineData("ship_corvette")]
    [InlineData("ship_courier")]
    public void ACabinWithRoom_GetsTheTwoCellBed_BesideItsQuartersMarker(string layout)
    {
        var server = Started(layout, out var repo, out var content);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var quarters = s.StationCells.Single(c => c.Type == "quarters").Cell;
            var bed = content.GetBlock("bed")!.NumericId;

            Assert.Equal(bed, s.Get(quarters));
            int head = s.Shapes[quarters];
            Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(head));

            Assert.True(FurnitureShapes.TryBedPartnerOffset(head, out int dx, out int dz));
            var foot = new Vector3i(quarters.X + dx, quarters.Y, quarters.Z + dz);
            Assert.Equal(bed, s.Get(foot));
            Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(s.Shapes[foot]));

            // The foot half may take neither another station's cell nor the walkway a respawning player lands on.
            Assert.DoesNotContain(s.StationCells, c => c.Cell == foot);
            Assert.False(s.Get(new Vector3i(foot.X, foot.Y - 1, foot.Z)).IsAir, "the foot half needs a floor");
        }
    }

    [Theory]
    [InlineData("ship_hammerhead")]
    [InlineData("ship_corvette")]
    [InlineData("ship_courier")]
    public void TheBedsFootHalf_StaysOutOfEveryDoorwayCorridor(string layout)
    {
        var server = Started(layout, out var repo, out _);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var quarters = s.StationCells.Single(c => c.Type == "quarters").Cell;
            Assert.True(FurnitureShapes.TryBedPartnerOffset(s.Shapes[quarters], out int dx, out int dz));
            var foot = new Vector3i(quarters.X + dx, quarters.Y, quarters.Z + dz);

            // #211: a doorway's contiguous air gap plus the approach rows on each side must stay walkable —
            // the same rule the authored layouts are checked against (ShipStructureTests), applied to the cell
            // the builder chose. Gap columns run along the door's wall axis, the corridor along the other.
            foreach (var door in s.DoorCells)
            {
                bool xJamb = !s.Get(new Vector3i(door.X - 1, door.Y, door.Z)).IsAir
                             || !s.Get(new Vector3i(door.X + 1, door.Y, door.Z)).IsAir;
                bool zJamb = !s.Get(new Vector3i(door.X, door.Y, door.Z - 1)).IsAir
                             || !s.Get(new Vector3i(door.X, door.Y, door.Z + 1)).IsAir;
                bool axisX = door.Z == 0 || (xJamb && !zJamb) || (!(zJamb && !xJamb) && xJamb);
                int[] walk = door.Z == 0 ? new[] { -1, 0, 1, 2 } : new[] { -2, -1, 0, 1, 2 };

                bool Solid(int g) => !s.Get(axisX
                    ? new Vector3i(g, door.Y, door.Z)
                    : new Vector3i(door.X, door.Y, g)).IsAir;

                int centre = axisX ? door.X : door.Z;
                int lo = centre, hi = centre;

                // The gap is scanned on the structure WITHOUT the bed, so the bed itself cannot narrow it.
                for (int step = 1; step <= 3 && !Solid(centre - step) && !InBed(centre - step); step++) { lo = centre - step; }
                for (int step = 1; step <= 3 && !Solid(centre + step) && !InBed(centre + step); step++) { hi = centre + step; }

                bool InBed(int g) => (axisX
                    ? new Vector3i(g, door.Y, door.Z)
                    : new Vector3i(door.X, door.Y, g)) is var probe && (probe == foot || probe == quarters);

                for (int g = lo; g <= hi; g++)
                {
                    foreach (int dw in walk)
                    {
                        var probe = axisX
                            ? new Vector3i(g, door.Y, door.Z + dw)
                            : new Vector3i(door.X + dw, door.Y, g);
                        Assert.NotEqual(probe, foot);
                    }
                }
            }
        }
    }

    [Fact]
    public void TheStarterBox_GetsTheOneCellCrewBunk_BecauseItHasNoRoomForABed()
    {
        var server = Started(layout: null, out var repo, out var content);
        using (repo)
        {
            var s = server.BuildShipStructureForTest("Host");
            var quarters = s.StationCells.Single(c => c.Type == "quarters").Cell;

            Assert.Equal(content.GetBlock("crew_bunk")!.NumericId, s.Get(quarters));
            Assert.False(s.Shapes.ContainsKey(quarters)); // a bunk is its own block, not a shaped bed
        }
    }

    [Fact]
    public void TheCrewBunk_AnswersEForTheHomeSpawn_LikeABed()
    {
        var server = Started(layout: null, out var repo, out var content);
        using (repo)
        {
            var camper = server.AddLocalPlayer("Camper");
            camper.State.AboardShip = false;
            camper.State.Position = new Vector3f(0.5f, 64, 0.5f);
            server.World.SetBlock(new Vector3i(1, 64, 0), content.GetBlock("crew_bunk")!.NumericId);

            server.SetSpawnPoint(camper.State.PlayerId, 1, 64, 0);

            Assert.False(string.IsNullOrEmpty(camper.State.CustomSpawnBodyId), "E on a bunk must arm the home spawn");
        }
    }

    [Fact]
    public void ABedBuiltIntoACabin_KeepsItsFormAcrossARebuild_AndComesOutAsAPair()
    {
        var server = Started(layout: null, out var repo, out var content);
        using (repo)
        {
            var host = server.AddLocalPlayer("Builder");
            host.State.InstantBuild = true; // no materials needed for the test placement
            var (origin, _) = server.LandedShipBoundsForTest("Builder");
            var s = server.BuildShipStructureForTest("Builder");

            // A free floor cell inside the cabin with a free neighbour for the foot half.
            var head = FreeCabinCell(s, out var foot);
            host.State.Position = new Vector3f(origin.X + head.X + 0.5f, origin.Y + head.Y, origin.Z + head.Z + 0.5f);

            server.HandleStructureEditForTest("Builder", new StructureEditIntent
            {
                StructureId = "ship:Builder",
                X = head.X,
                Y = head.Y,
                Z = head.Z,
                Mine = false,
                ItemKey = "bed",
                Yaw = ShapeCode.YawToward(foot.X - head.X, foot.Z - head.Z),
            });

            var built = server.BuildShipStructureForTest("Builder"); // rebuilt from the layout + persisted edits
            Assert.Equal((int)BlockShape.BedHead, ShapeCode.ShapeOf(built.Shapes[head]));
            Assert.Equal((int)BlockShape.BedFoot, ShapeCode.ShapeOf(built.Shapes[foot]));

            // Mining one half takes the other with it.
            server.HandleStructureEditForTest("Builder", new StructureEditIntent
            {
                StructureId = "ship:Builder",
                X = foot.X,
                Y = foot.Y,
                Z = foot.Z,
                Mine = true,
            });

            var after = server.BuildShipStructureForTest("Builder");
            Assert.True(after.Get(head).IsAir);
            Assert.True(after.Get(foot).IsAir);
        }
    }

    /// <summary>A free interior cell with a floor under it and a free horizontal neighbour — where a player
    /// could stand and build a bed.</summary>
    private static Vector3i FreeCabinCell(BlocksBeyondTheStars.GameServer.SpaceStructure s, out Vector3i foot)
    {
        foreach (var cell in AllInteriorCells(s))
        {
            if (!s.Get(cell).IsAir || s.Get(new Vector3i(cell.X, cell.Y - 1, cell.Z)).IsAir)
            {
                continue;
            }

            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = new Vector3i(cell.X + dx, cell.Y, cell.Z + dz);
                if (s.Get(next).IsAir && !s.Get(new Vector3i(next.X, next.Y - 1, next.Z)).IsAir
                    && !s.DoorCells.Contains(cell) && !s.DoorCells.Contains(next))
                {
                    foot = next;
                    return cell;
                }
            }
        }

        throw new InvalidOperationException("no free cabin cell with a free neighbour");
    }

    private static IEnumerable<Vector3i> AllInteriorCells(BlocksBeyondTheStars.GameServer.SpaceStructure s)
    {
        for (int x = 1; x < s.Width - 1; x++)
        {
            for (int z = 1; z < s.Length - 1; z++)
            {
                yield return new Vector3i(x, 1, z);
            }
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(src, dst));
        }

        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(src, dst), overwrite: true);
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
        catch (IOException)
        {
            // a locked save file on Windows must not fail the test run
        }
    }
}
