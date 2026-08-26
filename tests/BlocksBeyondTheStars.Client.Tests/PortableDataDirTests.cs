// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The portable-data redirect (#1285): a <c>portable_data_dir.txt</c> next to the executable moves the
/// persistent-data root. Rules under test: no marker → null (platform default); empty marker → <c>userdata</c>
/// next to the exe; relative paths anchor at the exe folder; absolute paths, quotes, comments, blank lines
/// and environment variables are handled; an unwritable target is reported instead of thrown.
/// </summary>
public sealed class PortableDataDirTests : IDisposable
{
    private readonly string _exeDir = Path.Combine(Path.GetTempPath(), "bbs-portable-" + Guid.NewGuid().ToString("N"));

    public PortableDataDirTests()
    {
        Directory.CreateDirectory(_exeDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_exeDir, recursive: true); } catch (IOException) { }
    }

    private void WriteMarker(string content) =>
        File.WriteAllText(Path.Combine(_exeDir, PortableDataDir.MarkerFileName), content);

    [Fact]
    public void NoMarker_KeepsTheDefault()
    {
        string? root = PortableDataDir.ResolveFromMarker(_exeDir, out string? error);
        Assert.Null(root);
        Assert.Null(error);
    }

    [Fact]
    public void EmptyMarker_UsesUserdataNextToTheExecutable()
    {
        WriteMarker("");
        string? root = PortableDataDir.ResolveFromMarker(_exeDir, out string? error);
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(Path.Combine(_exeDir, PortableDataDir.DefaultSubfolder)), root);
    }

    [Fact]
    public void CommentsAndBlankLinesOnly_CountAsEmpty()
    {
        WriteMarker("# where the data goes\r\n\r\n   # nothing yet\n");
        string? root = PortableDataDir.ResolveFromMarker(_exeDir, out _);
        Assert.Equal(Path.GetFullPath(Path.Combine(_exeDir, "userdata")), root);
    }

    [Fact]
    public void RelativePath_IsAnchoredAtTheExecutableFolder()
    {
        WriteMarker("# comment first\n  ../BBTS-Data  \n");
        string? root = PortableDataDir.ResolveFromMarker(_exeDir, out _);
        Assert.Equal(Path.GetFullPath(Path.Combine(_exeDir, "..", "BBTS-Data")), root);
    }

    [Fact]
    public void AbsolutePath_IsUsedAsIs_QuotesStripped()
    {
        string target = Path.Combine(_exeDir, "My Games", "bbts");
        WriteMarker($"\"{target}\"\n");
        string? root = PortableDataDir.ResolveFromMarker(_exeDir, out _);
        Assert.Equal(Path.GetFullPath(target), root);
    }

    [Fact]
    public void FirstDirectiveWins_LaterLinesIgnored()
    {
        string first = Path.Combine(_exeDir, "first");
        string second = Path.Combine(_exeDir, "second");
        Assert.Equal(Path.GetFullPath(first), PortableDataDir.Resolve(_exeDir, first + "\n" + second));
    }

    [Fact]
    public void EnvironmentVariables_AreExpanded()
    {
        string name = "BBTS_TEST_PORTABLE_" + Guid.NewGuid().ToString("N");
        string value = Path.Combine(_exeDir, "from-env");
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            Assert.Equal(Path.GetFullPath(value), PortableDataDir.Resolve(_exeDir, $"%{name}%"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void ByteOrderMark_DoesNotPoisonTheFirstLine()
    {
        Assert.Equal(Path.GetFullPath(Path.Combine(_exeDir, "data")), PortableDataDir.Resolve(_exeDir, "﻿data"));
    }

    [Fact]
    public void TryPrepare_CreatesTheFolderAndLeavesNoProbeBehind()
    {
        string dir = Path.Combine(_exeDir, "userdata", "nested");
        Assert.True(PortableDataDir.TryPrepare(dir, out string? error));
        Assert.Null(error);
        Assert.True(Directory.Exists(dir));
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public void TryPrepare_UnusablePath_ReportsInsteadOfThrowing()
    {
        // A directory path that collides with an existing FILE can never be created.
        string file = Path.Combine(_exeDir, "iam-a-file");
        File.WriteAllText(file, "x");
        Assert.False(PortableDataDir.TryPrepare(Path.Combine(file, "sub"), out string? error));
        Assert.Contains("not writable", error);
    }
}
