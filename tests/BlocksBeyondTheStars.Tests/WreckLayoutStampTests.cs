// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3521 on the server: a generation-38 planet wreck is built from a ship layout, recorded with its ship and the layout's
/// fingerprint, repairable to the end and claimable; a recorded layout that is gone or changed keeps the blocks and turns
/// repair and claim off; a generation-37 world keeps the classic box.
/// </summary>
public sealed class WreckLayoutStampTests : IDisposable
{
    private const int Gen37 = WorldDescription.LandmarkGeneration;
    private const int Gen38 = WorldDescription.StructurePipelineGeneration;
    private const string Planet = "rocky";

    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_wrecklayout_" + Guid.NewGuid().ToString("N"));

    private SvGameServer Boot(string name, long seed, int generation, out SqliteWorldRepository repo, GameContent? content = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = Planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceRailRuins = false,
            PlaceChests = false,
            PlaceVaults = false,
            PlaceDataCubes = false,
            PlaceMonuments = false,
            PlaceFactories = false,
            PlaceBanditCamps = false,
            PlaceLodges = false,
            PlacePioneers = false,
            PlaceLandmarks = false,
            PlaceWrecks = true,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, content ?? Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The first seed whose start body has a wreck, booted (the caller stops it and disposes the repository).</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, string Name, long Seed) WithWreck(string tag, int generation)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            string name = $"{tag}_{seed}";
            var server = Boot(name, seed, generation, out var repo);
            if (server.HasWreck)
            {
                return (server, repo, name, seed);
            }

            server.Stop();
            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No wreck on '{Planet}' across 60 seeds.");
    }

    private void EditMetadata(string name, Action<WorldMetadata> edit)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        repo.Initialize();
        var meta = repo.LoadMetadata()!;
        edit(meta);
        repo.SaveMetadata(meta);
    }

    private static string KeyOf(ushort id) => Content.BlockById(new BlockId(id))?.Key ?? "?";

    private static bool Placeable(string blockKey) => Content.Items.Values.Any(i => i.PlacesBlock == blockKey);

    [Fact]
    public void AFreshWreck_IsBuiltFromALayout_StampedWithItsInsideClear_AndRecordedWithItsShipAndFingerprint()
    {
        var (server, repo, _, _) = WithWreck("fresh", Gen38);
        using (repo)
        {
            var w = server.WreckForTest!;
            Assert.True(w.FromLayout);
            var design = Content.GetShip(w.ShipType)!;
            var layout = Content.GetShipLayout(design.Layout)!;

            var rec = server.PlacementRecordsForTest.Single(r => r.Kind == "wreck");
            Assert.Equal(w.ShipType, rec.Template);
            Assert.Equal(w.ShipType, rec.Design);
            var pin = MarkerSettings.Parse(rec.KitLayout);
            Assert.Equal("layout", pin.Get("src"));
            Assert.Equal(WreckGenerator.LayoutRevision, pin.GetInt("rev", 0));
            Assert.Equal(WreckGenerator.LayoutFingerprint(layout), pin.Get("fp"));

            var o = server.WreckOriginForTest;
            Assert.Equal((o.X, o.Y + 1, o.Z), (rec.X, rec.GroundY, rec.Z)); // the hull is sunk one block into the ground
            for (int x = 0; x < w.Width; x++)
                for (int y = 0; y < w.Height; y++)
                    for (int z = 0; z < w.Length; z++)
                    {
                        var cell = new Vector3i(o.X + x, o.Y + y, o.Z + z);
                        if (w.Get(x, y, z) != 0)
                        {
                            Assert.Equal(w.Get(x, y, z), server.World.GetBlock(cell).Value);
                        }
                        else if (w.IsInterior(x, y, z))
                        {
                            Assert.True(server.World.GetBlock(cell).IsAir, $"the inside of the hull is clear, not {KeyOf(server.World.GetBlock(cell).Value)}");
                        }
                    }

            Assert.Contains(server.WreckMarkers, m => m.Type == "loot");
            Assert.Contains(server.WreckMarkers, m => m.Type == "module");
            server.Stop();
        }
    }

    [Fact]
    public void EveryLayoutWreck_IsTheFlightHull_MinusTheDecay()
    {
        string[] accents = { "medbay_panel", "lab_panel", "cargo_floor", "engine_panel", "metal_panel" };
        var server = Boot("flight", 1, Gen38, out var repo);
        using (repo)
        {
            foreach (var design in Content.Ships.Values.Where(s => Content.GetShipLayout(s.Layout) is not null))
            {
                var layout = Content.GetShipLayout(design.Layout)!;
                var flight = server.BuildNpcShipStructureForTest(design.Key);
                var w = WreckGenerator.GenerateFromLayout(design, layout, 77, Content);
                var o = w.LayoutOffset;
                string hull = w.Origin == "alien" ? "crystal" : "iron_wall";
                var layoutCells = layout.Cells.Select(c => new Vector3i(c.X, c.Y, c.Z))
                    .Concat(ShipCellBlocks.FloorFootprint(layout).Select(c => new Vector3i(c.X, 0, c.Z))).ToHashSet();
                foreach (var p in layoutCells)
                {
                    string flown = KeyOf(flight.Get(p).Value);
                    ushort intact = w.IntactAt(p.X - o.X, p.Y - o.Y, p.Z - o.Z);
                    if (flight.Get(p).IsAir)
                    {
                        Assert.Equal((ushort)0, intact); // a doorway or a hatch: open in both
                        continue;
                    }

                    if (accents.Contains(flown))
                    {
                        continue; // the flyable ship paints its room floors; the wreck keeps the plain hull there
                    }

                    string expected = !Placeable(flown) || flown == "iron_wall" ? hull : flown;
                    Assert.True(expected == KeyOf(intact), $"{design.Key} {p}: the ship flies '{flown}', the wreck's mask holds '{KeyOf(intact)}'");

                    ushort now = w.Get(p.X - o.X, p.Y - o.Y, p.Z - o.Z);
                    Assert.True(now == 0 || now == intact || KeyOf(now) == "carbon" || KeyOf(now).EndsWith("_off", StringComparison.Ordinal),
                        $"{design.Key} {p}: the decay may only take or scorch, it put '{KeyOf(now)}'");
                }

                foreach (var door in flight.DoorCells)
                {
                    Assert.Equal((ushort)0, w.IntactAt(door.X - o.X, door.Y - o.Y, door.Z - o.Z));
                }
            }

            server.Stop();
        }
    }

    [Fact]
    public void ALayoutWreck_IsRepairableToTheEnd_AndClaimable()
    {
        var (server, repo, _, _) = WithWreck("repair", Gen38);
        using (repo)
        {
            var w = server.WreckForTest!;
            Assert.True(w.FromLayout);
            var player = server.AddLocalPlayer("Mechanic");
            var cells = server.WreckRepairCells().ToList();
            Assert.NotEmpty(cells);
            foreach (var cell in cells)
            {
                string item = Content.Items.Values.First(i => i.PlacesBlock == cell.BlockKey).Key;
                player.State.Position = new Vector3f(cell.Pos.X + 0.5f, cell.Pos.Y + 0.5f, cell.Pos.Z + 0.5f);
                player.State.Inventory.Add(item, 1, 99);
                Assert.True(server.RepairWreck("Mechanic", cell.Pos.X, cell.Pos.Y, cell.Pos.Z, item), $"repairing {cell.BlockKey} at {cell.Pos}");
            }

            Assert.Equal(0, server.WreckRepairRemaining);
            int before = server.OwnedShips.Count;
            var claim = server.ClaimWreck("Mechanic");
            Assert.True(claim.Ok);
            Assert.Equal(before + 1, server.OwnedShips.Count);
            Assert.Equal(w.ShipType, server.OwnedShips[claim.ShipId].ShipType);
            server.Stop();
        }
    }

    [Fact]
    public void AChangedOrMissingLayout_KeepsTheBlocks_AndTurnsRepairAndClaimOff()
    {
        var (server, repo, name, seed) = WithWreck("gone", Gen38);
        Vector3i solid;
        string ship;
        int repairs;
        using (repo)
        {
            var w = server.WreckForTest!;
            var o = server.WreckOriginForTest;
            (ship, repairs) = (w.ShipType, server.WreckRepairRemaining);
            solid = Enumerable.Range(0, w.Width).SelectMany(x => Enumerable.Range(0, w.Height).Select(y => (x, y)))
                .SelectMany(xy => Enumerable.Range(0, w.Length).Select(z => new Vector3i(xy.x, xy.y, z)))
                .Where(c => w.Get(c.X, c.Y, c.Z) != 0).Select(c => new Vector3i(o.X + c.X, o.Y + c.Y, o.Z + c.Z)).First();
            server.Stop();
        }

        // The layout as it was: the next start rebuilds the same wreck from its record.
        var replay = Boot(name, seed, Gen38, out var replayRepo);
        using (replayRepo)
        {
            Assert.True(replay.WreckForTest is { FromLayout: true } w && w.ShipType == ship);
            Assert.Equal(repairs, replay.WreckRepairRemaining);
            replay.Stop();
        }

        foreach (Action<StructurePlacementRecord> damage in new Action<StructurePlacementRecord>[]
                 {
                     r => r.KitLayout = "src=layout;rev=1;fp=0000000000000000", // the layout changed
                     r => r.Template = "no_such_ship",                          // the ship is gone
                 })
        {
            EditMetadata(name, meta => damage(meta.Placements.Single(r => r.Kind == "wreck")));
            var again = Boot(name, seed, Gen38, out var repo2);
            using (repo2)
            {
                Assert.True(again.HasWreck);
                Assert.Null(again.WreckForTest);
                Assert.False(string.IsNullOrEmpty(again.WreckName));
                Assert.False(again.World.GetBlock(solid).IsAir); // the blocks stay where they are
                Assert.Empty(again.WreckRepairCells());
                var player = again.AddLocalPlayer("Mechanic");
                player.State.Position = new Vector3f(solid.X + 0.5f, solid.Y + 1.5f, solid.Z + 0.5f);
                player.State.Inventory.Add("iron_wall", 1, 99);
                Assert.False(again.RepairWreck("Mechanic", solid.X, solid.Y, solid.Z, "iron_wall"));
                int before = again.OwnedShips.Count;
                Assert.False(again.ClaimWreck("Mechanic").Ok);
                Assert.Equal(before, again.OwnedShips.Count);
                again.Stop();
            }
        }
    }

    [Fact]
    public void TheCrashSiteOfAStandingWreck_IsItsRecordedBox_NotAFreshPickFromTheCurrentPool()
    {
        var (server, repo, name, seed) = WithWreck("site", Gen38);
        (int, int, int, int) box;
        string location;
        using (repo)
        {
            var w = server.WreckForTest!;
            var rec = server.PlacementRecordsForTest.Single(r => r.Kind == "wreck");
            Assert.Equal((w.Width, w.Length), (rec.Width, rec.Length)); // pinned with the stamp
            box = (rec.X + (rec.Width / 2), rec.Z + (rec.Length / 2), (rec.Width / 2) + 1, (rec.Length / 2) + 1);
            Assert.Equal(box, server.PlannedWreckSiteForTest());
            location = server.ActiveLocationId;
            server.Stop();
        }

        // A user ship that allows wrecks joins the pool with a heavy weight: a fresh plan would now size the site by it.
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var cells = new List<ShipLayoutCell>();
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 17; z++)
            {
                cells.Add(new ShipLayoutCell { X = x, Y = 0, Z = z, Id = "iron_wall" });
            }
        }

        cells.Add(new ShipLayoutCell { X = 1, Y = 1, Z = 1, Kind = "station", Id = "cockpit" });
        content.SetUserShips(new[]
        {
            new UserShipFile { Key = "zz_long_wreck", Uses = new List<string> { "wreck" }, Weight = 100, Layout = new ShipLayout { Cells = cells } },
        });
        var fresh = WreckGenerator.PickDesign(WreckGenerator.LayoutDesigns(content, claimable: true), StableHashing.Mixed("wreck:design:" + location, seed));
        Assert.Equal("zz_long_wreck", fresh?.Design.Key); // the premise: a fresh pick lands on the new ship

        var again = Boot(name, seed, Gen38, out var repo2, content);
        using (repo2)
        {
            Assert.Equal(box, again.PlannedWreckSiteForTest()); // what stands is reserved, not the new pick
            again.Stop();
        }
    }

    [Fact]
    public void OnGeneration37_TheWreckIsTheClassicBox_AndItsRecordHasNoTemplate()
    {
        var (server, repo, _, _) = WithWreck("classic", Gen37);
        using (repo)
        {
            var w = server.WreckForTest!;
            Assert.False(w.FromLayout);
            Assert.Equal(string.Empty, server.PlacementRecordsForTest.Single(r => r.Kind == "wreck").Template);
            long wSeed = server.Metadata.Seed ^ WorldGenerator.StableHash("wreck:" + server.ActiveLocationId);
            var box = WreckGenerator.Generate(Content.GetShip(w.ShipType)!, wSeed, Content);
            Assert.Equal((box.Width, box.Height, box.Length), (w.Width, w.Height, w.Length));
            Assert.Equal(box.BreachCount(), w.BreachCount());
            Assert.Equal(box.Markers.Select(m => (m.Type, m.LocalPos)), w.Markers.Select(m => (m.Type, m.LocalPos)));
            server.Stop();
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
            // Best effort: a lingering SQLite handle on Windows can hold the directory for a moment.
        }
    }
}
