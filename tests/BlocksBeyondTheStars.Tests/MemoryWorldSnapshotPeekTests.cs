// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The browser client adopts the saved world's player name before it asks for one (#1322): the name is
/// the player id, so a world with exactly one player belongs to that player. The peek must answer from
/// the real snapshot blob format, refuse to guess for empty or shared worlds, and shrug off garbage.
/// </summary>
public sealed class MemoryWorldSnapshotPeekTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_peek_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best effort
        }
    }

    private MemoryWorldRepository NewRepo() => new(new SaveGamePaths(_root, "peek"));

    [Fact]
    public void OnePlayer_IsTheWorldsOwner()
    {
        using var repo = NewRepo();
        repo.SavePlayer(new PlayerState { PlayerId = "Justus", Name = "Justus" });

        Assert.Equal("Justus", MemoryWorldSnapshotPeek.SolePlayerId(repo.ExportSnapshotBlob()));
    }

    [Fact]
    public void NoPlayer_GivesNoName()
    {
        using var repo = NewRepo();
        Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(repo.ExportSnapshotBlob()));
    }

    [Fact]
    public void TwoPlayers_GiveNoName_NobodyIsTheOwner()
    {
        using var repo = NewRepo();
        repo.SavePlayer(new PlayerState { PlayerId = "Justus", Name = "Justus" });
        repo.SavePlayer(new PlayerState { PlayerId = "Marcel", Name = "Marcel" });

        Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(repo.ExportSnapshotBlob()));
    }

    [Fact]
    public void OnlyTheTopLevelPlayersTable_Counts()
    {
        // A nested object that happens to be called "Players" (inside another table) must not be mistaken
        // for the player list, and the real table is found even after megabytes of other tables.
        string json = "{\"Ships\":{\"Players\":{\"ghost\":1}},\"BlockEdits\":[{\"X\":1},{\"X\":2}],\"Players\":{\"Lyxette\":{\"Name\":\"Lyxette\"}},\"Bases\":[]}";
        Assert.Equal("Lyxette", MemoryWorldSnapshotPeek.SolePlayerId(Gzip(json)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    public void GarbageOrNothing_GivesNoName_AndNeverThrows(byte[]? blob)
        => Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(blob));

    [Fact]
    public void ValidGzipWithoutAWorld_GivesNoName()
    {
        Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(Gzip("[1,2,3]")));
        Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(Gzip("{\"Version\":1}")));
        Assert.Null(MemoryWorldSnapshotPeek.SolePlayerId(Gzip("{\"Players\":{\"\":{}}}"))); // a blank id is no name
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
}
