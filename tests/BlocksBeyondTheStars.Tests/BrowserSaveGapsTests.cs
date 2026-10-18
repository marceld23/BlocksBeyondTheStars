// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using System.Text;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The browser singleplayer's save blob (<see cref="MemoryWorldRepository"/>) against the desktop save: #2787 it now keeps
/// flowing water, fire and who placed a block; #2788 an import over a used repository leaves nothing of the old world;
/// #2789 an alliance is one sorted row, as in the SQL repositories.
/// </summary>
public sealed class BrowserSaveGapsTests : IDisposable
{
    private const string Planet = "terra";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_browsersave_" + Guid.NewGuid().ToString("N"));

    private MemoryWorldRepository NewRepo(string name = "world_001")
    {
        var repo = new MemoryWorldRepository(new SaveGamePaths(_root, name));
        repo.Initialize();
        return repo;
    }

    private MemoryWorldRepository Reloaded(MemoryWorldRepository source)
    {
        var fresh = NewRepo("reloaded_" + Guid.NewGuid().ToString("N"));
        fresh.ImportSnapshotBlob(source.ExportSnapshotBlob());
        return fresh;
    }

    [Fact]
    public void AReload_KeepsFlowingWaterFireAndWhoPlacedABlock()
    {
        using var repo = NewRepo();
        var built = new Vector3i(1, 64, 2);
        var natural = new Vector3i(3, 64, 4);
        repo.SetBlock(Planet, built, 7, owner: "pilot");
        repo.SetBlock(Planet, natural, 9); // a server-internal write: no owner
        repo.SaveFluidCell(Planet, new Vector3i(5, 63, 5), 4, falling: true);
        repo.SaveFireCell(Planet, new Vector3i(6, 64, 6), 2.5, 3);
        var attribution = repo.GetBlockAttribution(Planet, built)!.Value;

        using var loaded = Reloaded(repo);

        var kept = loaded.GetBlockAttribution(Planet, built)!.Value;
        Assert.Equal("pilot", kept.Owner);
        Assert.Equal(attribution.EditedUtc, kept.EditedUtc);
        Assert.Equal(string.Empty, loaded.GetBlockAttribution(Planet, natural)!.Value.Owner);
        Assert.True(loaded.HasPlayerBlockEdits(Planet, built, built), "a drill must still see the player's block");
        Assert.False(loaded.HasPlayerBlockEdits(Planet, natural, natural));

        var water = Assert.Single(loaded.ListFluidCells(Planet));
        Assert.Equal(new Vector3i(5, 63, 5), water.WorldPosition);
        Assert.Equal(4, water.Level);
        Assert.True(water.Falling);

        var fire = Assert.Single(loaded.ListFireCells(Planet));
        Assert.Equal(new Vector3i(6, 64, 6), fire.WorldPosition);
        Assert.Equal(2.5, fire.Remaining);
        Assert.Equal(3, fire.Generation);
    }

    [Fact]
    public void ABlobFromBeforeTheNewFields_StillLoads_Unattributed()
    {
        const string json = "{\"Version\":1,\"BlockEdits\":[{\"Planet\":\"terra\",\"X\":1,\"Y\":64,\"Z\":2,\"Block\":7}]}";
        using var repo = NewRepo();
        repo.ImportSnapshotBlob(Gzip(json));

        var attribution = repo.GetBlockAttribution(Planet, new Vector3i(1, 64, 2))!.Value;
        Assert.Equal(string.Empty, attribution.Owner);
        Assert.Null(attribution.EditedUtc);
        Assert.Empty(repo.ListFluidCells(Planet));
        Assert.Empty(repo.ListFireCells(Planet));
    }

    [Fact]
    public void AnImport_OverAUsedRepository_LeavesNoCellOfTheOldWorld()
    {
        using var empty = NewRepo("empty");
        byte[] blob = empty.ExportSnapshotBlob();

        using var used = NewRepo();
        used.SaveCrystalCell(new StoredCrystalCell { Planet = Planet, X = 1, Y = 64, Z = 1, Kind = "Conduit", OwnerId = "pilot" });
        used.SaveFluidCell(Planet, new Vector3i(2, 64, 2), 3, falling: false);
        used.SaveFireCell(Planet, new Vector3i(3, 64, 3), 1.0, 1);

        used.ImportSnapshotBlob(blob);

        Assert.Empty(used.ListCrystalCells(Planet));
        Assert.Empty(used.ListFluidCells(Planet));
        Assert.Empty(used.ListFireCells(Planet));
    }

    [Fact]
    public void AnAlliance_IsOneSortedRow_WhicheverPlayerAsked()
    {
        using var repo = NewRepo();
        repo.SaveAlliance(new StoredAlliance { PlayerA = "wing", PlayerB = "pilot", FormedUtc = "2026-10-18T10:00:00Z" });
        repo.SaveAlliance(new StoredAlliance { PlayerA = "pilot", PlayerB = "wing", FormedUtc = "2026-10-18T11:00:00Z" });

        var row = Assert.Single(repo.ListAlliances());
        Assert.Equal(("pilot", "wing"), (row.PlayerA, row.PlayerB));
        Assert.Equal("2026-10-18T11:00:00Z", row.FormedUtc);

        repo.DeleteAlliance("wing", "pilot");
        Assert.Empty(repo.ListAlliances());
    }

    [Fact]
    public void AnOlderBlob_WithAnUnsortedOrDoubledPair_FoldsToOneRow()
    {
        const string json = "{\"Version\":1,\"Alliances\":["
            + "{\"PlayerA\":\"wing\",\"PlayerB\":\"pilot\",\"FormedUtc\":\"a\"},"
            + "{\"PlayerA\":\"pilot\",\"PlayerB\":\"wing\",\"FormedUtc\":\"b\"}]}";
        using var repo = NewRepo();
        repo.ImportSnapshotBlob(Gzip(json));

        var row = Assert.Single(repo.ListAlliances());
        Assert.Equal(("pilot", "wing"), (row.PlayerA, row.PlayerB));
    }

    private static byte[] Gzip(string json)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return buffer.ToArray();
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
            // Cleanup must never hide the actual test result.
        }
    }
}
