// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.ReportHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Every in-game F1 report reaches the inbox twice by design — the client posts to /api/bugreport itself, and
/// the game server forwards its /bump snapshot to the same endpoint. The admin list must show one row per
/// report instead of double-counting, while ingest and the read API keep both records.
/// </summary>
public sealed class ReportDuplicateGroupingTests
{
    private static BugReportRecord Row(
        string id,
        string title,
        string description,
        long createdUnix,
        string source = "",
        string screenshot = "",
        string status = "new",
        string category = "feedback",
        string playerId = "Pilot",
        string version = "2026.7.22")
        => new(
            Id: id,
            Title: title,
            Description: description,
            Email: "",
            GameVersion: version,
            BuildNumber: "",
            PlayerId: playerId,
            PlayerName: "Pilot",
            SessionId: "",
            Platform: "",
            ClientTimestamp: "",
            Category: category,
            Source: source,
            Kind: "",
            Status: status,
            ScreenshotFile: screenshot,
            ReportJson: source == "server" ? "{\"snapshot\":{}}" : "{}",
            CreatedUnix: createdUnix);

    /// <summary>The real shape of a pair, taken from a live report: the server forward wraps the player's own
    /// wording as "[feedback] &lt;title&gt; — &lt;description&gt;", so the client row's text is a substring.</summary>
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

        var groups = ReportHostPages.GroupDuplicates(rows);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Count);
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
            Row("a", "Absturz", "Das Spiel ist abgestürzt.", 1000, playerId: "Justus"),
            Row("b", "Absturz", "Das Spiel ist abgestürzt.", 1001, playerId: "Severin"),
            Row("c", "Absturz", "Das Spiel ist abgestürzt.", 1001, playerId: "Justus", version: "2026.7.21"),
        };

        Assert.Equal(3, ReportHostPages.GroupDuplicates(rows).Count);
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
