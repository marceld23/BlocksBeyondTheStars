// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Npgsql;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The structure overlay (terrain generation 39) below the server: the blob codec (<see cref="StructureOverlay"/>, its
/// builder and the block-id remap), its table in every repository — SQLite across a reopen, the browser's in-memory save
/// through its snapshot blob, PostgreSQL when <c>BBS_POSTGRES_TEST_CONNECTION_STRING</c> is set — with the palette migration
/// and the "was this body ever stamped" question, the server's stamp-order rule and the far silhouette sampled from cells.
/// </summary>
public sealed class StructureOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_overlay_" + Guid.NewGuid().ToString("N"));

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

    /// <summary>A few hundred cells over several chunks — negative coordinates, air, dyes, glows, shapes, a cell written twice.</summary>
    private static (StructureOverlay Overlay, Dictionary<Vector3i, (ushort, int, int, int)> Want) Sample()
    {
        var builder = new StructureOverlayBuilder();
        var want = new Dictionary<Vector3i, (ushort, int, int, int)>();
        var rng = new Random(39);
        for (int i = 0; i < 600; i++)
        {
            var cell = new Vector3i(rng.Next(-40, 40), rng.Next(-20, 90), rng.Next(-40, 40));
            ushort block = (ushort)(i % 7 == 0 ? 0 : rng.Next(1, 900));
            int tint = i % 5 == 0 ? rng.Next(1, 0xFFFFFF) : 0;
            int glow = i % 11 == 0 ? rng.Next(1, 0xFFFFFF) : 0;
            int shape = i % 13 == 0 ? rng.Next(1, 4000) : 0;
            builder.Set(cell, block, tint, glow, shape);
            want[cell] = (block, tint, glow, shape);
        }

        builder.Set(new Vector3i(3, 3, 3), 5, 0x123456, 0, 0);
        builder.Set(new Vector3i(3, 3, 3), 6, 0, 0, 0); // a later write replaces the earlier one, its dye too
        want[new Vector3i(3, 3, 3)] = (6, 0, 0, 0);
        Assert.Equal(want.Count, builder.Count);
        return (builder.Build(), want);
    }

    private static Dictionary<Vector3i, (ushort, int, int, int)> CellsOf(StructureOverlay overlay)
        => overlay.Cells().ToDictionary(c => c.Cell, c => (c.Block, c.Tint, c.Glow, c.Shape));

    [Fact]
    public void TheBlob_RoundTripsEveryCell_InChunkAndIndexOrder()
    {
        var (overlay, want) = Sample();
        byte[] blob = overlay.Encode();

        Assert.Equal(StructureOverlay.FormatVersion, blob[0]);
        Assert.True(StructureOverlay.TryDecode(blob, out var back));
        Assert.Equal(want, CellsOf(back));
        Assert.Equal(overlay.CellCount, back.CellCount);
        Assert.Equal(overlay.Chunks.Select(c => c.Coord), back.Chunks.Select(c => c.Coord));
        foreach (var chunk in back.Chunks)
        {
            for (int r = 1; r < chunk.RunCount; r++)
            {
                Assert.True(chunk.RunStart(r) >= chunk.RunStart(r - 1) + chunk.RunLength(r - 1), "runs out of order or overlapping");
            }
        }

        Assert.Equal(blob, back.Encode()); // the format is canonical: decode + encode gives the same bytes
    }

    [Fact]
    public void ARunOfOneValue_IsOneRun_AndEveryCellOfItIsFound()
    {
        var builder = new StructureOverlayBuilder();
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                builder.Set(new Vector3i(x, 5, z), 0, 0, 0, 0); // a whole row of a chunk carved to air …
            }
        }

        builder.Set(new Vector3i(3, 6, 0), 7, 0x00FF00, 0, 0); // … and two dyed cells beside each other: one run
        builder.Set(new Vector3i(4, 6, 0), 7, 0x00FF00, 0, 0);
        builder.Set(new Vector3i(5, 6, 0), 7, 0x0000FF, 0, 0); // another dye: a run of its own
        var chunk = Assert.Single(builder.Build().Chunks);

        Assert.Equal(3, chunk.RunCount);
        Assert.Equal(259, chunk.Count);
        Assert.Equal((256, 2, 1), (chunk.RunLength(0), chunk.RunLength(1), chunk.RunLength(2)));
        Assert.Equal(0, chunk.FindRun(WorldConstants.LocalIndex(15, 5, 15)));
        Assert.Equal(1, chunk.FindRun(WorldConstants.LocalIndex(4, 6, 0)));
        Assert.Equal(2, chunk.FindRun(WorldConstants.LocalIndex(5, 6, 0)));
        Assert.Equal(-1, chunk.FindRun(WorldConstants.LocalIndex(6, 6, 0)));
        Assert.Equal(-1, chunk.FindRun(WorldConstants.LocalIndex(0, 4, 0)));
    }

    [Fact]
    public void TheBuilder_AnswersItsOwnLastWrite_AndNothingElse()
    {
        var builder = new StructureOverlayBuilder();
        Assert.False(builder.TryGet(new Vector3i(1, 2, 3), out _));
        builder.Set(new Vector3i(1, 2, 3), 9, 0, 0, 0);
        builder.Set(new Vector3i(1, 2, 3), 0, 0, 0, 0); // carved again: air is a cell like any other
        Assert.True(builder.TryGet(new Vector3i(1, 2, 3), out ushort block));
        Assert.Equal(0, block);
        Assert.False(builder.TryGet(new Vector3i(1, 2, 4), out _));
        Assert.Equal(1, builder.Count);
    }

    [Fact]
    public void ABrokenBlob_IsRefused_NeverThrown()
    {
        var (overlay, _) = Sample();
        byte[] blob = overlay.Encode();

        Assert.False(StructureOverlay.TryDecode(null, out _));
        Assert.False(StructureOverlay.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(StructureOverlay.TryDecode(new byte[] { 2, 0, 0 }, out _)); // a format this build does not know
        Assert.False(StructureOverlay.TryDecode(new byte[] { 1, 0xFF, 0xFE, 0x12, 0x34 }, out _)); // not deflate
        Assert.False(StructureOverlay.TryDecode(blob.Take(blob.Length / 2).ToArray(), out _)); // cut short

        // A stream with a byte after the last chunk, and one with an index past the chunk's end.
        var empty = new StructureOverlay(Array.Empty<StructureOverlayChunk>()).Encode();
        Assert.True(StructureOverlay.TryDecode(empty, out var none));
        Assert.Equal(0, none.CellCount);
        Assert.False(StructureOverlay.TryDecode(Deflated(0, 7), out _));
        Assert.False(StructureOverlay.TryDecode(Deflated(1, 0, 0, 0, 1, 0x80, 0x20), out _)); // local 4096
    }

    /// <summary>A blob of format 1 around raw (already varint-coded) bytes.</summary>
    private static byte[] Deflated(params byte[] raw)
    {
        using var packed = new MemoryStream();
        packed.WriteByte(StructureOverlay.FormatVersion);
        using (var deflate = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        return packed.ToArray();
    }

    [Fact]
    public void TheRemap_TranslatesEveryBlockId_AndHandsBackTheSameBlobWhenNoneChanges()
    {
        var (overlay, want) = Sample();
        byte[] blob = overlay.Encode();
        ushort some = want.Values.First(v => v.Item1 != 0).Item1;

        Assert.Same(blob, StructureOverlay.RemapBlocks(blob, new Dictionary<ushort, ushort>()));
        Assert.Same(blob, StructureOverlay.RemapBlocks(blob, new Dictionary<ushort, ushort> { [4000] = 4001 })); // no such id

        var remap = new Dictionary<ushort, ushort> { [some] = 4000 };
        Assert.True(StructureOverlay.TryDecode(StructureOverlay.RemapBlocks(blob, remap), out var moved));
        var expected = want.ToDictionary(kv => kv.Key, kv => kv.Value.Item1 == some ? ((ushort)4000, kv.Value.Item2, kv.Value.Item3, kv.Value.Item4) : kv.Value);
        Assert.Equal(expected, CellsOf(moved));
    }

    [Fact]
    public void TheServer_AppliesOverlays_InStampOrder()
    {
        Assert.True(BlocksBeyondTheStars.GameServer.StructureOverlayIndex.CompareKeys("landmark:2", "landmark:10") < 0);
        Assert.True(BlocksBeyondTheStars.GameServer.StructureOverlayIndex.CompareKeys("landmark:10", "landmark:9") > 0);
        Assert.Equal(0, BlocksBeyondTheStars.GameServer.StructureOverlayIndex.CompareKeys("landmark:0", "landmark:0"));
        Assert.True(BlocksBeyondTheStars.GameServer.StructureOverlayIndex.CompareKeys("a:1", "b:0") < 0); // another kind: as text
    }

    // ---------------------------------------------------------------- the repositories

    private static StoredStructureOverlay Row(string planet, string key, byte[] data) => new() { Planet = planet, Key = key, Data = data };

    /// <summary>Two bodies, three overlays, a replace and a delete — then the palette migration moves an id inside a blob,
    /// and a body whose only stamp is an overlay counts as stamped.</summary>
    private static void AssertRoundTrip(IWorldRepository repo, Func<IWorldRepository> reopen)
    {
        var (overlay, want) = Sample();
        byte[] blob = overlay.Encode();
        ushort some = want.Values.First(v => v.Item1 != 0).Item1;
        repo.EnsureBlockPalette(new Dictionary<ushort, string> { [0] = "air", [some] = "stone" });
        Assert.False(repo.HasAnyBlockEdits("sys0-p1"));
        repo.SaveStructureOverlay(Row("sys0-p1", "landmark:0", blob));
        repo.SaveStructureOverlay(Row("sys0-p1", "landmark:1", new byte[] { 9 }));
        repo.SaveStructureOverlay(Row("sys0-p1", "landmark:1", new byte[] { 1, 2 })); // replaced
        repo.SaveStructureOverlay(Row("sys0-p2", "landmark:0", new byte[] { 7 }));
        repo.DeleteStructureOverlay("sys0-p2", "landmark:0");
        Assert.True(repo.HasAnyBlockEdits("sys0-p1")); // the overlay is a worldgen stamp — the body was materialised
        Assert.False(repo.HasAnyBlockEdits("sys0-p2"));

        var again = reopen();
        var rows = again.ListStructureOverlays("sys0-p1");
        Assert.Equal(new[] { "landmark:0", "landmark:1" }, rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.Equal("sys0-p1", r.Planet));
        Assert.Equal(blob, rows[0].Data);
        Assert.Equal(new byte[] { 1, 2 }, rows[1].Data);
        Assert.Empty(again.ListStructureOverlays("sys0-p2"));
        Assert.True(again.HasAnyBlockEdits("sys0-p1"));

        // A content update moved "stone" to another id: the overlay's cells follow it (the unreadable blob stays as it is).
        again.EnsureBlockPalette(new Dictionary<ushort, string> { [0] = "air", [4000] = "stone" });
        var migrated = again.ListStructureOverlays("sys0-p1");
        Assert.True(StructureOverlay.TryDecode(migrated[0].Data, out var moved));
        Assert.All(moved.Cells(), c => Assert.NotEqual(some, c.Block));
        Assert.Equal(want.Count(kv => kv.Value.Item1 == some), moved.Cells().Count(c => c.Block == 4000));
        Assert.Equal(new byte[] { 1, 2 }, migrated[1].Data);
    }

    [Fact]
    public void Sqlite_RoundTripsTheOverlays_AcrossAReopen()
    {
        var paths = new SaveGamePaths(_root, "sqlite");
        var repo = new SqliteWorldRepository(paths);
        repo.Initialize();
        SqliteWorldRepository? reopened = null;
        try
        {
            AssertRoundTrip(repo, () =>
            {
                repo.Dispose();
                reopened = new SqliteWorldRepository(paths);
                reopened.Initialize();
                return reopened;
            });
        }
        finally
        {
            repo.Dispose();
            reopened?.Dispose();
        }
    }

    [Fact]
    public void Memory_RoundTripsTheOverlays_ThroughTheSnapshotBlob()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "mem"));
        repo.Initialize();

        AssertRoundTrip(repo, () =>
        {
            var restored = new MemoryWorldRepository(new SaveGamePaths(_root, "mem2"));
            restored.Initialize();
            restored.ImportSnapshotBlob(repo.ExportSnapshotBlob());
            return restored;
        });
    }

    [Fact]
    public void PostgreSql_RoundTripsTheOverlays()
    {
        string? connectionString = Environment.GetEnvironmentVariable("BBS_POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return; // opt-in, like PostgreSqlRepositoryTests
        }

        string world = "pg_" + Guid.NewGuid().ToString("N");
        using var repo = new PostgreSqlWorldRepository(new SaveGamePaths(_root, world), connectionString);
        try
        {
            repo.Initialize();
            AssertRoundTrip(repo, () => repo);
        }
        finally
        {
            try
            {
                using var connection = new NpgsqlConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DROP SCHEMA IF EXISTS \"bbs_" + world + "\" CASCADE;";
                cmd.ExecuteNonQuery();
            }
            catch (NpgsqlException)
            {
                // Best-effort cleanup; the test world schema is unique and harmless if left behind.
            }
        }
    }

    // ---------------------------------------------------------------- the far silhouette

    /// <summary>The silhouette sampled from a landmark's cells is the one sampled from its layout when the cells are the
    /// layout's — every shipped sample, intact and as a ruin, in two turns.</summary>
    [Fact]
    public void TheSilhouetteFromCells_IsTheSilhouetteOfTheLayout()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        Assert.NotEmpty(content.Landmarks);
        foreach (var t in content.Landmarks.Where(t => !t.IsSource).Take(3))
        {
            foreach (bool ruin in new[] { false, true })
            {
                foreach (int turns in new[] { 0, 1 })
                {
                    var layout = LandmarkGenerator.Build(t, 20261112, turns, content, "grass", ruin);
                    var s = layout.Structure;
                    int g = layout.GroundRow;
                    var cells = new List<(int, int, int, ushort, int)>();
                    for (int x = 0; x < s.Width; x++)
                    {
                        for (int y = g; y < s.Height; y++)
                        {
                            for (int z = 0; z < s.Length; z++)
                            {
                                cells.Add((x, y - g, z, s.Get(x, y, z), s.GetModifier(x, y, z).Tint));
                            }
                        }
                    }

                    var a = LandmarkSilhouette.Build(layout);
                    var b = LandmarkSilhouette.FromCells(s.Width, s.Height - g, s.Length, cells.AsEnumerable().Reverse());
                    Assert.Equal((a.Step, a.Width, a.Height, a.Length), (b.Step, b.Width, b.Height, b.Length));
                    Assert.Equal(a.Cells, b.Cells);
                    Assert.Equal(a.Blocks, b.Blocks);
                    Assert.Equal(a.Tints, b.Tints);
                }
            }
        }
    }
}
