// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Block-edit attribution (issue #490): who last changed a cell, and when.
///
/// The design decision these tests pin down is that <c>block_edit</c> stays keyed by CELL — attribution adds
/// columns, never rows, so the table cannot grow with playtime. It is "last editor wins", not a history: the
/// question it has to answer is "who tore my house down", and that is by definition the most recent edit.
/// </summary>
public sealed class BlockAttributionTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteWorldRepository _repo;

    public BlockAttributionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_attrib_" + Guid.NewGuid().ToString("N"));
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "attrib"));
        _repo.Initialize();
    }

    [Fact]
    public void PlayerEdit_RecordsOwnerAndTime()
    {
        var pos = new Vector3i(10, 64, -20);
        _repo.SetBlock("sys0-p1", pos, 5, owner: "Justus");

        var attribution = _repo.GetBlockAttribution("sys0-p1", pos);

        Assert.NotNull(attribution);
        Assert.Equal("Justus", attribution!.Value.Owner);
        Assert.NotNull(attribution.Value.EditedUtc);
        Assert.True((DateTime.UtcNow - attribution.Value.EditedUtc!.Value).TotalMinutes < 5);
    }

    [Fact]
    public void LastEditorWins_AndNoExtraRowIsCreated()
    {
        var pos = new Vector3i(1, 64, 1);
        _repo.SetBlock("sys0-p1", pos, 5, owner: "Justus");   // built
        _repo.SetBlock("sys0-p1", pos, 0, owner: "Severin");  // …and torn down again

        Assert.Equal("Severin", _repo.GetBlockAttribution("sys0-p1", pos)!.Value.Owner);

        // The cell is still ONE row: the table is keyed by (planet,x,y,z), which is why attribution costs
        // columns instead of unbounded growth.
        var chunk = WorldConstants.WorldToChunk(pos);
        var edits = _repo.LoadChunkEdits("sys0-p1", chunk);
        Assert.Single(edits, e => e.WorldPosition.X == pos.X && e.WorldPosition.Y == pos.Y && e.WorldPosition.Z == pos.Z);
    }

    [Fact]
    public void ServerInternalWrite_DoesNotStealAuthorship()
    {
        var pos = new Vector3i(2, 64, 2);
        _repo.SetBlock("sys0-p1", pos, 5, owner: "Justus");
        _repo.SetBlock("sys0-p1", pos, 7); // worldgen stamp / flora regrowth / fluid flow — no owner

        // The player who built here is still the one on record. Otherwise a passing lava flow would quietly
        // launder every grief report on the map.
        Assert.Equal("Justus", _repo.GetBlockAttribution("sys0-p1", pos)!.Value.Owner);
    }

    [Fact]
    public void UntouchedCell_HasNoAttribution()
    {
        // An untouched cell is still procedural baseline — there is no row, and "nobody built here" must be
        // distinguishable from "someone built here anonymously".
        Assert.Null(_repo.GetBlockAttribution("sys0-p1", new Vector3i(999, 64, 999)));
    }

    [Fact]
    public void PreAttributionRow_ReadsAsUnknownOwner()
    {
        var pos = new Vector3i(3, 64, 3);
        _repo.SetBlock("sys0-p1", pos, 5); // stands in for a row written before attribution shipped

        var attribution = _repo.GetBlockAttribution("sys0-p1", pos);

        Assert.NotNull(attribution);
        Assert.Equal(string.Empty, attribution!.Value.Owner); // no back-fill is possible, and none is faked
        Assert.Null(attribution.Value.EditedUtc);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
