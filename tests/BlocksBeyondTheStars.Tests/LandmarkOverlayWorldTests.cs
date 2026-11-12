// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A landmark stored as a STRUCTURE OVERLAY (terrain generation 39): the record pins <c>storage=overlay</c>, the stamp writes
/// one blob and no block edit, and the world it makes is the world the cell-by-cell stamp of generation 38 made — every block,
/// dye, glow, shape and "built" mark of every chunk the landmark touches — on the stamp, after a reload and after a crash
/// that stored the blob but not the feature mark; a mined cell stays mined; every shipped landmark's blob fits a megabyte.
/// </summary>
public sealed class LandmarkOverlayWorldTests : IDisposable
{
    private const int OverlayBudgetBytes = 1024 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_lmoverlay_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public LandmarkOverlayWorldTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    private static GameContent ContentWith(params StructureTemplate[] landmarks)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetLandmarks(landmarks);
        return content;
    }

    private SvGameServer Start(GameContent content, out SqliteWorldRepository repo, string name, int generation,
        bool landmarks = true, long seed = 20261112)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = seed,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceLandmarks = landmarks,
            LandmarkChance = 1.0,
        };
        config.World.TerrainGeneration = generation;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static StructureTemplate Shape(string shape) => shape switch
    {
        "mine" => LandmarkShapes.Compacted(LandmarkShapes.Mine()),
        "intact" => LandmarkShapes.Tower("test_house", ruin: false, chest: "block=crate;hidden=1"),
        _ => LandmarkShapes.Compacted(LandmarkShapes.Tower()),
    };

    /// <summary>Everything a chunk holds: blocks, dyes and glows, shapes, the cells marked built.</summary>
    private sealed record ChunkState(ushort[] Blocks, string Modifiers, string Shapes, ushort[] Edited);

    private static ChunkState StateOf(SvGameServer server, ChunkCoord coord)
    {
        var c = server.World.GetOrLoadChunk(coord);
        string mods = string.Join(";", (c.Modifiers ?? new Dictionary<int, (int, int)>()).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value.Tint}:{kv.Value.Glow}"));
        string shapes = string.Join(";", (c.Shapes ?? new Dictionary<int, int>()).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
        return new ChunkState(c.ToArray(), mods, shapes, c.EditedRuns());
    }

    private static void AssertSameChunk(ChunkCoord coord, ChunkState want, ChunkState got)
    {
        int diff = Enumerable.Range(0, want.Blocks.Length).FirstOrDefault(i => want.Blocks[i] != got.Blocks[i], -1);
        Assert.True(diff < 0, $"chunk {coord.X},{coord.Y},{coord.Z}: local {diff} holds {(diff < 0 ? 0 : got.Blocks[diff])}, the cell-by-cell stamp wrote {(diff < 0 ? 0 : want.Blocks[diff])}");
        Assert.Equal(want.Modifiers, got.Modifiers);
        Assert.Equal(want.Shapes, got.Shapes);
        Assert.Equal(want.Edited, got.Edited);
    }

    private static StructureOverlay OverlayOf(IWorldRepository repo, string location, string key = "landmark:0")
    {
        var row = Assert.Single(repo.ListStructureOverlays(location), r => r.Key == key);
        Assert.True(StructureOverlay.TryDecode(row.Data, out var overlay));
        return overlay;
    }

    /// <summary>The block edits of the store that lie on a cell of the overlay.</summary>
    private static List<BlockEdit> EditsOnOverlay(IWorldRepository repo, string location, StructureOverlay overlay)
    {
        var cells = new HashSet<Vector3i>(overlay.Cells().Select(c => c.Cell));
        return overlay.Chunks.SelectMany(c => repo.LoadChunkEdits(location, c.Coord)).Where(e => cells.Contains(e.WorldPosition)).ToList();
    }

    [Theory]
    [InlineData("tower")]
    [InlineData("mine")]
    [InlineData("intact")]
    public void AGeneration39Landmark_IsOneOverlay_WithoutABlockEdit_AndTheWorldTheCellByCellStampMade(string shape)
    {
        var content = ContentWith(Shape(shape));
        string location;
        Vector3i origin;
        StructureOverlay overlay;
        var states = new Dictionary<ChunkCoord, ChunkState>();
        var server = Start(content, out var repo, "ov_new_" + shape, WorldDescription.SchoolClubLandmarksGeneration);
        using (repo)
        {
            location = server.World.LocationId;
            var lm = Assert.Single(server.LandmarksForTest);
            origin = lm.Origin;
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Equal(SvGameServer.LandmarkOverlayStorage, MarkerSettings.Parse(rec.KitLayout).Get(SvGameServer.LandmarkStoragePin));

            overlay = OverlayOf(repo, location);
            Assert.True(overlay.CellCount > lm.Structure.Width * lm.Structure.Length, $"only {overlay.CellCount} cells");
            Assert.Empty(EditsOnOverlay(repo, location, overlay)); // the stamp wrote no row
            foreach (var chunk in overlay.Chunks)
            {
                states[chunk.Coord] = StateOf(server, chunk.Coord);
            }

            server.Stop();
        }

        // The same seed at generation 38: the same seat, stamped cell by cell — the very same chunks.
        var old = Start(content, out var oldRepo, "ov_old_" + shape, WorldDescription.StructurePipelineGeneration);
        using (oldRepo)
        {
            var lm = Assert.Single(old.LandmarksForTest);
            Assert.Equal(origin, lm.Origin);
            var rec = Assert.Single(old.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Equal(string.Empty, MarkerSettings.Parse(rec.KitLayout).Get(SvGameServer.LandmarkStoragePin));
            Assert.Empty(oldRepo.ListStructureOverlays(location));
            Assert.NotEmpty(EditsOnOverlay(oldRepo, location, overlay));
            foreach (var (coord, want) in states)
            {
                AssertSameChunk(coord, StateOf(old, coord), want);
            }

            // …and every row the old stamp left in those chunks lies on a cell of the overlay.
            var cells = new HashSet<Vector3i>(overlay.Cells().Select(c => c.Cell));
            Assert.All(overlay.Chunks.SelectMany(c => oldRepo.LoadChunkEdits(location, c.Coord)), e => Assert.Contains(e.WorldPosition, cells));
            old.Stop();
        }
    }

    [Fact]
    public void AnOverlayLandmark_ReloadsIdentical_AndAMinedCellStaysMined()
    {
        var content = ContentWith(Shape("tower"));
        const string name = "ov_reload";
        Vector3i mined;
        var states = new Dictionary<ChunkCoord, ChunkState>();
        byte[] blob;
        var server = Start(content, out var repo, name, WorldDescription.SchoolClubLandmarksGeneration);
        using (repo)
        {
            string location = server.World.LocationId;
            var lm = Assert.Single(server.LandmarksForTest);
            var overlay = OverlayOf(repo, location);
            blob = repo.ListStructureOverlays(location).Single().Data;
            mined = overlay.Cells().First(c => c.Block == content.GetBlock("stone")!.NumericId.Value && c.Cell.Y > lm.GroundY).Cell;
            server.World.SetBlock(mined, BlockId.Air, owner: "tester"); // a player mines a stone of the landmark
            foreach (var chunk in overlay.Chunks)
            {
                states[chunk.Coord] = StateOf(server, chunk.Coord);
            }

            server.Stop();
        }

        var again = Start(content, out var repo2, name, WorldDescription.SchoolClubLandmarksGeneration);
        using (repo2)
        {
            string location = again.World.LocationId;
            Assert.Single(again.LandmarksForTest);
            Assert.Equal(blob, Assert.Single(repo2.ListStructureOverlays(location)).Data); // never written again
            Assert.True(again.World.GetBlock(mined).IsAir, "a mined stone of the landmark came back on reload");
            var edit = Assert.Single(repo2.LoadChunkEdits(location, WorldConstants.WorldToChunk(mined)), e => e.WorldPosition == mined);
            Assert.Equal(BlockId.AirValue, edit.Block);
            foreach (var (coord, want) in states)
            {
                AssertSameChunk(coord, want, StateOf(again, coord));
            }

            again.Stop();
        }
    }

    /// <summary>A player mines a shaped and a dyed cell of an overlay landmark and puts a plain stone in each place: after a
    /// reload the stones are plain cubes without a dye — the overlay's shape and colour stay under the player's edit.</summary>
    [Fact]
    public void APlainBlockPlacedOverAShapedOrDyedOverlayCell_StaysPlainAfterAReload()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var statue = content.Landmarks.Single(t => t.Key == "soundi_statue");
        var stone = content.GetBlock("stone")!.NumericId;
        const string name = "ov_plain";
        Vector3i shaped, dyed;
        var server = Start(content, out var repo, name, WorldDescription.SchoolClubLandmarksGeneration, landmarks: false);
        using (repo)
        {
            Assert.NotNull(server.ForceLandmark(statue, new Vector3i(600, 0, 300)));
            var overlay = OverlayOf(repo, server.World.LocationId);
            shaped = overlay.Cells().First(c => c.Shape != 0 && c.Block != BlockId.AirValue).Cell;
            dyed = overlay.Cells().First(c => c.Shape == 0 && (c.Tint != 0 || c.Glow != 0) && c.Block != BlockId.AirValue).Cell;
            foreach (var cell in new[] { shaped, dyed })
            {
                server.World.SetBlock(cell, BlockId.Air, owner: "tester");
                server.World.SetBlock(cell, stone, owner: "tester");
            }

            server.Stop();
        }

        var again = Start(content, out var repo2, name, WorldDescription.SchoolClubLandmarksGeneration, landmarks: false);
        using (repo2)
        {
            foreach (var cell in new[] { shaped, dyed })
            {
                var chunk = again.World.GetOrLoadChunk(WorldConstants.WorldToChunk(cell));
                var local = WorldConstants.WorldToLocal(cell);
                Assert.Equal(stone.Value, chunk.Get(local.X, local.Y, local.Z).Value);
                Assert.Equal(0, chunk.GetShape(local.X, local.Y, local.Z));
                Assert.Equal((0, 0), chunk.GetModifier(local.X, local.Y, local.Z));
            }

            again.Stop();
        }
    }

    /// <summary>An admin stands one of the school club's giants (made for generation 39) on an older world: it is stored as
    /// one overlay too, never as a block edit per cell.</summary>
    [Fact]
    public void AnAdminStampOfAGeneration39Giant_OnAnOlderWorld_IsAnOverlay()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var statue = content.Landmarks.Single(t => t.Key == "soundi_statue");
        Assert.True(statue.MinTerrainGeneration > WorldDescription.StructurePipelineGeneration);
        var server = Start(content, out var repo, "ov_admin_old", WorldDescription.StructurePipelineGeneration, landmarks: false);
        using (repo)
        {
            Assert.NotNull(server.ForceLandmark(statue, new Vector3i(600, 0, 300)));
            var rec = Assert.Single(server.PlacementRecordsForTest, r => r.Kind == LandmarkRules.RecordKind);
            Assert.Equal(SvGameServer.LandmarkOverlayStorage, MarkerSettings.Parse(rec.KitLayout).Get(SvGameServer.LandmarkStoragePin));
            var overlay = OverlayOf(repo, server.World.LocationId);
            Assert.True(overlay.CellCount > 1000, $"only {overlay.CellCount} cells");
            Assert.Empty(EditsOnOverlay(repo, server.World.LocationId, overlay));
            server.Stop();
        }
    }

    /// <summary>A crash after the overlay's row committed but before the feature was marked: the next load finds the blob,
    /// stamps nothing again and only marks the feature.</summary>
    [Fact]
    public void AnOverlayStoredBeforeACrash_IsNotStampedAgain()
    {
        var content = ContentWith(Shape("mine"));
        const string name = "ov_crash";
        var states = new Dictionary<ChunkCoord, ChunkState>();
        byte[] blob;
        string location;
        var server = Start(content, out var repo, name, WorldDescription.SchoolClubLandmarksGeneration);
        using (repo)
        {
            location = server.World.LocationId;
            var overlay = OverlayOf(repo, location);
            blob = repo.ListStructureOverlays(location).Single().Data;
            foreach (var chunk in overlay.Chunks)
            {
                states[chunk.Coord] = StateOf(server, chunk.Coord);
            }

            server.Stop();
        }

        using (var edit = new SqliteWorldRepository(new SaveGamePaths(_root, name)))
        {
            edit.Initialize();
            var meta = edit.LoadMetadata()!;
            Assert.Equal(1, meta.StampedFeatures.RemoveAll(f => f == location + "|landmark:0"));
            edit.SaveMetadata(meta);
        }

        var again = Start(content, out var repo2, name, WorldDescription.SchoolClubLandmarksGeneration);
        using (repo2)
        {
            Assert.Contains(location + "|landmark:0", again.Metadata.StampedFeatures);
            Assert.Equal(blob, Assert.Single(repo2.ListStructureOverlays(location)).Data);
            foreach (var (coord, want) in states)
            {
                AssertSameChunk(coord, want, StateOf(again, coord));
            }

            again.Stop();
        }
    }

    /// <summary>The size budget: every shipped landmark (the samples and the school club's buildings) stamped by hand on a
    /// generation-39 world keeps its overlay under a megabyte — and lies on no block edit. The sizes are reported.</summary>
    [Fact]
    public void EveryShippedLandmark_FitsItsOverlayInAMegabyte()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var templates = content.Landmarks.Where(t => !t.IsSource).ToList();
        Assert.NotEmpty(templates);
        var server = Start(content, out var repo, "ov_budget", WorldDescription.SchoolClubLandmarksGeneration, landmarks: false);
        using (repo)
        {
            string location = server.World.LocationId;
            for (int i = 0; i < templates.Count; i++)
            {
                var t = templates[i];
                var clock = Stopwatch.StartNew();
                var lm = server.ForceLandmark(t, new Vector3i(600 + (i * 320), 0, 300));
                Assert.NotNull(lm);
                var row = Assert.Single(repo.ListStructureOverlays(location), r => r.Key == "landmark:" + i);
                Assert.True(StructureOverlay.TryDecode(row.Data, out var overlay));
                _output.WriteLine($"{t.Key}: {t.Width}×{t.Height}×{t.Length}, {(lm!.Layout.Ruined ? "ruin" : "intact")}, " +
                                  $"{overlay.CellCount} cells in {overlay.Chunks.Count} chunks → {row.Data.Length} bytes " +
                                  $"({row.Data.Length / (double)Math.Max(1, overlay.CellCount):0.000} B/cell), {clock.ElapsedMilliseconds} ms");
                Assert.True(row.Data.Length <= OverlayBudgetBytes, $"{t.Key}: {row.Data.Length} bytes");
                Assert.Empty(EditsOnOverlay(repo, location, overlay));
            }

            server.Stop();
        }
    }
}
