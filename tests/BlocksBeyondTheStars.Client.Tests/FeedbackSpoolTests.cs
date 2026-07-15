// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Client.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The bounded feedback retry queue: a failed F1 upload survives on disk, every later failed
/// attempt is counted in the file name, and a report leaves the queue either as <c>sent/</c> or — after
/// <see cref="FeedbackSpool.MaxAttempts"/> — as <c>givenup/</c>: it never retries forever and is never
/// silently deleted.</summary>
public sealed class FeedbackSpoolTests : IDisposable
{
    private readonly string _dir;

    public FeedbackSpoolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bbts_feedbackspool_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Write_ThenListAndRead_RoundTrips()
    {
        var spool = new FeedbackSpool(_dir);
        Assert.Empty(spool.ListPending());

        string? path = spool.Write("{\"description\":\"door ate my hat\"}", "20260715_120000");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.StartsWith("feedback_", Path.GetFileName(path!));
        Assert.EndsWith("_try0.json", Path.GetFileName(path!));

        Assert.Single(spool.ListPending());
        Assert.Equal("{\"description\":\"door ate my hat\"}", spool.Read(path!));
    }

    [Fact]
    public void MarkSent_MovesOutOfPending_ButKeepsFile()
    {
        var spool = new FeedbackSpool(_dir);
        string? path = spool.Write("{\"a\":1}", "ts");
        Assert.NotNull(path);

        spool.MarkSent(path!);

        Assert.Empty(spool.ListPending());
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "sent")));
    }

    [Fact]
    public void RegisterFailedAttempt_CountsUp_AndParksAfterMaxAttempts()
    {
        var spool = new FeedbackSpool(_dir);
        Assert.NotNull(spool.Write("{\"a\":1}", "ts"));

        // Attempts 1 … MaxAttempts-1 keep the report queued, each under the next _tryN name.
        for (int attempt = 1; attempt < FeedbackSpool.MaxAttempts; attempt++)
        {
            string queued = Assert.Single(spool.ListPending());
            Assert.True(spool.RegisterFailedAttempt(queued));
            Assert.EndsWith($"_try{attempt}.json", Path.GetFileName(Assert.Single(spool.ListPending())));
        }

        // The final permitted attempt parks it in givenup/ — gone from the queue, kept on disk.
        Assert.False(spool.RegisterFailedAttempt(Assert.Single(spool.ListPending())));
        Assert.Empty(spool.ListPending());
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "givenup")));
    }

    [Fact]
    public void EmptyDirectoryOrBody_DisablesSpool_NeverThrows()
    {
        var disabled = new FeedbackSpool(string.Empty);
        Assert.Null(disabled.Write("{\"a\":1}", "ts"));
        Assert.Empty(disabled.ListPending());

        var spool = new FeedbackSpool(_dir);
        Assert.Null(spool.Write("", "ts"));
        Assert.Empty(spool.ListPending());
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
