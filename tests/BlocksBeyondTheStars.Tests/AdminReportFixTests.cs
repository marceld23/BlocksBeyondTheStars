// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The review fixes of the admin view's report side (#3502): the F1 title travels on its own and the description never
/// reaches the list; the panel open and the report actions are rate-limited; the portal mirror strips, caps and resolves
/// its input to a known player; feedback shares the report rate limit and the list is capped; reports and their events
/// compare player ids case-insensitively; a report is no probe for an invisible admin and "Call an admin" never says whether
/// one is online; the visible variant gets no positions; a hosted world forwards a call to the operators; and a player's
/// text in an event stays literal, with the masked-line events rate-limited.
/// </summary>
public sealed class AdminReportFixTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private long _clockMs = 3_000_000;

    private void Advance(AdminTestWorld w, long ms = 1_000)
    {
        _clockMs += ms;
        w.Server.SetChatClockForTest(_clockMs);
    }

    private void Send(AdminTestWorld w, PlayerSession who, object message)
    {
        Advance(w); // past the per-line throttle
        w.Server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(message));
    }

    private void Say(AdminTestWorld w, PlayerSession who, string text) => Send(w, who, new ChatIntent { Text = text });

    private static StoredPlayerReport Row(string state, long createdUnix, string target = "")
        => new()
        {
            Kind = AdminReportKinds.Help,
            ReporterId = "Someone",
            ReporterName = "Someone",
            TargetId = target,
            TargetName = target,
            CreatedUnix = createdUnix,
            State = state,
            HandledUnix = state == "done" ? createdUnix : 0,
        };

    // ---------------- 1: the F1 title ----------------

    [Fact]
    public void Feedback_FilesTheTitleFieldOnly_NeverAnyPartOfTheDescription()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        Send(w, player, new BumpReport { Description = "[feedback] my mail is kid@example.org — come to my base", FeedbackTitle = "" });
        Send(w, player, new BumpReport { Description = "[feedback] Door stuck — kid@example.org" }); // an older client: no field
        Send(w, player, new BumpReport { Description = "[feedback] x", FeedbackTitle = "Door\r\nFAKE " + new string('a', 300) });

        var rows = w.Repo.ListPlayerReports();
        Assert.Equal(3, rows.Count);
        Assert.Equal(string.Empty, rows[0].Note);
        Assert.Equal(string.Empty, rows[1].Note);
        Assert.StartsWith("Door  FAKE", rows[2].Note);
        Assert.Equal(80, rows[2].Note.Length);
        Assert.DoesNotContain(rows, r => r.Note.Contains("example.org") || r.Note.Contains('\n') || r.Note.Contains('\r'));
    }

    // ---------------- 2: rate limits ----------------

    [Fact]
    public void ThePanel_SendsItsBurstAtMostEveryTwoSeconds_ButClosingAlwaysPasses()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        w.Server.SetAdminModeForTest(admin, true);
        Advance(w);

        w.Server.SetAdminPanelForTest(admin, true);
        w.Server.SetAdminPanelForTest(admin, false);
        Assert.False(admin.Admin.PanelOpen);
        Advance(w, 500);
        w.Server.SetAdminPanelForTest(admin, true);
        Assert.True(admin.Admin.PanelOpen); // the stream resumes …
        Assert.Single(w.To<AdminEventBacklog>(admin)); // … without a second backlog

        Advance(w, 2_500);
        w.Server.SetAdminPanelForTest(admin, true);
        Assert.Equal(2, w.To<AdminEventBacklog>(admin).Count);
        Assert.Equal(2, w.To<AdminReportList>(admin).Count);
    }

    [Fact]
    public void ReportActions_AreCappedPerTenSeconds_AndAToggleIsLoggedOnce()
    {
        using var w = new AdminTestWorld(Content);
        var admin = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Server.CallAdminForTest(player, "help");
        long id = w.Repo.ListPlayerReports().Single().Id;
        Advance(w);

        for (int i = 0; i < 11; i++)
        {
            w.Server.AdminReportActionForTest(admin, id, done: i % 2 == 0); // the 11th (done) is over the cap
        }

        Assert.Equal("open", w.Repo.ListPlayerReports().Single().State);
        Assert.Single(w.Server.AdminEventsForTest, e => e.TextKey is "admin.ev.report_done" or "admin.ev.report_reopened");

        Advance(w, 11_000);
        w.Server.AdminReportActionForTest(admin, id, done: true);
        Assert.Equal("done", w.Repo.ListPlayerReports().Single().State);
        Assert.Equal(2, w.Server.AdminEventsForTest.Count(e => e.TextKey is "admin.ev.report_done" or "admin.ev.report_reopened"));
    }

    // ---------------- 3: the portal mirror ----------------

    [Fact]
    public void ThePortalMirror_StripsAndCapsItsInput_AndIgnoresANameNobodyKnows()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");
        w.Join("Jonah");
        w.Transport.Sent.Clear();

        Send(w, player, new PlayerReportIntent { TargetName = "nobody here", Note = "x", AlsoFiledWithPortal = true });
        Send(w, player, new PlayerReportIntent { TargetName = new string('J', 5000), Note = "x", AlsoFiledWithPortal = true });
        Send(w, player, new PlayerReportIntent
        {
            TargetName = "\"jonah\"\r\n",
            Note = "he did it\r\n[INFO] fake log line " + new string('z', 5000),
            AlsoFiledWithPortal = true,
        });

        var row = Assert.Single(w.Repo.ListPlayerReports());
        Assert.Equal("Jonah", row.TargetId);
        Assert.Equal("Jonah", row.TargetName);
        Assert.DoesNotContain('\n', row.Note);
        Assert.DoesNotContain('\r', row.Note);
        Assert.Equal(200, row.Note.Length);
        Assert.Empty(w.Said(player)); // the mirror stays silent — also for a name nobody knows
    }

    // ---------------- 4: feedback shares the limit; the list is capped ----------------

    [Fact]
    public void Feedback_SharesTheReportRateLimit_Quietly()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        for (int i = 0; i < 5; i++)
        {
            Send(w, player, new BumpReport { Description = "[feedback] t" + i, FeedbackTitle = "t" + i });
        }

        Assert.Equal(3, w.Repo.ListPlayerReports().Count);
        Assert.DoesNotContain("@srv.report.too_many", w.Said(player)); // the feedback itself went through
    }

    [Fact]
    public void TheList_KeepsAtMost500Rows_AndForgetsOldDoneOnes()
    {
        using var w = new AdminTestWorld(Content);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        w.Repo.AddPlayerReport(Row("done", now - (100L * 24 * 3600))); // past the 90 days
        w.Repo.AddPlayerReport(Row("done", now - (10L * 24 * 3600)));
        w.Repo.AddPlayerReport(Row("open", now - (400L * 24 * 3600))); // open rows never age out
        Assert.Equal(2, w.Server.WorldReportCountForTest);

        using var big = new AdminTestWorld(Content);
        for (int i = 0; i < 520; i++)
        {
            big.Repo.AddPlayerReport(Row("open", now));
        }

        Assert.Equal(500, big.Server.WorldReportCountForTest);
        var admin = big.Join("Owner", PlayerRole.WorldAdmin);
        Assert.Equal(200, big.Server.AdminReportListForTest(admin).Reports.Length);
    }

    // ---------------- 5 + 7a: identities, offline and invisible targets ----------------

    [Fact]
    public void AReportAboutAnAdmin_StaysHiddenFromThem_WhateverTheLetterCase()
    {
        using var w = new AdminTestWorld(Content);
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var mira = w.Join("Mira", PlayerRole.Admin);
        w.Server.DisconnectLocalPlayerForTest("Mira"); // offline now — a known player all the same
        var player = w.Join("Justus");
        w.Repo.AddPlayerReport(Row("open", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), target: "mira")); // an older row, other case

        Say(w, player, "/report mira she is mean");

        Assert.Contains("@srv.report.sent:Mira", w.Said(player));
        Assert.Contains(w.Repo.ListPlayerReports(), r => r.TargetId == "Mira" && r.Kind == AdminReportKinds.Player);
        mira = w.Join("Mira", PlayerRole.Admin);
        Assert.Empty(w.Server.AdminReportListForTest(mira).Reports);
        Assert.Equal(2, w.Server.AdminReportListForTest(owner).Reports.Length);
        w.Server.SetAdminModeForTest(mira, true);
        Advance(w, 3_000);
        w.Transport.Sent.Clear();
        w.Server.SetAdminPanelForTest(mira, true);
        Assert.DoesNotContain(w.To<AdminEventBacklog>(mira).Single().Events, e => e.Category == AdminEventCategories.Report);
    }

    [Fact]
    public void ReportingAnInvisibleAdmin_AnswersLikeAnyKnownPlayer_AndNeverGivesAwayWhereTheyHover()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var op = w.Join("Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var player = w.Join("Justus");
        w.Join("Ghost");
        w.Server.DisconnectLocalPlayerForTest("Ghost");
        var entry = op.State.Position;
        w.Server.SetAdminModeForTest(op, true);
        op.State.Position = AdminTestWorld.Offset(op, 80f, 40f, 0f);
        w.Transport.Sent.Clear();

        Say(w, player, "/report operator");
        Say(w, player, "/report ghost");
        Say(w, player, "/report Nobody");

        var said = w.Said(player);
        Assert.Contains("@srv.report.sent:Operator", said);
        Assert.Contains("@srv.report.sent:Ghost", said);
        Assert.Contains("@srv.report.no_target", said);
        var row = w.Repo.ListPlayerReports().First(r => r.TargetId == "Operator");
        Assert.Equal(entry.X, row.X); // the entry point, never the hover position
        Assert.Equal(entry.Y, row.Y);
        w.Server.SetAdminModeForTest(owner, true);
        Advance(w, 3_000);
        w.Server.SetAdminPanelForTest(owner, true);
        Assert.DoesNotContain(w.To<AdminEventBacklog>(owner).Single().Events, e => e.TargetId == "Operator"); // staff-only
    }

    // ---------------- 11a + 11c: the visible variant; the hosted call ----------------

    [Fact]
    public void TheVisibleVariant_GetsBodiesButNoPositions()
    {
        using var w = new AdminTestWorld(Content, hosted: true, fleetAdmins: "Operator");
        var owner = w.Join("Owner", PlayerRole.WorldAdmin);
        var op = w.Join("Operator");
        var player = w.Join("Justus");
        w.Server.CallAdminForTest(player, "help, I am stuck");
        w.Server.SetAdminModeForTest(owner, true);
        w.Server.SetAdminModeForTest(op, true);
        Advance(w);
        w.Transport.Sent.Clear();
        w.Server.SetAdminPanelForTest(owner, true);
        w.Server.SetAdminPanelForTest(op, true);

        var mine = w.Server.AdminRosterForTest(owner).Players.Single(p => p.Name == "Justus");
        Assert.False(mine.HasPosition);
        Assert.Equal(0f, mine.X);
        Assert.Equal(player.CurrentLocationId, mine.LocationId);
        Assert.DoesNotContain(w.To<AdminEventBacklog>(owner).Single().Events, e => e.HasPosition || e.X != 0f);
        Assert.DoesNotContain(w.To<AdminReportList>(owner).Single().Reports, r => r.HasPosition || r.X != 0f);

        Assert.True(w.Server.AdminRosterForTest(op).Players.Single(p => p.Name == "Justus").HasPosition); // the fleet admin does
        Assert.Contains(w.To<AdminReportList>(op).Single().Reports, r => r.HasPosition);
    }

    [Fact]
    public void OnAHostedWorld_ACallAlsoReachesTheOperators()
    {
        using var hosted = new AdminTestWorld(Content, hosted: true);
        hosted.Server.CallAdminForTest(hosted.Join("Justus"), "someone is mean to me");
        string json = Assert.IsType<string>(hosted.Server.LastAdminCallJsonForTest);
        Assert.Contains("admin-call", json);
        Assert.Contains("someone is mean to me", json);

        using var family = new AdminTestWorld(Content);
        family.Server.CallAdminForTest(family.Join("Justus"), "where is the shop?");
        Assert.Null(family.Server.LastAdminCallJsonForTest);
    }

    // ---------------- 17: literal arguments, the masked-line limit ----------------

    [Fact]
    public void APlayersText_IsLiteralInTheLog_WhileADeathCauseStaysAKey()
    {
        using var w = new AdminTestWorld(Content);
        var odd = w.Join("@srv.admin.mode_on {1}"); // a name is a player's text too

        w.Server.KillPlayerForTest(odd, "@srv.death.fall");

        var join = w.Server.AdminEventsForTest.Last(e => e.TextKey == "admin.ev.join");
        Assert.Equal("@@srv.admin.mode_on {1}", join.Args[0]);
        var death = w.Server.AdminEventsForTest.Last(e => e.TextKey == "admin.ev.death");
        Assert.Equal("@@srv.admin.mode_on {1}", death.Args[0]);
        Assert.Equal("@admin.cause.fall", death.Args[1]);
    }

    [Fact]
    public void MaskedLines_AreLoggedAtMostEveryThirtySeconds()
    {
        using var w = new AdminTestWorld(Content);
        var player = w.Join("Justus");

        Advance(w);
        w.Server.CallAdminForTest(player, "call me 0151 2345678");
        Advance(w);
        w.Server.CallAdminForTest(player, "again 0151 2345678");
        Advance(w, 31_000);
        w.Server.CallAdminForTest(player, "and again 0151 2345678");

        Assert.Equal(3, w.Repo.ListPlayerReports().Count); // every call was filed, masked
        Assert.Equal(2, w.Server.AdminEventsForTest.Count(e => e.TextKey == "admin.ev.filter_masked"));
    }
}
