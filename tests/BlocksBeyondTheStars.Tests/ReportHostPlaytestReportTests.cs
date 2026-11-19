// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The closing report of a playtest task as the inbox files it: its own category, the tester's work as an optional
/// attachment (which plain feedback may never carry), and the pairing of the client's row with the server's /bump
/// snapshot of the same moment — the two arrive under different categories, and an operator action must reach both.
/// </summary>
public sealed class ReportHostPlaytestReportTests
{
    private static readonly string Key = FeedbackReplyKey.Derive("playtest-install");

    private static string Payload(string reportType, bool attachment)
    {
        string files = attachment
            ? ",\"attachments\":[{\"fileName\":\"ship.json\",\"mimeType\":\"application/json\",\"base64\":\""
              + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"formatVersion\":1}")) + "\"}]"
            : string.Empty;
        return "{\"title\":\"Testpilot: Der schwere Schuss\",\"description\":\"Auftrag: Der schwere Schuss\","
               + "\"gameVersion\":\"2026.11.22\",\"playerName\":\"Layex\",\"replyKey\":\"" + Key + "\","
               + "\"reportJson\":{\"reportType\":\"" + reportType + "\",\"playtest\":{\"taskId\":\"heavy-shot\",\"rev\":2}}"
               + files + "}";
    }

    private static BugReportRecord Row(string id, string category, string description, long createdUnix, string source = "",
        string replyKey = "", string playerName = "Layex")
        => new(
            Id: id,
            Title: "t",
            Description: description,
            Email: "",
            GameVersion: "2026.11.22",
            BuildNumber: "",
            PlayerId: source == "server" ? playerName : "install-token",
            PlayerName: playerName,
            SessionId: "",
            Platform: "",
            ClientTimestamp: "",
            Category: category,
            Source: source,
            Kind: "",
            Status: "new",
            ScreenshotFile: "",
            ReportJson: "{}",
            CreatedUnix: createdUnix,
            ReplyKey: replyKey,
            FixedInVersion: "");

    [Fact]
    public void PlaytestReport_GetsItsOwnCategory()
    {
        var report = ReportIngest.Parse(Payload(ReportIngest.PlaytestReportType, attachment: false), new ReportHostConfig(), out string error);

        Assert.NotNull(report);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ReportIngest.PlaytestCategory, report!.Category);
        Assert.Equal(Key, report.ReplyKey);
        Assert.False(ReportIngest.IsSubmission(report.Category)); // never offered for adoption, never pruned as a submission
    }

    [Fact]
    public void PlaytestReport_MayCarryTheTestersWork_PlainFeedbackMayNot()
    {
        var config = new ReportHostConfig();

        var playtest = ReportIngest.Parse(Payload(ReportIngest.PlaytestReportType, attachment: true), config, out _)!;
        var feedback = ReportIngest.Parse(Payload("something-else", attachment: true), config, out _)!;

        var kept = Assert.Single(playtest.Attachments);
        Assert.Equal("application/json", kept.Mime);
        Assert.Equal("feedback", feedback.Category);
        Assert.Empty(feedback.Attachments);
        Assert.Equal(config.MaxCreationAttachmentBytes, config.MaxAttachmentBytesFor(ReportIngest.PlaytestCategory));
    }

    [Fact]
    public void PlaytestReport_PairsWithTheServersSnapshotOfTheSameMoment()
    {
        var client = Row("a", ReportIngest.PlaytestCategory, "Auftrag: Der schwere Schuss", 1000, replyKey: Key);
        var snapshot = Row("b", "feedback", "[feedback] Testpilot — Auftrag: Der schwere Schuss", 1003, source: "server", replyKey: Key);

        var groups = ReportHostPages.GroupDuplicates(new[] { client, snapshot });

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(new[] { "b", "a" }, ReportHostPages.PairOf(snapshot, new[] { client, snapshot }).ConvertAll(r => r.Id));
    }

    [Fact]
    public void PlaytestReport_DoesNotPairWithStrangers()
    {
        var client = Row("a", ReportIngest.PlaytestCategory, "Auftrag: Der schwere Schuss", 1000, replyKey: Key);

        // Another install's snapshot, a client-direct feedback of the same player, a crash, and a snapshot much later.
        var otherInstall = Row("b", "feedback", "[feedback] Testpilot — Auftrag: Der schwere Schuss", 1002, source: "server",
            replyKey: FeedbackReplyKey.Derive("someone-else"));
        var ownFeedback = Row("c", "feedback", "Auftrag: Der schwere Schuss", 1002, replyKey: Key);
        var crash = Row("d", "crash", "Auftrag: Der schwere Schuss", 1002, source: "server", replyKey: Key);
        var late = Row("e", "feedback", "[feedback] Testpilot — Auftrag: Der schwere Schuss", 5000, source: "server", replyKey: Key);

        foreach (var other in new[] { otherInstall, ownFeedback, crash, late })
        {
            Assert.Equal(2, ReportHostPages.GroupDuplicates(new[] { client, other }).Count);
        }
    }
}
