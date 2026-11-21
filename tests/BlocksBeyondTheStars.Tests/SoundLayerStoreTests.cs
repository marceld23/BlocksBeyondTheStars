// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using Xunit.Abstractions;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3907 — the sound layer's storage (table <c>sound_edit</c>, apart from the block edits) in the SQLite save and in the
/// in-memory save's snapshot, and the measurement the cap (<see cref="OtherSideRules.SoundBlockCap"/>) is judged by: what
/// a chunk with 0 / 100 / 1000 sound cells weighs on the wire and costs to compose, and what 2000 cells weigh in the save.
/// </summary>
public sealed class SoundLayerStoreTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_soundstore_" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public SoundLayerStoreTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private static (int X, int Y, int Z, int Glow, string Owner)[] Rows(IWorldRepository repo, string planet)
        => repo.ListSoundCells(planet).Select(c => (c.X, c.Y, c.Z, c.Glow, c.Owner)).OrderBy(c => c).ToArray();

    /// <summary>The same script against either repository: set, replace, remove, count — per location and per builder.</summary>
    private static void Exercise(IWorldRepository repo)
    {
        repo.SaveSoundCell("sys0-p1", new Vector3i(1, 2, 3), 0x38404C, "Elora");
        repo.SaveSoundCell("sys0-p1", new Vector3i(-40, 70, 9001), 0xF0F6FF, "Elora");
        repo.SaveSoundCell("sys0-p1", new Vector3i(4, 5, 6), 0x808EA0, "Kamo");
        repo.SaveSoundCell("station:x", new Vector3i(1, 2, 3), 0x505A68, "Elora");
        repo.SaveSoundCell("sys0-p1", new Vector3i(4, 5, 6), 0x9AA8BA, "Elora"); // re-sung: a new glow, a new builder
        repo.SaveSoundCell("sys0-p1", new Vector3i(7, 8, 9), 0x687484, "Kamo");
        repo.DeleteSoundCell("sys0-p1", new Vector3i(7, 8, 9));
        repo.DeleteSoundCell("sys0-p1", new Vector3i(100, 100, 100)); // nothing there: no error
    }

    private static void Check(IWorldRepository repo)
    {
        Assert.Equal(
            new[] { (-40, 70, 9001, 0xF0F6FF, "Elora"), (1, 2, 3, 0x38404C, "Elora"), (4, 5, 6, 0x9AA8BA, "Elora") },
            Rows(repo, "sys0-p1"));
        Assert.Equal(new[] { (1, 2, 3, 0x505A68, "Elora") }, Rows(repo, "station:x"));
        Assert.Empty(repo.ListSoundCells("sys0-p2"));
        Assert.Equal(3, repo.CountSoundCells("sys0-p1", "Elora"));
        Assert.Equal(0, repo.CountSoundCells("sys0-p1", "Kamo"));
        Assert.Equal(1, repo.CountSoundCells("station:x", "Elora"));
    }

    [Fact]
    public void Sqlite_KeepsTheSoundCells_AcrossAReopen_ApartFromTheBlockEdits()
    {
        var paths = new SaveGamePaths(_root, "sound_sqlite");
        using (var repo = new SqliteWorldRepository(paths))
        {
            repo.Initialize();
            Exercise(repo);
            Check(repo);
            Assert.False(repo.HasAnyBlockEdits("sys0-p1"), "a sound block is never a block edit");
            Assert.Empty(repo.LoadChunkEdits("sys0-p1", WorldConstants.WorldToChunk(new Vector3i(1, 2, 3))));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var reopened = new SqliteWorldRepository(paths);
        reopened.Initialize();
        Check(reopened);
    }

    [Fact]
    public void TheMemorySave_CarriesTheSoundCells_InItsSnapshot()
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, "sound_memory"));
        repo.Initialize();
        Exercise(repo);
        Check(repo);
        Assert.False(repo.HasAnyBlockEdits("sys0-p1"));

        var blob = repo.ExportSnapshotBlob();
        var restored = new MemoryWorldRepository(new SaveGamePaths(_root, "sound_memory_2"));
        restored.Initialize();
        restored.SaveSoundCell("sys0-p1", new Vector3i(55, 55, 55), 1, "Stale"); // an import replaces what was there
        restored.ImportSnapshotBlob(blob);
        Check(restored);
    }

    [Fact]
    public void TheThreeRefusals_AndTheBlocksName_AreInBothLanguages()
    {
        foreach (string language in new[] { "en", "de" })
        {
            var table = TestLocales.Load(language);
            foreach (string key in new[]
            {
                GameServer.GameServer.SoundCapKey, GameServer.GameServer.SoundNotYoursKey, GameServer.GameServer.SoundBlockedKey,
                "block.sound_block.name",
            })
            {
                Assert.True(table.TryGetValue(key, out var text) && text.Length > 0, $"{language}: {key}");
            }
        }

        Assert.Equal("Sound block", TestLocales.Load("en")["block.sound_block.name"]);
        Assert.Equal("Klangblock", TestLocales.Load("de")["block.sound_block.name"]);
    }

    /// <summary>The cap's measurement. The numbers go to the test output (and into docs/developer/OTHER_SIDE.md); the
    /// assertions are loose ceilings on the sizes only — wall-clock time is reported, never asserted.</summary>
    [Fact]
    public void Measurement_AChunkWithSoundCells_AndTheTableOfTwoThousand()
    {
        using var w = new NpcLifeWorld(Content.Value, "sound_measure", padHalf: 12);
        var coord = WorldConstants.WorldToChunk(new Vector3i(NpcLifeWorld.Cx, w.Feet + 4, NpcLifeWorld.Cz));
        var origin = WorldConstants.ChunkOrigin(coord);
        var free = Enumerable.Range(0, WorldConstants.BlocksPerChunk)
            .Select(i => new Vector3i(origin.X + (i % 16), origin.Y + (i / 256), origin.Z + ((i / 16) % 16)))
            .Where(c => w.Server.World.GetBlock(c).IsAir)
            .ToList();
        Assert.True(free.Count >= 1000, $"the measured chunk needs 1000 free cells (has {free.Count})");

        // Scattered cells are the worst case for the run-length encoding: every lone cell breaks a run in two.
        var scattered = free.OrderBy(c => unchecked((uint)((c.X * 73856093) ^ (c.Y * 19349663) ^ (c.Z * 83492791)))).ToList();
        int[] sizes = new int[3];
        int[] counts = { 0, 100, 1000 };
        int placed = 0;
        for (int step = 0; step < counts.Length; step++)
        {
            for (; placed < counts[step]; placed++)
            {
                w.Server.PutSoundCellForTest(scattered[placed], OtherSideRules.ToneGlow[placed % 8], NpcLifeWorld.Owner);
            }

            sizes[step] = w.Server.EncodeChunkUncachedForTest(coord).Length;
            var clock = Stopwatch.StartNew();
            const int rounds = 300;
            for (int i = 0; i < rounds; i++)
            {
                w.Server.EncodeChunkUncachedForTest(coord);
            }

            _output.WriteLine(FormattableString.Invariant(
                $"chunk with {counts[step],4} scattered sound cells: {sizes[step],5} bytes on the wire, {clock.Elapsed.TotalMilliseconds * 1000.0 / rounds:F0} µs to compose + encode"));
        }

        Assert.True(sizes[0] < sizes[1] && sizes[1] < sizes[2]);
        Assert.True(sizes[2] < 16 * 1024, $"a chunk with 1000 scattered sound cells stays under 16 KB ({sizes[2]} bytes)");

        // The best case: the same 1000 cells as one solid body in the chunk above (what a wall or a floor looks like).
        var above = new ChunkCoord(coord.X, coord.Y + 1, coord.Z);
        var corner = WorldConstants.ChunkOrigin(above);
        for (int i = 0; i < 1000; i++)
        {
            w.Server.PutSoundCellForTest(new Vector3i(corner.X + (i % 10), corner.Y + (i / 100), corner.Z + ((i / 10) % 10)),
                OtherSideRules.ToneGlow[0], NpcLifeWorld.Owner);
        }

        int solid = w.Server.EncodeChunkUncachedForTest(above).Length;
        _output.WriteLine(FormattableString.Invariant($"chunk with 1000 sound cells in one body: {solid,5} bytes on the wire"));
        Assert.True(solid < sizes[2]);
        MeasureTheTable();
    }

    private void MeasureTheTable()
    {
        var paths = new SaveGamePaths(_root, "sound_table");
        using var repo = new SqliteWorldRepository(paths);
        repo.Initialize();
        repo.Flush();
        long before = new FileInfo(paths.DatabaseFile).Length;
        var rng = new Random(3907);
        repo.RunInTransaction(() =>
        {
            for (int i = 0; i < OtherSideRules.SoundBlockCap; i++)
            {
                repo.SaveSoundCell("sys0-p1", new Vector3i(rng.Next(0, 8000), rng.Next(-200, 400), rng.Next(-2000, 2000)),
                    OtherSideRules.ToneGlow[i % 8], "Homesteader");
            }
        });
        repo.Flush();
        long table = new FileInfo(paths.DatabaseFile).Length - before;
        _output.WriteLine(FormattableString.Invariant(
            $"sound_edit with {OtherSideRules.SoundBlockCap} cells: {table} bytes in the SQLite save ({table / (double)OtherSideRules.SoundBlockCap:F1} per cell)"));
        Assert.True(table < 256 * 1024, $"2000 sound cells stay under 256 KB in the save ({table} bytes)");
    }
}
