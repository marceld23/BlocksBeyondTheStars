// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The local crash spool is the durable retry queue: it writes report bodies to disk, lists the
/// unsent ones, and relocates accepted ones to <c>sent/</c> (never deletes). Best-effort and never throws.</summary>
public sealed class CrashReportSpoolTests : IDisposable
{
    private readonly string _dir;

    public CrashReportSpoolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bbts_clientcrash_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Write_ThenListAndRead_RoundTrips()
    {
        var spool = new CrashReportSpool(_dir);
        Assert.Equal(0, spool.CountPending());

        string? path = spool.Write("{\"kind\":\"crash\"}", "20260628_120000");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.StartsWith("crash_", Path.GetFileName(path!));

        Assert.Equal(1, spool.CountPending());
        Assert.Equal("{\"kind\":\"crash\"}", spool.Read(path!));
    }

    [Fact]
    public void MarkSent_MovesOutOfPending_ButKeepsFile()
    {
        var spool = new CrashReportSpool(_dir);
        string? path = spool.Write("{\"a\":1}", "ts");
        Assert.NotNull(path);

        spool.MarkSent(path!);

        Assert.Equal(0, spool.CountPending());                       // no longer queued
        Assert.False(File.Exists(path));                             // moved...
        string sent = Path.Combine(_dir, "sent");
        Assert.True(Directory.Exists(sent));
        Assert.Single(Directory.GetFiles(sent));                     // ...into sent/, not deleted
    }

    [Fact]
    public void EmptyDirectory_DisablesSpool_NeverThrows()
    {
        var spool = new CrashReportSpool(string.Empty);
        Assert.Null(spool.Write("{\"a\":1}", "ts"));
        Assert.Empty(spool.ListPending());
        Assert.Equal(0, spool.CountPending());
    }

    [Fact]
    public void EmptyBody_WritesNothing()
    {
        var spool = new CrashReportSpool(_dir);
        Assert.Null(spool.Write("", "ts"));
        Assert.Equal(0, spool.CountPending());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
