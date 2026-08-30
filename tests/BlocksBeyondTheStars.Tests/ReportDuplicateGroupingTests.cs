// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Feedback;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Every in-game F1 report reaches the inbox twice by design — the client posts to /api/bugreport itself, and
/// the game server forwards its /bump snapshot to the same endpoint. The admin list must show one row per
/// report instead of double-counting, while ingest and the read API keep both records.
/// <para>
/// The two halves do NOT share a player id (#1359): the client row carries the install token, the server
/// forward the player name — which is why the fixture below stamps them differently, exactly like production.
/// </para>
/// </summary>
public sealed class ReportDuplicateGroupingTests
{
    /// <summary>What the client-direct row carries as <c>playerId</c>: the install's name-claim token.</summary>
    private const string Token = "417de473e6b84861afaf0c0ffee0badd";

    private static BugReportRecord Row(
        string id,
        string title,
        string description,
        long createdUnix,
        string source = "",
        string screenshot = "",
        string status = "new",
        string category = "feedback",
        string playerName = "Pilot",
        string version = "2026.7.22",
        string replyKey = "")
        => new(
            Id: id,
            Title: title,
            Description: description,
            Email: "",
            GameVersion: version,
            BuildNumber: "",
            // The real shape: the server forward's player id is the NAME, the client row's is the token.
            PlayerId: source == "server" ? playerName : Token,
            PlayerName: playerName,
            SessionId: "",
            Platform: "",
            ClientTimestamp: "",
            Category: category,
            Source: source,
            Kind: "",
            Status: status,
            ScreenshotFile: screenshot,
            ReportJson: source == "server" ? "{\"snapshot\":{}}" : "{}",
            CreatedUnix: createdUnix,
            ReplyKey: replyKey,
            FixedInVersion: "");

    /// <summary>The real shape of a pair, taken from a live report: the server forward wraps the player's own
    /// wording as "[feedback] &lt;title&gt; — &lt;description&gt;", so the client row's text is a substring —
    /// and the two rows carry different player ids (token vs. name), which must not keep them apart.</summary>
    [Fact]
    public void TheTwoRowsOfOneReport_CollapseIntoOneGroup()
    {
        var rows = new[]
        {
            Row("client1", "Treppen Winkel", "Ich will das Treppen in verschiedenen Winkeln platzierbar sind.", 1000),
            Row("server1", "Bump [Minecraft]: [feedback] Treppen Winkel — Ich will das Treppen in ver",
                "[feedback] Treppen Winkel — Ich will das Treppen in verschiedenen Winkeln platzierbar sind.",
                1000, source: "server", screenshot: "bump_1.jpg"),
        };

        Assert.NotEqual(rows[0].PlayerId, rows[1].PlayerId); // the #1359 trap: ids differ by design

        var groups = ReportHostPages.GroupDuplicates(rows);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
    }

    /// <summary>#1380: the rows an operator action covers — the addressed row first, then its paired half; an
    /// unrelated report inside the same window is never part of it.</summary>
    [Fact]
    public void PairOf_ReturnsTheRowAndItsHalf_NeverAStranger()
    {
        var client = Row("client1", "Treppen Winkel", "Ich will das Treppen in verschiedenen Winkeln platzierbar sind.", 1000);
        var server = Row("server1", "Bump [Minecraft]: [feedback] Treppen Winkel — Ich will das Treppen in ver",
            "[feedback] Treppen Winkel — Ich will das Treppen in verschiedenen Winkeln platzierbar sind.",
            1001, source: "server", screenshot: "bump_1.jpg");
        var stranger = Row("server2", "Bump [w]: [feedback] Lampe — geht aus.", "[feedback] Lampe — geht aus.", 1002, source: "server", playerName: "Justus");
        var window = new[] { client, server, stranger };

        var pair = ReportHostPages.PairOf(server, window);
        Assert.Equal(2, pair.Count);
        Assert.Equal("server1", pair[0].Id);
        Assert.Equal("client1", pair[1].Id);

        pair = ReportHostPages.PairOf(client, window);
        Assert.Equal(2, pair.Count);
        Assert.Equal("client1", pair[0].Id);
        Assert.Equal("server1", pair[1].Id);

        pair = ReportHostPages.PairOf(stranger, window);
        Assert.Single(pair);
        Assert.Equal("server2", pair[0].Id);
    }

    /// <summary>A #1359 client passes its reply key through /bump, so both halves carry the same key — the
    /// exact identity, which wins over the name (here the player renamed between the two uploads).</summary>
    [Fact]
    public void HalvesWithTheSameReplyKey_PairEvenWhenTheNameDiffers()
    {
        string key = FeedbackReplyKey.Derive("install-secret");
        var rows = new[]
        {
            Row("client1", "Lampe", "Die Helmlampe geht im Wasser aus.", 1000, playerName: "Pilot", replyKey: key),
            Row("server1", "Bump [w]: [feedback] Lampe — Die Helmlampe geht im Wasser aus.",
                "[feedback] Lampe — Die Helmlampe geht im Wasser aus.", 1001, source: "server", playerName: "Justus", replyKey: key),
        };

        Assert.Single(ReportHostPages.GroupDuplicates(rows));
    }

    /// <summary>Two installs never share a key: same name, same wording, same second — still two reports when
    /// both halves carry keys that differ (e.g. two kids both called "Pilot" on two machines).</summary>
    [Fact]
    public void DifferentReplyKeys_NeverPair()
    {
        var rows = new[]
        {
            Row("a", "Absturz", "Das Spiel ist abgestürzt.", 1000, replyKey: FeedbackReplyKey.Derive("machine-1")),
            Row("b", "Bump [w]: [feedback] Absturz — Das Spiel ist abgestürzt.", "[feedback] Absturz — Das Spiel ist abgestürzt.",
                1000, source: "server", replyKey: FeedbackReplyKey.Derive("machine-2")),
        };

        Assert.Equal(2, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void UnrelatedReports_StayApart()
    {
        var rows = new[]
        {
            Row("a", "Erfolge", "Ich möchte das es Erfolge gibt.", 1000),
            Row("b", "Holztüren", "Ich möchte einfache Holztüren.", 1002), // same moment, different report
        };

        Assert.Equal(2, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void SameTextFarApartInTime_StaysApart()
    {
        // The player hitting the same wall twice an hour later is two reports, not a duplicated one.
        var rows = new[]
        {
            Row("a", "Das 2 Blöcke Problem", "Ich kann durch einen 2 Blöcke hohen bereich nicht durch", 1000),
            Row("b", "Das 2 Blöcke Problem", "Ich kann durch einen 2 Blöcke hohen bereich nicht durch", 4600),
        };

        Assert.Equal(2, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void ReportsFromDifferentPlayersOrBuilds_NeverPairUp()
    {
        var rows = new[]
        {
            Row("a", "Absturz", "Das Spiel ist abgestürzt.", 1000, playerName: "Justus"),
            Row("b", "Absturz", "Das Spiel ist abgestürzt.", 1001, playerName: "Severin"),
            Row("c", "Absturz", "Das Spiel ist abgestürzt.", 1001, playerName: "Justus", version: "2026.7.21"),
        };

        Assert.Equal(3, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void RowsWithoutAnyReporterIdentity_StayApart()
    {
        // No key and no name on either side — nothing to prove they are the same reporter.
        var rows = new[]
        {
            Row("a", "x", "Der Bohrer bohrt nicht.", 1000, playerName: ""),
            Row("b", "Bump: [feedback] x — Der Bohrer bohrt nicht.", "[feedback] x — Der Bohrer bohrt nicht.", 1000, source: "server", playerName: ""),
        };

        Assert.Equal(2, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void EmptyDescriptions_AreNeverTreatedAsDuplicates()
    {
        var rows = new[]
        {
            Row("a", "one", "", 1000),
            Row("b", "two", "", 1001),
        };

        Assert.Equal(2, ReportHostPages.GroupDuplicates(rows).Count);
    }

    [Fact]
    public void EveryRowSurvivesGrouping()
    {
        var rows = new[]
        {
            Row("client1", "Items futsch", "Ich habe Glas gemacht, NICHTS DA.", 1000),
            Row("server1", "Bump: [feedback] Items futsch", "[feedback] Items futsch — Ich habe Glas gemacht, NICHTS DA.", 1001, source: "server"),
            Row("lonely", "Nur einmal", "Dieser Bericht kam nur einmal an.", 5000),
        };

        var groups = ReportHostPages.GroupDuplicates(rows);

        // Grouping is a display concern — it must never drop a record.
        Assert.Equal(rows.Length, groups.Sum(g => g.Count));
        Assert.Equal(2, groups.Count);
    }
}
