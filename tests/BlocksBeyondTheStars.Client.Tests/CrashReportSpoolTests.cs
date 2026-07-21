// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The local crash spool is the durable retry queue: it writes report bodies to disk, lists the
/// unsent ones, and relocates accepted ones to <c>sent/</c>. Both folders are capped, oldest pruned first
/// (#421 M14 — a never-uploading install must not grow the spool forever). Best-effort and never throws.</summary>
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
    public void Write_BeyondMaxPending_PrunesOldestFirst()
    {
        var spool = new CrashReportSpool(_dir);
        string? oldest = spool.Write("{\"n\":0}", "20260628_000000");
        Assert.NotNull(oldest);
        for (int i = 1; i <= CrashReportSpool.MaxPending + 4; i++)
        {
            Assert.NotNull(spool.Write($"{{\"n\":{i}}}", $"20260628_{i:D6}"));
        }

        Assert.Equal(CrashReportSpool.MaxPending, spool.CountPending());
        Assert.False(File.Exists(oldest));                           // the oldest reports made room...
        Assert.Contains(spool.ListPending(),
            p => Path.GetFileName(p).Contains($"_{CrashReportSpool.MaxPending + 4:D6}_")); // ...the newest survives
    }

    [Fact]
    public void MarkSent_BeyondMaxSent_PrunesSentArchive()
    {
        var spool = new CrashReportSpool(_dir);
        for (int i = 0; i < CrashReportSpool.MaxSent + 3; i++)
        {
            string? path = spool.Write($"{{\"n\":{i}}}", $"20260628_{i:D6}");
            Assert.NotNull(path);
            spool.MarkSent(path!);
        }

        Assert.Equal(0, spool.CountPending());
        Assert.Equal(CrashReportSpool.MaxSent, Directory.GetFiles(Path.Combine(_dir, "sent")).Length);
    }

    [Fact]
    public void Write_PublishesAtomically_LeavesNoTempAndReadsBackWhole()
    {
        // The body is written to a ".tmp" sibling and renamed into place (#425 N14) so a concurrent
        // FlushPending reader never observes a half-written file. After a successful Write the spool must
        // hold only the finished report — the temp name must neither linger nor leak into the pending scan.
        var spool = new CrashReportSpool(_dir);
        string body = "{\"kind\":\"crash\",\"payload\":\"" + new string('x', 4096) + "\"}";

        string? path = spool.Write(body, "20260721_120000");
        Assert.NotNull(path);
        Assert.EndsWith(".json", path);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));          // no leftover temp file
        Assert.Single(spool.ListPending());                       // scan sees exactly the complete report...
        Assert.Equal(body, spool.Read(path!));                    // ...and it round-trips whole
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
