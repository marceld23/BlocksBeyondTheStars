// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Where a local world's "last played" picture lives and how it is written (<see cref="WorldPreviewFiles"/>): inside the
/// world's own save folder (so deleting the world deletes it), atomically, never into a folder that is gone, and read back
/// only when it is a sane size.
/// </summary>
public sealed class WorldPreviewFilesTests : IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "bbs-preview-" + Guid.NewGuid().ToString("N"));

    public WorldPreviewFilesTests() => Directory.CreateDirectory(_saves);

    public void Dispose()
    {
        if (Directory.Exists(_saves))
        {
            Directory.Delete(_saves, recursive: true);
        }
    }

    private string World(string name)
    {
        string dir = Path.Combine(_saves, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ThePicture_LivesInTheWorldsSaveFolder()
    {
        string? path = WorldPreviewFiles.PathFor(_saves, "My world");

        Assert.Equal(Path.Combine(Path.GetFullPath(_saves), "My world", "preview.jpg"), path);
        Assert.Equal(WorldPreviewFiles.FileName, Path.GetFileName(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void ANameThatIsNoSaveFolder_HasNoPicturePath(string? name)
    {
        Assert.Null(WorldPreviewFiles.PathFor(_saves, name));
    }

    [Fact]
    public void WithoutASavesRoot_ThereIsNoPicturePath()
    {
        Assert.Null(WorldPreviewFiles.PathFor(null, "My world"));
        Assert.Null(WorldPreviewFiles.PathFor("", "My world"));
    }

    [Fact]
    public void AName_IsMappedToItsFolder_LikeTheServerDoes()
    {
        // '/' is no file-name character on any platform: the server's SaveGamePaths turns it into '_', so does the picture.
        string? path = WorldPreviewFiles.PathFor(_saves, "a/b");

        Assert.Equal(Path.Combine(Path.GetFullPath(_saves), "a_b", WorldPreviewFiles.FileName), path);
        Assert.Equal("a_b", WorldPreviewFiles.FolderName("a/b"));
        Assert.Equal("world", WorldPreviewFiles.FolderName(" "));
    }

    [Fact]
    public void Write_ReplacesThePictureAtomically_AndLeavesNoTempFile()
    {
        World("Alpha");
        string path = WorldPreviewFiles.PathFor(_saves, "Alpha")!;

        Assert.True(WorldPreviewFiles.Write(path, new byte[] { 1, 2, 3 }));
        Assert.True(WorldPreviewFiles.Write(path, new byte[] { 4, 5 }));

        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + WorldPreviewFiles.TempSuffix));
        Assert.Equal(new byte[] { 4, 5 }, WorldPreviewFiles.TryRead(path));
    }

    [Fact]
    public void Write_NeverBringsBackADeletedWorld()
    {
        string path = WorldPreviewFiles.PathFor(_saves, "Gone")!;

        Assert.False(WorldPreviewFiles.Write(path, new byte[] { 1 }));
        Assert.False(Directory.Exists(Path.Combine(_saves, "Gone")), "no empty folder appears for a world that is not there");
    }

    [Fact]
    public void Write_RefusesNothingAndTooMuch()
    {
        World("Alpha");
        string path = WorldPreviewFiles.PathFor(_saves, "Alpha")!;

        Assert.False(WorldPreviewFiles.Write(path, null));
        Assert.False(WorldPreviewFiles.Write(path, Array.Empty<byte>()));
        Assert.False(WorldPreviewFiles.Write(path, new byte[WorldPreviewFiles.MaxBytes + 1]));
        Assert.False(WorldPreviewFiles.Write(null, new byte[] { 1 }));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeletingTheWorldFolder_DeletesThePicture()
    {
        string dir = World("Alpha");
        string path = WorldPreviewFiles.PathFor(_saves, "Alpha")!;
        Assert.True(WorldPreviewFiles.Write(path, new byte[] { 1 }));

        Directory.Delete(dir, recursive: true); // what LocalServerLauncher.DeleteWorld does

        Assert.Null(WorldPreviewFiles.TryRead(path));
        Assert.Null(WorldPreviewFiles.WrittenUtc(path));
    }

    [Fact]
    public void TryRead_SkipsAMissingEmptyOrOversizedFile()
    {
        string dir = World("Alpha");
        string path = Path.Combine(dir, WorldPreviewFiles.FileName);

        Assert.Null(WorldPreviewFiles.TryRead(path));
        Assert.Null(WorldPreviewFiles.TryRead(null));

        File.WriteAllBytes(path, Array.Empty<byte>());
        Assert.Null(WorldPreviewFiles.TryRead(path));

        File.WriteAllBytes(path, new byte[WorldPreviewFiles.MaxBytes + 1]);
        Assert.Null(WorldPreviewFiles.TryRead(path));
    }

    [Fact]
    public void WrittenUtc_NamesWhenThePictureWasWritten()
    {
        World("Alpha");
        string path = WorldPreviewFiles.PathFor(_saves, "Alpha")!;
        Assert.Null(WorldPreviewFiles.WrittenUtc(path));

        WorldPreviewFiles.Write(path, new byte[] { 1 });
        var when = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, when);

        Assert.Equal(when, WorldPreviewFiles.WrittenUtc(path));
    }

    [Fact]
    public void ThePicture_Is16By9AndSmall()
    {
        Assert.Equal(16f / 9f, (float)WorldPreviewFiles.Width / WorldPreviewFiles.Height, 3);
        Assert.InRange(WorldPreviewFiles.JpegQuality, 70, 90);
    }

    [Fact]
    public void CoverRect_ShowsAPictureOfTheBoxShapeWhole()
    {
        Assert.Equal((0f, 0f, 1f, 1f), WorldPreviewFiles.CoverRect(640, 360, 480f, 270f));
        Assert.Equal((0f, 0f, 1f, 1f), WorldPreviewFiles.CoverRect(0, 360, 480f, 270f));
        Assert.Equal((0f, 0f, 1f, 1f), WorldPreviewFiles.CoverRect(640, 360, 0f, 270f));
    }

    [Fact]
    public void CoverRect_CutsTheSidesOfAWiderPicture_AndTheTopAndBottomOfATallerOne()
    {
        var (x, y, w, h) = WorldPreviewFiles.CoverRect(400, 100, 200f, 100f); // 4:1 into 2:1 — half the width shows
        Assert.Equal(0.25f, x, 4);
        Assert.Equal(0f, y, 4);
        Assert.Equal(0.5f, w, 4);
        Assert.Equal(1f, h, 4);

        (x, y, w, h) = WorldPreviewFiles.CoverRect(100, 100, 200f, 100f); // 1:1 into 2:1 — half the height shows
        Assert.Equal(0f, x, 4);
        Assert.Equal(0.25f, y, 4);
        Assert.Equal(1f, w, 4);
        Assert.Equal(0.5f, h, 4);
    }
}
